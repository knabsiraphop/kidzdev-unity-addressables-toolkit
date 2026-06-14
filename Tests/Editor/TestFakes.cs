using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Tests
{
    /// <summary>A loadable dummy asset for fake-loader tests.</summary>
    internal sealed class TestAsset : ScriptableObject { }

    /// <summary>
    /// Controllable <see cref="IAssetLoader"/>: records every load/release, can serve a fixed
    /// asset, gate completion on a <see cref="UniTaskCompletionSource{T}"/>, and fail the first
    /// N loads to exercise retry paths.
    /// </summary>
    internal sealed class FakeAssetLoader : IAssetLoader
    {
        public readonly List<(object key, Type type)> Loads = new();
        public readonly List<(object key, Type type)> Releases = new();

        /// <summary>Asset returned by every load (must be castable to the requested type).</summary>
        public UnityEngine.Object Asset;

        /// <summary>When set, loads await this gate before completing.</summary>
        public UniTaskCompletionSource<bool> Gate;

        /// <summary>Loads fail with <see cref="InvalidOperationException"/> while &gt; 0 (decremented per failure).</summary>
        public int FailuresRemaining;

        public async UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default) where T : UnityEngine.Object
        {
            Loads.Add((key, typeof(T)));
            if (Gate != null)
                await Gate.Task;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException($"Fake load failure for '{key}'.");
            }
            return (T)Asset;
        }

        public void Release<T>(object key) where T : UnityEngine.Object => Releases.Add((key, typeof(T)));
        public void Release(object key, Type type) => Releases.Add((key, type));
        public bool IsLoaded<T>(object key) where T : UnityEngine.Object => false;
        public void ReleaseAll() { }
    }

    /// <summary>Recording <see cref="IAssetPool"/> that spawns plain GameObjects.</summary>
    internal sealed class FakeAssetPool : IAssetPool
    {
        public readonly List<GameObject> Spawned = new();
        public readonly List<GameObject> Released = new();

        public UniTask<GameObject> GetAsync(object key, Transform parent = null, CancellationToken ct = default)
        {
            var instance = new GameObject($"fake-pooled:{key}");
            Spawned.Add(instance);
            return UniTask.FromResult(instance);
        }

        public void Release(GameObject instance) => Released.Add(instance);
        public UniTask Prewarm(object key, int count, CancellationToken ct = default) => UniTask.CompletedTask;
        public void ClearPool(object key) { }
        public void ClearAll() { }

        public void DestroySpawned()
        {
            foreach (var instance in Spawned)
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            Spawned.Clear();
        }
    }
}
