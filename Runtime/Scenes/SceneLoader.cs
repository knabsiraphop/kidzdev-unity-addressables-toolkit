using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Addressable scene load/unload with additive-scene tracking.
    /// </summary>
    /// <remarks>
    /// Unlike the drx reference, this does <b>not</b> force
    /// <c>Resources.UnloadUnusedAssets()</c> + <c>GC.Collect()</c> on every unload (a
    /// main-thread hitch). Pass <paramref name="heavyUnload"/>:true to opt in for large
    /// scene swaps where reclaiming memory is worth the stall.
    /// </remarks>
    public static class SceneLoader
    {
        private static readonly Dictionary<object, SceneInstance> _additive = new();

        /// <summary>Load an Addressable scene (single or additive).</summary>
        public static async UniTask<SceneInstance> LoadAsync(
            object key,
            LoadSceneMode mode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            CancellationToken ct = default)
        {
            var handle = Addressables.LoadSceneAsync(key, mode, activateOnLoad);
            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw handle.OperationException ?? new InvalidOperationException($"Failed to load scene '{key}'.");

            var instance = handle.Result;
            if (mode == LoadSceneMode.Additive)
                _additive[key] = instance;
            return instance;
        }

        /// <summary>
        /// Unload an additively loaded scene tracked by <see cref="LoadAsync"/>.
        /// No-op if the key was not additively loaded.
        /// </summary>
        public static async UniTask UnloadAsync(object key, bool heavyUnload = false, CancellationToken ct = default)
        {
            if (!_additive.TryGetValue(key, out var instance))
                return;
            _additive.Remove(key);

            var handle = Addressables.UnloadSceneAsync(instance);
            await handle.ToUniTask(cancellationToken: ct);

            if (heavyUnload)
            {
                await Resources.UnloadUnusedAssets().ToUniTask();
                GC.Collect();
            }
        }

        public static bool IsAdditiveLoaded(object key) => _additive.ContainsKey(key);
    }
}
