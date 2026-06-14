using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Holds an <see cref="AssetScope"/> and disposes it when the GameObject is destroyed.
    /// Added automatically by <see cref="AssetScopeExtensions.GetAssetScope(Component)"/>;
    /// you rarely add it by hand.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // hidden from the Add Component menu — managed via GetAssetScope()
    public sealed class AssetScopeBinder : MonoBehaviour
    {
        /// <summary>The scope whose lifetime is tied to this GameObject.</summary>
        public AssetScope Scope { get; } = new AssetScope();

        private void OnDestroy() => Scope.Dispose();
    }
}
