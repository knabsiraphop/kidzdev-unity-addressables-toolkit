# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.1] - 2026-06-14

### Changed

- **Breaking:** the runtime and editor namespaces (and their assembly definitions) were renamed from
  `KidzDev.AddressablesToolkit` / `…Editor` to `KidzDev.Unity.AddressablesToolkit` / `…Editor`.
  Update `using` directives accordingly (e.g. `using KidzDev.Unity.AddressablesToolkit;`). The package
  id (`com.kidzdev.unity.addressables-toolkit`) and the git install URL are unchanged.
- `package.json` now declares an `author` (`KidzDev`), so the Unity Package Manager shows the
  publisher consistently with the other KidzDev packages.

## [1.4.0] - 2026-06-13

Expands the Demo sample into a full tour of every public API, and fixes a `ComponentReference<T>`
instance leak.

### Added

- Demo sample: `demo-atlas.png` — a 4-sprite sheet (Sprite mode **Multiple**: `icon`, `icon_missing`,
  `star`, `heart`) that drives the new `SpriteAtlasLoader` section via the `demo-atlas[icon]`
  sub-object key convention — and `demo-scene.unity`, a tiny additive scene the `SceneLoader` section
  loads/unloads. Committed `.meta` files keep their references stable across imports.

### Changed

- Demo sample: `AddressablesToolkitFullDemo` reworked from a high-level-flow demo into a scrollable
  IMGUI tour that wires **every public API** in the toolkit to a button — added sections for
  `AssetLoader`/`AssetLocator`, `AddressablePool`, `ComponentReference<T>`, `SpriteAtlasLoader`,
  `SceneLoader`, and the full remote stack (`ContentDownloader`, `CatalogUpdater`,
  `RemoteContentUpdater`, `AddressableCdn`), plus a settings readout and runtime overrides. The
  `prefabReference`/`componentReference` fields come pre-wired to `demo-prefab`, and the sample README
  documents marking the four demo assets addressable.

### Fixed

- `ComponentReference<T>.ReleaseInstance` now actually destroys the instance. `InstantiateComponentAsync`
  wraps the instantiation in a chain operation that holds its own reference, so calling
  `Addressables.ReleaseInstance` alone left the instance at a non-zero ref count — it was never
  destroyed and every instantiate leaked a clone. `ReleaseInstance` now releases the chain handle too.

## [1.3.0] - 2026-06-12

Production-hardening pass: fixes from a full audit of v1.2.0, locked in by new test assemblies.

### Added

- Tests: `Tests/Editor` (EditMode) and `Tests/Runtime` (PlayMode) assemblies covering `AssetScope`
  tracking/release, pool recycle/retry/concurrency, settings resolution, and `DownloadResult`
  classification. Run them by adding the package to `"testables"` in the host `manifest.json`.
- `ContentDownloader`: multi-key overloads of `GetDownloadSizeAsync` / `DownloadAsync` that run a
  single union operation, so bundles shared between keys are sized and downloaded once.
- `ComponentReference<T>`: parameterless constructor, so the documented empty subclass
  (`[Serializable] public class EnemyRef : ComponentReference<Enemy> { }`) actually compiles —
  previously it failed with CS7036 because the base types only define the guid constructor.

### Changed

- `RemoteContentUpdater.RunAsync` sizes and downloads across all labels in one union operation.
  Confirm-dialog totals no longer double-count bundles shared between labels, and the reported
  progress is the true aggregate instead of per-label stitching.

### Fixed

- `AddressableCdn`: the `WebRequestOverride` no longer rewrites local requests onto the CDN.
  StreamingAssets-hosted content (every WebGL bundle, Android `jar:file://` URLs, local groups
  opted into UnityWebRequest) is left untouched; only http(s) URLs outside StreamingAssets are
  redirected.
- `AddressablePrefabPool`: a failed prefab load no longer poisons its pool entry — the entry is
  evicted so the next `GetAsync`/`Prewarm` retries. Concurrent `GetAsync`/`Prewarm` calls for a
  cold key now share one load instead of throwing `Already continuation registered`
  (`UniTask.Preserve()` replaced with a `UniTaskCompletionSource`, which supports many awaiters).
- `SceneLoader`: a `Single` load clears the now-dead additive tracking entries; a failed load
  releases its handle; a cancelled load is unloaded once it lands instead of staying resident
  untracked; additively re-loading an already-tracked key logs a warning about the replaced entry.
- Toolkit statics (`AssetLoader.Default`, `AddressablePool.Default`, `AddressablesService.Default`,
  `SceneLoader` tracking, the settings instance and `EnvironmentOverride`) now reset at play-session
  start (`SubsystemRegistration`), so "Enter Play Mode without domain reload" no longer carries a
  Ready service, dead handles, and destroyed pool roots into the next session.

## [1.2.0] - 2026-06-07

SOLID-oriented refactor: a few dependency-inversion seams, one single-responsibility split, and
intent-revealing names. A couple of public names changed (no compatibility shims) — update call sites.

### Added

- `IAssetLoader`, `IAssetPool`, and `IAddressablesService` — the toolkit's substitution/testing seams.
  Each backing facade exposes a `.Default` instance you can inject (`AssetLoader.Default`,
  `AddressablePool.Default`, `AddressablesService.Default`), and `AssetScope` now accepts an
  `IAssetLoader` + `IAssetPool` via its constructor.
- `ReferenceCountedAssetLoader`, `AddressablePrefabPool`, `AddressablesInitializer` — the public default
  implementations behind those interfaces.
- `CatalogUpdater` — catalog check/update + cache-reset, split out of `RemoteContentUpdater` (SRP).

### Changed

- `DownloadHelper` renamed to `ContentDownloader` (intent-revealing).
- `RemoteContentUpdater` now orchestrates only the download flow. `DownloadProgress` and
  `AddressablesState` moved to their own files; internal variable/field names clarified throughout.
- Sample demo updated to depend on the new interfaces.

### Removed

- `RemoteContentUpdater.CheckAndUpdateCatalogsAsync` and `ClearCatalogCacheForResume`
  (moved to `CatalogUpdater`), and the `DownloadHelper` name (use `ContentDownloader`).

## [1.1.1] - 2026-06-07

Turns the Demo sample into a complete, ready-to-run scene instead of scripts only.

### Added

- Sample: a ready-to-run `Demo.unity` scene (orthographic camera + the `AddressablesToolkitFullDemo`
  driver + a preview instance of `demo-prefab`), plus the assets it uses — `demo-prefab.prefab`
  (a `SpriteRenderer`), `demo-sprite.png`, and `Resources/AddressablesToolkitSettings.asset`
  (Content Source = Local, found automatically via `Resources`). Committed `.meta` files keep the
  scene's references stable across imports.

### Removed

- Sample: `AddressablesBootstrapDemo` — its high-level flow is already covered interactively by
  `AddressablesToolkitFullDemo`.

## [1.1.0] - 2026-06-07

A high-level layer that turns the toolkit's individual utilities into a drop-in
initialize-then-use flow for real projects. No breaking changes to existing APIs.

### Added

- `AddressablesToolkitSettings`: one `ScriptableObject` (loaded from `Resources`) for content
  source (Local/Remote), CDN-override toggle, named remote environments (dev/staging/production/
  any), content version + platform overrides, preload labels, and init-flow toggles. Environment
  is overridable at runtime via `EnvironmentOverride` or the `ADDRESSABLES_ENV` variable
- `AddressablesService`: settings-driven initialization flow (CDN install → `InitializeAsync` →
  catalog update → preload download → `Ready`) with an `AddressablesState` machine, `StateChanged`
  event, idempotent/joined calls, threaded cancellation, and optional `autoInitializeOnLaunch`
- `AssetScope`: disposable, lifetime-bound owner for leak-proof load/instantiate/pool/release —
  accepts a key or an `AssetReference`; `this.GetAssetScope()` binds one to a GameObject so it
  releases automatically on destroy (via `AssetScopeBinder` / `AssetScopeExtensions`)
- `AssetLoader.Release(object key, Type type)`: non-generic release used by `AssetScope`
- Editor: **Tools > Addressables Toolkit > Settings** creates/locates the settings asset
- Sample: `AddressablesBootstrapDemo` (minimal high-level flow) and `AddressablesToolkitFullDemo`
  (interactive IMGUI tour of init + scope load/instantiate/pool/release, no scene assets required)

## [1.0.1] - 2026-06-07

Integrates the lessons and capabilities of two shipping Addressables systems
(see `ADDRESSABLE_SYSTEM_synthesis.md`). Standardizes on UniTask.

### Added

- AssetLocator: existence / `TryLoad` probe that always releases its locations handle
- RemoteContentUpdater: full check → update-catalog → size → confirm → download pipeline
  with typed `DownloadResult`, threaded cancellation, and catalog-hash-only resume
- AddressableCdn: `WebRequestOverride` CDN/version URL rewrite with explicit platform mapping
- SpriteAtlasLoader: `"{atlas}[{sprite}]"` sprite loading with optional missing-sprite fallback
- SceneLoader: addressable scene load/unload with additive tracking (no forced GC on unload)
- Editor: BundleSizeManifestBuilder (`{buildTarget}_BundleSize.json`, cross-platform bundle keys)
- Editor: `Validate Runtime Editor Usage` — flags unguarded `using UnityEditor;` in player assemblies
- Tooling: `/release` Claude Code command — bump `package.json` version, commit, tag, and push

### Changed

- **Breaking:** all async APIs return `UniTask`/`UniTask<T>` instead of `System.Threading.Tasks.Task`
- **Breaking:** `AssetLoader.Release`/`IsLoaded` are now generic (`Release<T>` / `IsLoaded<T>`);
  the cache is keyed by `(key, type)` so loading one key as two types no longer returns null
- Adds `com.cysharp.unitask` as a package dependency (requires the OpenUPM scoped registry)
- Scripts organized into objective subfolders (Runtime: Loading, Pooling, Remote, Sprites, Scenes;
  Editor: Build, Validation, Authoring). Assembly names and namespaces are unchanged.

### Fixed

- AssetLoader: type-unsafe cache key returning silent `null` for a key loaded as a second type
- AssetLoader: cancellation is now cooperative (observed during the load, not only after)

### Removed

- `release.sh` / `release.ps1` shell scripts (replaced by the `/release` command)

## [1.0.0] - 2026-06-06

### Added

- AssetLoader: reference-counted async loading
- AddressablePool: prefab pooling with a persistent root
- ComponentReference: typed AssetReference
- DownloadHelper: download size, predownload with progress, cache clear
- Editor tools: mark / label / remove addressables, validation
- Build automation: menu + CLI/CI-callable content build
- Demo sample
