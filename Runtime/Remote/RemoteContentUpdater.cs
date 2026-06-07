using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Startup remote-content update orchestrator. Composes <see cref="CatalogUpdater"/> and
    /// <see cref="ContentDownloader"/> into the resilient flow:
    /// <list type="number">
    ///   <item>apply catalog updates <b>before</b> sizing,</item>
    ///   <item>size across labels,</item>
    ///   <item>confirm with the player,</item>
    ///   <item>download with aggregate progress.</item>
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

            try
            {
                // 1 + 2) Check for catalog updates and apply them BEFORE sizing/downloading.
                await CatalogUpdater.CheckAndUpdateCatalogsAsync(ct);

                // 3) Size across labels against the now-current catalog.
                long totalBytes = 0;
                var pendingDownloads = new List<(object label, long size)>();
                foreach (var label in labels)
                {
                    ct.ThrowIfCancellationRequested();
                    long size = await ContentDownloader.GetDownloadSizeAsync(label, ct);
                    if (size > 0)
                    {
                        pendingDownloads.Add((label, size));
                        totalBytes += size;
                    }
                }

                if (totalBytes == 0)
                    return DownloadResult.NoUpdate();

                // 4) Confirm.
                if (confirm != null && !await confirm(totalBytes))
                    return DownloadResult.Rejected();

                // 5) Download each pending label, reporting aggregate progress.
                long downloadedBytes = 0;
                foreach (var (label, size) in pendingDownloads)
                {
                    ct.ThrowIfCancellationRequested();
                    await ContentDownloader.DownloadAsync(label, new AggregateProgress(progress, downloadedBytes, totalBytes), ct);
                    downloadedBytes += size;
                }

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

        /// <summary>Relays a single label's progress into the overall byte total.</summary>
        private sealed class AggregateProgress : IProgress<DownloadProgress>
        {
            private readonly IProgress<DownloadProgress> _inner;
            private readonly long _bytesBeforeThisLabel;
            private readonly long _grandTotalBytes;

            public AggregateProgress(IProgress<DownloadProgress> inner, long bytesBeforeThisLabel, long grandTotalBytes)
            {
                _inner = inner;
                _bytesBeforeThisLabel = bytesBeforeThisLabel;
                _grandTotalBytes = grandTotalBytes;
            }

            public void Report(DownloadProgress labelProgress)
            {
                if (_inner == null) return;
                long downloadedBytes = _bytesBeforeThisLabel + labelProgress.DownloadedBytes;
                float percent = _grandTotalBytes > 0 ? Mathf.Clamp01((float)downloadedBytes / _grandTotalBytes) : 1f;
                _inner.Report(new DownloadProgress(percent, downloadedBytes, _grandTotalBytes));
            }
        }
    }
}
