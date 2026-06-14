using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Tests
{
    /// <summary>
    /// EditMode coverage for the pool's load path. Recycle/Prewarm/ClearPool need
    /// <c>Object.Destroy</c> and <c>DontDestroyOnLoad</c>, so they live in the PlayMode tests.
    /// </summary>
    public class AddressablePrefabPoolTests
    {
        private FakeAssetLoader _loader;
        private GameObject _prefab;
        private readonly List<GameObject> _cleanup = new();

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("test-prefab");
            _cleanup.Add(_prefab);
            _loader = new FakeAssetLoader { Asset = _prefab };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _cleanup)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _cleanup.Clear();
        }

        [Test]
        public async Task GetAsync_FailedLoad_EvictsThePool_SoTheNextCallRetries()
        {
            _loader.FailuresRemaining = 1;
            var pool = new AddressablePrefabPool(_loader);

            try
            {
                await pool.GetAsync("hero");
                Assert.Fail("Expected the faked load failure to propagate.");
            }
            catch (InvalidOperationException)
            {
            }

            // Before the fix this rethrew the cached fault forever; now it retries.
            var instance = await pool.GetAsync("hero");
            _cleanup.Add(instance);

            Assert.That(instance, Is.Not.Null);
            Assert.That(_loader.Loads.Count, Is.EqualTo(2), "second GetAsync must trigger a fresh load");
            Assert.That(instance.GetComponent<PooledObject>(), Is.Not.Null);
        }

        [Test]
        public async Task GetAsync_ConcurrentCallers_ShareOneLoad()
        {
            var gate = new UniTaskCompletionSource<bool>();
            _loader.Gate = gate;
            var pool = new AddressablePrefabPool(_loader);

            var first = pool.GetAsync("hero");
            var second = pool.GetAsync("hero");
            gate.TrySetResult(true);

            var a = await first;
            var b = await second;
            _cleanup.Add(a);
            _cleanup.Add(b);

            Assert.That(_loader.Loads.Count, Is.EqualTo(1), "both callers must join the same prefab load");
            Assert.That(a, Is.Not.SameAs(b));
        }
    }
}
