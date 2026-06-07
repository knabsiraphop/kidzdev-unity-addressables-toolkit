using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Editor
{
    /// <summary>Project-window menu items for quickly managing Addressable entries.</summary>
    public static class AddressableEditorUtils
    {
        private const string MenuRoot = "Assets/Addressables Toolkit/";

        [MenuItem(MenuRoot + "Mark Addressable (address = name)", false, 1000)]
        private static void MarkByName() => Mark(useFullPath: false);

        [MenuItem(MenuRoot + "Mark Addressable (address = path)", false, 1001)]
        private static void MarkByPath() => Mark(useFullPath: true);

        [MenuItem(MenuRoot + "Label by Parent Folder", false, 1010)]
        private static void LabelByFolder()
        {
            var settings = GetSettings();
            if (settings == null) return;

            int count = 0;
            foreach (var guid in Selection.assetGUIDs)
            {
                var entry = settings.FindAssetEntry(guid);
                if (entry == null) continue;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                var label = Path.GetFileName(Path.GetDirectoryName(path));
                if (string.IsNullOrEmpty(label)) continue;

                settings.AddLabel(label);
                entry.SetLabel(label, true);
                count++;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables Toolkit] Labeled {count} entr{(count == 1 ? "y" : "ies")} by parent folder.");
        }

        [MenuItem(MenuRoot + "Remove from Addressables", false, 1020)]
        private static void RemoveSelected()
        {
            var settings = GetSettings();
            if (settings == null) return;

            int count = 0;
            foreach (var guid in Selection.assetGUIDs)
                if (settings.RemoveAssetEntry(guid))
                    count++;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables Toolkit] Removed {count} entr{(count == 1 ? "y" : "ies")}.");
        }

        private static void Mark(bool useFullPath)
        {
            var settings = GetSettings();
            if (settings == null) return;

            var group = settings.DefaultGroup;
            int count = 0;
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || Directory.Exists(path))
                    continue; // skip folders

                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = useFullPath ? path : Path.GetFileNameWithoutExtension(path);
                count++;
            }

            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Addressables Toolkit] Marked {count} asset(s) addressable in group '{group.Name}'.");
        }

        private static AddressableAssetSettings GetSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                Debug.LogError("[Addressables Toolkit] No Addressable Asset Settings found. Create them via Window > Asset Management > Addressables > Groups.");
            return settings;
        }

        [MenuItem(MenuRoot + "Mark Addressable (address = name)", true)]
        [MenuItem(MenuRoot + "Mark Addressable (address = path)", true)]
        [MenuItem(MenuRoot + "Label by Parent Folder", true)]
        [MenuItem(MenuRoot + "Remove from Addressables", true)]
        private static bool ValidateSelection() => Selection.assetGUIDs.Length > 0;
    }
}
