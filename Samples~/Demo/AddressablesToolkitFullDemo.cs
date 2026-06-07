using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace KidzDev.AddressablesToolkit.Samples
{
    /// <summary>
    /// Interactive, self-contained tour of the whole toolkit. Drop this on an empty GameObject and
    /// press Play — the UI is drawn with IMGUI (<see cref="OnGUI"/>), so it needs no scene, canvas,
    /// or prefab assets. Configure the addressable keys in the inspector to match your groups.
    ///
    /// It demonstrates, end to end:
    /// <list type="bullet">
    ///   <item>reading <see cref="AddressablesToolkitSettings"/> (content source, environment, CDN, preload),</item>
    ///   <item>the <see cref="AddressablesService"/> init flow with live progress and a confirm dialog,</item>
    ///   <item><see cref="AssetScope"/> load / instantiate / pool / sprite — by key AND by AssetReference,</item>
    ///   <item>early single-item release and whole-scope disposal.</item>
    /// </list>
    /// </summary>
    public class AddressablesToolkitFullDemo : MonoBehaviour
    {
        [Header("Addressable keys (must exist in your Addressables groups)")]
        [SerializeField] private string prefabAddress = "demo-prefab";
        [SerializeField] private string spriteAddress = "demo-sprite";

        [Header("Optional — assign an AssetReference to a prefab in the inspector")]
        [SerializeField] private AssetReference prefabReference;

        // --- demo state ------------------------------------------------------
        // Depend on the IAddressablesService seam (resolved to the process-wide default here).
        private readonly IAddressablesService _service = AddressablesService.Default;
        private AssetScope _scope;
        private CancellationTokenSource _cts;

        private float _downloadPercent;
        private bool _initInFlight;
        private int _instanceCount;
        private int _pooledCount;
        private GameObject _lastInstance;
        private Sprite _loadedSprite;

        // confirm-dialog plumbing
        private bool _awaitingConfirm;
        private long _confirmBytes;
        private UniTaskCompletionSource<bool> _confirmTcs;

        // scrolling log
        private string _log = "";
        private Vector2 _logScroll;

        private AssetScope Scope => _scope ??= new AssetScope();

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _scope?.Dispose(); // releases every asset/instance/pooled object the demo borrowed
        }

        // =====================================================================
        //  Flow
        // =====================================================================

        private void StartInitialize()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            InitializeAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid InitializeAsync(CancellationToken ct)
        {
            _initInFlight = true;
            _downloadPercent = 0f;
            Log("Initializing…");

            var progress = new Progress<DownloadProgress>(p => _downloadPercent = p.Percent);
            bool ready = await _service.InitializeAsync(progress, Confirm, ct);

            _initInFlight = false;
            Log(ready
                ? "Ready — content can be loaded."
                : $"Init did not reach Ready: {_service.LastDownloadResult.Outcome}.");
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
        //  Asset operations (all routed through the lifetime-bound scope)
        // =====================================================================

        private async UniTaskVoid InstantiateByKey()
        {
            try
            {
                _lastInstance = await Scope.InstantiateAsync(prefabAddress, transform);
                _lastInstance.transform.position = RandomPos();
                _instanceCount++;
                Log($"Instantiated '{prefabAddress}' by key.");
            }
            catch (Exception e) { Log($"Instantiate (key) failed: {e.Message}"); }
        }

        private async UniTaskVoid InstantiateByReference()
        {
            if (prefabReference == null || !prefabReference.RuntimeKeyIsValid())
            {
                Log("Assign a valid AssetReference in the inspector first.");
                return;
            }

            try
            {
                _lastInstance = await Scope.InstantiateAsync(prefabReference, transform);
                _lastInstance.transform.position = RandomPos();
                _instanceCount++;
                Log("Instantiated by AssetReference.");
            }
            catch (Exception e) { Log($"Instantiate (reference) failed: {e.Message}"); }
        }

        private async UniTaskVoid SpawnPooled()
        {
            try
            {
                var go = await Scope.SpawnPooledAsync(prefabAddress, transform);
                go.transform.position = RandomPos();
                _pooledCount++;
                Log($"Spawned pooled '{prefabAddress}' (total {_pooledCount}).");
            }
            catch (Exception e) { Log($"Spawn pooled failed: {e.Message}"); }
        }

        private async UniTaskVoid LoadSprite()
        {
            try
            {
                _loadedSprite = await Scope.LoadAsync<Sprite>(spriteAddress);
                Log($"Loaded sprite '{spriteAddress}'.");
            }
            catch (Exception e) { Log($"Load sprite failed: {e.Message}"); }
        }

        private void ReleaseLastInstance()
        {
            if (_lastInstance == null) { Log("No instance to release."); return; }
            Scope.ReleaseInstance(_lastInstance);
            _lastInstance = null;
            _instanceCount = Mathf.Max(0, _instanceCount - 1);
            Log("Released last instantiated object (early, single-item).");
        }

        private void DisposeScope()
        {
            _scope?.Dispose();
            _scope = null;
            _lastInstance = null;
            _loadedSprite = null;
            _instanceCount = 0;
            _pooledCount = 0;
            Log("Disposed scope — every asset, instance, and pooled object released at once.");
        }

        private static Vector3 RandomPos()
            => new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));

        private void Log(string message)
        {
            Debug.Log($"[FullDemo] {message}");
            _log = $"{DateTime.Now:HH:mm:ss}  {message}\n{_log}";
            if (_log.Length > 4000)
                _log = _log.Substring(0, 4000);
        }

        // =====================================================================
        //  UI (IMGUI — no assets required)
        // =====================================================================

        private void OnGUI()
        {
            const float pad = 10f;
            var rect = new Rect(pad, pad, 380f, Screen.height - pad * 2f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>Addressables Toolkit — Full Demo</b>", RichLabel());

            DrawSettingsSummary();
            GUILayout.Space(6f);
            DrawInitSection();
            GUILayout.Space(6f);
            DrawAssetSection();
            GUILayout.Space(6f);
            DrawLog();

            GUILayout.EndArea();

            DrawSpritePreview();
            DrawConfirmDialog();
        }

        private void DrawSettingsSummary()
        {
            var s = AddressablesToolkitSettings.Instance;
            GUILayout.Label("<b>Settings</b>", RichLabel());
            GUILayout.Label($"Source: {s.contentSource}");
            if (s.contentSource == ContentSource.Remote)
            {
                var env = s.ResolveEnvironment();
                GUILayout.Label($"Environment: {s.ResolveEnvironmentName()}");
                GUILayout.Label($"CDN: {(env != null ? env.CdnBaseUrl : "<none>")}");
                GUILayout.Label($"Version: {s.ResolveVersion()}");
                GUILayout.Label($"Override URL: {s.overrideRemoteUrl}");
                GUILayout.Label($"Preload labels: {s.preloadLabels.Count}");
            }
        }

        private void DrawInitSection()
        {
            GUILayout.Label("<b>1 · Initialize</b>", RichLabel());
            GUILayout.Label($"State: {_service.State}");

            using (new GuiDisabledScope(_initInFlight))
            {
                if (GUILayout.Button(_service.IsReady ? "Re-run Initialize" : "Initialize"))
                    StartInitialize();
            }

            if (_initInFlight && GUILayout.Button("Cancel"))
                _cts?.Cancel();

            // progress bar
            var bar = GUILayoutUtility.GetRect(100, 18);
            GUI.Box(bar, GUIContent.none);
            var fill = new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_downloadPercent), bar.height);
            GUI.color = new Color(0.3f, 0.7f, 1f);
            GUI.Box(fill, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(bar, $" download {_downloadPercent:P0}");
        }

        private void DrawAssetSection()
        {
            GUILayout.Label("<b>2 · Load / Instantiate / Release</b>", RichLabel());
            GUILayout.Label($"Instances: {_instanceCount}   Pooled: {_pooledCount}");

            using (new GuiDisabledScope(!_service.IsReady))
            {
                if (GUILayout.Button("Instantiate by key (scope)")) InstantiateByKey().Forget();
                if (GUILayout.Button("Instantiate by AssetReference (scope)")) InstantiateByReference().Forget();
                if (GUILayout.Button("Spawn pooled (scope)")) SpawnPooled().Forget();
                if (GUILayout.Button("Load sprite (scope)")) LoadSprite().Forget();
                if (GUILayout.Button("Release last instance (early)")) ReleaseLastInstance();
            }

            if (GUILayout.Button("Dispose scope — release everything")) DisposeScope();
        }

        private void DrawLog()
        {
            GUILayout.Label("<b>Log</b>", RichLabel());
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(120f));
            GUILayout.Label(_log);
            GUILayout.EndScrollView();
        }

        private void DrawSpritePreview()
        {
            if (_loadedSprite == null) return;
            var tex = _loadedSprite.texture;
            if (tex == null) return;
            var r = new Rect(Screen.width - 138f, 10f, 128f, 128f);
            GUI.Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), "sprite");
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
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
