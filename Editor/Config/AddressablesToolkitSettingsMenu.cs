using System.IO;
using UnityEditor;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Locates the project's <see cref="AddressablesToolkitSettings"/> asset, creating one under
    /// <c>Assets/Resources/</c> on first use so the runtime loader (<c>Resources.Load</c>) finds it.
    /// </summary>
    public static class AddressablesToolkitSettingsMenu
    {
        private const string DefaultFolder = "Assets/Resources";
        private static string DefaultAssetPath =>
            $"{DefaultFolder}/{AddressablesToolkitSettings.ResourcesPath}.asset";

        [MenuItem("Tools/Addressables Toolkit/Settings", false, 3100)]
        public static void OpenOrCreate()
        {
            var settings = FindExisting() ?? Create();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private static AddressablesToolkitSettings FindExisting()
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(AddressablesToolkitSettings)}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AddressablesToolkitSettings>(path);
                if (asset != null)
                    return asset;
            }
            return null;
        }

        private static AddressablesToolkitSettings Create()
        {
            if (!AssetDatabase.IsValidFolder(DefaultFolder))
                Directory.CreateDirectory(DefaultFolder);

            var settings = ScriptableObject.CreateInstance<AddressablesToolkitSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Addressables Toolkit] Created settings at '{DefaultAssetPath}'.");
            return settings;
        }
    }
}
