using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Guided build flow: pick Local/Remote and a baseline asset, check group schemas against it
    /// (warnings shown right here, not just Console), then build/update/clear content. The
    /// selected mode is only locked in for the build call itself — <see cref="AddressablesBuildOverrideScope"/>
    /// switches the active Addressables profile to one named "Local"/"Remote" if it exists (warns,
    /// doesn't block, if it doesn't), forces <see cref="AddressablesToolkitSettings.contentSource"/>
    /// to match, forces every group's <c>BundleNaming</c> to the baseline's value for that mode,
    /// and forces every group's Build &amp; Load Paths to the built-in "Local.BuildPath"/"Local.LoadPath"
    /// or "Remote.BuildPath"/"Remote.LoadPath" profile variables — then restores everything right
    /// after the build finishes. Nothing ends up permanently changed just from using this window.
    /// Build/Update/Clear/Open Folder are all always clickable — running the schema check first is
    /// a recommendation, not a gate. Never performs any CDN upload/publish step.
    /// </summary>
    public sealed class AddressablesBuildWindow : EditorWindow
    {
        private ContentSource _mode;
        private AddressablesGroupSchemaBaseline _selectedBaseline;
        private bool _checked;
        private List<string> _warnings = new();
        private Vector2 _warningsScroll;
        private List<string> _forcedPreview = new();
        private string _buildStatus;
        private bool _buildFailed;
        private IReadOnlyList<string> _createdFiles = new List<string>();
        private Vector2 _createdFilesScroll;
        private string _buildFolder;

        [MenuItem("Tools/Addressables Toolkit/Build Addressables...", false, 3200)]
        public static void Open()
        {
            var window = GetWindow<AddressablesBuildWindow>(true, "Build Addressables");
            window.minSize = new Vector2(420, 320);
            window._mode = AddressablesToolkitSettings.Instance.contentSource;
            window.RefreshBaseline();
            window.ResetBuildState();
        }

        private void OnEnable() => RefreshBaseline();
        private void OnFocus() => RefreshBaseline();

        private void RefreshBaseline() => AddressablesGroupSchemaBaseline.TryFindSingle(out _selectedBaseline, out _);

        private void ResetBuildState()
        {
            _checked = false;
            _warnings = new List<string>();
            _forcedPreview = new List<string>();
            _buildStatus = null;
            _createdFiles = new List<string>();
            _buildFolder = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Content Source", EditorStyles.boldLabel);
            var newMode = (ContentSource)EditorGUILayout.EnumPopup(_mode);
            if (newMode != _mode)
            {
                _mode = newMode;
                ResetBuildState();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_selectedBaseline == null))
            {
                if (GUILayout.Button("Select Schema Baseline"))
                {
                    Selection.activeObject = _selectedBaseline;
                    EditorGUIUtility.PingObject(_selectedBaseline);
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Check Group Schemas"))
            {
                var settingsForCheck = AddressableAssetSettingsDefaultObject.Settings;
                _warnings = AddressablesGroupSchemaValidator.CollectMessages(_selectedBaseline);
                _forcedPreview = AddressablesBuildOperations.BuildForcedValuesPreview(settingsForCheck, _mode, _selectedBaseline);
                _checked = true;
                _buildStatus = null;
            }

            if (_checked)
            {
                if (_warnings.Count == 0)
                {
                    EditorGUILayout.HelpBox("No drift — groups already match the baseline.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox($"{_warnings.Count} field(s) match neither baseline:", MessageType.Warning);
                    _warningsScroll = EditorGUILayout.BeginScrollView(_warningsScroll, GUILayout.Height(120));
                    foreach (var warning in _warnings)
                        EditorGUILayout.LabelField(warning, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndScrollView();
                }

                if (_forcedPreview.Count > 0)
                {
                    EditorGUILayout.LabelField($"Forced onto every group when building {_mode}:", EditorStyles.miniBoldLabel);
                    foreach (var line in _forcedPreview)
                        EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Build Content"))
            {
                var dialogMessage = _mode == ContentSource.Remote
                    ? "This writes bundles and a catalog to the local build output only. " +
                      "It does NOT upload or publish to the CDN — that stays a separate step."
                    : "This builds bundles and a catalog for local (in-player) content.";

                if (EditorUtility.DisplayDialog("Build Addressables Content", dialogMessage, "Build", "Cancel"))
                    RunBuild();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Update Build"))
            {
                if (EditorUtility.DisplayDialog("Build Content Update",
                        "Builds a content update against the previous content state. Requires a prior full Build Content.",
                        "Update", "Cancel"))
                    AddressablesBuilder.BuildContentUpdate();
            }

            if (GUILayout.Button("Clear Build"))
            {
                if (EditorUtility.DisplayDialog("Clean Content",
                        "Deletes the player content build cache. Does not touch source assets.",
                        "Clean", "Cancel"))
                    AddressablesBuilder.CleanContent();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_buildStatus))
                EditorGUILayout.HelpBox(_buildStatus, _buildFailed ? MessageType.Error : MessageType.Info);

            if (GUILayout.Button("Open Build Folder"))
            {
                var folder = _buildFolder ?? AddressablesBuildOperations.ResolveBuildFolder(AddressableAssetSettingsDefaultObject.Settings, _mode);
                if (string.IsNullOrEmpty(folder))
                    Debug.LogWarning("[Addressables Toolkit] Could not resolve a build folder — no Addressable Asset Settings found.");
                else
                    EditorUtility.RevealInFinder(folder);
            }

            if (_createdFiles.Count > 0)
            {
                EditorGUILayout.LabelField($"{_createdFiles.Count} file(s) created/updated:", EditorStyles.miniBoldLabel);
                _createdFilesScroll = EditorGUILayout.BeginScrollView(_createdFilesScroll, GUILayout.Height(140));
                foreach (var path in _createdFiles)
                    EditorGUILayout.LabelField(path, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void RunBuild()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            using var scope = AddressablesBuildOverrideScope.Begin(settings, _mode, _selectedBaseline);

            EditorUtility.DisplayProgressBar("Build Addressables", "Building content...", 0.5f);
            try
            {
                var outcome = AddressablesBuilder.BuildContentCore();
                _buildFailed = !outcome.Success;
                _buildStatus = outcome.Success
                    ? $"Build succeeded in {outcome.Duration:F1}s. Output: {outcome.OutputPath}"
                    : $"Build failed: {outcome.Error}";
                _createdFiles = outcome.CreatedFiles;
                _buildFolder = outcome.Success ? scope.ResolvedBuildFolder : null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
