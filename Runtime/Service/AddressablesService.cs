using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// Static convenience facade over the process-wide default <see cref="IAddressablesService"/>
    /// (<see cref="AddressablesInitializer"/>). Keeps the ergonomic
    /// <c>AddressablesService.InitializeAsync()</c> entry point and the launch auto-bootstrap; for
    /// testability/DI, depend on <see cref="IAddressablesService"/> and inject <see cref="Default"/>.
    /// </summary>
    public static class AddressablesService
    {
        private static IAddressablesService _default;

        /// <summary>The process-wide default service. Inject this where an <see cref="IAddressablesService"/> is needed.</summary>
        public static IAddressablesService Default => _default ??= new AddressablesInitializer();

        /// <inheritdoc cref="IAddressablesService.State"/>
        public static AddressablesState State => Default.State;

        /// <inheritdoc cref="IAddressablesService.IsReady"/>
        public static bool IsReady => Default.IsReady;

        /// <inheritdoc cref="IAddressablesService.StateChanged"/>
        public static event Action<AddressablesState> StateChanged
        {
            add => Default.StateChanged += value;
            remove => Default.StateChanged -= value;
        }

        /// <inheritdoc cref="IAddressablesService.LastDownloadResult"/>
        public static DownloadResult LastDownloadResult => Default.LastDownloadResult;

        /// <inheritdoc cref="IAddressablesService.InitializeAsync(IProgress{DownloadProgress}, RemoteContentUpdater.ConfirmDownload, CancellationToken)"/>
        public static UniTask<bool> InitializeAsync(
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
            => Default.InitializeAsync(progress, confirm, ct);

        /// <inheritdoc cref="IAddressablesService.InitializeAsync(AddressablesToolkitSettings, IProgress{DownloadProgress}, RemoteContentUpdater.ConfirmDownload, CancellationToken)"/>
        public static UniTask<bool> InitializeAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
            => Default.InitializeAsync(settings, progress, confirm, ct);

        /// <inheritdoc cref="IAddressablesService.Reset"/>
        public static void Reset() => Default.Reset();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            var settings = AddressablesToolkitSettings.Instance;
            if (settings != null && settings.autoInitializeOnLaunch)
                Default.InitializeAsync(settings).Forget();
        }
    }
}
