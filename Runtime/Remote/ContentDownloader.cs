using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Low-level primitives for remote Addressable content: query download size, predownload bundles
    /// with progress, and clear the download cache. Each operation releases its handle on all paths.
    /// The multi-key overloads run a single union operation, so bundles shared between keys are
    /// sized and downloaded once. These throw on failure; <see cref="RemoteContentUpdater"/> wraps
    /// them with typed <see cref="DownloadResult"/> classification for a full update flow.
    /// </summary>
    /// <remarks>Replaces the former <c>DownloadHelper</c> (a clearer, intent-revealing name).</remarks>
    public static class ContentDownloader
    {
        /// <summary>Bytes still needing download for a key/label (0 = already cached).</summary>
        public static UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct = default)
            => AwaitSizeAsync(Addressables.GetDownloadSizeAsync(key), ct);

        /// <summary>
        /// Bytes still needing download across several keys/labels. Bundles shared between keys
        /// are counted once (union of locations), so the total is safe to show in a confirm dialog.
        /// </summary>
        public static UniTask<long> GetDownloadSizeAsync(IEnumerable<object> keys, CancellationToken ct = default)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            return AwaitSizeAsync(Addressables.GetDownloadSizeAsync((IEnumerable)keys), ct);
        }

        /// <summary>Download bundles for a key/label, reporting progress until done.</summary>
        public static UniTask DownloadAsync(object key, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
            => AwaitDownloadAsync(Addressables.DownloadDependenciesAsync(key, false), key, progress, ct);

        /// <summary>
        /// Download bundles for several keys/labels as one union operation — shared bundles
        /// download once and the reported progress is the true aggregate.
        /// </summary>
        public static UniTask DownloadAsync(IEnumerable<object> keys, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            return AwaitDownloadAsync(
                Addressables.DownloadDependenciesAsync((IEnumerable)keys, Addressables.MergeMode.Union, false),
                "<multiple keys>", progress, ct);
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

        private static async UniTask<long> AwaitSizeAsync(AsyncOperationHandle<long> sizeHandle, CancellationToken ct)
        {
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

        private static async UniTask AwaitDownloadAsync(
            AsyncOperationHandle downloadHandle, object keyForError,
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
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
                          ?? new InvalidOperationException($"Failed to download dependencies for '{keyForError}'.");
                }
            }
            finally
            {
                Addressables.Release(downloadHandle);
            }
        }
    }
}
