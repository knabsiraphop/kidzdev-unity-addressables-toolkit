using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Object pool for Addressable prefabs: the prefab for each key is loaded once and reused, and
    /// instances are recycled instead of destroyed. Depend on this abstraction (rather than the
    /// static <see cref="AddressablePool"/> facade) for substitutable, testable pooling.
    /// </summary>
    public interface IAssetPool
    {
        /// <summary>Get an active instance, loading + warming the prefab if needed.</summary>
        UniTask<GameObject> GetAsync(object key, Transform parent = null, CancellationToken ct = default);

        /// <summary>Return an instance to its pool (deactivates, does not destroy).</summary>
        void Release(GameObject instance);

        /// <summary>Pre-instantiate <paramref name="count"/> inactive instances.</summary>
        UniTask Prewarm(object key, int count, CancellationToken ct = default);

        /// <summary>Destroy every instance of a key and release the prefab handle.</summary>
        void ClearPool(object key);

        /// <summary>Clear every pool.</summary>
        void ClearAll();
    }
}
