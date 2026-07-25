using System;
using System.Collections.Generic;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>Progress snapshot for a remote-content download.</summary>
    public readonly struct DownloadProgress
    {
        public readonly float Percent;        // 0..1, aggregate across the whole run
        public readonly long DownloadedBytes;
        public readonly long TotalBytes;
        public readonly float BytesPerSecond;

        /// <summary>
        /// Per-label breakdown, ordered as the input labels were passed to
        /// <see cref="RemoteContentUpdater.RunAsync"/>. Empty unless that call was made with
        /// <c>perLabelProgress: true</c>. A shared bundle counts toward every label that
        /// references it, so <c>Labels[i].TotalBytes</c> summed across entries can legitimately
        /// exceed <see cref="TotalBytes"/> (which is deduped) — that is by design, not a bug.
        /// The array is mutated and reused in place across a single run for a hot polling path;
        /// copy it if you need to retain a snapshot.
        /// </summary>
        public readonly IReadOnlyList<LabelDownloadProgress> Labels;

        /// <summary>The entry in <see cref="Labels"/> currently downloading, or null when idle/between labels.</summary>
        public readonly LabelDownloadProgress? CurrentLabel;

        internal DownloadProgress(UnityEngine.ResourceManagement.AsyncOperations.DownloadStatus status)
        {
            Percent = status.Percent;
            DownloadedBytes = status.DownloadedBytes;
            TotalBytes = status.TotalBytes;
            BytesPerSecond = 0f;
            Labels = Array.Empty<LabelDownloadProgress>();
            CurrentLabel = null;
        }

        internal DownloadProgress(float percent, long downloadedBytes, long totalBytes)
        {
            Percent = percent;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            BytesPerSecond = 0f;
            Labels = Array.Empty<LabelDownloadProgress>();
            CurrentLabel = null;
        }

        internal DownloadProgress(
            float percent, long downloadedBytes, long totalBytes,
            float bytesPerSecond, IReadOnlyList<LabelDownloadProgress> labels, LabelDownloadProgress? currentLabel)
        {
            Percent = percent;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            BytesPerSecond = bytesPerSecond;
            Labels = labels ?? Array.Empty<LabelDownloadProgress>();
            CurrentLabel = currentLabel;
        }
    }
}
