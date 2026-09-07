# VRMCast Architecture

Status: MVP-A (render foundation). Updated whenever the architecture changes (PRD 40).

VRMCast is the repository name for the product the PRD calls "VRM Live Camera". Code, folders and
namespaces use `VRMCast`; the virtual camera device published in MVP-E will still be user-facing text
and can carry whichever name is chosen then.

## Stack (PRD 2, decisions D-001 … D-013)

| Layer | Choice | Pinned |
| --- | --- | --- |
| Engine | Unity 6.3 LTS | `6000.3.20f1` (`ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | Built-in Render Pipeline | see D-014 below |
| VRM runtime | UniVRM (`com.vrmc.gltf`, `com.vrmc.univrm`, `com.vrmc.vrm`) | `v0.131.2` via git URL in `Packages/manifest.json` |
| UI | Unity UI Toolkit (runtime) | built-in module |
| Tracking | MediaPipeUnityPlugin | not added yet (MVP-B) |
| Virtual camera | Core Media I/O Camera Extension | not added yet (MVP-E) |

The Unity project lives at the repository root (`Assets/`, `Packages/`, `ProjectSettings/`) so the
standard Unity `.gitignore` applies. Application code lives under `Assets/VRMCast/`.

### D-014 Built-in Render Pipeline for MVP-A

UniVRM auto-detects the active pipeline and ships MToon for both Built-in and URP. Built-in was chosen
for the foundation because it needs no pipeline asset, renders straight into an ARGB render texture
with alpha, and has the longest UniVRM track record. Switching to URP later is a project-settings and
material-generator change, not an architecture change; `RenderService` does not depend on the pipeline.

## Assemblies

```
VRMCast.Core        (Assets/VRMCast/Core)     pure C#, noEngineReferences = true
VRMCast.Runtime     (Assets/VRMCast/Runtime)  Unity + UniVRM; services, UI controller, bootstrap
VRMCast.Editor      (Assets/VRMCast/Editor)   scene builder, build script, setup self-check
VRMCast.Core.Tests  (Assets/VRMCast/Tests)    NUnit EditMode tests for VRMCast.Core
```

`VRMCast.Core` has no `UnityEngine` reference on purpose. Everything that can be expressed as plain
data and math lives there so it is unit-testable outside Unity (`Scripts/run-core-tests.sh` compiles
it with the .NET SDK against `netstandard2.1` / C# 9, the same profile Unity uses).

## Service graph (PRD 27)

```
AppBootstrap (MonoBehaviour, composition root, scene-serialized references)
 ├── RenderService          avatar Camera + OutputRenderTexture (1920x1080 ARGB32, MSAA 4)
 ├── AvatarService          VRM 0.x / 1.0 runtime load, unload, friendly errors
 ├── BackgroundService      clear color per mode + image plane (Fill/Fit/Stretch)
 ├── AvatarCameraController framing presets, zoom/pan/orbit/FOV via AvatarCameraSolver
 ├── OutputService          Frame Output Bus: fans FrameRendered out to IFrameOutput consumers
 │    ├── PreviewOutput     the in-app preview (UI binds the texture directly)
 │    └── NullOutput        debug consumer; validates frame size (virtual camera comes in MVP-E)
 ├── DiagnosticsService     render FPS window + snapshot/report
 └── MainView (UI Toolkit)  binds Main.uxml to the services above; never touches UniVRM
```

Rules enforced by construction:

- No `GameObject.Find`, `FindObjectOfType` or `Resources.Load` in runtime code. `AppBootstrap` builds
  the graph explicitly from serialized references (`UIDocument`, background material).
- UI calls services. `MainView` receives an `AppServices` object; UniVRM types never leak above
  `LoadedAvatar`.
- All Unity object mutation is on the main thread. UniVRM's `RuntimeOnlyAwaitCaller` only moves
  parsing/decoding to worker threads; instantiation happens on the main thread.
- Outputs are consumers (PRD 3.5). `RenderService` raises `FrameRendered(RenderTexture, timestamp)` from
  `Camera.onPostRender`; it has no knowledge of who listens.

## Frame flow

```
Avatar (UniVRM instance under AvatarRoot)
   + Key Light (directional) + ambient
   + BackgroundImagePlane (child of AvatarCamera, Image mode only)
        |
   AvatarCamera  --clear color from BackgroundSettings-->  OutputRenderTexture (ARGB32 + depth, MSAA)
        |
   Camera.onPostRender -> RenderService.FrameRendered -> OutputService -> IFrameOutput.SubmitFrame
        |
   UI Toolkit preview Image displays OutputRenderTexture (UI itself renders to the window, never to the RT)
```

`ScreenClearCamera` in the scene renders nothing (culling mask 0); it only clears the window so the OS
never shows uninitialised memory behind the UI. The avatar camera has no display target at all, which
is how "UI must never appear in the output texture" is guaranteed rather than filtered.

## VRM loading (PRD 5)

1. `VrmFileInspector` (Core) reads only the GLB header and JSON chunk and reports the version from the
   glTF extension keys: `VRMC_vrm` → 1.0, `VRM` → 0.x. It also pulls title/author for the UI.
2. `AvatarService` resolves the version with the optional advanced override, then loads with
   `VrmUtility.LoadAsync` (0.x) or `Vrm10.LoadPathAsync(canLoadVrm0X: false)` (1.0). 0.x files are
   loaded natively, not migrated, so both runtimes stay exercised.
3. On success the previous avatar is disposed first; the new root is parented under `AvatarRoot`.
4. Failures map to the sentences in `VrmLoadErrors`; the exception goes to the Unity log.

`LoadedAvatar` (abstract) → `Vrm0Avatar` / `Vrm1Avatar` expose bones, renderer bounds, expression
names and a `SetExpressionWeight` entry point so later phases drive expressions without knowing the
version.

## Avatar camera (PRD 16)

`AvatarCameraSolver` is pure math: preset span (from humanoid bone heights or bounds) + margin →
distance for the current vertical FOV; a width check protects narrow aspect ratios; zoom scales the
distance, orbit rotates around the framing target, pan translates camera and target together in the
view plane in units of avatar height. Aspect is an input, so changing output resolution preserves
framing (PRD 16.3). VRM avatars face +Z in Unity; the camera sits on +Z at yaw 0.

Preview gestures (all scoped to the preview element, so they never fight UI drags): scroll = zoom,
left drag = orbit, Shift+left / right / middle drag = pan.

## Backgrounds (PRD 17)

Solid, chroma and transparent are camera clear colors (chroma default `#00FF00`). Transparent clears
alpha to 0; the render texture is always ARGB so an alpha-capable output can use it (D-010). Image mode
enables a hand-built quad parented to the camera at 50 m, resized every LateUpdate to the frustum and
the `ImageFitSolver` result. PNG and JPEG are loaded through `ImageConversion`; HEIC is not supported.

## Localization

The UI is bilingual: Traditional Chinese (default) and English, switchable from the header dropdown and
remembered in `PlayerPrefs` (`vrmcast.language`). `VRMCast.Core.Localization` holds the `Localizer`, the
`Message` struct (key + format args, translated at display time) and `LocalizationTable` with both string
tables; a test enforces that both languages define the same keys. Services never produce sentences:
`VrmLoadErrors` and `BackgroundService.LastError` are keys, `AvatarLoadResult.Error` is a `Message`, and
`MainView` translates when it renders. Dropdowns map by index, so labels change language without touching
the enum mapping. Diagnostics reports stay English so they paste cleanly into bug reports.

Unity's default UI font has no CJK glyphs, so `Assets/VRMCast/UI/Fonts/NotoSansTC-Regular.otf`
(SIL OFL) is applied through `-unity-font-definition` on the app root and the modal overlay.

## File selection

Standalone Unity has no native open-file dialog without a plugin, and MVP-A adds no native code. The
app therefore ships an in-app file browser (`FilePickerView`) and also accepts a `.vrm` path on the
command line (`open -a VRMCast.app --args /path/to/avatar.vrm`). A small NSOpenPanel bridge under
`Native/macOS` is the intended replacement once native code enters the project in MVP-E.

## Reserved interfaces (PRD 8, 15, 19)

- `VRMCast.Core.Tracking`: `TrackingFrame` contract, `ITrackingProvider` and its face/pose/hand/external
  sub-interfaces, and `LatestFrameBuffer<T>` (single-slot, thread-safe mailbox). No provider exists yet.
- `VRMCast.Output.IFrameOutput` with `PreviewOutput` and `NullOutput`. `MacVirtualCameraOutput` arrives
  after the Camera Extension spike (Docs/VirtualCamera.md).

## Editor tooling

- `VRMCast > Setup > Rebuild Main Scene` regenerates `Scenes/Main.unity`, the `PanelSettings` asset,
  the runtime theme and the background material and rewires the bootstrap. `EditorSetupCheck` runs it
  automatically once per session if those assets did not deserialize.
- `VRMCast > Build > macOS (Apple Silicon)` / `Scripts/build-macos.sh` build the player through
  `BuildScript.BuildMacOS`, which always rebuilds the scene first and sets the arm64 architecture.
- `Scripts/generate-meta-files.py` writes deterministic `.meta` files for assets added outside Unity.
