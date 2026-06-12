# KidzDev Addressables Toolkit

Utilities for working with Unity Addressables — reference-counted loading, prefab pooling, typed asset references, a resilient remote-update pipeline, sprite/atlas + scene helpers, and editor/build tooling. The design distills the lessons of two shipping Addressables systems (see `ADDRESSABLE_SYSTEM_synthesis.md`).

## Requirements

- Unity **6000.0** or newer.
- `com.unity.addressables` — installed automatically as a package dependency.
- **UniTask** (`com.cysharp.unitask`) — the toolkit's async backbone. All async APIs return
  `UniTask`/`UniTask<T>`. UniTask is not on Unity's registry, so add the OpenUPM scoped registry
  to your project's `Packages/manifest.json`:

  ```json
  {
    "scopedRegistries": [
      { "name": "package.openupm.com", "url": "https://package.openupm.com", "scopes": ["com.cysharp"] }
    ]
  }
  ```

## Installation

Install via git URL, pinned to a release tag.

**Package Manager** → *Add package from git URL…*

```
https://github.com/knabsiraphop/kidzdev-addressables-toolkit.git#v1.3.0
```

Or add the dependency directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.kidzdev.addressables-toolkit": "https://github.com/knabsiraphop/kidzdev-addressables-toolkit.git#v1.3.0"
  }
}
```

## Usage

The toolkit has two layers. The **high-level layer** (`AddressablesToolkitSettings` +
`AddressablesService` + `AssetScope`) is the recommended way to wire Addressables into a real
project: configure one asset, initialize once, then load/instantiate through lifetime-bound
scopes that release themselves. The **low-level tools** below it (`AssetLoader`, `AddressablePool`,
`ContentDownloader`, …) remain available when you want direct control.

### Architecture

Most of the toolkit is plain static utilities (`AssetLoader`, `AddressablePool`, `ContentDownloader`,
`CatalogUpdater`, `RemoteContentUpdater`, `AddressableCdn`, `AssetLocator`, …). Three of them also
expose an **interface seam** for substitution and unit-testing — `IAssetLoader`, `IAssetPool`, and
`IAddressablesService` — each reachable via a `.Default` instance on its facade
(`AssetLoader.Default`, `AddressablePool.Default`, `AddressablesService.Default`). `AssetScope`
depends on `IAssetLoader` + `IAssetPool` (constructor-injectable), so loading logic can be unit-tested
with fakes. If you don't care about DI, the static facades keep working as-is.

### Quick start

```csharp
using KidzDev.AddressablesToolkit;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class Boot : MonoBehaviour
{
    private async UniTaskVoid Start()
    {
        // 1) Initialize from the settings asset (CDN override → init → catalog → preload).
        if (!await AddressablesService.InitializeAsync())
            return;

        // 2) Load/instantiate through a scope bound to this GameObject's lifetime.
        //    Destroying this object releases everything — no manual cleanup.
        var scope = this.GetAssetScope();
        var hero = await scope.InstantiateAsync("hero-prefab", parent: transform);
    }
}
```

### AddressablesToolkitSettings — one configuration asset

A `ScriptableObject` that drives the whole flow. Create it via **Tools > Addressables Toolkit >
Settings** (it lands in `Assets/Resources/` so it loads automatically at runtime in any project).

- **Content source** — `Local` (bundles ship in the player; CDN/catalog/download steps are skipped)
  or `Remote` (content fetched from a CDN).
- **Override remote URL** — when on, installs an `AddressableCdn` `WebRequestOverride` onto the
  active environment's CDN; when off, uses the URLs baked into your Addressables profile.
- **Environments** — a list of named CDN targets (`dev`, `staging`, `production`, or any you add).
  The active one is chosen by `activeEnvironment`, overridable at runtime via
  `AddressablesToolkitSettings.EnvironmentOverride` or the `ADDRESSABLES_ENV` variable (CI/editor).
- **Content version / platform folder** — appended to the CDN URL; empty = `Application.version` /
  auto-detected platform.
- **Preload labels** — labels/addresses to predownload during initialization.
- **Initialization flow** — `autoInitializeOnLaunch`, `checkCatalogUpdates`,
  `predownloadPreloadContent`, plus `verboseLogging`.

```csharp
// Point the same build at a different backend before initializing:
AddressablesToolkitSettings.EnvironmentOverride = "staging";
```

### AddressablesService — initialization flow

Takes Addressables from launch to ready, driven by the settings asset. Calls are **idempotent**
and join a single in-flight initialization, so any system can `await` it before touching content.

```csharp
using System;
using KidzDev.AddressablesToolkit;

// Drive a loading screen from state transitions.
AddressablesService.StateChanged += s => Debug.Log($"State → {s}"); // Initializing → … → Ready

var progress = new Progress<DownloadProgress>(p => Debug.Log($"{p.Percent:P0}"));
bool ready = await AddressablesService.InitializeAsync(
    progress: progress,
    confirm: bytes => ShowConfirmPopupAsync(bytes)); // return UniTask<bool>

if (!ready)
{
    // Inspect why (declined download, offline, error, cancelled):
    Debug.LogError(AddressablesService.LastDownloadResult.Outcome);
}

bool isReady = AddressablesService.IsReady;
AddressablesService.Reset(); // re-run the flow later (does not release loaded assets)
```

For Local content or simple/dev projects, set `autoInitializeOnLaunch` and the service initializes
itself before the first scene. For a remote flow with a download UI, call `InitializeAsync` yourself
from your loading screen so you can pass `progress`/`confirm`.

### AssetScope — leak-proof load / instantiate / release

The memory-safety centerpiece: a disposable owner that tracks everything you load, instantiate, or
pool, and releases it all on `Dispose`. Bind it to a GameObject with `this.GetAssetScope()` and it
disposes automatically on destroy — so assets can't outlive the object that needed them. Works with
a plain **key or an `AssetReference`** everywhere.

```csharp
public class Hud : MonoBehaviour
{
    [SerializeField] private AssetReferenceSprite coinIcon;

    private async UniTaskVoid Start()
    {
        var scope = this.GetAssetScope(); // released automatically when this object is destroyed

        Sprite icon   = await scope.LoadAsync<Sprite>(coinIcon);          // by AssetReference
        Sprite banner = await scope.LoadAsync<Sprite>("ui_banner");       // by key
        GameObject fx = await scope.InstantiateAsync("vfx_explosion");    // released on dispose
        GameObject e  = await scope.SpawnPooledAsync("enemy");            // returned to pool on dispose

        // Optional early release of one item:
        scope.ReleaseInstance(fx);
    }
}
```

For a lifetime you control yourself (a screen, a flow), `new AssetScope()` and `using`/`Dispose`:

```csharp
using var scope = new AssetScope();
var config = await scope.LoadAsync<TextAsset>("level_config");
// ... everything released when the using-block exits ...
```

### AssetLoader — reference-counted async loading

```csharp
using KidzDev.AddressablesToolkit;
using UnityEngine;

// The cache is keyed by (key, type); identical (key, T) pairs share one handle and the
// asset is released only when every borrower releases.
var prefab = await AssetLoader.LoadAsync<GameObject>("demo-prefab");
var instance = Object.Instantiate(prefab);

// ... later ... (release is generic — release the same T you loaded)
AssetLoader.Release<GameObject>("demo-prefab");

// Helpers
bool loaded = AssetLoader.IsLoaded<GameObject>("demo-prefab");
AssetLoader.ReleaseAll();
```

### AddressablePool — prefab pooling with a persistent root

```csharp
using KidzDev.AddressablesToolkit;
using UnityEngine;

// Pre-instantiate a few inactive instances.
await AddressablePool.Prewarm("demo-prefab", 3);

// Borrow an instance (loads + warms the prefab on first use).
GameObject enemy = await AddressablePool.GetAsync("demo-prefab", parent: transform);

// Return it to the pool (deactivates, does not destroy).
AddressablePool.Release(enemy);

// Destroy all instances of a key and release the prefab handle.
AddressablePool.ClearPool("demo-prefab");
```

### ComponentReference — typed AssetReference

```csharp
using System;
using KidzDev.AddressablesToolkit;
using UnityEngine;

// Declare a concrete subclass so it shows up in the inspector.
[Serializable] public class RigidbodyRef : ComponentReference<Rigidbody> { }

public class Spawner : MonoBehaviour
{
    [SerializeField] private RigidbodyRef bodyRef;

    private async void Start()
    {
        // The handle is caller-owned (not ref-counted) and awaitable directly via UniTask.
        var handle = bodyRef.InstantiateComponentAsync(transform);
        Rigidbody rb = await handle;
        rb.AddForce(Vector3.up);

        // Release the spawned instance when done.
        bodyRef.ReleaseInstance(handle);
    }
}
```

### ContentDownloader — remote content size, predownload, and cache clear

```csharp
using System;
using KidzDev.AddressablesToolkit;
using UnityEngine;

// Bytes still needing download (0 = already cached / local).
long size = await ContentDownloader.GetDownloadSizeAsync("remote-label");

if (size > 0)
{
    var progress = new Progress<DownloadProgress>(p => Debug.Log($"{p.Percent:P0}"));
    await ContentDownloader.DownloadAsync("remote-label", progress);
}

// Clear cached bundles for a key/label.
bool cleared = await ContentDownloader.ClearCacheAsync("remote-label");
```

### RemoteContentUpdater — full startup update pipeline

The resilient flow both reference systems converged on: check for catalog updates → **apply
them before sizing** → size across labels → confirm with the player → download with aggregate
progress. Failures come back as a typed `DownloadResult` instead of an exception, and a single
`CancellationToken` threads through every step.

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using KidzDev.AddressablesToolkit;

// Optionally point Addressables at your versioned CDN first.
AddressableCdn.Install(baseUrl: "https://cdn.example.com/Addressables", version: Application.version);

var progress = new Progress<DownloadProgress>(p => Debug.Log($"{p.Percent:P0}"));

DownloadResult result = await RemoteContentUpdater.RunAsync(
    labels: new object[] { "ui", "prefab", "scene" },
    progress: progress,
    confirm: bytes => ShowConfirmPopupAsync(bytes), // return UniTask<bool>
    ct: cancellationToken);

switch (result.Outcome)
{
    case DownloadOutcome.NoUpdate:
    case DownloadOutcome.Success:    GoToGame(); break;
    case DownloadOutcome.Rejected:   /* user declined */ break;
    case DownloadOutcome.NoInternet: /* show offline error */ break;
    default:                         Debug.LogError(result.Message); break;
}

// On cancel/quit, optionally allow a partial download to resume next launch:
CatalogUpdater.ClearCatalogCacheForResume();
```

### AssetLocator — existence checks and TryLoad

```csharp
bool exists = await AssetLocator.ExistsAsync<Sprite>("icon_key");

// Loads only if a location of the right type exists; returns null otherwise (no throw).
Sprite s = await AssetLocator.TryLoadAsync<Sprite>("icon_key");
// release with AssetLoader.Release<Sprite>("icon_key");
```

### SpriteAtlasLoader — sprites packed in a SpriteAtlas

Uses the `"{atlasKey}[{spriteName}]"` addressable-key convention shared by both reference systems.

```csharp
Sprite icon = await SpriteAtlasLoader.LoadAsync("atl_ui_shared", "icon_coin");

// With a placeholder fallback when the sprite is missing:
Sprite safe = await SpriteAtlasLoader.LoadOrFallbackAsync(
    "atl_ui_shared", "icon_unknown",
    fallbackAtlasKey: "atl_ui_shared", fallbackSpriteName: "icon_missing");

SpriteAtlasLoader.Release("atl_ui_shared", "icon_coin");
```

### SceneLoader — addressable scenes

```csharp
await SceneLoader.LoadAsync("MainMenu"); // single
await SceneLoader.LoadAsync("Hud", UnityEngine.SceneManagement.LoadSceneMode.Additive);
await SceneLoader.UnloadAsync("Hud");                 // tracked additive unload
await SceneLoader.UnloadAsync("Hud", heavyUnload: true); // opt in to UnloadUnusedAssets + GC
```

## Editor tools

**Assets > Addressables Toolkit** (right-click in the Project window, on a selection):

- **Mark Addressable (address = name)** — mark selected assets addressable, using the file name as the address.
- **Mark Addressable (address = path)** — mark selected assets addressable, using the full asset path as the address.
- **Label by Parent Folder** — add a label named after each asset's parent folder.
- **Remove from Addressables** — remove the selected assets from Addressables.

**Tools > Addressables Toolkit**:

- **Validate Addressables** — scan entries for duplicate addresses and missing assets.
- **Validate Runtime Editor Usage** — flag any unguarded `using UnityEditor;` in a player
  (runtime) assembly — the High-severity bug both reference systems shipped.
- **Build Content** — build Addressables player content.
- **Clean Content** — clean built player content.
- **Build Content Update** — build a content update from the previous content state.

A `BundleSizeManifestBuilder` also runs automatically after every content build, writing
`ServerData/{buildTarget}_BundleSize.json` (bundle name + size) so a remote-update flow can
estimate the total download before fetching bundles.

## Build from CLI

The build entry points are callable from CI in batch mode:

```
Unity -batchmode -quit -projectPath <path> -executeMethod KidzDev.AddressablesToolkit.Editor.AddressablesBuilder.BuildContent
```

Pass `-aaProfile <ProfileName>` to switch the active Addressables profile before building. A failed build calls `EditorApplication.Exit(1)` so CI detects the failure.

## Samples

Open **Window > Package Manager**, select *KidzDev Addressables Toolkit*, then import **Demo** from the **Samples** tab. It's a ready-to-run scene plus the assets it needs:

- `Demo.unity` — open it, mark `demo-prefab` and `demo-sprite` addressable (right-click → **Addressables Toolkit > Mark Addressable (address = name)**), then press Play. The on-screen panel (`AddressablesToolkitFullDemo`) drives the high-level flow: `AddressablesService.InitializeAsync` from the bundled settings asset, then load/instantiate/pool through a GameObject-bound `AssetScope` that auto-releases on destroy.
- `AddressablesToolkitDemo` — the low-level tools (`AssetLoader`, `AddressablePool`, `ContentDownloader`, `RemoteContentUpdater`) used directly.

## License

MIT — see [LICENSE.md](LICENSE.md).
