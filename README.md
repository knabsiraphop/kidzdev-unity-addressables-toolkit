# KidzDev Addressables Toolkit

Utilities for working with Unity Addressables — reference-counted loading, prefab pooling, typed asset references, remote-download helpers, and editor/build tooling.

## Requirements

- Unity **6000.0** or newer.
- `com.unity.addressables` — installed automatically as a package dependency.

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

// Identical keys share one handle; the asset is released only when every borrower releases.
var prefab = await AssetLoader.LoadAsync<GameObject>("demo-prefab");
var instance = Object.Instantiate(prefab);

// ... later ...
AssetLoader.Release("demo-prefab");

// Helpers
bool loaded = AssetLoader.IsLoaded("demo-prefab");
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
        var handle = bodyRef.InstantiateComponentAsync(transform);
        Rigidbody rb = await handle.Task;
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

## Editor tools

**Assets > Addressables Toolkit** (right-click in the Project window, on a selection):

- **Mark Addressable (address = name)** — mark selected assets addressable, using the file name as the address.
- **Mark Addressable (address = path)** — mark selected assets addressable, using the full asset path as the address.
- **Label by Parent Folder** — add a label named after each asset's parent folder.
- **Remove from Addressables** — remove the selected assets from Addressables.

**Tools > Addressables Toolkit**:

- **Validate Addressables** — scan entries for duplicate addresses and missing assets.
- **Build Content** — build Addressables player content.
- **Clean Content** — clean built player content.
- **Build Content Update** — build a content update from the previous content state.

## Build from CLI

The build entry points are callable from CI in batch mode:

```
Unity -batchmode -quit -projectPath <path> -executeMethod KidzDev.AddressablesToolkit.Editor.AddressablesBuilder.BuildContent
```

Pass `-aaProfile <ProfileName>` to switch the active Addressables profile before building. A failed build calls `EditorApplication.Exit(1)` so CI detects the failure.

## Samples

Open **Window > Package Manager**, select *KidzDev Addressables Toolkit*, then import **Demo** from the **Samples** tab. It demonstrates `AssetLoader`, `AddressablePool`, and `DownloadHelper` together.

## License

MIT — see [LICENSE.md](LICENSE.md).
