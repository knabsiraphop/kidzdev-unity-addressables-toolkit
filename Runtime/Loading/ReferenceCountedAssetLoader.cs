using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// The default <see cref="IAssetLoader"/>: a reference-counted loader whose cache is keyed by
    /// <em>both</em> the addressable key and the requested type. Loading the same key as two
    /// different types (e.g. a texture as <c>Texture2D</c> and as <c>Sprite</c>) is legitimate and
    /// yields two independent handles — never a silent wrong-type cast.
    /// </summary>
    /// <remarks>
    /// Each instance owns its own cache. The process-wide default lives behind
    /// <see cref="AssetLoader.Default"/>; create additional instances only when you genuinely want
    /// isolated ownership. Not thread-safe; use from the main thread, like the rest of Addressables.
    /// </remarks>
    public sealed class ReferenceCountedAssetLoader : IAssetLoader
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly object _key;
            private readonly Type _assetType;

            public CacheKey(object key, Type assetType)
            {
                _key = key;
                _assetType = assetType;
            }

            public bool Equals(CacheKey other) => _assetType == other._assetType && Equals(_key, other._key);
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode()
                => unchecked(((_key?.GetHashCode() ?? 0) * 397) ^ (_assetType?.GetHashCode() ?? 0));
        }

        private sealed class CacheEntry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        private readonly Dictionary<CacheKey, CacheEntry> _cache = new();

        /// <inheritdoc/>
        public async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            var cacheKey = new CacheKey(key, typeof(T));
            if (_cache.TryGetValue(cacheKey, out var cacheEntry))
            {
                cacheEntry.RefCount++;
            }
            else
            {
                cacheEntry = new CacheEntry { Handle = Addressables.LoadAssetAsync<T>(key), RefCount = 1 };
                _cache[cacheKey] = cacheEntry;
            }

            try
            {
                await cacheEntry.Handle.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                Release<T>(key);
                throw;
            }

            if (cacheEntry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Release<T>(key);
                throw new InvalidOperationException($"Failed to load addressable '{key}' as {typeof(T).Name}.");
            }

            return cacheEntry.Handle.Result as T;
        }

        /// <inheritdoc/>
        public void Release<T>(object key) where T : UnityEngine.Object
            => Release(new CacheKey(key, typeof(T)));

        /// <inheritdoc/>
        public void Release(object key, Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Release(new CacheKey(key, type));
        }

        private void Release(CacheKey cacheKey)
        {
            if (!_cache.TryGetValue(cacheKey, out var cacheEntry))
                return;

            if (--cacheEntry.RefCount > 0)
                return;

            _cache.Remove(cacheKey);
            Addressables.Release(cacheEntry.Handle);
        }

        /// <inheritdoc/>
        public bool IsLoaded<T>(object key) where T : UnityEngine.Object
            => _cache.ContainsKey(new CacheKey(key, typeof(T)));

        /// <inheritdoc/>
        public void ReleaseAll()
        {
            foreach (var cacheEntry in _cache.Values)
                Addressables.Release(cacheEntry.Handle);
            _cache.Clear();
        }
    }
}
