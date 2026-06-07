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
https://github.com/knabsiraphop/kidzdev-addressables-toolkit.git#v1.0.0
```

Or add the dependency directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.kidzdev.addressables-toolkit": "https://github.com/knabsiraphop/kidzdev-addressables-toolkit.git#v1.0.0"
  }
}
```

## Usage

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

### DownloadHelper — remote content size, predownload, and cache clear

```csharp
using System;
using KidzDev.AddressablesToolkit;
using UnityEngine;

// Bytes still needing download (0 = already cached / local).
long size = await DownloadHelper.GetDownloadSizeAsync("remote-label");

if (size > 0)
{
    var progress = new Progress<DownloadProgress>(p => Debug.Log($"{p.Percent:P0}"));
    await DownloadHelper.DownloadAsync("remote-label", progress);
}

// Clear cached bundles for a key/label.
bool cleared = await DownloadHelper.ClearCacheAsync("remote-label");
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
RemoteContentUpdater.ClearCatalogCacheForResume();
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

Open **Window > Package Manager**, select *KidzDev Addressables Toolkit*, then import **Demo** from the **Samples** tab. It demonstrates `AssetLoader`, `AddressablePool`, `DownloadHelper`, and `RemoteContentUpdater` together.

## License

MIT — see [LICENSE.md](LICENSE.md).
