using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
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

    /// <summary>
    /// Low-level primitives for remote Addressable content: query download size,
    /// predownload bundles with progress, and clear the download cache. These throw
    /// on failure; <see cref="RemoteContentUpdater"/> wraps them with typed
    /// <see cref="DownloadResult"/> classification for a full update flow.
    /// </summary>
    public static class DownloadHelper
    {
        /// <summary>Bytes still needing download for a key/label (0 = already cached).</summary>
        public static async UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct = default)
        {
            var handle = Addressables.GetDownloadSizeAsync(key);
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
                return handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : 0L;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        /// <summary>Download bundles for a key/label, reporting progress until done.</summary>
        public static async UniTask DownloadAsync(object key, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            var handle = Addressables.DownloadDependenciesAsync(key, false);
            try
            {
                while (!handle.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new DownloadProgress(handle.GetDownloadStatus()));
                    await UniTask.Yield();
                }

                await handle.ToUniTask(cancellationToken: ct);
                progress?.Report(new DownloadProgress(handle.GetDownloadStatus()));

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    throw handle.OperationException
                          ?? new InvalidOperationException($"Failed to download dependencies for '{key}'.");
                }
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        /// <summary>Clear cached bundles for a key/label. Returns true on success.</summary>
        public static async UniTask<bool> ClearCacheAsync(object key, CancellationToken ct = default)
        {
            var handle = Addressables.ClearDependencyCacheAsync(key, false);
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
                return handle.Status == AsyncOperationStatus.Succeeded && handle.Result;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }
    }
}
