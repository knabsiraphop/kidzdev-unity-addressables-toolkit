using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Low-level primitives for remote Addressable content: query download size, predownload bundles
    /// with progress, and clear the download cache. Each operation releases its handle on all paths.
    /// These throw on failure; <see cref="RemoteContentUpdater"/> wraps them with typed
    /// <see cref="DownloadResult"/> classification for a full update flow.
    /// </summary>
    /// <remarks>Replaces the former <c>DownloadHelper</c> (a clearer, intent-revealing name).</remarks>
    public static class ContentDownloader
    {
        /// <summary>Bytes still needing download for a key/label (0 = already cached).</summary>
        public static async UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct = default)
        {
            var sizeHandle = Addressables.GetDownloadSizeAsync(key);
            try
            {
                await sizeHandle.ToUniTask(cancellationToken: ct);
                return sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0L;
            }
            finally
            {
                Addressables.Release(sizeHandle);
            }
        }

        /// <summary>Download bundles for a key/label, reporting progress until done.</summary>
        public static async UniTask DownloadAsync(object key, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            var downloadHandle = Addressables.DownloadDependenciesAsync(key, false);
            try
            {
                while (!downloadHandle.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new DownloadProgress(downloadHandle.GetDownloadStatus()));
                    await UniTask.Yield();
                }

                await downloadHandle.ToUniTask(cancellationToken: ct);
                progress?.Report(new DownloadProgress(downloadHandle.GetDownloadStatus()));

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    throw downloadHandle.OperationException
                          ?? new InvalidOperationException($"Failed to download dependencies for '{key}'.");
                }
            }
            finally
            {
                Addressables.Release(downloadHandle);
            }
        }

        /// <summary>Clear cached bundles for a key/label. Returns true on success.</summary>
        public static async UniTask<bool> ClearCacheAsync(object key, CancellationToken ct = default)
        {
            var clearHandle = Addressables.ClearDependencyCacheAsync(key, false);
            try
            {
                await clearHandle.ToUniTask(cancellationToken: ct);
                return clearHandle.Status == AsyncOperationStatus.Succeeded && clearHandle.Result;
            }
            finally
            {
                Addressables.Release(clearHandle);
            }
        }
    }
}
