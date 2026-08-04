using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Warns when a group's schema field matches neither the Local nor the Remote baseline
    /// from the project's <see cref="AddressablesGroupSchemaBaseline"/> asset. Read-only —
    /// never mutates a group's schema.
    /// </summary>
    public static class AddressablesGroupSchemaValidator
    {
        /// <summary>Returns drift messages instead of logging them — used by
        /// <see cref="AddressablesBuildWindow"/>'s "Check Group Schemas" button.</summary>
        internal static List<string> CollectMessages() => CollectMessages(null);

        /// <summary>Same check, using <paramref name="baselineOverride"/> instead of auto-finding
        /// the one baseline asset in the project — used by <see cref="AddressablesBuildWindow"/>
        /// when the user has explicitly picked a baseline asset in the window.</summary>
        internal static List<string> CollectMessages(AddressablesGroupSchemaBaseline baselineOverride)
        {
            var messages = new List<string>();

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                messages.Add("No Addressable Asset Settings found.");
                return messages;
            }

            AddressablesGroupSchemaBaseline baseline;
            if (baselineOverride != null)
                baseline = baselineOverride;
            else if (!TryLoadBaseline(out baseline, messages))
                return messages;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                var bundled = group.GetSchema<BundledAssetGroupSchema>();
                if (bundled != null)
                    CompareBundledSchema(group.Name, bundled, baseline.LocalDefaults, baseline.RemoteDefaults, messages);

                var contentUpdate = group.GetSchema<ContentUpdateGroupSchema>();
                if (contentUpdate != null)
                    CompareContentUpdateSchema(group.Name, contentUpdate, baseline.LocalDefaults, baseline.RemoteDefaults, messages);
            }

            return messages;
        }

        private static bool TryLoadBaseline(out AddressablesGroupSchemaBaseline baseline, List<string> messages)
        {
            if (AddressablesGroupSchemaBaseline.TryFindSingle(out baseline, out var error))
                return true;

            Debug.LogError($"[Addressables Toolkit] {error}");
            messages.Add(error);
            return false;
        }

        private static void CompareBundledSchema(string groupName, BundledAssetGroupSchema schema, SchemaDefaults local, SchemaDefaults remote, List<string> messages)
        {
            // BundleNamingMode is deliberately not checked here — AddressablesBuildWindow forces
            // it to the selected mode's value for the duration of every build instead, so drift
            // on this one field is never meaningful to report.
            CompareField(groupName, nameof(SchemaDefaults.AssetBundleCompression), schema.Compression, local.AssetBundleCompression, remote.AssetBundleCompression, messages);
            CompareField(groupName, nameof(SchemaDefaults.AssetBundleCrc), ToCrcMode(schema.UseAssetBundleCrc, schema.UseAssetBundleCrcForCachedBundles), local.AssetBundleCrc, remote.AssetBundleCrc, messages);
            CompareField(groupName, nameof(SchemaDefaults.CacheClearBehavior), schema.AssetBundledCacheClearBehavior, local.CacheClearBehavior, remote.CacheClearBehavior, messages);
            CompareField(groupName, nameof(SchemaDefaults.IncludeInBuild), schema.IncludeInBuild, local.IncludeInBuild, remote.IncludeInBuild, messages);
            CompareField(groupName, nameof(SchemaDefaults.UseAssetBundleCache), schema.UseAssetBundleCache, local.UseAssetBundleCache, remote.UseAssetBundleCache, messages);
        }

        private static void CompareContentUpdateSchema(string groupName, ContentUpdateGroupSchema schema, SchemaDefaults local, SchemaDefaults remote, List<string> messages)
        {
            CompareField(groupName, nameof(SchemaDefaults.PreventUpdates), schema.StaticContent, local.PreventUpdates, remote.PreventUpdates, messages);
        }

        private static AssetBundleCrcMode ToCrcMode(bool useCrc, bool useCrcForCachedBundles)
        {
            if (!useCrc) return AssetBundleCrcMode.Disabled;
            return useCrcForCachedBundles ? AssetBundleCrcMode.EnabledIncludingCached : AssetBundleCrcMode.EnabledExcludingCached;
        }

        private static void CompareField<T>(string groupName, string fieldName, T current, T expectedLocal, T expectedRemote, List<string> messages)
        {
            if (EqualityComparer<T>.Default.Equals(current, expectedLocal)) return;
            if (EqualityComparer<T>.Default.Equals(current, expectedRemote)) return;

            messages.Add($"{groupName}.{fieldName}: {current} (expected Local={expectedLocal} or Remote={expectedRemote})");
        }
    }
}
