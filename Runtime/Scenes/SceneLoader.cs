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
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                // The load continues in the background; unload it once it lands so an
                // abandoned scene doesn't stay resident untracked.
                UnloadAbandonedAsync(handle).Forget();
                throw;
            }
            catch
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                throw handle.OperationException ?? new InvalidOperationException($"Failed to load scene '{key}'.");
            }

            var instance = handle.Result;
            if (mode == LoadSceneMode.Single)
            {
                // A Single load destroyed every other scene — drop the now-dead additive entries.
                _additive.Clear();
            }
            else
            {
                if (_additive.ContainsKey(key))
                    Debug.LogWarning($"[AddressablesToolkit] Scene '{key}' additively loaded again; only the newest instance is tracked for UnloadAsync.");
                _additive[key] = instance;
            }
            return instance;
        }

        private static async UniTaskVoid UnloadAbandonedAsync(AsyncOperationHandle<SceneInstance> handle)
        {
            try
            {
                await handle.ToUniTask();
            }
            catch
            {
                if (handle.IsValid()) Addressables.Release(handle);
                return;
            }

            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                await Addressables.UnloadSceneAsync(handle).ToUniTask();
            else if (handle.IsValid())
                Addressables.Release(handle);
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

        /// <summary>Drop all additive tracking (play-mode restarts without domain reload).</summary>
        internal static void ResetTracking() => _additive.Clear();
    }
}
