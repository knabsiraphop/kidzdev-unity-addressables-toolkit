# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
