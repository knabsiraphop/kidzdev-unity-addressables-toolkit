namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>State of a single label within a multi-label <see cref="RemoteContentUpdater"/> run.</summary>
    public enum LabelDownloadState
    {
        Pending,
        Downloading,
        Complete
    }

    /// <summary>
    /// Progress snapshot for one label within a <see cref="RemoteContentUpdater.RunAsync"/> call made
    /// with <c>perLabelProgress: true</c>. <see cref="TotalBytes"/> is this label's full size, sized
    /// upfront before any label starts downloading — it does not shrink when an earlier label's cache
    /// hit already satisfied a bundle this label also references, so the bar this drives stays
    /// order-independent.
    /// </summary>
    public readonly struct LabelDownloadProgress
    {
        public readonly object Label;
        public readonly LabelDownloadState State;
        public readonly long DownloadedBytes;
        public readonly long TotalBytes;
        public readonly float Percent;        // 0..1
        public readonly float BytesPerSecond;  // 0 unless State == Downloading

        internal LabelDownloadProgress(object label, LabelDownloadState state, long downloadedBytes, long totalBytes, float bytesPerSecond)
        {
            Label = label;
            State = state;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Percent = totalBytes > 0 ? (float)downloadedBytes / totalBytes : 1f;
            BytesPerSecond = bytesPerSecond;
        }
    }
}
