using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace KidzDev.Unity.AddressablesToolkit
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
        /// <summary>Create an unassigned reference — lets concrete subclasses compile without
        /// declaring a constructor (the base types only define the guid constructor).</summary>
        public ComponentReference() : base(string.Empty) { }

        public ComponentReference(string guid) : base(guid) { }

        /// <summary>Load the prefab asset and return its TComponent.</summary>
        public AsyncOperationHandle<TComponent> LoadComponentAsync()
            => Addressables.ResourceManager.CreateChainOperation(base.LoadAssetAsync<GameObject>(), ToComponent);

        /// <summary>Instantiate the prefab and return the TComponent on the instance.</summary>
        public AsyncOperationHandle<TComponent> InstantiateComponentAsync(Transform parent = null)
            => Addressables.ResourceManager.CreateChainOperation(base.InstantiateAsync(parent), ToComponent);

        /// <summary>Release an instance created via InstantiateComponentAsync.</summary>
        /// <remarks>
        /// <see cref="InstantiateComponentAsync"/> wraps the instantiation in a chain operation, which
        /// holds its own reference to the underlying instance operation. Releasing only the instance
        /// (<see cref="Addressables.ReleaseInstance(GameObject)"/>) leaves that chain reference, so the
        /// instance never reaches zero refs and is never destroyed. We therefore drop the instance
        /// <b>and</b> release the chain handle. Pass the handle returned by
        /// <see cref="InstantiateComponentAsync"/>.
        /// </remarks>
        public void ReleaseInstance(AsyncOperationHandle<TComponent> handle)
        {
            if (!handle.IsValid())
                return;
            if (handle.Result != null)
                Addressables.ReleaseInstance(handle.Result.gameObject);
            Addressables.Release(handle);
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
