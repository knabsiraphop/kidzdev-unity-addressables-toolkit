using UnityEditor;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit.Editor
{
    /// <summary>
    /// Draws <see cref="SchemaDefaults.BundleNamingMode"/> with a plain
    /// <see cref="EditorGUILayout.EnumPopup(GUIContent, System.Enum)"/> instead of the default
    /// Inspector's reflection-based <c>PropertyField</c>. Addressables ships a
    /// <c>[CustomPropertyDrawer(typeof(BundledAssetGroupSchema.BundleNamingStyle))]</c> that renders
    /// friendlier text ("Filename" / "Append Hash to Filename" / ...), but the real Group Inspector's
    /// own OnGUI code never uses it — it calls a plain EnumPopup, so a real group shows raw nicified
    /// enum names ("Append Hash" / "No Hash" / "Only Hash" / "File Name Hash"). Without this override
    /// our baseline asset and a real group would show different-looking text for the same value.
    /// </summary>
    [CustomEditor(typeof(AddressablesGroupSchemaBaseline))]
    public sealed class AddressablesGroupSchemaBaselineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSchemaDefaults("Local Defaults", serializedObject.FindProperty(nameof(AddressablesGroupSchemaBaseline.LocalDefaults)));
            EditorGUILayout.Space();
            DrawSchemaDefaults("Remote Defaults", serializedObject.FindProperty(nameof(AddressablesGroupSchemaBaseline.RemoteDefaults)));

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSchemaDefaults(string header, SerializedProperty root)
        {
            if (root == null) return;

            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Checked (drift-warned by Check Group Schemas)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.AssetBundleCompression)));
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.AssetBundleCrc)));
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.CacheClearBehavior)));
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.IncludeInBuild)));
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.UseAssetBundleCache)));
            EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(SchemaDefaults.PreventUpdates)));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Not checked — forced onto every group at build time instead", EditorStyles.miniBoldLabel);
            DrawBundleNamingMode(root.FindPropertyRelative(nameof(SchemaDefaults.BundleNamingMode)));
        }

        private static void DrawBundleNamingMode(SerializedProperty property)
        {
            if (property == null) return;

            var current = (BundledAssetGroupSchema.BundleNamingStyle)property.enumValueIndex;
            var label = new GUIContent(property.displayName, property.tooltip);

            EditorGUI.BeginChangeCheck();
            var newValue = (BundledAssetGroupSchema.BundleNamingStyle)EditorGUILayout.EnumPopup(label, current);
            if (EditorGUI.EndChangeCheck())
                property.enumValueIndex = (int)newValue;
        }
    }
}
