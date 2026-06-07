# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
