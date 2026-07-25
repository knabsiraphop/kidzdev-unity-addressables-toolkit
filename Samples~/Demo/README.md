# Addressables Toolkit — Demo

A ready-to-run sample. Open the scene, mark the four assets addressable, press **Play**.

## What's included

| Asset | Purpose |
| --- | --- |
| `FullDemo.unity` | The demo scene — an orthographic camera, the `AddressablesToolkitFullDemo` driver, and a preview of `demo-prefab`. |
| `demo-prefab.prefab` | A `SpriteRenderer` prefab (uses `demo-sprite`). Instantiated / pooled at runtime. |
| `demo-sprite.png` | A sprite loaded by key at runtime. |
| `demo-scene.unity` | A tiny additive scene (a lit cube marker, no camera) the **Scenes** section loads/unloads. |
| `demo-atlas.png` | A 4-sprite sheet (Sprite mode **Multiple**: `icon`, `icon_missing`, `star`, `heart`) the **SpriteAtlasLoader** section loads via the `demo-atlas[icon]` sub-object convention. |
| `Resources/AddressablesToolkitSettings.asset` | Toolkit settings (Content Source = **Local**). Lives in `Resources`, so the runtime finds it automatically. |
| `AddressablesToolkitFullDemo.cs` | Interactive IMGUI tour that exercises **every public API** in the toolkit — drives the scene. |
| `AddressablesToolkitDemo.cs` | Minimal low-level copy-paste reference (`AssetLoader` / `AddressablePool` / `ContentDownloader` / `RemoteContentUpdater`). |

## Run it (two steps)

A package sample can't ship Addressables group entries — those live in **your** project's
`AddressableAssetsData`. So after importing the sample:

1. Select **`demo-prefab`**, **`demo-sprite`**, **`demo-scene`**, and **`demo-atlas`** in this folder →
   right-click → **Addressables Toolkit ▸ Mark Addressable (address = name)**.
   Their addresses become `demo-prefab` / `demo-sprite` / `demo-scene` / `demo-atlas` — exactly what
   the scene expects.
   - `demo-scene` is what the **7 · SceneLoader** section loads additively.
   - `demo-atlas` backs the **6 · SpriteAtlasLoader** section: its sprites are addressed as
     `demo-atlas[icon]` / `demo-atlas[icon_missing]` via the Addressables sub-object convention, so
     marking the single sheet addressable is all that's needed.
   Without these, the matching sections just log a "not found" outcome instead of loading anything.
2. Open **`FullDemo.unity`** and press **Play**.

The settings asset is already under a `Resources` folder, so no extra setup is needed for Local content.

> **Remote content caveat:** the **1 · Service**, **8 · Remote**, and **8b · Per-label progress**
> sections were exercised against a **Local** content source only — the catalog-update / predownload /
> per-label-progress flow has not been verified against a real production CDN. Point `contentSource`
> at **Remote** with your own CDN and test end-to-end before relying on it.

The scene's `AddressablesToolkitFullDemo` already has its **`prefabReference`** and **`componentReference`**
fields pre-wired to `demo-prefab`, so the AssetReference paths (section 2) and the
`ComponentReference<T>` section (5) work without any inspector setup. Each of those sections also shows
an **● assigned / ○ assign in inspector** hint, so if you drop the script on a fresh GameObject you'll
know what still needs wiring.

## Using the scene

The on-screen IMGUI panel (`AddressablesToolkitFullDemo`) is a scrollable tour that wires **every
public API in the toolkit** to a button, so you can watch each call run and read its result in the
on-screen log. Sections:

- **Settings** — the resolved content source, environment, CDN, version, platform folder, and
  preload labels (`AddressablesToolkitSettings.Resolve*` / `GetPreloadKeys`), plus runtime overrides
  (`EnvironmentOverride`, `OverrideInstance`).
- **1 · Service** — `IAddressablesService` init (both the implicit-`.Instance` and explicit-settings
  overloads), live `State` / `StateChanged` / `LastDownloadResult`, a progress bar + confirm dialog,
  cancel, and `Reset`. Local content is instant; switch the settings to **Remote** + a CDN to
  exercise the catalog-update / predownload flow.
- **2 · AssetScope** (recommended) — load / instantiate / instantiate-component / spawn-pooled / load
  sprite, **by key and by `AssetReference`**; early single-item release; `Dispose`; plus the
  GameObject- and Component-bound `GetAssetScope()` (auto-disposed on destroy).
- **3 · AssetLoader + AssetLocator** — ref-counted `LoadAsync` / `IsLoaded` / `Release` / `ReleaseAll`
  and existence probes (`ExistsAsync`, `TryLoadAsync`).
- **4 · AddressablePool** — `Prewarm` / `GetAsync` / `Release` / `ClearPool` / `ClearAll`, showing
  `PooledObject.Key`.
- **5 · ComponentReference&lt;T&gt;** — `InstantiateComponentAsync` / `LoadComponentAsync` /
  `ReleaseInstance` (assign the reference in the inspector first).
- **6 · SpriteAtlasLoader** — the `"{atlas}[{sprite}]"` key convention, `LoadAsync`,
  `LoadOrFallbackAsync`, `Release`.
- **7 · SceneLoader** — additive `LoadAsync` / `IsAdditiveLoaded` / `UnloadAsync`.
- **8 · Remote** — `ContentDownloader` (single + multi-key size/download, clear cache),
  `CatalogUpdater` (check + clear-for-resume), `RemoteContentUpdater.RunAsync` (both the default
  union flow and `perLabelProgress: true`), and `AddressableCdn` install/uninstall.
- **8b · Per-label progress** — shown after running `RunAsync (per-label)`. An aggregate bar
  (deduped total across every label), a current-label bar (resets as `RemoteContentUpdater` moves
  from one label to the next), and a status line per label (`Pending` / `Downloading` / `Complete`)
  with its own downloaded/total/percent — all from the one richer `DownloadProgress.Labels` /
  `.CurrentLabel` / `.BytesPerSecond` payload that mode reports.

The asset operations gate on the service being **Ready**, so press **Initialize** first. Sprite-atlas,
scene, and remote calls need matching content in your groups — without it they fail gracefully and log
the typed outcome, which is itself part of the tour.

Every scope borrow goes through a single `AssetScope`; disposing it (or destroying the demo object)
releases all of it at once — the toolkit's memory-safety model in action.

## Low-level reference — `AddressablesToolkitDemo`

Not attached in the scene. Drop it on an empty GameObject and press Play to see `AssetLoader`,
`AddressablePool`, `ContentDownloader`, and `RemoteContentUpdater` used directly, with manual release.
Local assets report download size `0` — the download / update APIs are for remote (CDN) content.

## ComponentReference

```csharp
[Serializable] public class RigidbodyRef : ComponentReference<Rigidbody> { }

[SerializeField] RigidbodyRef body;
var handle = body.InstantiateComponentAsync(transform);
Rigidbody rb = await handle;
body.ReleaseInstance(handle);
```
