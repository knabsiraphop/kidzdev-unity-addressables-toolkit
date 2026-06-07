using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Editor
{
    /// <summary>
    /// Addressables content build entry points. Callable from the menu or from CI via:
    ///   -executeMethod KidzDev.AddressablesToolkit.Editor.AddressablesBuilder.BuildContent
    /// Optional CLI arg: -aaProfile <ProfileName> to switch the active profile first.
    /// </summary>
    public static class AddressablesBuilder
    {
        [MenuItem("Tools/Addressables Toolkit/Build Content", false, 3000)]
        public static void BuildContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Fail("No Addressable Asset Settings found."); return; }

            ApplyProfileFromArgs(settings);
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            HandleResult(result, "Build");
        }

        [MenuItem("Tools/Addressables Toolkit/Clean Content", false, 3001)]
        public static void CleanContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Fail("No Addressable Asset Settings found."); return; }

            AddressableAssetSettings.CleanPlayerContent();
            Debug.Log("[Addressables Toolkit] Cleaned player content.");
        }

        [MenuItem("Tools/Addressables Toolkit/Build Content Update", false, 3002)]
        public static void BuildContentUpdate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Fail("No Addressable Asset Settings found."); return; }

            ApplyProfileFromArgs(settings);

            var statePath = ContentUpdateScript.GetContentStateDataPath(false);
            if (string.IsNullOrEmpty(statePath) || !System.IO.File.Exists(statePath))
            {
                Fail($"No previous content state at '{statePath}'. Run a full Build Content first.");
                return;
            }

            var result = ContentUpdateScript.BuildContentUpdate(settings, statePath);
            HandleResult(result, "Content update");
        }

        private static void ApplyProfileFromArgs(AddressableAssetSettings settings)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-aaProfile") continue;

                var name = args[i + 1];
                var id = settings.profileSettings.GetProfileId(name);
                if (string.IsNullOrEmpty(id))
                    Debug.LogWarning($"[Addressables Toolkit] Profile '{name}' not found; using active profile.");
                else
                {
                    settings.activeProfileId = id;
                    Debug.Log($"[Addressables Toolkit] Active profile set to '{name}'.");
                }
                return;
            }
        }

        private static void HandleResult(AddressablesPlayerBuildResult result, string label)
        {
            if (result != null && !string.IsNullOrEmpty(result.Error))
            {
                Fail($"{label} failed: {result.Error}");
                return;
            }

            double seconds = result != null ? result.Duration : 0;
            Debug.Log($"[Addressables Toolkit] {label} succeeded in {seconds:F1}s.");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[Addressables Toolkit] {message}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1); // non-zero exit so CI detects failure
        }
    }
}
