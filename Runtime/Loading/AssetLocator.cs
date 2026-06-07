using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Existence checks for Addressable keys, backed by LoadResourceLocationsAsync.
    /// </summary>
    /// <remarks>
    /// Every probe <b>releases</b> its locations handle in a <c>finally</c>. Both
    /// reference systems leaked this handle on every existence check (drx#3 /
    /// myworld#2) — the leak is fixed here once, so callers can probe freely.
    /// </remarks>
    public static class AssetLocator
    {
        /// <summary>True if at least one resource location exists for the key.</summary>
        public static async UniTask<bool> ExistsAsync(object key, CancellationToken ct = default)
        {
            var handle = Addressables.LoadResourceLocationsAsync(key);
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
                return handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Count > 0;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        /// <summary>True if a location of asset type <typeparamref name="T"/> exists for the key.</summary>
        public static async UniTask<bool> ExistsAsync<T>(object key, CancellationToken ct = default)
        {
            var handle = Addressables.LoadResourceLocationsAsync(key, typeof(T));
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
                return handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Count > 0;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        /// <summary>
        /// Load the asset only if a location of the right type exists; returns null
        /// otherwise (no exception for the missing case). Borrows through
        /// <see cref="AssetLoader"/> — release with <c>AssetLoader.Release&lt;T&gt;(key)</c>.
        /// </summary>
        public static async UniTask<T> TryLoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            if (!await ExistsAsync<T>(key, ct))
                return null;
            return await AssetLoader.LoadAsync<T>(key, ct);
        }
    }
}
