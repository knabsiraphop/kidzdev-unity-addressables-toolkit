using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Tests.Play
{
    public class AssetScopeBinderPlayTests
    {
        [Test]
        public void GetAssetScope_ReturnsTheSameScopeForTheSameGameObject()
        {
            var go = new GameObject("scope-host");
            try
            {
                var scope = go.GetAssetScope();
                Assert.That(go.GetAssetScope(), Is.SameAs(scope));

                var collider = go.AddComponent<BoxCollider>();
                Assert.That(collider.GetAssetScope(), Is.SameAs(scope), "component overload must reach the same scope");
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [Test]
        public async Task DestroyingTheGameObject_DisposesItsScope()
        {
            var go = new GameObject("scope-host");
            var scope = go.GetAssetScope();
            Assert.That(scope.IsDisposed, Is.False);

            Object.Destroy(go);
            await UniTask.DelayFrame(2); // OnDestroy runs when the deferred destroy lands

            Assert.That(scope.IsDisposed, Is.True, "the binder must dispose the scope on destroy");
        }
    }
}
