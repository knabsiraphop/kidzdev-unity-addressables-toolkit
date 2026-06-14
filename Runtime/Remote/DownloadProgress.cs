using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>Progress snapshot for a remote-content download.</summary>
    public readonly struct DownloadProgress
    {
        public readonly float Percent;        // 0..1
        public readonly long DownloadedBytes;
        public readonly long TotalBytes;

        internal DownloadProgress(DownloadStatus status)
        {
            Percent = status.Percent;
            DownloadedBytes = status.DownloadedBytes;
            TotalBytes = status.TotalBytes;
        }

        internal DownloadProgress(float percent, long downloadedBytes, long totalBytes)
        {
            Percent = percent;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }
    }
}
