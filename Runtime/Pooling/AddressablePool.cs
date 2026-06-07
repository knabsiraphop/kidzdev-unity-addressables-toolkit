using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Static convenience facade over the process-wide default <see cref="IAssetPool"/>
    /// (<see cref="AddressablePrefabPool"/>). For testability/DI, depend on <see cref="IAssetPool"/>
    /// and inject <see cref="Default"/> instead.
    /// </summary>
    public static class AddressablePool
    {
        private static IAssetPool _default;

        /// <summary>The process-wide default pool.</summary>
        public static IAssetPool Default => _default ??= new AddressablePrefabPool(AssetLoader.Default);

        /// <inheritdoc cref="IAssetPool.GetAsync"/>
        public static UniTask<GameObject> GetAsync(object key, Transform parent = null, CancellationToken ct = default)
            => Default.GetAsync(key, parent, ct);

        /// <inheritdoc cref="IAssetPool.Release"/>
        public static void Release(GameObject instance) => Default.Release(instance);

        /// <inheritdoc cref="IAssetPool.Prewarm"/>
        public static UniTask Prewarm(object key, int count, CancellationToken ct = default)
            => Default.Prewarm(key, count, ct);

        /// <inheritdoc cref="IAssetPool.ClearPool"/>
        public static void ClearPool(object key) => Default.ClearPool(key);

        /// <inheritdoc cref="IAssetPool.ClearAll"/>
        public static void ClearAll() => Default.ClearAll();
    }
}
