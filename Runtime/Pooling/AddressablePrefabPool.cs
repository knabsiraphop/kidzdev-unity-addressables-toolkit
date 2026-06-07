using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// The default <see cref="IAssetPool"/>. Each prefab is loaded once through the injected
    /// <see cref="IAssetLoader"/> and reused; inactive instances are parented to a persistent,
    /// inactive root so they survive scene loads. Each pool instance keeps its own root and tables;
    /// the process-wide default lives behind <see cref="AddressablePool.Default"/>.
    /// </summary>
    public sealed class AddressablePrefabPool : IAssetPool
    {
        private sealed class Pool
        {
            // Preserved so the cached load can be awaited by many GetAsync/Prewarm calls.
            public UniTask<GameObject> PrefabTask;
            public readonly Queue<GameObject> Inactive = new();
            public readonly HashSet<GameObject> Active = new();
        }

        private readonly IAssetLoader _assetLoader;
        private readonly Dictionary<object, Pool> _poolsByKey = new();
        private Transform _inactiveRoot;

        /// <param name="assetLoader">Loader used to fetch prefabs; defaults to <see cref="AssetLoader.Default"/>.</param>
        public AddressablePrefabPool(IAssetLoader assetLoader = null)
            => _assetLoader = assetLoader ?? AssetLoader.Default;

        private Transform InactiveRoot
        {
            get
            {
                if (_inactiveRoot == null)
                {
                    var rootObject = new GameObject("[AddressablePool]");
                    Object.DontDestroyOnLoad(rootObject);
                    rootObject.SetActive(false);
                    _inactiveRoot = rootObject.transform;
                }
                return _inactiveRoot;
            }
        }

        /// <inheritdoc/>
        public async UniTask<GameObject> GetAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            var pool = EnsurePool(key);
            var prefab = await pool.PrefabTask;
            ct.ThrowIfCancellationRequested();

            GameObject instance = null;
            while (pool.Inactive.Count > 0 && instance == null)
                instance = pool.Inactive.Dequeue(); // skip any externally destroyed entries
            if (instance == null)
                instance = CreateInstance(key, prefab);

            instance.transform.SetParent(parent, false);
            instance.SetActive(true);
            pool.Active.Add(instance);
            return instance;
        }

        /// <inheritdoc/>
        public void Release(GameObject instance)
        {
            if (instance == null) return;

            var pooledMarker = instance.GetComponent<PooledObject>();
            if (pooledMarker == null || !_poolsByKey.TryGetValue(pooledMarker.Key, out var pool))
            {
                Object.Destroy(instance); // not ours — destroy so it doesn't leak
                return;
            }

            if (!pool.Active.Remove(instance))
                return; // already released

            instance.SetActive(false);
            instance.transform.SetParent(InactiveRoot, false);
            pool.Inactive.Enqueue(instance);
        }

        /// <inheritdoc/>
        public async UniTask Prewarm(object key, int count, CancellationToken ct = default)
        {
            var pool = EnsurePool(key);
            var prefab = await pool.PrefabTask;
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < count; i++)
            {
                var instance = CreateInstance(key, prefab);
                instance.SetActive(false);
                instance.transform.SetParent(InactiveRoot, false);
                pool.Inactive.Enqueue(instance);
            }
        }

        /// <inheritdoc/>
        public void ClearPool(object key)
        {
            if (!_poolsByKey.TryGetValue(key, out var pool))
                return;

            foreach (var instance in pool.Inactive)
                if (instance != null) Object.Destroy(instance);
            foreach (var instance in pool.Active)
                if (instance != null) Object.Destroy(instance);

            _poolsByKey.Remove(key);
            _assetLoader.Release<GameObject>(key);
        }

        /// <inheritdoc/>
        public void ClearAll()
        {
            foreach (var key in new List<object>(_poolsByKey.Keys))
                ClearPool(key);
        }

        private Pool EnsurePool(object key)
        {
            if (!_poolsByKey.TryGetValue(key, out var pool))
            {
                pool = new Pool { PrefabTask = _assetLoader.LoadAsync<GameObject>(key).Preserve() };
                _poolsByKey[key] = pool;
            }
            return pool;
        }

        private static GameObject CreateInstance(object key, GameObject prefab)
        {
            var instance = Object.Instantiate(prefab);
            instance.AddComponent<PooledObject>().Key = key;
            return instance;
        }
    }
}
