using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Startup remote-content update orchestrator. Composes <see cref="CatalogUpdater"/> and
    /// <see cref="ContentDownloader"/> into the resilient flow:
    /// <list type="number">
    ///   <item>apply catalog updates <b>before</b> sizing,</item>
    ///   <item>size once across all labels (bundles shared between labels are counted once),</item>
    ///   <item>confirm with the player,</item>
    ///   <item>download everything as one union operation with true aggregate progress.</item>
    /// </list>
    /// One <see cref="CancellationToken"/> threads through; failures come back as a typed
    /// <see cref="DownloadResult"/> rather than thrown. (Catalog operations now live on
    /// <see cref="CatalogUpdater"/>.)
    /// </summary>
    public static class RemoteContentUpdater
    {
        /// <summary>Confirmation gate. Return true to proceed with the download.</summary>
        public delegate UniTask<bool> ConfirmDownload(long totalBytes);

        /// <summary>Run the full update flow over the given labels/keys.</summary>
        public static async UniTask<DownloadResult> RunAsync(
            IEnumerable<object> labels,
            IProgress<DownloadProgress> progress = null,
            ConfirmDownload confirm = null,
            CancellationToken ct = default)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));

            var keys = labels as IReadOnlyCollection<object> ?? new List<object>(labels);
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

                // 5) Download everything as one union operation; its DownloadStatus already
                //    aggregates across labels, so progress needs no per-label stitching.
                await ContentDownloader.DownloadAsync(keys, progress, ct);

                progress?.Report(new DownloadProgress(1f, totalBytes, totalBytes));
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
    }
}
