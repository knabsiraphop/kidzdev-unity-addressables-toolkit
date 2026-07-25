using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// On every Addressables content build, writes a bundle-size manifest to
    /// <c>ServerData/{buildTarget}_BuildReport.json</c> — a per-bundle + total size report for
    /// build diagnostics (CI size gates, tracking size growth across builds, dashboards). Not
    /// consumed by the toolkit's runtime download flow, which sizes content on-device via
    /// <see cref="UnityEngine.AddressableAssets.Addressables.GetDownloadSizeAsync(object)"/>
    /// instead (see <c>ContentDownloader.GetDownloadSizeAsync</c>).
    /// </summary>
    /// <remarks>
    /// Bundle keys use <see cref="Path.GetFileName(string)"/> so they are correct on every
    /// build agent — a hard-coded Windows <c>'\\'</c> separator would produce empty keys on
    /// macOS/Linux build agents.
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

        private const int TopOffendersLoggedToConsole = 3;

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
                var entries = CollectBundleEntries(result.FileRegistry.GetFilePaths());
                var manifest = new Manifest
                {
                    buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    totalSize = entries.Sum(e => e.size),
                    bundles = entries
                };

                var file = WriteManifest(manifest);
                LogSummary(manifest, file);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Addressables Toolkit] Failed to write bundle-size manifest: {e}");
            }
        }

        private static List<BundleEntry> CollectBundleEntries(IEnumerable<string> builtFilePaths)
        {
            return builtFilePaths
                .Where(path => !string.IsNullOrEmpty(path)
                    && path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path))
                .Select(path => new BundleEntry
                {
                    bundle = Path.GetFileName(path), // cross-platform key
                    size = new FileInfo(path).Length
                })
                .ToList();
        }

        private static string WriteManifest(Manifest manifest)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "ServerData");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{manifest.buildTarget}_BuildReport.json");
            File.WriteAllText(file, JsonUtility.ToJson(manifest, true));
            return file;
        }

        private static void LogSummary(Manifest manifest, string file)
        {
            var largest = manifest.bundles
                .OrderByDescending(e => e.size)
                .Take(TopOffendersLoggedToConsole)
                .Select(e => $"{e.bundle} ({e.size / 1024f:0.#} KB)");

            Debug.Log(
                $"[Addressables Toolkit] Wrote bundle-size manifest ({manifest.bundles.Count} bundles, " +
                $"{manifest.totalSize} bytes) → {file}. Largest: {string.Join(", ", largest)}.");
        }
    }
}
