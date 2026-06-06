using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace KidzDev.AddressablesToolkit.Editor
{
    internal static class _EditorCompileCheck
    {
        public static AddressableAssetSettings Probe()
            => AddressableAssetSettingsDefaultObject.Settings;
    }
}