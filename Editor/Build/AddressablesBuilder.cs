using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>Structured result of <see cref="AddressablesBuilder.BuildContentCore"/>.</summary>
    internal readonly struct BuildContentOutcome
    {
        public readonly bool Success;
        public readonly string Error;
        public readonly double Duration;
        public readonly string OutputPath;
        public readonly IReadOnlyList<string> CreatedFiles;

        private BuildContentOutcome(bool success, string error, double duration, string outputPath, IReadOnlyList<string> createdFiles)
        {
            Success = success;
            Error = error;
            Duration = duration;
            OutputPath = outputPath;
            CreatedFiles = createdFiles ?? Array.Empty<string>();
        }

        public static BuildContentOutcome Failed(string error) => new BuildContentOutcome(false, error, 0, null, null);

        public static BuildContentOutcome From(AddressablesPlayerBuildResult result, IReadOnlyList<string> createdFiles)
        {
            if (result != null && !string.IsNullOrEmpty(result.Error))
                return new BuildContentOutcome(false, result.Error, result.Duration, result.OutputPath, createdFiles);

            return new BuildContentOutcome(true, null, result?.Duration ?? 0, result?.OutputPath, createdFiles);
        }
    }

    /// <summary>
    /// Addressables content build entry points. No menu items of their own — reachable from
    /// Tools &gt; Addressables Toolkit &gt; Build Addressables... (<see cref="AddressablesBuildWindow"/>),
    /// or from CI via:
    ///   -executeMethod KidzDev.Unity.AddressablesToolkit.Editor.AddressablesBuilder.BuildContent
    /// Optional CLI arg: -aaProfile <ProfileName> to switch the active profile first.
    /// </summary>
    public static class AddressablesBuilder
    {
        public static void BuildContent() => BuildContentCore();

        /// <summary>Same build as <see cref="BuildContent"/>, returning a structured outcome
        /// for callers that need it (e.g. <see cref="AddressablesBuildWindow"/>).</summary>
        internal static BuildContentOutcome BuildContentCore()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                const string message = "No Addressable Asset Settings found.";
                Fail(message);
                return BuildContentOutcome.Failed(message);
            }

            ApplyProfileFromArgs(settings);

            var packedModeIndex = settings.DataBuilders.FindIndex(db => db is BuildScriptPackedMode);
            if (packedModeIndex < 0)
            {
                const string message = "No BuildScriptPackedMode data builder found in Addressable Asset Settings.";
                Fail(message);
                return BuildContentOutcome.Failed(message);
            }

            var originalIndex = settings.ActivePlayerDataBuilderIndex;
            settings.ActivePlayerDataBuilderIndex = packedModeIndex;
            try
            {
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                var createdFiles = GetCreatedFiles(result);
                HandleResult(result, "Build", createdFiles);
                return BuildContentOutcome.From(result, createdFiles);
            }
            finally
            {
                settings.ActivePlayerDataBuilderIndex = originalIndex;
            }
        }

        public static void CleanContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Fail("No Addressable Asset Settings found."); return; }

            AddressableAssetSettings.CleanPlayerContent();
            Debug.Log("[Addressables Toolkit] Cleaned player content.");
        }

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
            HandleResult(result, "Content update", GetCreatedFiles(result));
        }

        private static void ApplyProfileFromArgs(AddressableAssetSettings settings)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-aaProfile") continue;

                ApplyProfileByName(settings, args[i + 1]);
                return;
            }
        }

        /// <summary>Switches the active Addressables profile by name. Returns false (and logs a
        /// warning, keeping the current active profile) if no profile with that name exists.</summary>
        internal static bool ApplyProfileByName(AddressableAssetSettings settings, string profileName)
        {
            var id = settings.profileSettings.GetProfileId(profileName);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[Addressables Toolkit] Profile '{profileName}' not found; using active profile.");
                return false;
            }

            settings.activeProfileId = id;
            Debug.Log($"[Addressables Toolkit] Active profile set to '{profileName}'.");
            return true;
        }

        /// <summary>Silent existence check for <paramref name="profileName"/> — for callers (e.g.
        /// <c>AddressablesBuildOverrideScope</c>) that want to try a profile opportunistically
        /// without <see cref="ApplyProfileByName"/>'s not-found warning firing on every call when
        /// the profile is expected not to exist most of the time.</summary>
        internal static bool ProfileExists(AddressableAssetSettings settings, string profileName)
        {
            return !string.IsNullOrEmpty(settings.profileSettings.GetProfileId(profileName));
        }

        private static void HandleResult(AddressablesPlayerBuildResult result, string label, IReadOnlyList<string> createdFiles)
        {
            if (result != null && !string.IsNullOrEmpty(result.Error))
            {
                Fail($"{label} failed: {result.Error}");
                return;
            }

            double seconds = result != null ? result.Duration : 0;
            Debug.Log($"[Addressables Toolkit] {label} succeeded in {seconds:F1}s.");

            if (createdFiles.Count > 0)
                Debug.Log($"[Addressables Toolkit] {createdFiles.Count} file(s) created/updated:\n{string.Join("\n", createdFiles)}");
        }

        private static List<string> GetCreatedFiles(AddressablesPlayerBuildResult result)
        {
            var paths = result?.FileRegistry?.GetFilePaths();
            if (paths == null) return new List<string>();

            var list = new List<string>(paths);
            list.Sort();
            return list;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[Addressables Toolkit] {message}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1); // non-zero exit so CI detects failure
        }
    }
}
