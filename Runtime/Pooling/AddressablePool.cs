using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Object pool for Addressable prefabs. The prefab for each key is loaded
    /// once (via <see cref="AssetLoader"/>) and reused; instances are recycled
    /// instead of being destroyed. Inactive instances are parented to a persistent
    /// root so they survive scene loads.
    /// </summary>
    public static class AddressablePool
    {
        private sealed class Pool
        {
            // Preserved so the cached load can be awaited by many GetAsync/Prewarm calls.
            public UniTask<GameObject> PrefabTask;
            public readonly Queue<GameObject> Inactive = new();
            public readonly HashSet<GameObject> Active = new();
        }

        private static readonly Dictionary<object, Pool> _pools = new();
        private static Transform _root;

        private static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("[AddressablePool]");
                    Object.DontDestroyOnLoad(go);
                    go.SetActive(false);
                    _root = go.transform;
                }
                return _root;
            }
        }

        /// <summary>Get an active instance, loading + warming the prefab if needed.</summary>
        public static async UniTask<GameObject> GetAsync(object key, Transform parent = null, CancellationToken ct = default)
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

        /// <summary>Return an instance to its pool (deactivates, does not destroy).</summary>
        public static void Release(GameObject instance)
        {
            if (instance == null) return;

            var marker = instance.GetComponent<PooledObject>();
            if (marker == null || !_pools.TryGetValue(marker.Key, out var pool))
            {
                Object.Destroy(instance); // not ours — destroy so it doesn't leak
                return;
            }

            if (!pool.Active.Remove(instance))
                return; // already released

            instance.SetActive(false);
            instance.transform.SetParent(Root, false);
            pool.Inactive.Enqueue(instance);
        }

        /// <summary>Pre-instantiate count inactive instances.</summary>
        public static async UniTask Prewarm(object key, int count, CancellationToken ct = default)
        {
            var pool = EnsurePool(key);
            var prefab = await pool.PrefabTask;
            ct.ThrowIfCancellationRequested();

            for (int i = 0; i < count; i++)
            {
                var instance = CreateInstance(key, prefab);
                instance.SetActive(false);
                instance.transform.SetParent(Root, false);
                pool.Inactive.Enqueue(instance);
            }
        }

        /// <summary>Destroy every instance of a key and release the prefab handle.</summary>
        public static void ClearPool(object key)
        {
            if (!_pools.TryGetValue(key, out var pool))
                return;

            foreach (var go in pool.Inactive)
                if (go != null) Object.Destroy(go);
            foreach (var go in pool.Active)
                if (go != null) Object.Destroy(go);

            _pools.Remove(key);
            AssetLoader.Release<GameObject>(key);
        }

        public static void ClearAll()
        {
            foreach (var key in new List<object>(_pools.Keys))
                ClearPool(key);
        }

        private static Pool EnsurePool(object key)
        {
            if (!_pools.TryGetValue(key, out var pool))
            {
                pool = new Pool { PrefabTask = AssetLoader.LoadAsync<GameObject>(key).Preserve() };
                _pools[key] = pool;
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
