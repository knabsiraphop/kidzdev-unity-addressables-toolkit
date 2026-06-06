using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace KidzDev.AddressablesToolkit
{
    internal static class _CompileCheck
    {
        public static AsyncOperationHandle<GameObject> Probe(string key)
            => Addressables.LoadAssetAsync<GameObject>(key);
    }
}