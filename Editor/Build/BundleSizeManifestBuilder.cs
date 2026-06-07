using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Editor
{
    /// <summary>
    /// On every Addressables content build, writes a bundle-size manifest to
    /// <c>ServerData/{buildTarget}_BundleSize.json</c>. A remote-update flow can fetch
    /// this small file to estimate total download size before pulling bundles (the
    /// pattern the myworld reference used).
    /// </summary>
    /// <remarks>
    /// The bundle key is taken with <see cref="Path.GetFileName(string)"/> so it is
    /// correct on every build agent. The myworld reference hard-coded the Windows
    /// <c>'\\'</c> separator (its bug #7), producing empty bundle keys — and therefore
    /// wrong runtime size estimates — on macOS/Linux Android build agents.
    /// </remarks>
    [InitializeOnLoad]
    public static class BundleSizeManifestBuilder
    {
        [Serializable]
        public struct BundleEntry
        {
            public string bundle;
            public long size;
        }

        [Serializable]
        private struct Manifest
        {
            public string buildTarget;
            public long totalSize;
            public List<BundleEntry> bundles;
        }

        static BundleSizeManifestBuilder()
        {
            BuildScript.buildCompleted -= OnBuildCompleted;
            BuildScript.buildCompleted += OnBuildCompleted;
        }

        // BuildScript.buildCompleted is Action<AddressableAssetBuildResult>; the file
        // registry lives on the AddressablesPlayerBuildResult subtype.
        private static void OnBuildCompleted(AddressableAssetBuildResult buildResult)
        {
            if (buildResult is not AddressablesPlayerBuildResult result
                || !string.IsNullOrEmpty(result.Error)
                || result.FileRegistry == null)
                return;

            try
            {
                var entries = new List<BundleEntry>();
                long total = 0;

                foreach (var path in result.FileRegistry.GetFilePaths())
                {
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!File.Exists(path))
                        continue;

                    long size = new FileInfo(path).Length;
                    entries.Add(new BundleEntry { bundle = Path.GetFileName(path), size = size }); // cross-platform key
                    total += size;
                }

                var manifest = new Manifest
                {
                    buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    totalSize = total,
                    bundles = entries
                };

                var dir = Path.Combine(Directory.GetCurrentDirectory(), "ServerData");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"{manifest.buildTarget}_BundleSize.json");
                File.WriteAllText(file, JsonUtility.ToJson(manifest, true));

                Debug.Log($"[Addressables Toolkit] Wrote bundle-size manifest ({entries.Count} bundles, {total} bytes) → {file}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Addressables Toolkit] Failed to write bundle-size manifest: {e}");
            }
        }
    }
}
