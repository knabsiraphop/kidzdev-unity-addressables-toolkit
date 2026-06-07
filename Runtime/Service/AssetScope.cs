using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// A disposable, lifetime-bound owner for Addressable assets and instances. Everything
    /// loaded, instantiated, or pooled through a scope is tracked and released together when
    /// the scope is disposed — so a feature/screen/scene can load freely and never leak: you
    /// release the scope, not each asset. Accepts a plain key <em>or</em> an
    /// <see cref="AssetReference"/> everywhere.
    /// </summary>
    /// <remarks>
    /// Depends on <see cref="IAssetLoader"/> and <see cref="IAssetPool"/> (defaulting to
    /// <see cref="AssetLoader.Default"/> / <see cref="AddressablePool.Default"/>) — pass fakes to
    /// unit-test the tracking/release logic in isolation. Bind a scope to a GameObject with
    /// <c>this.GetAssetScope()</c> (see <see cref="AssetScopeExtensions"/>) and it disposes
    /// automatically on destroy. Not thread-safe; use from the main thread, like the rest of
    /// Addressables.
    /// </remarks>
    public sealed class AssetScope : IDisposable
    {
        private readonly IAssetLoader _assetLoader;
        private readonly IAssetPool _assetPool;

        private readonly List<(object key, Type type)> _borrowedAssets = new();
        private readonly List<GameObject> _instances = new();
        private readonly List<GameObject> _pooledInstances = new();
        private bool _disposed;

        /// <summary>Create a scope over the process-wide default loader and pool.</summary>
        public AssetScope() : this(null, null) { }

        /// <summary>Create a scope over explicit collaborators (for DI / testing).</summary>
        public AssetScope(IAssetLoader assetLoader, IAssetPool assetPool)
        {
            _assetLoader = assetLoader ?? AssetLoader.Default;
            _assetPool = assetPool ?? AddressablePool.Default;
        }

        /// <summary>True once <see cref="Dispose"/> has run; further loads throw.</summary>
        public bool IsDisposed => _disposed;

        // --- Loading (ref-counted via the asset loader) ---------------------

        /// <summary>Load an asset, tracked for release when the scope is disposed.</summary>
        public async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            var asset = await _assetLoader.LoadAsync<T>(key, ct);

            // If the scope was disposed while the load was in flight, don't leak the borrow.
            if (_disposed)
            {
                _assetLoader.Release<T>(key);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _borrowedAssets.Add((key, typeof(T)));
            return asset;
        }

        /// <summary>Load the asset behind an <see cref="AssetReference"/>, tracked for release.</summary>
        public UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (reference == null || !reference.RuntimeKeyIsValid())
                throw new ArgumentException("AssetReference is null or unassigned.", nameof(reference));
            return LoadAsync<T>(reference.RuntimeKey, ct);
        }

        // --- Instantiating (Addressables-owned instances) -------------------

        /// <summary>Instantiate a prefab; the instance is released on dispose.</summary>
        public async UniTask<GameObject> InstantiateAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var instantiateHandle = Addressables.InstantiateAsync(key, parent);
            try
            {
                await instantiateHandle.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                if (instantiateHandle.IsValid()) Addressables.Release(instantiateHandle);
                throw;
            }

            if (instantiateHandle.Status != AsyncOperationStatus.Succeeded || instantiateHandle.Result == null)
            {
                if (instantiateHandle.IsValid()) Addressables.Release(instantiateHandle);
                throw instantiateHandle.OperationException ?? new InvalidOperationException($"Failed to instantiate '{key}'.");
            }

            var instance = instantiateHandle.Result;
            if (_disposed)
            {
                Addressables.ReleaseInstance(instance);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _instances.Add(instance);
            return instance;
        }

        /// <summary>Instantiate a prefab from an <see cref="AssetReference"/>; released on dispose.</summary>
        public UniTask<GameObject> InstantiateAsync(AssetReference reference, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (reference == null || !reference.RuntimeKeyIsValid())
                throw new ArgumentException("AssetReference is null or unassigned.", nameof(reference));
            return InstantiateAsync(reference.RuntimeKey, parent, ct);
        }

        /// <summary>Instantiate a prefab and return a component on it. Throws if absent.</summary>
        public async UniTask<T> InstantiateAsync<T>(object key, Transform parent = null, CancellationToken ct = default)
            where T : Component
        {
            var instance = await InstantiateAsync(key, parent, ct);
            var component = instance.GetComponent<T>();
            if (component == null)
            {
                ReleaseInstance(instance);
                throw new InvalidOperationException($"Instantiated prefab '{key}' has no {typeof(T).Name}.");
            }
            return component;
        }

        // --- Pooling (returned to the asset pool) ---------------------------

        /// <summary>Borrow a pooled instance; returned to its pool on dispose.</summary>
        public async UniTask<GameObject> SpawnPooledAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var instance = await _assetPool.GetAsync(key, parent, ct);

            if (_disposed)
            {
                _assetPool.Release(instance);
                throw new ObjectDisposedException(nameof(AssetScope));
            }

            _pooledInstances.Add(instance);
            return instance;
        }

        // --- Early, explicit release of a single item -----------------------

        /// <summary>Release one instantiated object before the scope is disposed.</summary>
        public void ReleaseInstance(GameObject instance)
        {
            if (instance != null && _instances.Remove(instance))
                Addressables.ReleaseInstance(instance);
        }

        /// <summary>Return one pooled instance before the scope is disposed.</summary>
        public void ReleasePooled(GameObject instance)
        {
            if (_pooledInstances.Remove(instance))
                _assetPool.Release(instance);
        }

        // --- Teardown -------------------------------------------------------

        /// <summary>Release every asset, instance, and pooled object this scope owns. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Instances and pooled objects first (they hold their own handles), then borrows.
            foreach (var instance in _instances)
                if (instance != null) Addressables.ReleaseInstance(instance);
            _instances.Clear();

            foreach (var instance in _pooledInstances)
                _assetPool.Release(instance);
            _pooledInstances.Clear();

            foreach (var (key, type) in _borrowedAssets)
                _assetLoader.Release(key, type);
            _borrowedAssets.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssetScope));
        }
    }
}
