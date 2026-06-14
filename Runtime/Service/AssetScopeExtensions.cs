using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Convenience accessors for a GameObject-bound <see cref="AssetScope"/>. The returned
    /// scope is disposed automatically when the GameObject is destroyed, so assets loaded
    /// through it cannot outlive the object that needed them.
    /// </summary>
    public static class AssetScopeExtensions
    {
        /// <summary>
        /// Get (or create) the <see cref="AssetScope"/> bound to this GameObject's lifetime.
        /// Repeated calls on the same GameObject return the same scope.
        /// </summary>
        public static AssetScope GetAssetScope(this GameObject go)
        {
            if (go == null)
                throw new System.ArgumentNullException(nameof(go));

            var binder = go.GetComponent<AssetScopeBinder>();
            if (binder == null)
                binder = go.AddComponent<AssetScopeBinder>();
            return binder.Scope;
        }

        /// <summary>
        /// Get (or create) the <see cref="AssetScope"/> bound to this component's GameObject —
        /// e.g. <c>this.GetAssetScope().LoadAsync&lt;Sprite&gt;("icon")</c> from a MonoBehaviour.
        /// </summary>
        public static AssetScope GetAssetScope(this Component component)
        {
            if (component == null)
                throw new System.ArgumentNullException(nameof(component));
            return component.gameObject.GetAssetScope();
        }
    }
}
