using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// The single entry point that takes Addressables from launch to ready-to-use, driven by an
    /// <see cref="AddressablesToolkitSettings"/> asset (CDN override → init → catalog update →
    /// preload → <see cref="AddressablesState.Ready"/>). Calls are idempotent and join one in-flight
    /// initialization, so any number of systems can <c>await InitializeAsync()</c> before touching
    /// content. Local content skips the CDN/catalog/download steps entirely.
    /// </summary>
    public interface IAddressablesService
    {
        /// <summary>Current lifecycle state.</summary>
        AddressablesState State { get; }

        /// <summary>True once initialization has completed and content can be loaded.</summary>
        bool IsReady { get; }

        /// <summary>Raised on every state transition (on the calling/main thread).</summary>
        event Action<AddressablesState> StateChanged;

        /// <summary>Result of the most recent preload-download step (default if none ran).</summary>
        DownloadResult LastDownloadResult { get; }

        /// <summary>Initialize using the active <see cref="AddressablesToolkitSettings.Instance"/>.</summary>
        UniTask<bool> InitializeAsync(
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default);

        /// <summary>Initialize using explicit settings. Returns true when the runtime reaches Ready.</summary>
        UniTask<bool> InitializeAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default);

        /// <summary>Reset to <see cref="AddressablesState.Uninitialized"/> so a later call re-runs the flow.</summary>
        void Reset();
    }
}
