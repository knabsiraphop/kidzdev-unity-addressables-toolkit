# Addressables Toolkit — Demo

A ready-to-run sample. Open the scene, mark two assets addressable, press **Play**.

## What's included

| Asset | Purpose |
| --- | --- |
| `Demo.unity` | The demo scene — an orthographic camera, the `AddressablesToolkitFullDemo` driver, and a preview of `demo-prefab`. |
| `demo-prefab.prefab` | A `SpriteRenderer` prefab (uses `demo-sprite`). Instantiated / pooled at runtime. |
| `demo-sprite.png` | A sprite loaded by key at runtime. |
| `Resources/AddressablesToolkitSettings.asset` | Toolkit settings (Content Source = **Local**). Lives in `Resources`, so the runtime finds it automatically. |
| `AddressablesToolkitFullDemo.cs` | Interactive IMGUI tour of the whole toolkit — drives the scene. |
| `AddressablesToolkitDemo.cs` | Minimal low-level copy-paste reference (`AssetLoader` / `AddressablePool` / `DownloadHelper` / `RemoteContentUpdater`). |

## Run it (two steps)

A package sample can't ship Addressables group entries — those live in **your** project's
`AddressableAssetsData`. So after importing the sample:

1. Select **`demo-prefab`** and **`demo-sprite`** in this folder → right-click →
   **Addressables Toolkit ▸ Mark Addressable (address = name)**.
   Their addresses become `demo-prefab` / `demo-sprite` — exactly what the scene expects.
2. Open **`Demo.unity`** and press **Play**.

The settings asset is already under a `Resources` folder, so no extra setup is needed for Local content.

## Using the scene

The on-screen IMGUI panel (`AddressablesToolkitFullDemo`) walks the whole toolkit:

- **1 · Initialize** — runs `AddressablesService.InitializeAsync` from the settings asset, with a
  live progress bar and a download-confirm dialog. Local content is instant; switch the settings to
  **Remote** + a CDN to exercise the catalog-update / predownload flow.
- **2 · Load / Instantiate / Release** — instantiate by key **and** by `AssetReference`, spawn pooled,
  load a sprite (drawn top-right), release one item early, or **Dispose scope** to release everything.

Every borrow goes through a single `AssetScope`; disposing it (or destroying the demo object)
releases all of it at once — the toolkit's memory-safety model in action.

## Low-level reference — `AddressablesToolkitDemo`

Not attached in the scene. Drop it on an empty GameObject and press Play to see `AssetLoader`,
`AddressablePool`, `DownloadHelper`, and `RemoteContentUpdater` used directly, with manual release.
Local assets report download size `0` — the download / update APIs are for remote (CDN) content.

## ComponentReference

```csharp
[Serializable] public class RigidbodyRef : ComponentReference<Rigidbody> { }

[SerializeField] RigidbodyRef body;
var handle = body.InstantiateComponentAsync(transform);
Rigidbody rb = await handle;
body.ReleaseInstance(handle);
```
