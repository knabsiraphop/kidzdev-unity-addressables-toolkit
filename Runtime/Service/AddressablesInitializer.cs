using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace KidzDev.AddressablesToolkit
{
    /// <summary>
    /// The default <see cref="IAddressablesService"/>: the settings-driven initialization flow
    /// (CDN override → <c>Addressables.InitializeAsync</c> → catalog update → preload → Ready). A
    /// nullable preserved task lets concurrent callers join one init; it is cleared on failure to
    /// allow a retry. Drives <see cref="AddressableCdn"/>, <see cref="CatalogUpdater"/>, and
    /// <see cref="RemoteContentUpdater"/>.
    /// </summary>
    public sealed class AddressablesInitializer : IAddressablesService
    {
        private UniTask<bool>? _inFlight;

        /// <inheritdoc/>
        public AddressablesState State { get; private set; } = AddressablesState.Uninitialized;

        /// <inheritdoc/>
        public bool IsReady => State == AddressablesState.Ready;

        /// <inheritdoc/>
        public event Action<AddressablesState> StateChanged;

        /// <inheritdoc/>
        public DownloadResult LastDownloadResult { get; private set; }

        /// <inheritdoc/>
        public UniTask<bool> InitializeAsync(
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
            => InitializeAsync(AddressablesToolkitSettings.Instance, progress, confirm, ct);

        /// <inheritdoc/>
        public UniTask<bool> InitializeAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress = null,
            RemoteContentUpdater.ConfirmDownload confirm = null,
            CancellationToken ct = default)
        {
            if (IsReady)
                return UniTask.FromResult(true);

            if (_inFlight.HasValue)
                return _inFlight.Value;

            var initTask = RunAsync(settings, progress, confirm, ct).Preserve();
            _inFlight = initTask;
            return AwaitAndUnlatch(initTask);
        }

        private async UniTask<bool> AwaitAndUnlatch(UniTask<bool> initTask)
        {
            var ready = await initTask;
            if (!ready)
                _inFlight = null; // failed/declined: allow a fresh attempt (e.g. after reconnect)
            return ready;
        }

        private async UniTask<bool> RunAsync(
            AddressablesToolkitSettings settings,
            IProgress<DownloadProgress> progress,
            RemoteContentUpdater.ConfirmDownload confirm,
            CancellationToken ct)
        {
            if (settings == null)
            {
                Debug.LogError("[AddressablesToolkit] InitializeAsync called with null settings.");
                SetState(AddressablesState.Failed);
                return false;
            }

            var isRemote = settings.contentSource == ContentSource.Remote;

            try
            {
                SetState(AddressablesState.Initializing);

                // 1) Point Addressables at the active environment's CDN, if requested.
                if (isRemote && settings.overrideRemoteUrl)
                {
                    var environment = settings.ResolveEnvironment();
                    if (environment == null || string.IsNullOrEmpty(environment.CdnBaseUrl))
                    {
                        Debug.LogError("[AddressablesToolkit] overrideRemoteUrl is on but no environment / CDN URL is configured.");
                        SetState(AddressablesState.Failed);
                        return false;
                    }

                    var version = settings.ResolveVersion();
                    AddressableCdn.Install(environment.CdnBaseUrl, version, settings.ResolvePlatformFolder());
                    Log(settings, $"CDN override → {environment.Name}: {environment.CdnBaseUrl} (v{version}).");
                }

                // 2) Initialize the Addressables system.
                await Addressables.InitializeAsync().ToUniTask(cancellationToken: ct);
                Log(settings, "Addressables initialized.");

                // 3) Apply catalog updates so sizing/downloading see the latest bundles.
                if (isRemote && settings.checkCatalogUpdates)
                {
                    SetState(AddressablesState.UpdatingCatalog);
                    var updatedCatalogs = await CatalogUpdater.CheckAndUpdateCatalogsAsync(ct);
                    Log(settings, updatedCatalogs.Count > 0
                        ? $"Updated {updatedCatalogs.Count} catalog(s)."
                        : "Catalogs already current.");
                }

                // 4) Predownload the configured preload labels.
                if (isRemote && settings.predownloadPreloadContent)
                {
                    var preloadKeys = settings.GetPreloadKeys();
                    if (preloadKeys.Count > 0)
                    {
                        SetState(AddressablesState.DownloadingContent);
                        var downloadResult = await RemoteContentUpdater.RunAsync(preloadKeys, progress, confirm, ct);
                        LastDownloadResult = downloadResult;
                        Log(settings, $"Preload: {downloadResult.Outcome} ({downloadResult.Bytes} bytes).");

                        if (!downloadResult.IsSuccess)
                        {
                            SetState(AddressablesState.Failed);
                            return false;
                        }
                    }
                }

                SetState(AddressablesState.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log(settings, "Initialization cancelled.");
                SetState(AddressablesState.Failed);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressablesToolkit] Initialization failed: {e}");
                SetState(AddressablesState.Failed);
                return false;
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _inFlight = null;
            SetState(AddressablesState.Uninitialized);
        }

        private void SetState(AddressablesState state)
        {
            if (State == state)
                return;

            State = state;
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AddressablesToolkit] A StateChanged handler threw: {e}");
            }
        }

        private static void Log(AddressablesToolkitSettings settings, string message)
        {
            if (settings != null && settings.verboseLogging)
                Debug.Log($"[AddressablesToolkit] {message}");
        }
    }
}
