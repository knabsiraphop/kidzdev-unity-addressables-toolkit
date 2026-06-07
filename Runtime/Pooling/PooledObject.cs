using UnityEngine;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>Marks a pooled instance and records the key it was created from.</summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        public object Key { get; internal set; }
    }
}
