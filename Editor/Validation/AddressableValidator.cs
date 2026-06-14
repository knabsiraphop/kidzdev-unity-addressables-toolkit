using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using CompilationAssembly = UnityEditor.Compilation.Assembly;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>Scans Addressable entries for duplicate addresses and missing assets.</summary>
    public static class AddressableValidator
    {
        [MenuItem("Tools/Addressables Toolkit/Validate Addressables", false, 2000)]
        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Toolkit] No Addressable Asset Settings found.");
                return;
            }

            var byAddress = new Dictionary<string, List<string>>();
            int total = 0, missing = 0;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    total++;
                    if (string.IsNullOrEmpty(entry.AssetPath) || AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath) == null)
                    {
                        Debug.LogWarning($"[Addressables Toolkit] Missing asset for entry '{entry.address}' in group '{group.Name}'.");
                        missing++;
                    }

                    if (!byAddress.TryGetValue(entry.address, out var groups))
                        byAddress[entry.address] = groups = new List<string>();
                    groups.Add(group.Name);
                }
            }

            int duplicates = 0;
            foreach (var pair in byAddress)
            {
                if (pair.Value.Count > 1)
                {
                    duplicates++;
                    Debug.LogWarning($"[Addressables Toolkit] Duplicate address '{pair.Key}' used by {pair.Value.Count} entries (groups: {string.Join(", ", pair.Value)}).");
                }
            }

            Debug.Log($"[Addressables Toolkit] Validation done — {total} entries, {missing} missing, {duplicates} duplicate address(es).");
        }

        /// <summary>
        /// Flags an unguarded <c>using UnityEditor;</c> inside a <b>player</b> (runtime)
        /// assembly — the High-severity bug both reference systems shipped (drx#1 /
        /// myworld#8). <c>UnityEditor</c> does not exist in player builds, so an
        /// unguarded directive is dead at best and a player-build compile error at worst.
        /// Unity package assemblies are skipped (not actionable from this project).
        /// </summary>
        [MenuItem("Tools/Addressables Toolkit/Validate Runtime Editor Usage", false, 2001)]
        public static void ValidateRuntimeEditorUsage()
        {
            int flagged = 0, scanned = 0;

            // Read-only packages (registry/git/built-in) aren't ours to fix — skip them.
            // Only Assets/ and embedded/local packages are actionable in this project.
            var readOnlyPrefixes = new List<string>();
            foreach (var pkg in PackageInfo.GetAllRegisteredPackages())
            {
                if (pkg.source == PackageSource.Embedded || pkg.source == PackageSource.Local)
                    continue;
                readOnlyPrefixes.Add(pkg.assetPath.Replace('\\', '/').TrimEnd('/') + "/");
            }

            foreach (CompilationAssembly asm in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                foreach (var src in asm.sourceFiles)
                {
                    if (string.IsNullOrEmpty(src) || !src.EndsWith(".cs"))
                        continue;

                    var normalized = src.Replace('\\', '/');
                    if (IsUnderReadOnlyPackage(normalized, readOnlyPrefixes))
                        continue;
                    if (!File.Exists(src))
                        continue;

                    scanned++;
                    if (HasUnguardedEditorUsing(src, out int lineNumber))
                    {
                        flagged++;
                        Debug.LogWarning($"[Addressables Toolkit] Unguarded 'using UnityEditor' in player assembly '{asm.name}': {src}:{lineNumber}. Wrap it in #if UNITY_EDITOR … #endif.");
                    }
                }
            }

            Debug.Log($"[Addressables Toolkit] Runtime editor-usage scan done — {scanned} player file(s), {flagged} unguarded UnityEditor using(s).");
        }

        private static bool IsUnderReadOnlyPackage(string normalizedPath, List<string> readOnlyPrefixes)
        {
            foreach (var prefix in readOnlyPrefixes)
                if (normalizedPath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // Heuristic: treats any enclosing #if block whose condition mentions UNITY_EDITOR
        // as a valid guard. Good enough for the common one-line top-of-file directive.
        private static bool HasUnguardedEditorUsing(string file, out int lineNumber)
        {
            lineNumber = 0;
            var lines = File.ReadAllLines(file);
            var guardStack = new Stack<bool>();

            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("#if"))
                {
                    guardStack.Push(t.Contains("UNITY_EDITOR"));
                }
                else if (t.StartsWith("#endif"))
                {
                    if (guardStack.Count > 0) guardStack.Pop();
                }
                else if (t.StartsWith("using UnityEditor") && t.Contains(";"))
                {
                    bool guarded = false;
                    foreach (var isEditorGuard in guardStack)
                        if (isEditorGuard) { guarded = true; break; }

                    if (!guarded)
                    {
                        lineNumber = i + 1;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
