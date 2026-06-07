using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// A serializable AssetReference restricted to prefabs that contain a TComponent.
    /// Create a concrete subclass to use it in the inspector:
    ///   [Serializable] public class EnemyReference : ComponentReference&lt;Enemy&gt; { }
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AssetLoader"/>, the handles returned here are <b>caller-owned</b>:
    /// they are not ref-counted by the toolkit. Keep the handle and call
    /// <see cref="ReleaseInstance"/> (for instantiated objects) or
    /// <c>Addressables.Release(handle)</c> (for loaded assets) when done. The handle is
    /// awaitable directly (<c>await handle</c> or <c>await handle.ToUniTask()</c>).
    /// </remarks>
    [Serializable]
    public class ComponentReference<TComponent> : AssetReferenceGameObject
        where TComponent : Component
    {
        public ComponentReference(string guid) : base(guid) { }

        /// <summary>Load the prefab asset and return its TComponent.</summary>
        public AsyncOperationHandle<TComponent> LoadComponentAsync()
            => Addressables.ResourceManager.CreateChainOperation(base.LoadAssetAsync<GameObject>(), ToComponent);

        /// <summary>Instantiate the prefab and return the TComponent on the instance.</summary>
        public AsyncOperationHandle<TComponent> InstantiateComponentAsync(Transform parent = null)
            => Addressables.ResourceManager.CreateChainOperation(base.InstantiateAsync(parent), ToComponent);

        /// <summary>Release an instance created via InstantiateComponentAsync.</summary>
        public void ReleaseInstance(AsyncOperationHandle<TComponent> handle)
        {
            if (handle.IsValid() && handle.Result != null)
                Addressables.ReleaseInstance(handle.Result.gameObject);
        }

        private AsyncOperationHandle<TComponent> ToComponent(AsyncOperationHandle<GameObject> handle)
        {
            var go = handle.Result;
            var component = go != null ? go.GetComponent<TComponent>() : null;
            return Addressables.ResourceManager.CreateCompletedOperation(
                component,
                component != null ? string.Empty : $"Prefab '{go}' has no {typeof(TComponent).Name}.");
        }

#if UNITY_EDITOR
        public override bool ValidateAsset(Object obj)
        {
            var go = obj as GameObject;
            return go != null && go.GetComponent<TComponent>() != null;
        }

        public override bool ValidateAsset(string mainAssetPath)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(mainAssetPath);
            return go != null && go.GetComponent<TComponent>() != null;
        }
#endif
    }
}
