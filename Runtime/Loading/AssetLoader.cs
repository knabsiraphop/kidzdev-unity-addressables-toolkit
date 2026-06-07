using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Reference-counted async loader for Addressable assets, and the toolkit's
    /// canonical ownership model — pooling, sprite, and scene helpers all route
    /// their handles through here. Identical (key, type) pairs share one handle;
    /// the asset is released only when every borrower has released.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by <em>both</em> the addressable key and the requested
    /// type. Loading the same key as two different types is a legitimate operation
    /// (e.g. a texture loaded as <c>Texture2D</c> and as <c>Sprite</c>) and yields
    /// two independent handles — never a silent wrong-type cast.
    /// </remarks>
    public static class AssetLoader
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly object _key;
            private readonly Type _type;

            public CacheKey(object key, Type type)
            {
                _key = key;
                _type = type;
            }

            public bool Equals(CacheKey other) => _type == other._type && Equals(_key, other._key);
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode()
                => unchecked(((_key?.GetHashCode() ?? 0) * 397) ^ (_type?.GetHashCode() ?? 0));
        }

        private sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        private static readonly Dictionary<CacheKey, Entry> _cache = new();

        /// <summary>
        /// Load (or join an in-flight load of) the asset at <paramref name="key"/>.
        /// Cancellation is cooperative: a cancelled await releases this borrow and
        /// throws, but cannot abort an Addressables operation already in flight.
        /// </summary>
        public static async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            var cacheKey = new CacheKey(key, typeof(T));
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                entry.RefCount++;
            }
            else
            {
                entry = new Entry { Handle = Addressables.LoadAssetAsync<T>(key), RefCount = 1 };
                _cache[cacheKey] = entry;
            }

            try
            {
                await entry.Handle.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                Release<T>(key);
                throw;
            }

            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Release<T>(key);
                throw new InvalidOperationException($"Failed to load addressable '{key}' as {typeof(T).Name}.");
            }

            return entry.Handle.Result as T;
        }

        /// <summary>Release one borrow of (key, T). The handle frees at zero refs.</summary>
        public static void Release<T>(object key) where T : UnityEngine.Object
            => Release(new CacheKey(key, typeof(T)));

        private static void Release(CacheKey cacheKey)
        {
            if (!_cache.TryGetValue(cacheKey, out var entry))
                return;

            if (--entry.RefCount > 0)
                return;

            _cache.Remove(cacheKey);
            Addressables.Release(entry.Handle);
        }

        public static bool IsLoaded<T>(object key) where T : UnityEngine.Object
            => _cache.ContainsKey(new CacheKey(key, typeof(T)));

        /// <summary>Force-release every cached handle regardless of ref count.</summary>
        public static void ReleaseAll()
        {
            foreach (var entry in _cache.Values)
                Addressables.Release(entry.Handle);
            _cache.Clear();
        }
    }
}
