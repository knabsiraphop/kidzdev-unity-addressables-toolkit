using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace KidzDev.Unity.AddressablesToolkit.Samples
{
    /// <summary>
    /// Exhaustive, interactive tour of the <b>entire</b> toolkit. Drop this on an empty GameObject and
    /// press Play — the UI is drawn with IMGUI (<see cref="OnGUI"/>), so it needs no scene, canvas, or
    /// prefab assets. Configure the addressable keys in the inspector to match your groups.
    ///
    /// Every public surface of the toolkit is wired to a button here so you can watch each call run
    /// and read its result in the on-screen log. Coverage, by section:
    /// <list type="bullet">
    ///   <item><b>Settings</b> — <see cref="AddressablesToolkitSettings"/> resolution helpers + runtime overrides.</item>
    ///   <item><b>Service</b> — <see cref="AddressablesService"/> init (both overloads), state, StateChanged, Reset.</item>
    ///   <item><b>Scope</b> — <see cref="AssetScope"/> load / instantiate / pool / release by key AND AssetReference,
    ///         plus the GameObject-bound <see cref="AssetScopeExtensions.GetAssetScope(GameObject)"/>.</item>
    ///   <item><b>Loader + Locator</b> — <see cref="AssetLoader"/> / <see cref="AssetLocator"/> static facades.</item>
    ///   <item><b>Pool</b> — <see cref="AddressablePool"/> static facade + <see cref="PooledObject"/>.</item>
    ///   <item><b>ComponentReference</b> — load / instantiate / release a typed component reference.</item>
    ///   <item><b>Sprite atlas</b> — <see cref="SpriteAtlasLoader"/> key convention + fallback.</item>
    ///   <item><b>Scenes</b> — <see cref="SceneLoader"/> additive load / unload / tracking.</item>
    ///   <item><b>Remote</b> — <see cref="ContentDownloader"/>, <see cref="CatalogUpdater"/>,
    ///         <see cref="RemoteContentUpdater"/>, <see cref="AddressableCdn"/>.</item>
    /// </list>
    /// Local content makes the remote calls return cheap, harmless results (size 0, NoUpdate, etc.) —
    /// switch the settings asset to Remote + a CDN to exercise the real catalog/predownload flow.
    /// </summary>
    public class AddressablesToolkitFullDemo : MonoBehaviour
    {
        /// <summary>Concrete <see cref="ComponentReference{T}"/> so it shows up in the inspector.</summary>
        [Serializable] public class SpriteRendererReference : ComponentReference<SpriteRenderer>
        {
            public SpriteRendererReference() : base(string.Empty) { }
        }

        [Header("Addressable keys (must exist in your Addressables groups)")]
        [SerializeField] private string prefabAddress = "demo-prefab";
        [SerializeField] private string spriteAddress = "demo-sprite";

        [Header("Optional — only needed for the matching sections")]
        [Tooltip("An additive Addressable scene key for the Scenes section.")]
        [SerializeField] private string sceneAddress = "demo-scene";
        [Tooltip("Label/address fed to the Remote section alongside the prefab + sprite keys.")]
        [SerializeField] private string remoteLabel = "core";

        [Header("Sprite atlas (SpriteAtlasLoader uses \"{atlasKey}[{spriteName}]\")")]
        [SerializeField] private string atlasKey = "demo-atlas";
        [SerializeField] private string atlasSpriteName = "icon";
        [SerializeField] private string fallbackAtlasKey = "demo-atlas";
        [SerializeField] private string fallbackSpriteName = "icon_missing";

        [Header("Assign in the inspector to exercise the AssetReference paths")]
        [SerializeField] private AssetReference prefabReference;
        [SerializeField] private SpriteRendererReference componentReference;

        // --- collaborators (depend on the seam, resolved to the process-wide default) --------
        private readonly IAddressablesService _service = AddressablesService.Default;

        // --- demo state ----------------------------------------------------------------------
        private AssetScope _scope;
        private CancellationTokenSource _cts;

        private float _downloadPercent;
        private bool _initInFlight;
        private bool _busy; // guards the ad-hoc one-shot operations from re-entry

        private int _instanceCount;
        private int _pooledCount;
        private GameObject _lastScopeInstance;   // AssetScope.InstantiateAsync
        private GameObject _lastScopePooled;     // AssetScope.SpawnPooledAsync
        private Sprite _loadedSprite;
        private string _loadedSpriteKey;         // loader key behind the preview, so it can self-verify

        // direct-facade state
        private GameObject _directPooled;         // AddressablePool.GetAsync
        private bool _hasComponentInstance;
        private AsyncOperationHandle<SpriteRenderer> _componentInstance;
        private bool _hasComponentAsset;
        private AsyncOperationHandle<SpriteRenderer> _componentAsset;
        private GameObject _boundChild;           // GameObject.GetAssetScope() demo
        private bool _cdnInstalled;               // AddressableCdn.Install/Uninstall state

        // settings runtime overrides
        private string _envOverrideField = "";

        // confirm-dialog plumbing
        private bool _awaitingConfirm;
        private long _confirmBytes;
        private UniTaskCompletionSource<bool> _confirmTcs;

        // structured, color-coded activity log (newest first) + panel scroll positions
        private readonly struct LogEntry
        {
            public readonly string Time;
            public readonly string Message;
            public readonly Color Color;
            public LogEntry(string time, string message, Color color) { Time = time; Message = message; Color = color; }
        }

        private readonly List<LogEntry> _entries = new();
        private Vector2 _logScroll;
        private Vector2 _panelScroll;

        private AssetScope Scope => _scope ??= new AssetScope();

        // =====================================================================
        //  Unity lifecycle
        // =====================================================================

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            _envOverrideField = AddressablesToolkitSettings.EnvironmentOverride ?? "";
        }

        private void OnEnable() => _service.StateChanged += OnServiceStateChanged;
        private void OnDisable() => _service.StateChanged -= OnServiceStateChanged;

        private void OnServiceStateChanged(AddressablesState state) => Log($"State → {state} (StateChanged event).");

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _scope?.Dispose();                 // releases every asset/instance/pooled object the scope owns
            ReleaseComponentHandles();
            if (_boundChild != null) Destroy(_boundChild);
        }

        // =====================================================================
        //  Service flow  (IAddressablesService)
        // =====================================================================

        // InitializeAsync joins one in-flight init, so both are safe even while a caller awaits.
        private void StartInitialize() => InitializeAsync(useExplicitSettings: false, _cts.Token).Forget();
        private void StartInitializeExplicit()
        {
            _service.Reset(); // re-run from scratch so the explicit-settings overload actually executes the flow
            InitializeAsync(useExplicitSettings: true, _cts.Token).Forget();
        }

        private async UniTaskVoid InitializeAsync(bool useExplicitSettings, CancellationToken ct)
        {
            _initInFlight = true;
            _downloadPercent = 0f;
            var progress = new Progress<DownloadProgress>(p => _downloadPercent = p.Percent);

            bool ready;
            if (useExplicitSettings)
            {
                Log("Initializing (explicit-settings overload)…");
                ready = await _service.InitializeAsync(AddressablesToolkitSettings.Instance, progress, Confirm, ct);
            }
            else
            {
                Log("Initializing (implicit .Instance overload)…");
                ready = await _service.InitializeAsync(progress, Confirm, ct);
            }

            _initInFlight = false;
            Log(ready
                ? "Ready — content can be loaded."
                : $"Init did not reach Ready: {_service.LastDownloadResult.Outcome}.");
        }

        private void ResetService()
        {
            _service.Reset(); // back to Uninitialized so a later Initialize re-runs the whole flow
            Log("Service reset → Uninitialized.");
        }

        // Cancel in-flight work, then hand out a fresh token so the demo stays usable afterwards.
        private void CancelCurrent()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            Log("Cancelled in-flight operations.");
        }

        // Confirm gate: surfaces a Yes/No dialog in OnGUI and completes when the player answers.
        private UniTask<bool> Confirm(long bytes)
        {
            _confirmBytes = bytes;
            _confirmTcs = new UniTaskCompletionSource<bool>();
            _awaitingConfirm = true;
            return _confirmTcs.Task;
        }

        // =====================================================================
        //  AssetScope — the recommended lifetime-bound path
        // =====================================================================

        private UniTaskVoid LoadAssetByKey() => RunScope(async () =>
        {
            await Scope.LoadAsync<GameObject>(prefabAddress, _cts.Token);
            Log($"Scope.LoadAsync<GameObject>('{prefabAddress}') — borrow tracked by the scope.");
        });

        private UniTaskVoid LoadAssetByReference() => RunScope(async () =>
        {
            if (!ReferenceAssigned(prefabReference)) return;
            await Scope.LoadAsync<GameObject>(prefabReference, _cts.Token);
            Log("Scope.LoadAsync<GameObject>(AssetReference) — borrow tracked by the scope.");
        });

        private UniTaskVoid InstantiateByKey() => RunScope(async () =>
        {
            _lastScopeInstance = await Scope.InstantiateAsync(prefabAddress, transform, _cts.Token);
            _lastScopeInstance.transform.position = RandomPos();
            _instanceCount++;
            Log($"Scope.InstantiateAsync('{prefabAddress}') by key.");
        });

        private UniTaskVoid InstantiateByReference() => RunScope(async () =>
        {
            if (!ReferenceAssigned(prefabReference)) return;
            _lastScopeInstance = await Scope.InstantiateAsync(prefabReference, transform, _cts.Token);
            _lastScopeInstance.transform.position = RandomPos();
            _instanceCount++;
            Log("Scope.InstantiateAsync(AssetReference).");
        });

        private UniTaskVoid InstantiateComponentByKey() => RunScope(async () =>
        {
            // Generic overload: instantiate and return a component on the instance (throws if absent).
            var sr = await Scope.InstantiateAsync<SpriteRenderer>(prefabAddress, transform, _cts.Token);
            _lastScopeInstance = sr.gameObject;
            _lastScopeInstance.transform.position = RandomPos();
            _instanceCount++;
            Log($"Scope.InstantiateAsync<SpriteRenderer>('{prefabAddress}') — got {sr.GetType().Name}.");
        });

        private UniTaskVoid SpawnPooled() => RunScope(async () =>
        {
            _lastScopePooled = await Scope.SpawnPooledAsync(prefabAddress, transform, _cts.Token);
            _lastScopePooled.transform.position = RandomPos();
            _pooledCount++;
            var key = _lastScopePooled.GetComponent<PooledObject>()?.Key;
            Log($"Scope.SpawnPooledAsync('{prefabAddress}') — PooledObject.Key = '{key}'.");
        });

        private UniTaskVoid LoadSprite() => RunScope(async () =>
        {
            ShowSprite(await Scope.LoadAsync<Sprite>(spriteAddress, _cts.Token), spriteAddress);
            Log($"Scope.LoadAsync<Sprite>('{spriteAddress}') — preview shown on the right.");
        });

        private void ReleaseLastInstance()
        {
            if (_lastScopeInstance == null) { Log("No scope instance to release."); return; }
            Scope.ReleaseInstance(_lastScopeInstance);
            _lastScopeInstance = null;
            _instanceCount = Mathf.Max(0, _instanceCount - 1);
            Log("Scope.ReleaseInstance — early, single-item release.");
        }

        private void ReleaseLastPooled()
        {
            if (_lastScopePooled == null) { Log("No scope-pooled instance to release."); return; }
            Scope.ReleasePooled(_lastScopePooled);
            _lastScopePooled = null;
            _pooledCount = Mathf.Max(0, _pooledCount - 1);
            Log("Scope.ReleasePooled — returned one pooled instance to its pool.");
        }

        private void DisposeScope()
        {
            _scope?.Dispose();
            Log($"Scope.Dispose — released everything (IsDisposed = {_scope?.IsDisposed}).");
            _scope = null;
            _lastScopeInstance = null;
            _lastScopePooled = null;
            ShowSprite(null, null);
            _instanceCount = 0;
            _pooledCount = 0;
        }

        // Drive the preview from a load result. The preview later re-checks the loader cache, so it
        // self-clears the moment the underlying sprite is released by ANY path.
        private void ShowSprite(Sprite sprite, string key)
        {
            _loadedSprite = sprite;
            _loadedSpriteKey = sprite != null ? key : null;
        }

        // GameObject.GetAssetScope() — a scope bound to a child's lifetime, auto-disposed on destroy.
        private UniTaskVoid BoundScopeDemo() => RunScope(async () =>
        {
            if (_boundChild != null) { Log("Bound object already exists; destroy it first."); return; }
            _boundChild = new GameObject("BoundScopeChild");
            _boundChild.transform.SetParent(transform);

            var boundScope = _boundChild.GetAssetScope(); // adds an AssetScopeBinder behind the scenes
            var go = await boundScope.InstantiateAsync(prefabAddress, _boundChild.transform, _cts.Token);
            go.transform.position = RandomPos();
            Log("GameObject.GetAssetScope() — instantiated into a bound child. Destroy it to auto-release.");
        });

        private void DestroyBoundObject()
        {
            if (_boundChild == null) { Log("No bound object to destroy."); return; }
            Destroy(_boundChild); // AssetScopeBinder.OnDestroy disposes the scope → instance released
            _boundChild = null;
            Log("Destroyed bound child — its AssetScope auto-disposed.");
        }

        // Component.GetAssetScope() — a scope bound to THIS component's GameObject.
        private UniTaskVoid LoadViaComponentScope() => RunScope(async () =>
        {
            ShowSprite(await this.GetAssetScope().LoadAsync<Sprite>(spriteAddress, _cts.Token), spriteAddress);
            Log("Component.GetAssetScope().LoadAsync<Sprite> — bound to this demo object's lifetime.");
        });

        // =====================================================================
        //  AssetLoader + AssetLocator (static facades)
        // =====================================================================

        private UniTaskVoid LoaderLoad() => RunOneShot(async () =>
        {
            await AssetLoader.LoadAsync<GameObject>(prefabAddress, _cts.Token);
            Log($"AssetLoader.LoadAsync<GameObject>('{prefabAddress}') — IsLoaded now {AssetLoader.IsLoaded<GameObject>(prefabAddress)}.");
        });

        private void LoaderIsLoaded()
            => Log($"AssetLoader.IsLoaded<GameObject>('{prefabAddress}') = {AssetLoader.IsLoaded<GameObject>(prefabAddress)}.");

        private void LoaderRelease()
        {
            AssetLoader.Release<GameObject>(prefabAddress);
            Log($"AssetLoader.Release<GameObject>('{prefabAddress}') — IsLoaded now {AssetLoader.IsLoaded<GameObject>(prefabAddress)}.");
        }

        private void LoaderReleaseAll()
        {
            AssetLoader.ReleaseAll();
            Log("AssetLoader.ReleaseAll — every cached handle force-released.");
        }

        private UniTaskVoid LocatorExists() => RunOneShot(async () =>
        {
            bool any = await AssetLocator.ExistsAsync(prefabAddress, _cts.Token);
            bool typed = await AssetLocator.ExistsAsync<GameObject>(prefabAddress, _cts.Token);
            Log($"AssetLocator.ExistsAsync('{prefabAddress}') = {any}; ExistsAsync<GameObject> = {typed}.");
        });

        private UniTaskVoid LocatorTryLoad() => RunOneShot(async () =>
        {
            var sprite = await AssetLocator.TryLoadAsync<Sprite>(spriteAddress, _cts.Token);
            Log($"AssetLocator.TryLoadAsync<Sprite>('{spriteAddress}') → {(sprite != null ? "loaded" : "null (no location)")}.");
            if (sprite != null) AssetLoader.Release<Sprite>(spriteAddress);
        });

        // =====================================================================
        //  AddressablePool (static facade) + PooledObject
        // =====================================================================

        private UniTaskVoid PoolPrewarm() => RunOneShot(async () =>
        {
            await AddressablePool.Prewarm(prefabAddress, 3, _cts.Token);
            Log($"AddressablePool.Prewarm('{prefabAddress}', 3) — 3 inactive instances ready.");
        });

        private UniTaskVoid PoolGet() => RunOneShot(async () =>
        {
            _directPooled = await AddressablePool.GetAsync(prefabAddress, transform, _cts.Token);
            _directPooled.transform.position = RandomPos();
            Log($"AddressablePool.GetAsync('{prefabAddress}') — PooledObject.Key = '{_directPooled.GetComponent<PooledObject>()?.Key}'.");
        });

        private void PoolReleaseDirect()
        {
            if (_directPooled == null) { Log("No direct-pool instance to release."); return; }
            AddressablePool.Release(_directPooled);
            _directPooled = null;
            Log("AddressablePool.Release — returned the instance to its pool (deactivated, not destroyed).");
        }

        private void PoolClear()
        {
            AddressablePool.ClearPool(prefabAddress);
            _directPooled = null;
            Log($"AddressablePool.ClearPool('{prefabAddress}') — destroyed its instances + released the prefab.");
        }

        private void PoolClearAll()
        {
            AddressablePool.ClearAll();
            _directPooled = null;
            Log("AddressablePool.ClearAll — every pool cleared.");
        }

        // =====================================================================
        //  ComponentReference<T>
        // =====================================================================

        private UniTaskVoid ComponentInstantiate() => RunOneShot(async () =>
        {
            if (!ReferenceAssigned(componentReference)) return;
            ReleaseComponentInstance();
            _componentInstance = componentReference.InstantiateComponentAsync(transform);
            var sr = await _componentInstance.ToUniTask(cancellationToken: _cts.Token);
            _hasComponentInstance = true;
            if (sr != null) sr.transform.position = RandomPos();
            Log($"ComponentReference.InstantiateComponentAsync → {(sr != null ? sr.GetType().Name : "null")} (caller-owned handle).");
        });

        private UniTaskVoid ComponentLoadAsset() => RunOneShot(async () =>
        {
            if (!ReferenceAssigned(componentReference)) return;
            ReleaseComponentAsset();
            _componentAsset = componentReference.LoadComponentAsync();
            var sr = await _componentAsset.ToUniTask(cancellationToken: _cts.Token);
            _hasComponentAsset = true;
            Log($"ComponentReference.LoadComponentAsync → {(sr != null ? sr.GetType().Name : "null")} (asset, not an instance).");
        });

        private void ComponentRelease()
        {
            if (!_hasComponentInstance && !_hasComponentAsset)
            {
                Log("ComponentReference: nothing held to release.");
                return;
            }
            ReleaseComponentHandles();
            Log("ComponentReference: released instance (ReleaseInstance) + loaded asset (Release chain + ReleaseAsset).");
        }

        private void ReleaseComponentHandles()
        {
            ReleaseComponentInstance();
            ReleaseComponentAsset();
        }

        private void ReleaseComponentInstance()
        {
            if (_hasComponentInstance)
            {
                componentReference.ReleaseInstance(_componentInstance);
                _hasComponentInstance = false;
            }
        }

        private void ReleaseComponentAsset()
        {
            if (_hasComponentAsset)
            {
                // Free the chain handle returned by LoadComponentAsync, THEN clear the underlying
                // AssetReference's cached operation. AssetReference.LoadAssetAsync caches its handle in
                // m_Operation; releasing only the chain leaves that set, so the next LoadComponentAsync
                // returns an invalid handle and throws. ReleaseAsset() resets it so re-loading works.
                if (_componentAsset.IsValid()) Addressables.Release(_componentAsset);
                componentReference.ReleaseAsset();
                _hasComponentAsset = false;
            }
        }

        // =====================================================================
        //  SpriteAtlasLoader
        // =====================================================================

        private UniTaskVoid AtlasLoad() => RunOneShot(async () =>
        {
            var key = SpriteAtlasLoader.Key(atlasKey, atlasSpriteName);
            Log($"SpriteAtlasLoader.Key → '{key}'.");
            ShowSprite(await SpriteAtlasLoader.LoadAsync(atlasKey, atlasSpriteName, _cts.Token), key);
            Log($"SpriteAtlasLoader.LoadAsync('{atlasKey}', '{atlasSpriteName}') — loaded.");
        });

        private UniTaskVoid AtlasLoadOrFallback() => RunOneShot(async () =>
        {
            var sprite = await SpriteAtlasLoader.LoadOrFallbackAsync(
                atlasKey, atlasSpriteName, fallbackAtlasKey, fallbackSpriteName, _cts.Token);

            // We don't get told which key won — ask the loader which one it actually cached.
            var primary = SpriteAtlasLoader.Key(atlasKey, atlasSpriteName);
            var fallback = SpriteAtlasLoader.Key(fallbackAtlasKey, fallbackSpriteName);
            var key = AssetLoader.IsLoaded<Sprite>(primary) ? primary
                    : AssetLoader.IsLoaded<Sprite>(fallback) ? fallback : null;

            ShowSprite(sprite, key);
            Log($"SpriteAtlasLoader.LoadOrFallbackAsync → {(sprite != null ? $"got '{key}'" : "null (both missing)")}.");
        });

        private void AtlasRelease()
        {
            SpriteAtlasLoader.Release(atlasKey, atlasSpriteName);
            Log($"SpriteAtlasLoader.Release('{atlasKey}', '{atlasSpriteName}').");
        }

        // =====================================================================
        //  SceneLoader
        // =====================================================================

        private UniTaskVoid SceneLoad() => RunOneShot(async () =>
        {
            SceneInstance scene = await SceneLoader.LoadAsync(
                sceneAddress, LoadSceneMode.Additive, activateOnLoad: true, _cts.Token);
            Log($"SceneLoader.LoadAsync('{sceneAddress}', Additive) → '{scene.Scene.name}'.");
        });

        private void SceneIsLoaded()
            => Log($"SceneLoader.IsAdditiveLoaded('{sceneAddress}') = {SceneLoader.IsAdditiveLoaded(sceneAddress)}.");

        private UniTaskVoid SceneUnload() => RunOneShot(async () =>
        {
            await SceneLoader.UnloadAsync(sceneAddress, heavyUnload: false, _cts.Token);
            Log($"SceneLoader.UnloadAsync('{sceneAddress}') — additive scene unloaded.");
        });

        // =====================================================================
        //  Remote: ContentDownloader / CatalogUpdater / RemoteContentUpdater / AddressableCdn
        // =====================================================================

        private List<object> RemoteKeys()
        {
            var keys = new List<object> { prefabAddress, spriteAddress };
            if (!string.IsNullOrEmpty(remoteLabel)) keys.Add(remoteLabel);
            return keys;
        }

        private UniTaskVoid DownloadSizeSingle() => RunOneShot(async () =>
        {
            long size = await ContentDownloader.GetDownloadSizeAsync(prefabAddress, _cts.Token);
            Log($"ContentDownloader.GetDownloadSizeAsync('{prefabAddress}') = {size} bytes (0 = local/cached).");
        });

        private UniTaskVoid DownloadSizeMulti() => RunOneShot(async () =>
        {
            long size = await ContentDownloader.GetDownloadSizeAsync(RemoteKeys(), _cts.Token);
            Log($"ContentDownloader.GetDownloadSizeAsync(keys) = {size} bytes (union — shared bundles counted once).");
        });

        private UniTaskVoid DownloadSingle() => RunOneShot(async () =>
        {
            var progress = new Progress<DownloadProgress>(p => _downloadPercent = p.Percent);
            await ContentDownloader.DownloadAsync(prefabAddress, progress, _cts.Token);
            Log($"ContentDownloader.DownloadAsync('{prefabAddress}') — done.");
        });

        private UniTaskVoid DownloadMulti() => RunOneShot(async () =>
        {
            var progress = new Progress<DownloadProgress>(p => _downloadPercent = p.Percent);
            await ContentDownloader.DownloadAsync(RemoteKeys(), progress, _cts.Token);
            Log("ContentDownloader.DownloadAsync(keys) — union download done.");
        });

        private UniTaskVoid ClearCache() => RunOneShot(async () =>
        {
            bool ok = await ContentDownloader.ClearCacheAsync(prefabAddress, _cts.Token);
            Log($"ContentDownloader.ClearCacheAsync('{prefabAddress}') = {ok}.");
        });

        private UniTaskVoid CatalogCheck() => RunOneShot(async () =>
        {
            var updated = await CatalogUpdater.CheckAndUpdateCatalogsAsync(_cts.Token);
            Log($"CatalogUpdater.CheckAndUpdateCatalogsAsync → {updated.Count} catalog(s) updated.");
        });

        private void CatalogClearForResume()
        {
            CatalogUpdater.ClearCatalogCacheForResume();
            Log("CatalogUpdater.ClearCatalogCacheForResume — catalog-hash cache cleared (bundles kept).");
        }

        private UniTaskVoid RunFullUpdate() => RunOneShot(async () =>
        {
            var progress = new Progress<DownloadProgress>(p => _downloadPercent = p.Percent);
            DownloadResult result = await RemoteContentUpdater.RunAsync(RemoteKeys(), progress, Confirm, _cts.Token);
            Log($"RemoteContentUpdater.RunAsync → {result.Outcome} ({result.Bytes} bytes). IsSuccess = {result.IsSuccess}.");
        });

        private void CdnInstall()
        {
            try
            {
                var s = AddressablesToolkitSettings.Instance;
                var env = s.ResolveEnvironment();
                var baseUrl = env != null && !string.IsNullOrEmpty(env.CdnBaseUrl)
                    ? env.CdnBaseUrl
                    : "https://cdn.example.com/Addressables";
                AddressableCdn.Install(baseUrl, s.ResolveVersion(), s.ResolvePlatformFolder());
                _cdnInstalled = true;
                Log($"AddressableCdn.Install → {baseUrl}/{PlatformFolderSafe()}/{s.ResolveVersion()} (local content is left alone).");
            }
            catch (Exception e) { Log($"AddressableCdn.Install failed: {e.Message}"); }
        }

        private void CdnUninstall()
        {
            AddressableCdn.Uninstall();
            _cdnInstalled = false;
            Log("AddressableCdn.Uninstall — WebRequestOverride cleared.");
        }

        // =====================================================================
        //  Settings runtime overrides
        // =====================================================================

        private void ApplyEnvOverride()
        {
            AddressablesToolkitSettings.EnvironmentOverride =
                string.IsNullOrWhiteSpace(_envOverrideField) ? null : _envOverrideField.Trim();
            Log($"AddressablesToolkitSettings.EnvironmentOverride = '{AddressablesToolkitSettings.EnvironmentOverride}'. " +
                $"Resolves to '{AddressablesToolkitSettings.Instance.ResolveEnvironmentName()}'.");
        }

        private void ReassertSettingsInstance()
        {
            // OverrideInstance lets tests/custom loaders supply the active asset; re-asserting the
            // current one is a harmless way to show the call.
            AddressablesToolkitSettings.OverrideInstance(AddressablesToolkitSettings.Instance);
            Log("AddressablesToolkitSettings.OverrideInstance(current) — active settings re-asserted.");
        }

        // =====================================================================
        //  Shared runners / helpers
        // =====================================================================

        // Scope ops can run concurrently; they only need a non-disposed scope.
        private UniTaskVoid RunScope(Func<UniTask> op) => RunGuarded(op, guardBusy: false);

        // One-shot static-facade ops are guarded so two don't race on the same key.
        private UniTaskVoid RunOneShot(Func<UniTask> op) => RunGuarded(op, guardBusy: true);

        private async UniTaskVoid RunGuarded(Func<UniTask> op, bool guardBusy)
        {
            if (guardBusy)
            {
                if (_busy) { Log("Busy — wait for the current operation to finish."); return; }
                _busy = true;
            }
            try { await op(); }
            catch (OperationCanceledException) { Log("Operation cancelled."); }
            catch (Exception e) { Log($"Failed: {e.Message}"); }
            finally { if (guardBusy) _busy = false; }
        }

        private bool ReferenceAssigned(AssetReference reference)
        {
            if (reference != null && reference.RuntimeKeyIsValid()) return true;
            Log("Assign a valid AssetReference in the inspector first.");
            return false;
        }

        private static string PlatformFolderSafe()
        {
            try { return AddressableCdn.GetPlatformFolder(); }
            catch (Exception e) { return $"<unsupported: {e.Message}>"; }
        }

        private static Vector3 RandomPos()
            => new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));

        private void Log(string message)
        {
            Debug.Log($"[FullDemo] {message}");
            _entries.Insert(0, new LogEntry(DateTime.Now.ToString("HH:mm:ss"), message, ColorFor(message)));
            if (_entries.Count > 200)
                _entries.RemoveRange(200, _entries.Count - 200);
            _logScroll = Vector2.zero; // keep the newest line in view
        }

        // Infer a line color from the message so outcomes read at a glance — no need to thread a
        // severity through the ~40 call sites.
        private static Color ColorFor(string message)
        {
            var m = message.ToLowerInvariant();
            if (m.Contains("fail") || m.Contains("not found") || m.Contains("no instance") ||
                m.Contains("no scope") || m.Contains("error") || m.Contains("assign a valid"))
                return new Color(1f, 0.45f, 0.45f);                       // red — failure / nothing to do
            if (m.Contains("cancel") || m.Contains("busy") || m.Contains("declined") ||
                m.Contains("null") || m.Contains("already") || m.Contains("did not reach"))
                return new Color(1f, 0.85f, 0.35f);                       // amber — caution / no-op
            if (m.Contains("ready") || m.Contains("success") || m.Contains("done") ||
                m.Contains(" loaded") || m.Contains("instantiated") || m.Contains("spawned") ||
                m.Contains("updated"))
                return new Color(0.45f, 0.9f, 0.5f);                      // green — success
                                                                          // (" loaded" avoids matching "IsLoaded")
            return new Color(0.85f, 0.9f, 1f);                            // default — info
        }

        // =====================================================================
        //  UI (IMGUI — no assets required)
        // =====================================================================

        private void OnGUI()
        {
            const float pad = 10f;

            // --- LEFT: controls -------------------------------------------------
            float leftWidth = Mathf.Min(410f, Screen.width * 0.42f);
            var leftRect = new Rect(pad, pad, leftWidth, Screen.height - pad * 2f);
            GUILayout.BeginArea(leftRect, GUI.skin.box);
            GUILayout.Label("<b>Addressables Toolkit — Full Demo</b>", RichLabel());

            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            DrawSettingsSummary();
            DrawSettingsOverrides();
            DrawServiceSection();
            DrawScopeSection();
            DrawLoaderSection();
            DrawPoolSection();
            DrawComponentReferenceSection();
            DrawAtlasSection();
            DrawSceneSection();
            DrawRemoteSection();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // --- RIGHT: large visual activity monitor ---------------------------
            float rightWidth = Mathf.Clamp(Screen.width - leftWidth - pad * 3f, 260f, 460f);
            var rightRect = new Rect(Screen.width - rightWidth - pad, pad, rightWidth, Screen.height - pad * 2f);
            DrawActivityPanel(rightRect);

            DrawConfirmDialog();
        }

        private void DrawSettingsSummary()
        {
            var s = AddressablesToolkitSettings.Instance;
            Header("Settings");
            GUILayout.Label($"Source: {s.contentSource}   Override URL: {s.overrideRemoteUrl}");
            GUILayout.Label($"Environment: {s.ResolveEnvironmentName()}");
            var env = s.ResolveEnvironment();
            GUILayout.Label($"CDN: {(env != null ? env.CdnBaseUrl : "<none>")}");
            GUILayout.Label($"Version: {s.ResolveVersion()}");
            GUILayout.Label($"Platform folder: {s.ResolvePlatformFolder() ?? PlatformFolderSafe()}");
            GUILayout.Label($"Preload labels ({s.GetPreloadKeys().Count}): {string.Join(", ", s.GetPreloadKeys())}");
        }

        private void DrawSettingsOverrides()
        {
            Header("Settings · runtime overrides");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Env override:", GUILayout.Width(80f));
            _envOverrideField = GUILayout.TextField(_envOverrideField ?? "");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply EnvironmentOverride")) ApplyEnvOverride();
            if (GUILayout.Button("Re-assert OverrideInstance")) ReassertSettingsInstance();
            GUILayout.EndHorizontal();
        }

        private void DrawServiceSection()
        {
            Header("1 · Service (IAddressablesService)");
            GUILayout.Label($"State: {_service.State}   Ready: {_service.IsReady}");
            GUILayout.Label($"LastDownloadResult: {_service.LastDownloadResult.Outcome}");

            using (new GuiDisabledScope(_initInFlight))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_service.IsReady ? "Re-run Initialize" : "Initialize"))
                    StartInitialize();
                if (GUILayout.Button("Init (explicit settings)"))
                    StartInitializeExplicit();
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel")) CancelCurrent();
            if (GUILayout.Button("Reset service")) ResetService();
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Live state, progress, sprite & log are on the right →</i>", RichLabel());
        }

        private void DrawScopeSection()
        {
            Header("2 · AssetScope (recommended)");
            GUILayout.Label($"Held → Instances: {_instanceCount}   Pooled: {_pooledCount}   Disposed: {_scope?.IsDisposed ?? false}");
            AssignedLine("prefabReference", prefabReference != null && prefabReference.RuntimeKeyIsValid());

            using (new GuiDisabledScope(!_service.IsReady))
            {
                if (GUILayout.Button("Load asset by key")) LoadAssetByKey().Forget();
                if (GUILayout.Button("Load asset by AssetReference")) LoadAssetByReference().Forget();
                if (GUILayout.Button("Instantiate by key")) InstantiateByKey().Forget();
                if (GUILayout.Button("Instantiate by AssetReference")) InstantiateByReference().Forget();
                if (GUILayout.Button("Instantiate component<SpriteRenderer>")) InstantiateComponentByKey().Forget();
                if (GUILayout.Button("Spawn pooled")) SpawnPooled().Forget();
                if (GUILayout.Button("Load sprite")) LoadSprite().Forget();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Release last instance")) ReleaseLastInstance();
                if (GUILayout.Button("Release last pooled")) ReleaseLastPooled();
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Bound-scope demo (GameObject.GetAssetScope)")) BoundScopeDemo().Forget();
                if (GUILayout.Button("Load via this.GetAssetScope() (component ext)")) LoadViaComponentScope().Forget();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Destroy bound object")) DestroyBoundObject();
            if (GUILayout.Button("Dispose scope")) DisposeScope();
            GUILayout.EndHorizontal();
        }

        private void DrawLoaderSection()
        {
            Header("3 · AssetLoader + AssetLocator");
            GUILayout.Label("<i>AssetLoader.Default cache (AssetScope & AddressablePool share it):</i>", RichLabel());
            HeldLine($"{prefabAddress} <GameObject>", AssetLoader.IsLoaded<GameObject>(prefabAddress));
            HeldLine($"{spriteAddress} <Sprite>", AssetLoader.IsLoaded<Sprite>(spriteAddress));
            using (new GuiDisabledScope(!_service.IsReady))
            {
                if (GUILayout.Button("LoadAsync<GameObject>")) LoaderLoad().Forget();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("IsLoaded?")) LoaderIsLoaded();
                if (GUILayout.Button("Release")) LoaderRelease();
                if (GUILayout.Button("ReleaseAll")) LoaderReleaseAll();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Exists?")) LocatorExists().Forget();
                if (GUILayout.Button("TryLoad sprite")) LocatorTryLoad().Forget();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPoolSection()
        {
            Header("4 · AddressablePool + PooledObject");
            HeldLine("direct pooled instance active (Get → Release)", _directPooled != null);
            using (new GuiDisabledScope(!_service.IsReady))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Prewarm(3)")) PoolPrewarm().Forget();
                if (GUILayout.Button("Get")) PoolGet().Forget();
                if (GUILayout.Button("Release")) PoolReleaseDirect();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ClearPool")) PoolClear();
                if (GUILayout.Button("ClearAll")) PoolClearAll();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawComponentReferenceSection()
        {
            Header("5 · ComponentReference<T>");
            bool assigned = componentReference != null && componentReference.RuntimeKeyIsValid();
            AssignedLine("componentReference", assigned);
            HeldLine("instance held", _hasComponentInstance);
            HeldLine("loaded asset held", _hasComponentAsset);
            using (new GuiDisabledScope(!_service.IsReady || !assigned))
            {
                if (GUILayout.Button("InstantiateComponentAsync")) ComponentInstantiate().Forget();
                if (GUILayout.Button("LoadComponentAsync (asset)")) ComponentLoadAsset().Forget();
            }
            if (GUILayout.Button("Release instance + asset")) ComponentRelease();
        }

        private void DrawAtlasSection()
        {
            Header("6 · SpriteAtlasLoader");
            HeldLine(SpriteAtlasLoader.Key(atlasKey, atlasSpriteName), AssetLoader.IsLoaded<Sprite>(SpriteAtlasLoader.Key(atlasKey, atlasSpriteName)));
            HeldLine(SpriteAtlasLoader.Key(fallbackAtlasKey, fallbackSpriteName), AssetLoader.IsLoaded<Sprite>(SpriteAtlasLoader.Key(fallbackAtlasKey, fallbackSpriteName)));
            using (new GuiDisabledScope(!_service.IsReady))
            {
                if (GUILayout.Button("LoadAsync (atlas sprite)")) AtlasLoad().Forget();
                if (GUILayout.Button("LoadOrFallbackAsync")) AtlasLoadOrFallback().Forget();
            }
            if (GUILayout.Button("Release atlas sprite")) AtlasRelease();
        }

        private void DrawSceneSection()
        {
            Header("7 · SceneLoader");
            HeldLine($"'{sceneAddress}' additive loaded", SceneLoader.IsAdditiveLoaded(sceneAddress));
            using (new GuiDisabledScope(!_service.IsReady))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Load additive")) SceneLoad().Forget();
                if (GUILayout.Button("Is loaded?")) SceneIsLoaded();
                if (GUILayout.Button("Unload")) SceneUnload().Forget();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawRemoteSection()
        {
            Header("8 · Remote (CDN / catalog / download)");
            HeldLine("CDN override installed", _cdnInstalled);
            GUILayout.Label($"   download {_downloadPercent:P0}   ·   last: {_service.LastDownloadResult.Outcome}");
            using (new GuiDisabledScope(!_service.IsReady))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Size (1)")) DownloadSizeSingle().Forget();
                if (GUILayout.Button("Size (keys)")) DownloadSizeMulti().Forget();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Download (1)")) DownloadSingle().Forget();
                if (GUILayout.Button("Download (keys)")) DownloadMulti().Forget();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear cache")) ClearCache().Forget();
                if (GUILayout.Button("Catalog check")) CatalogCheck().Forget();
                GUILayout.EndHorizontal();
                if (GUILayout.Button("RemoteContentUpdater.RunAsync (full flow)")) RunFullUpdate().Forget();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Catalog clear-for-resume")) CatalogClearForResume();
            if (GUILayout.Button("CDN install")) CdnInstall();
            if (GUILayout.Button("CDN uninstall")) CdnUninstall();
            GUILayout.EndHorizontal();
        }

        // cached IMGUI styles (built lazily inside OnGUI when the skin is available)
        private GUIStyle _logStyle;
        private GUIStyle _bigStyle;

        // The right-hand "Activity Monitor": live status, progress, sprite preview, and the big log.
        private void DrawActivityPanel(Rect rect)
        {
            _logStyle ??= new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true, fontSize = 12 };
            _bigStyle ??= new GUIStyle(GUI.skin.label) { richText = false, fontStyle = FontStyle.Bold, fontSize = 15 };

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>Activity Monitor</b>", RichLabel());

            DrawStatusDashboard();
            DrawProgressBar();
            DrawSpritePreview(rect.width);

            GUILayout.Space(4f);
            GUILayout.Label("<b>Log</b>  <i>(newest first)</i>", RichLabel());
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            if (_entries.Count == 0)
                GUILayout.Label("No activity yet — press Initialize, then try the controls on the left.", _logStyle);
            foreach (var e in _entries)
            {
                var prev = GUI.contentColor;
                GUI.contentColor = e.Color;
                GUILayout.Label($"{e.Time}  {e.Message}", _logStyle);
                GUI.contentColor = prev;
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawStatusDashboard()
        {
            var state = _service.State;
            Color stateColor =
                state == AddressablesState.Ready ? new Color(0.45f, 0.9f, 0.5f) :
                state == AddressablesState.Failed ? new Color(1f, 0.45f, 0.45f) :
                state == AddressablesState.Uninitialized ? new Color(0.85f, 0.9f, 1f) :
                new Color(1f, 0.85f, 0.35f); // any in-progress state

            var prev = GUI.contentColor;
            GUI.contentColor = stateColor;
            GUILayout.Label($"●  State: {state}", _bigStyle);
            GUI.contentColor = prev;

            GUILayout.Label($"Ready: {_service.IsReady}     Busy: {_busy}     Init: {_initInFlight}");
            GUILayout.Label($"Instances: {_instanceCount}     Pooled: {_pooledCount}");
            GUILayout.Label($"Scope disposed: {_scope?.IsDisposed ?? false}     Bound object: {_boundChild != null}");
            GUILayout.Label($"Last download: {_service.LastDownloadResult.Outcome}");
            GUILayout.Label("<i>Each section (left) shows its own live ● held / ○ free status.</i>", RichLabel());
        }

        private static void HeldLine(string label, bool held)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = held ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.6f, 0.6f, 0.65f);
            GUILayout.Label($"   {(held ? "●" : "○")} {label}");
            GUI.contentColor = prev;
        }

        // Inspector-assignment hint for the serialized AssetReference fields.
        private static void AssignedLine(string field, bool assigned)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = assigned ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.85f, 0.35f);
            GUILayout.Label(assigned ? $"   ● {field}: assigned" : $"   ○ {field}: assign in inspector", RichLabel());
            GUI.contentColor = prev;
        }

        private void DrawProgressBar()
        {
            var bar = GUILayoutUtility.GetRect(100, 22, GUILayout.ExpandWidth(true));
            GUI.Box(bar, GUIContent.none);
            var fill = new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_downloadPercent), bar.height);
            GUI.color = new Color(0.3f, 0.7f, 1f);
            GUI.Box(fill, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(bar, $"  download {_downloadPercent:P0}");
        }

        private void DrawSpritePreview(float panelWidth)
        {
            // Honesty check: if the loader no longer holds the shown sprite (released by any path —
            // AtlasRelease, Dispose scope, ReleaseAll, …), drop the stale preview so it never lies.
            if (_loadedSpriteKey != null && !AssetLoader.IsLoaded<Sprite>(_loadedSpriteKey))
                ShowSprite(null, null);

            GUILayout.Label(_loadedSpriteKey != null
                ? $"<b>Loaded sprite:</b> {_loadedSpriteKey}"
                : "<b>Loaded sprite:</b> (none)", RichLabel());

            float size = Mathf.Clamp(panelWidth - 30f, 80f, 140f);
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUI.Box(r, GUIContent.none);
            if (_loadedSprite != null && _loadedSprite.texture != null)
                GUI.DrawTexture(r, _loadedSprite.texture, ScaleMode.ScaleToFit, true);
            else
                GUI.Label(new Rect(r.x + 6, r.y + size / 2f - 10f, size, 20f), "(released / none)");
        }

        private void DrawConfirmDialog()
        {
            if (!_awaitingConfirm) return;

            var w = 320f; var h = 130f;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("<b>Download required</b>", RichLabel());
            GUILayout.Label($"This will download {_confirmBytes:N0} bytes. Continue?");
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yes")) ResolveConfirm(true);
            if (GUILayout.Button("No")) ResolveConfirm(false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ResolveConfirm(bool answer)
        {
            _awaitingConfirm = false;
            _confirmTcs?.TrySetResult(answer);
        }

        private static void Header(string title)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"<b>{title}</b>", RichLabel());
        }

        private static GUIStyle RichLabel() => new(GUI.skin.label) { richText = true };

        /// <summary>Tiny helper so <c>using</c> can scope <see cref="GUI.enabled"/>.</summary>
        private readonly struct GuiDisabledScope : IDisposable
        {
            private readonly bool _previous;
            public GuiDisabledScope(bool disabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = _previous && !disabled;
            }
            public void Dispose() => GUI.enabled = _previous;
        }
    }
}
