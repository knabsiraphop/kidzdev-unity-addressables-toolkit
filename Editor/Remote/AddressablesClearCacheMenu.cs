using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Dev convenience: clears cached Addressable bundles for the active settings' preload keys,
    /// forcing a full re-download on the next play session. Editor-only, Play mode only.
    /// </summary>
    public static class AddressablesClearCacheMenu
    {
        [MenuItem("Tools/Addressables Toolkit/Clear Cached Content", false, 2500)]
        public static void ClearCachedContent()
        {
            ClearAsync().Forget();
        }

        [MenuItem("Tools/Addressables Toolkit/Clear Cached Content", true)]
        public static bool ValidateClearCachedContent() => EditorApplication.isPlaying;

        private static async UniTaskVoid ClearAsync()
        {
            var keys = AddressablesToolkitSettings.Instance.GetPreloadKeys();
            if (keys.Count == 0)
            {
                Debug.LogWarning("[AddressablesToolkit] Clear Cached Content: no preload labels configured — nothing to clear.");
                return;
            }

            var summary = new StringBuilder();
            summary.AppendLine("[AddressablesToolkit] Clear Cached Content results:");

            foreach (var key in keys)
            {
                bool cleared;
                try
                {
                    cleared = await ContentDownloader.ClearCacheAsync(key);
                }
                catch (System.Exception ex)
                {
                    summary.AppendLine($"  '{key}': FAILED ({ex.Message})");
                    continue;
                }

                summary.AppendLine(cleared
                    ? $"  '{key}': cleared"
                    : $"  '{key}': failed");
            }

            Debug.Log(summary.ToString());
        }
    }
}
