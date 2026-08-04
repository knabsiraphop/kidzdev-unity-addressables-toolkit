using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Locks the active Addressables profile, <see cref="AddressablesToolkitSettings.contentSource"/>,
    /// every group's <c>BundleNaming</c>, and every group's Build &amp; Load Paths to the selected
    /// mode for the lifetime of the scope, then restores all four (in reverse order) on
    /// <see cref="Dispose"/> — <c>using var scope = Begin(...)</c> around a build. Adding a fifth
    /// override later means adding one more private nested class and one more <c>Push</c> call in
    /// <see cref="Begin"/>, not touching the restore logic.
    /// </summary>
    internal sealed class AddressablesBuildOverrideScope : IDisposable
    {
        private readonly Stack<IDisposable> _applied = new();

        /// <summary>Absolute folder the selected mode's groups will write bundles to, resolved
        /// while applying the Build &amp; Load Paths override.</summary>
        internal string ResolvedBuildFolder { get; private set; }

        private AddressablesBuildOverrideScope() { }

        internal static AddressablesBuildOverrideScope Begin(AddressableAssetSettings settings, ContentSource mode, AddressablesGroupSchemaBaseline baseline)
        {
            var scope = new AddressablesBuildOverrideScope();
            var schemas = AddressablesBuildOperations.EnumerateBundledSchemas(settings).ToList();

            scope._applied.Push(new ActiveProfileOverride(settings, mode));
            scope._applied.Push(new ContentSourceOverride(mode));
            scope._applied.Push(new BundleNamingOverride(schemas, baseline, mode));

            var paths = new BuildLoadPathOverride(settings, schemas, mode);
            scope._applied.Push(paths);
            scope.ResolvedBuildFolder = paths.ResolvedBuildFolder ?? AddressablesBuildOperations.ResolveBuildFolder(settings, mode);

            return scope;
        }

        public void Dispose()
        {
            while (_applied.Count > 0)
                _applied.Pop().Dispose();
        }

        private sealed class ActiveProfileOverride : IDisposable
        {
            private readonly AddressableAssetSettings _settings;
            private readonly string _originalProfileId;

            public ActiveProfileOverride(AddressableAssetSettings settings, ContentSource mode)
            {
                _settings = settings;
                if (settings == null) return;

                // Check silently first — most projects have no profile literally named
                // "Local"/"Remote", so routing through ApplyProfileByName unconditionally would
                // log its not-found warning on every single build.
                var profileName = mode.ToString();
                if (!AddressablesBuilder.ProfileExists(settings, profileName)) return;

                _originalProfileId = settings.activeProfileId;
                AddressablesBuilder.ApplyProfileByName(settings, profileName);
            }

            public void Dispose()
            {
                if (_settings != null && _originalProfileId != null)
                    _settings.activeProfileId = _originalProfileId;
            }
        }

        private sealed class ContentSourceOverride : IDisposable
        {
            private readonly ContentSource _original;

            public ContentSourceOverride(ContentSource mode)
            {
                _original = AddressablesToolkitSettings.Instance.contentSource;
                AddressablesToolkitSettings.Instance.contentSource = mode;
            }

            public void Dispose() => AddressablesToolkitSettings.Instance.contentSource = _original;
        }

        private sealed class BundleNamingOverride : IDisposable
        {
            private readonly List<(BundledAssetGroupSchema schema, BundledAssetGroupSchema.BundleNamingStyle value)> _originalValues = new();

            public BundleNamingOverride(IReadOnlyList<BundledAssetGroupSchema> schemas, AddressablesGroupSchemaBaseline baseline, ContentSource mode)
            {
                var expected = AddressablesBuildOperations.GetExpectedBundleNaming(baseline, mode);
                if (expected == null) return;

                foreach (var schema in schemas)
                {
                    if (schema.BundleNaming == expected.Value) continue;

                    _originalValues.Add((schema, schema.BundleNaming));
                    schema.BundleNaming = expected.Value;
                }
            }

            public void Dispose()
            {
                foreach (var (schema, value) in _originalValues)
                    schema.BundleNaming = value;
            }
        }

        private sealed class BuildLoadPathOverride : IDisposable
        {
            // Restoring by name (ProfileValueReference.GetName + SetVariableByName) silently no-ops
            // for a group whose Build/Load Path was a "Custom" literal value rather than a named
            // profile variable — GetName() returns "" for those (no named profile data matches a
            // custom id), and SetVariableByName/SetVariableById both refuse non-named ids too, so a
            // custom path would be left pointing at the forced Local/Remote variable forever. The
            // raw Id (ProfileValueReference.Id, public get) round-trips both cases identically, but
            // there's no public setter for it (internal, different assembly) — write it back via
            // SerializedObject on the schema's serialized "m_BuildPath.m_Id" / "m_LoadPath.m_Id"
            // fields instead, which doesn't care about C# access modifiers.
            private readonly List<(BundledAssetGroupSchema schema, string buildPathId, string loadPathId)> _originalPaths = new();

            internal string ResolvedBuildFolder { get; private set; }

            public BuildLoadPathOverride(AddressableAssetSettings settings, IReadOnlyList<BundledAssetGroupSchema> schemas, ContentSource mode)
            {
                if (settings == null) return;

                var (buildPathVar, loadPathVar) = AddressablesBuildOperations.GetPathVariableNames(mode);

                foreach (var schema in schemas)
                {
                    var originalBuildPathId = schema.BuildPath.Id;
                    var originalLoadPathId = schema.LoadPath.Id;

                    var buildChanged = schema.BuildPath.SetVariableByName(settings, buildPathVar);
                    var loadChanged = schema.LoadPath.SetVariableByName(settings, loadPathVar);

                    if (buildChanged || loadChanged)
                        _originalPaths.Add((schema, originalBuildPathId, originalLoadPathId));

                    // Only trust this schema's resolved value once we know its BuildPath actually
                    // points at the forced variable — if SetVariableByName failed for this schema,
                    // GetValue would still return its stale pre-override path.
                    if (buildChanged && ResolvedBuildFolder == null)
                    {
                        var relativePath = schema.BuildPath.GetValue(settings);
                        if (!string.IsNullOrEmpty(relativePath))
                            ResolvedBuildFolder = AddressablesBuildOperations.ToAbsolutePath(relativePath);
                    }
                }
            }

            public void Dispose()
            {
                foreach (var (schema, buildPathId, loadPathId) in _originalPaths)
                {
                    TryRestoreRawId(schema, "m_BuildPath.m_Id", buildPathId);
                    TryRestoreRawId(schema, "m_LoadPath.m_Id", loadPathId);
                }
            }

            private static void TryRestoreRawId(BundledAssetGroupSchema schema, string propertyPath, string originalId)
            {
                try
                {
                    if (schema == null) return;

                    var serializedSchema = new SerializedObject(schema);
                    var property = serializedSchema.FindProperty(propertyPath);
                    if (property == null) return;

                    property.stringValue = originalId;
                    serializedSchema.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception e)
                {
                    // One schema failing to restore shouldn't take the rest of the loop down with it.
                    Debug.LogError($"[Addressables Toolkit] Failed to restore {propertyPath} on '{schema?.Group?.Name}': {e.Message}");
                }
            }
        }
    }
}
