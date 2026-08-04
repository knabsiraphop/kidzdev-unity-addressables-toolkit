using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Pure, stateless helpers behind <see cref="AddressablesBuildWindow"/> — every method takes
    /// its inputs as parameters rather than holding state, so nothing here is coupled to the
    /// window's field lifecycle (and it's callable directly from EditMode tests).
    /// </summary>
    internal static class AddressablesBuildOperations
    {
        /// <summary>The Local.BuildPath/Local.LoadPath or Remote.BuildPath/Remote.LoadPath
        /// profile variable names for the given mode.</summary>
        internal static (string BuildPathVar, string LoadPathVar) GetPathVariableNames(ContentSource mode)
        {
            return mode == ContentSource.Local
                ? (AddressableAssetSettings.kLocalBuildPath, AddressableAssetSettings.kLocalLoadPath)
                : (AddressableAssetSettings.kRemoteBuildPath, AddressableAssetSettings.kRemoteLoadPath);
        }

        /// <summary>The baseline's expected BundleNaming for the given mode, or null if there's
        /// no baseline asset.</summary>
        internal static BundledAssetGroupSchema.BundleNamingStyle? GetExpectedBundleNaming(AddressablesGroupSchemaBaseline baseline, ContentSource mode)
        {
            if (baseline == null) return null;
            return mode == ContentSource.Local ? baseline.LocalDefaults.BundleNamingMode : baseline.RemoteDefaults.BundleNamingMode;
        }

        /// <summary>Every group's <see cref="BundledAssetGroupSchema"/> in <paramref name="settings"/>,
        /// skipping groups with none. Use <c>schema.Group.Name</c> for the owning group's name.</summary>
        internal static IEnumerable<BundledAssetGroupSchema> EnumerateBundledSchemas(AddressableAssetSettings settings)
        {
            if (settings == null) yield break;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null) yield return schema;
            }
        }

        /// <summary>Non-mutating: evaluates what the given mode's Build Path profile variable
        /// resolves to right now, without touching any group's schema. Used for "Open Build
        /// Folder" before any build has run this session, and as the fallback when no group has
        /// a BundledAssetGroupSchema.</summary>
        internal static string ResolveBuildFolder(AddressableAssetSettings settings, ContentSource mode)
        {
            if (settings == null) return null;

            var (buildPathVar, _) = GetPathVariableNames(mode);
            var probe = new ProfileValueReference();
            if (!probe.SetVariableByName(settings, buildPathVar)) return null;

            var relativePath = probe.GetValue(settings);
            return string.IsNullOrEmpty(relativePath) ? null : ToAbsolutePath(relativePath);
        }

        internal static string ToAbsolutePath(string projectRelativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot) ? null : Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        /// <summary>Informational preview of what <see cref="AddressablesBuildOverrideScope"/> will
        /// force onto every group for the given mode — shown alongside the drift check so the
        /// forced (unchecked) fields aren't invisible.</summary>
        internal static List<string> BuildForcedValuesPreview(AddressableAssetSettings settings, ContentSource mode, AddressablesGroupSchemaBaseline baseline)
        {
            var lines = new List<string>();
            if (settings == null) return lines;

            var namingText = GetExpectedBundleNaming(baseline, mode)?.ToString() ?? "no baseline asset selected";
            var (buildPathVar, loadPathVar) = GetPathVariableNames(mode);

            foreach (var schema in EnumerateBundledSchemas(settings))
                lines.Add($"{schema.Group.Name}: BundleNaming -> {namingText}; BuildPath -> {buildPathVar}; LoadPath -> {loadPathVar}");

            return lines;
        }
    }
}
