namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>Lifecycle of the Addressables runtime, as driven by <see cref="IAddressablesService"/>.</summary>
    public enum AddressablesState
    {
        Uninitialized,
        Initializing,
        UpdatingCatalog,
        DownloadingContent,
        Ready,
        Failed
    }
}
