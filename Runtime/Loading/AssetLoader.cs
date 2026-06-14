using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Static convenience facade over the process-wide default <see cref="IAssetLoader"/>
    /// (<see cref="ReferenceCountedAssetLoader"/>). Keeps the ergonomic <c>AssetLoader.LoadAsync(…)</c>
    /// entry point; for testability/DI, depend on <see cref="IAssetLoader"/> and inject
    /// <see cref="Default"/> (or your own implementation) instead.
    /// </summary>
    public static class AssetLoader
    {
        private static IAssetLoader _default;

        /// <summary>The process-wide default loader. Inject this where an <see cref="IAssetLoader"/> is needed.</summary>
        public static IAssetLoader Default => _default ??= new ReferenceCountedAssetLoader();

        /// <summary>Drop the default so the next access builds a fresh loader (play-mode restarts without domain reload).</summary>
        internal static void ResetDefault() => _default = null;

        /// <inheritdoc cref="IAssetLoader.LoadAsync{T}"/>
        public static UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object => Default.LoadAsync<T>(key, ct);

        /// <inheritdoc cref="IAssetLoader.Release{T}"/>
        public static void Release<T>(object key) where T : UnityEngine.Object => Default.Release<T>(key);

        /// <inheritdoc cref="IAssetLoader.Release(object, Type)"/>
        public static void Release(object key, Type type) => Default.Release(key, type);

        /// <inheritdoc cref="IAssetLoader.IsLoaded{T}"/>
        public static bool IsLoaded<T>(object key) where T : UnityEngine.Object => Default.IsLoaded<T>(key);

        /// <inheritdoc cref="IAssetLoader.ReleaseAll"/>
        public static void ReleaseAll() => Default.ReleaseAll();
    }
}
