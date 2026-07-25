using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Startup remote-content update orchestrator. Composes <see cref="CatalogUpdater"/> and
    /// <see cref="ContentDownloader"/> into the resilient flow:
    /// <list type="number">
    ///   <item>apply catalog updates <b>before</b> sizing,</item>
    ///   <item>size once across all labels (bundles shared between labels are counted once),</item>
    ///   <item>confirm with the player,</item>
    ///   <item>download everything, reporting aggregate (and optionally per-label) progress.</item>
    /// </list>
    /// One <see cref="CancellationToken"/> threads through; failures come back as a typed
    /// <see cref="DownloadResult"/> rather than thrown. (Catalog operations now live on
    /// <see cref="CatalogUpdater"/>.)
    /// </summary>
    /// <remarks>
    /// By default the download itself is one Addressables union operation, so the aggregate
    /// <see cref="DownloadProgress"/> is a true dedup'd total with no per-label visibility. Passing
    /// <c>perLabelProgress: true</c> switches to a sequential per-label loop (still deduped for real
    /// network cost via Addressables' own bundle cache — a bundle a later label shares with an
    /// earlier one reports as already satisfied) so <see cref="DownloadProgress.Labels"/> is
    /// populated; this changes operation/retry granularity from one op to N, not real download cost.
    /// </remarks>
    public static class RemoteContentUpdater
    {
        /// <summary>Confirmation gate. Return true to proceed with the download.</summary>
        public delegate UniTask<bool> ConfirmDownload(long totalBytes);

        /// <summary>Run the full update flow over the given labels/keys.</summary>
        /// <param name="perLabelProgress">
        /// When true, downloads labels sequentially instead of as one union operation so
        /// <see cref="DownloadProgress.Labels"/>/<see cref="DownloadProgress.CurrentLabel"/> are
        /// populated. Default false keeps the single-union-operation path unchanged.
        /// </param>
        public static async UniTask<DownloadResult> RunAsync(
            IEnumerable<object> labels,
            IProgress<DownloadProgress> progress = null,
            ConfirmDownload confirm = null,
            CancellationToken ct = default,
            bool perLabelProgress = false)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));

            var keys = labels as IReadOnlyList<object> ?? new List<object>(labels);
            if (keys.Count == 0)
                return DownloadResult.NoUpdate();

            try
            {
                // 1 + 2) Check for catalog updates and apply them BEFORE sizing/downloading.
                await CatalogUpdater.CheckAndUpdateCatalogsAsync(ct);

                // 3) Size once across every label against the now-current catalog. Sizing per
                //    label and summing would double-count bundles shared between labels.
                long totalBytes = await ContentDownloader.GetDownloadSizeAsync(keys, ct);
                if (totalBytes == 0)
                    return DownloadResult.NoUpdate();

                // 4) Confirm.
                if (confirm != null && !await confirm(totalBytes))
                    return DownloadResult.Rejected();

                // 5) Download.
                if (perLabelProgress)
                {
                    var finalLabels = await DownloadWithLabelProgressAsync(keys, totalBytes, progress, ct);
                    progress?.Report(new DownloadProgress(1f, totalBytes, totalBytes, 0f, finalLabels, null));
                }
                else
                {
                    // Single union operation; its DownloadStatus already aggregates across
                    // labels, so progress needs no per-label stitching.
                    await ContentDownloader.DownloadAsync(keys, progress, ct);
                    progress?.Report(new DownloadProgress(1f, totalBytes, totalBytes));
                }

                return DownloadResult.Success(totalBytes);
            }
            catch (OperationCanceledException)
            {
                return DownloadResult.Cancelled();
            }
            catch (Exception e)
            {
                return DownloadResult.FromException(e);
            }
        }

        /// <summary>
        /// Downloads each label sequentially via <see cref="ContentDownloader"/>'s single-key
        /// overload so real per-label progress is observable, while keeping the aggregate byte
        /// count deduped (a shared bundle's bytes are only ever added to the aggregate once, by
        /// whichever label happened to fetch it first).
        /// </summary>
        private static async UniTask<LabelDownloadProgress[]> DownloadWithLabelProgressAsync(
            IReadOnlyList<object> keys,
            long aggregateTotalBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken ct)
        {
            // Size every label individually, upfront, before any label starts downloading. This is
            // what keeps each label's own TotalBytes order-independent — its live size mid-download
            // would otherwise shrink by whatever an earlier label's cache hit already satisfied for
            // a bundle the two labels share.
            var sizeTasks = new UniTask<long>[keys.Count];
            for (int i = 0; i < keys.Count; i++)
                sizeTasks[i] = ContentDownloader.GetDownloadSizeAsync(keys[i], ct);
            long[] labelTotals = await UniTask.WhenAll(sizeTasks);

            var labels = new LabelDownloadProgress[keys.Count];
            for (int i = 0; i < keys.Count; i++)
                labels[i] = new LabelDownloadProgress(keys[i], LabelDownloadState.Pending, 0, labelTotals[i], 0f);

            var rate = new DownloadRateTracker();
            rate.Reset(0);
            long completedNetworkBytes = 0;

            void ReportSnapshot(int currentIndex, long aggregateDownloaded, float bps)
            {
                var current = currentIndex >= 0 ? labels[currentIndex] : (LabelDownloadProgress?)null;
                float aggregatePercent = aggregateTotalBytes > 0 ? (float)aggregateDownloaded / aggregateTotalBytes : 1f;
                progress?.Report(new DownloadProgress(aggregatePercent, aggregateDownloaded, aggregateTotalBytes, bps, labels, current));
            }

            for (int i = 0; i < keys.Count; i++)
            {
                long labelTotal = labelTotals[i];
                long lastInnerTotal = 0;

                labels[i] = new LabelDownloadProgress(keys[i], LabelDownloadState.Downloading, 0, labelTotal, 0f);
                ReportSnapshot(i, completedNetworkBytes, 0f);

                var innerProgress = new ActionProgress<DownloadProgress>(inner =>
                {
                    lastInnerTotal = inner.TotalBytes;

                    // How much of this label's true size was already satisfied by an earlier
                    // label's cache hit on a shared bundle.
                    long alreadyCredited = Math.Max(0, labelTotal - inner.TotalBytes);
                    long displayedDownloaded = Math.Min(labelTotal, alreadyCredited + inner.DownloadedBytes);
                    long aggregateDownloaded = completedNetworkBytes + inner.DownloadedBytes;
                    float bps = rate.Sample(aggregateDownloaded);

                    labels[i] = new LabelDownloadProgress(keys[i], LabelDownloadState.Downloading, displayedDownloaded, labelTotal, bps);
                    ReportSnapshot(i, aggregateDownloaded, bps);
                });

                await ContentDownloader.DownloadAsync(keys[i], innerProgress, ct);

                completedNetworkBytes += lastInnerTotal;
                labels[i] = new LabelDownloadProgress(keys[i], LabelDownloadState.Complete, labelTotal, labelTotal, 0f);
                ReportSnapshot(-1, completedNetworkBytes, 0f);
            }

            return labels;
        }

        private sealed class ActionProgress<T> : IProgress<T>
        {
            private readonly Action<T> _action;
            public ActionProgress(Action<T> action) => _action = action;
            public void Report(T value) => _action(value);
        }
    }
}
