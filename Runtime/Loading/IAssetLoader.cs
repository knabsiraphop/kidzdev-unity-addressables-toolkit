using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Reference-counted async loader for Addressable assets — the toolkit's canonical
    /// ownership model. Identical <c>(key, type)</c> borrows share one handle; the asset is
    /// released only when every borrower has released. Depend on this abstraction (rather than
    /// the static <see cref="AssetLoader"/> facade) to make consumers testable and substitutable.
    /// </summary>
    public interface IAssetLoader
    {
        /// <summary>Load (or join an in-flight load of) the asset at <paramref name="key"/>.</summary>
        UniTask<T> LoadAsync<T>(object key, CancellationToken ct = default) where T : UnityEngine.Object;

        /// <summary>Release one borrow of <c>(key, T)</c>. The handle frees at zero refs.</summary>
        void Release<T>(object key) where T : UnityEngine.Object;

        /// <summary>Non-generic release of one borrow of <c>(key, type)</c>, for callers that
        /// only know the asset's <see cref="Type"/> at runtime (e.g. <see cref="AssetScope"/>).</summary>
        void Release(object key, Type type);

        /// <summary>True if <c>(key, T)</c> currently has a cached handle.</summary>
        bool IsLoaded<T>(object key) where T : UnityEngine.Object;

        /// <summary>Force-release every cached handle regardless of ref count.</summary>
        void ReleaseAll();
    }
}
