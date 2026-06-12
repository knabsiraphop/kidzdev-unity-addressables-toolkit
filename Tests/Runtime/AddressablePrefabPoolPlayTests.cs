using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Tests.Play
{
    /// <summary>Recording loader that serves a fixed prefab.</summary>
    internal sealed class FakeAssetLoader : IAssetLoader
    {
        public readonly List<(object key, Type type)> Releases = new();
        public UnityEngine.Object Asset;

        public UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default) where T : UnityEngine.Object
            => UniTask.FromResult((T)Asset);

        public void Release<T>(object key) where T : UnityEngine.Object => Releases.Add((key, typeof(T)));
        public void Release(object key, Type type) => Releases.Add((key, type));
        public bool IsLoaded<T>(object key) where T : UnityEngine.Object => false;
        public void ReleaseAll() { }
    }

    public class AddressablePrefabPoolPlayTests
    {
        private FakeAssetLoader _loader;
        private AddressablePrefabPool _pool;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("test-prefab");
            _loader = new FakeAssetLoader { Asset = _prefab };
            _pool = new AddressablePrefabPool(_loader);
        }

        [TearDown]
        public void TearDown()
        {
            _pool.ClearAll();
            if (_prefab != null) UnityEngine.Object.Destroy(_prefab);
        }

        [Test]
        public async Task Release_DeactivatesAndRecyclesTheSameInstance()
        {
            var first = await _pool.GetAsync("k");
            Assert.That(first.activeSelf, Is.True);

            _pool.Release(first);
            Assert.That(first.activeSelf, Is.False, "released instances are deactivated, not destroyed");

            var second = await _pool.GetAsync("k");
            Assert.That(second, Is.SameAs(first), "the pool must recycle the released instance");
            Assert.That(second.activeSelf, Is.True);
        }

        [Test]
        public async Task Release_DoubleRelease_IsHarmless()
        {
            var instance = await _pool.GetAsync("k");
            _pool.Release(instance);
            _pool.Release(instance); // second release of the same instance is a no-op

            var a = await _pool.GetAsync("k");
            var b = await _pool.GetAsync("k");
            Assert.That(a, Is.Not.SameAs(b), "double release must not put the instance in the queue twice");
        }

        [Test]
        public async Task Prewarm_CreatesInactiveInstances_ThatGetAsyncConsumes()
        {
            await _pool.Prewarm("k", 2);

            var a = await _pool.GetAsync("k");
            var b = await _pool.GetAsync("k");
            var markerA = a.GetComponent<PooledObject>();

            Assert.That(markerA, Is.Not.Null);
            Assert.That(a, Is.Not.SameAs(b));
        }

        [Test]
        public async Task ClearPool_DestroysInstances_AndReleasesThePrefabBorrow()
        {
            var instance = await _pool.GetAsync("k");

            _pool.ClearPool("k");
            Assert.That(_loader.Releases, Is.EqualTo(new[] { ((object)"k", typeof(GameObject)) }));

            await UniTask.DelayFrame(2); // Object.Destroy is deferred
            Assert.That(instance == null, Is.True, "ClearPool must destroy active instances");
        }

        [Test]
        public async Task Release_ForeignInstance_DestroysItInsteadOfPooling()
        {
            var foreign = new GameObject("not-from-pool");
            _pool.Release(foreign);

            await UniTask.DelayFrame(2);
            Assert.That(foreign == null, Is.True, "objects without a pool marker are destroyed");
        }
    }
}
