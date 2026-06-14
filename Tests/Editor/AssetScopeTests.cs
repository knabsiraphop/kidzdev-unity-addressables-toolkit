using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace KidzDev.Unity.AddressablesToolkit.Tests
{
    public class AssetScopeTests
    {
        private FakeAssetLoader _loader;
        private FakeAssetPool _pool;
        private TestAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<TestAsset>();
            _loader = new FakeAssetLoader { Asset = _asset };
            _pool = new FakeAssetPool();
        }

        [TearDown]
        public void TearDown()
        {
            _pool.DestroySpawned();
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
        }

        [Test]
        public async Task LoadAsync_TracksEveryBorrow_AndDisposeReleasesThemAll()
        {
            var scope = new AssetScope(_loader, _pool);
            await scope.LoadAsync<TestAsset>("a");
            await scope.LoadAsync<TestAsset>("a"); // same key twice = two borrows
            await scope.LoadAsync<TestAsset>("b");

            Assert.That(_loader.Releases, Is.Empty);
            scope.Dispose();

            Assert.That(_loader.Releases, Is.EquivalentTo(new[]
            {
                ((object)"a", typeof(TestAsset)),
                ((object)"a", typeof(TestAsset)),
                ((object)"b", typeof(TestAsset)),
            }));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var scope = new AssetScope(_loader, _pool);
            scope.Dispose();
            scope.Dispose();
            Assert.That(scope.IsDisposed, Is.True);
            Assert.That(_loader.Releases, Is.Empty);
        }

        [Test]
        public void LoadAsync_AfterDispose_Throws()
        {
            var scope = new AssetScope(_loader, _pool);
            scope.Dispose();
            // Async method: the exception lands in the task, so assert on the awaited result.
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await scope.LoadAsync<TestAsset>("a"));
        }

        [Test]
        public void LoadAsync_UnassignedAssetReference_ThrowsArgumentException()
        {
            var scope = new AssetScope(_loader, _pool);
            Assert.Throws<ArgumentException>(() => scope.LoadAsync<TestAsset>(new AssetReference()).Forget());
            Assert.Throws<ArgumentException>(() => scope.LoadAsync<TestAsset>((AssetReference)null).Forget());
        }

        [Test]
        public async Task Dispose_DuringInFlightLoad_ReleasesTheBorrowAndThrows()
        {
            var gate = new UniTaskCompletionSource<bool>();
            _loader.Gate = gate;

            var scope = new AssetScope(_loader, _pool);
            var pending = scope.LoadAsync<TestAsset>("late");
            scope.Dispose();
            gate.TrySetResult(true);

            try
            {
                await pending;
                Assert.Fail("Expected ObjectDisposedException.");
            }
            catch (ObjectDisposedException)
            {
            }

            // The in-flight borrow must not leak: it is released as soon as the load lands.
            Assert.That(_loader.Releases, Is.EqualTo(new[] { ((object)"late", typeof(TestAsset)) }));
        }

        [Test]
        public async Task SpawnPooledAsync_DisposeReturnsInstancesToPool()
        {
            var scope = new AssetScope(_loader, _pool);
            var first = await scope.SpawnPooledAsync("enemy");
            var second = await scope.SpawnPooledAsync("enemy");

            scope.Dispose();

            Assert.That(_pool.Released, Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public async Task ReleasePooled_Early_IsNotReleasedAgainOnDispose()
        {
            var scope = new AssetScope(_loader, _pool);
            var instance = await scope.SpawnPooledAsync("enemy");

            scope.ReleasePooled(instance);
            scope.Dispose();

            Assert.That(_pool.Released, Is.EqualTo(new[] { instance }));
        }
    }
}
