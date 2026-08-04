using System;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Mirrors the Group Inspector's "Asset Bundle CRC" popup (<see cref="BundledAssetGroupSchema"/>'s
    /// UseAssetBundleCrc / UseAssetBundleCrcForCachedBundles are one combined control there, not two
    /// checkboxes) — Disabled = UseAssetBundleCrc off; the other two are UseAssetBundleCrc on, with
    /// UseAssetBundleCrcForCachedBundles on/off respectively.
    /// </summary>
    public enum AssetBundleCrcMode
    {
        Disabled,
        EnabledIncludingCached,
        EnabledExcludingCached
    }

    /// <summary>
    /// Explicit allowlist of <see cref="BundledAssetGroupSchema"/> / <see cref="ContentUpdateGroupSchema"/>
    /// fields governed by an <see cref="AddressablesGroupSchemaBaseline"/>. Field names here match the
    /// Group Inspector's own labels (e.g. <see cref="PreventUpdates"/> is
    /// <c>ContentUpdateGroupSchema.StaticContent</c> — the property name and the Inspector label
    /// genuinely differ in Addressables itself; tooltips carry the real property name for that reason).
    /// </summary>
    [Serializable]
    public struct SchemaDefaults
    {
        // --- Checked: AddressablesGroupSchemaValidator warns when a group's value matches
        // neither LocalDefaults nor RemoteDefaults for these fields. Read-only, never written.
        // (AddressablesGroupSchemaBaselineEditor draws the "Checked"/"Not checked" grouping headers
        // itself — no [Header] here, it would double up with that.) ---

        [Tooltip("BundledAssetGroupSchema.Compression.")]
        public BundledAssetGroupSchema.BundleCompressionMode AssetBundleCompression;

        [Tooltip("BundledAssetGroupSchema.UseAssetBundleCrc + UseAssetBundleCrcForCachedBundles, " +
                 "combined into the one popup the Group Inspector actually shows.")]
        public AssetBundleCrcMode AssetBundleCrc;

        [Tooltip("BundledAssetGroupSchema.AssetBundledCacheClearBehavior.")]
        public BundledAssetGroupSchema.CacheClearBehavior CacheClearBehavior;

        [Tooltip("BundledAssetGroupSchema.IncludeInBuild.")]
        public bool IncludeInBuild;

        [Tooltip("BundledAssetGroupSchema.UseAssetBundleCache.")]
        public bool UseAssetBundleCache;

        [Tooltip("ContentUpdateGroupSchema.StaticContent — shown in the Group Inspector as " +
                 "\"Prevent Updates\", not \"Static Content\".")]
        public bool PreventUpdates;

        // --- Not checked: AddressablesBuildWindow force-applies this field to every group for
        // the duration of the build instead (then restores it), so drift here is never reported. ---

        [Tooltip("BundledAssetGroupSchema.BundleNaming. AddressablesBuildWindow sets every " +
                 "group's BundleNaming to this value while building, then restores the original " +
                 "afterward — never flagged by Check Group Schemas.")]
        public BundledAssetGroupSchema.BundleNamingStyle BundleNamingMode;
    }

    /// <summary>
    /// The two known-good schema configurations for this project — one for groups that ship
    /// content in the player (Local) and one for groups that ship content on a CDN (Remote).
    /// A group is fine as long as it matches either set field-for-field; <c>AddressablesGroupSchemaValidator</c>
    /// warns on any field that matches neither. Editor-only — never referenced from Runtime.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AddressablesGroupSchemaBaseline",
        menuName = "KidzDev/Addressables Toolkit/Group Schema Baseline",
        order = 1)]
    public sealed class AddressablesGroupSchemaBaseline : ScriptableObject
    {
        [Tooltip("Expected schema values for groups whose content ships in the player.")]
        public SchemaDefaults LocalDefaults;

        [Tooltip("Expected schema values for groups whose content ships on a CDN.")]
        public SchemaDefaults RemoteDefaults;

        /// <summary>Finds the one baseline asset in the project. Fails (with a reason) if there
        /// are zero or more than one — callers should not silently no-op on either case.</summary>
        internal static bool TryFindSingle(out AddressablesGroupSchemaBaseline baseline, out string error)
        {
            baseline = null;

            var guids = AssetDatabase.FindAssets("t:" + nameof(AddressablesGroupSchemaBaseline));
            if (guids.Length == 0)
            {
                error = "No AddressablesGroupSchemaBaseline asset found. Create one via " +
                        "Assets > Create > KidzDev > Addressables Toolkit > Group Schema Baseline.";
                return false;
            }

            if (guids.Length > 1)
            {
                var paths = new string[guids.Length];
                for (int i = 0; i < guids.Length; i++)
                    paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
                error = $"Multiple AddressablesGroupSchemaBaseline assets found: {string.Join(", ", paths)}. Keep exactly one.";
                return false;
            }

            baseline = AssetDatabase.LoadAssetAtPath<AddressablesGroupSchemaBaseline>(AssetDatabase.GUIDToAssetPath(guids[0]));
            error = baseline == null ? "Failed to load the AddressablesGroupSchemaBaseline asset." : null;
            return baseline != null;
        }
    }
}
