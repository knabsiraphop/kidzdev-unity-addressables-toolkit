using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Samples
{
    /// <summary>
    /// Minimal runnable demo (loader + pool + download + remote update). Mark a prefab
    /// addressable, set its address below, then press Play. See README for setup steps.
    /// Uses UniTask throughout, matching the toolkit's async surface.
    /// </summary>
    public class AddressablesToolkitDemo : MonoBehaviour
    {
        [Header("Set to an address that exists in your Addressables groups")]
        [SerializeField] private string prefabAddress = "demo-prefab";

        private async UniTaskVoid Start()
        {
            try
            {
                // 1) AssetLoader — reference-counted load (cache keyed by address + type).
                var prefab = await AssetLoader.LoadAsync<GameObject>(prefabAddress);
                var single = Instantiate(prefab, new Vector3(-2f, 0f, 0f), Quaternion.identity);
                Debug.Log($"[Demo] Loaded and instantiated '{prefabAddress}'.");

                // 2) AddressablePool — pooled instances reuse the same prefab handle.
                await AddressablePool.Prewarm(prefabAddress, 3);
                for (int i = 0; i < 3; i++)
                {
                    var pooled = await AddressablePool.GetAsync(prefabAddress);
                    pooled.transform.position = new Vector3(i, 0f, 0f);
                }
                Debug.Log("[Demo] Spawned 3 instances from the pool.");

                // 3) DownloadHelper — bytes still to download (0 for local content).
                long size = await DownloadHelper.GetDownloadSizeAsync(prefabAddress);
                Debug.Log($"[Demo] Download size for '{prefabAddress}': {size} bytes (0 = local).");

                // 4) RemoteContentUpdater — full check → update → size → confirm → download.
                //    Returns NoUpdate for local content; downloads against a real CDN.
                var progress = new Progress<DownloadProgress>(p => Debug.Log($"[Demo] {p.Percent:P0}"));
                DownloadResult result = await RemoteContentUpdater.RunAsync(
                    labels: new object[] { prefabAddress },
                    progress: progress,
                    confirm: bytes => UniTask.FromResult(true)); // auto-confirm in the demo
                Debug.Log($"[Demo] Update result: {result.Outcome} ({result.Bytes} bytes).");

                Destroy(single);
                AssetLoader.Release<GameObject>(prefabAddress);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Demo] Failed — is '{prefabAddress}' a valid addressable address? {e.Message}");
            }
        }

        private void OnDestroy() => AddressablePool.ClearPool(prefabAddress);
    }
}
