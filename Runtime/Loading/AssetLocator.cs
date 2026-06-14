using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Existence checks for Addressable keys (backed by <c>LoadResourceLocationsAsync</c>), plus a
    /// "load only if present" probe. Every check <b>releases</b> its locations handle in a
    /// <c>finally</c>, so callers can probe freely without leaking.
    /// </summary>
    public static class AssetLocator
    {
        /// <summary>True if at least one resource location exists for the key.</summary>
        public static async UniTask<bool> ExistsAsync(object key, CancellationToken ct = default)
        {
            var locationsHandle = Addressables.LoadResourceLocationsAsync(key);
            try
            {
                await locationsHandle.ToUniTask(cancellationToken: ct);
                return locationsHandle.Status == AsyncOperationStatus.Succeeded && locationsHandle.Result.Count > 0;
            }
            finally
            {
                if (locationsHandle.IsValid()) Addressables.Release(locationsHandle);
            }
        }

        /// <summary>True if a location of asset type <typeparamref name="T"/> exists for the key.</summary>
        public static async UniTask<bool> ExistsAsync<T>(object key, CancellationToken ct = default)
        {
            var locationsHandle = Addressables.LoadResourceLocationsAsync(key, typeof(T));
            try
            {
                await locationsHandle.ToUniTask(cancellationToken: ct);
                return locationsHandle.Status == AsyncOperationStatus.Succeeded && locationsHandle.Result.Count > 0;
            }
            finally
            {
                if (locationsHandle.IsValid()) Addressables.Release(locationsHandle);
            }
        }

        /// <summary>
        /// Load the asset only if a location of the right type exists; returns null otherwise (no
        /// exception for the missing case). Borrows through <see cref="AssetLoader"/> — release with
        /// <c>AssetLoader.Release&lt;T&gt;(key)</c>.
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
