using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// Clears the toolkit's static state at the very start of every play session. With
    /// "Enter Play Mode without domain reload" enabled, statics survive between editor play
    /// sessions — without this reset a second session would see a Ready service, a loader
    /// cache full of dead handles, a pool whose root and prefab references were destroyed,
    /// and stale scene tracking. In a player build this runs once at startup, before any
    /// of this state exists, so it is a no-op there.
    /// </summary>
    internal static class AddressablesToolkitRuntimeReset
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AssetLoader.ResetDefault();
            AddressablePool.ResetDefault();
            AddressablesService.ResetDefault();
            SceneLoader.ResetTracking();
            AddressablesToolkitSettings.ResetRuntimeStatics();
        }
    }
}
