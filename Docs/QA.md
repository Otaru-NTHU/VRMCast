# VRMCast QA

## Automated

| Suite | How | Needs Unity |
| --- | --- | --- |
| `VRMCast.Core.Tests` (69 tests) | `Scripts/run-core-tests.sh` (dotnet 8 SDK) | no |
| same tests in the Unity Test Runner | `Scripts/run-unity-tests.sh` or Window > General > Test Runner > EditMode | yes |
| dependency pins | `Scripts/verify-dependencies.sh` | no |

Core tests cover: GLB/VRM version inspection (0.x, 1.0, both, plain glTF, truncated, bad magic,
malformed JSON, missing file), the JSON reader, framing presets and zoom/pan/orbit/aspect behaviour,
background defaults and Fill/Fit/Stretch math, hex colours, output presets, the FPS window, the
diagnostics report, and the latest-frame buffer.

## Manual QA: MVP-A definition of done (PRD 41)

Test on an Apple Silicon Mac with a standalone build (`Scripts/build-macos.sh`) and, separately, in
the editor. Use at least two VRM 0.x and two VRM 1.0 models (PRD 37.1); good free candidates are the
VRM Consortium sample models and models exported from VRoid Studio in both formats.

1. **Launch.** The window opens with the sidebar, an empty preview showing chroma green, and the status
   bar reading `1920×1080 | 30 FPS`. No Unity splash screen.
2. **Load VRM 0.x.** Load VRM → pick a 0.x file. Expected: model appears framed at Bust, status shows
   `VRM 0.x`, author, expression count, SpringBone state. Hair/skirt physics moves when you orbit quickly.
3. **Load VRM 1.0.** Same with a 1.0 file. Status shows `VRM 1.0`. Version was detected automatically:
   the Advanced import box never appeared.
4. **Reload without restart.** Load a different file, then the first one again, five times. Expected: a
   single avatar visible each time, memory in Activity Monitor returns to roughly the same level after
   each load (no unbounded growth), no errors in `~/Library/Logs/VRMCast/VRMCast/Player.log`.
5. **Unload.** Unload → preview shows only the background, status returns to "No avatar loaded".
6. **Bad files.** Load a `.png` renamed to `.vrm`, a plain `.glb`, and a truncated `.vrm`. Expected: a
   sentence from PRD 5.3 in the model status and the banner, never a stack trace. The Advanced import
   box appears after a failure and Retry with a forced version gives a clear message.
7. **Framing presets.** Switch Face / Bust / Half Body / Full Body. Each keeps the intended span in frame
   with a little margin; Full Body shows the feet. Reset Camera returns to the preset.
8. **Gestures.** Over the preview: scroll zooms, left drag orbits, right/middle or Shift+drag pans. Dragging
   a sidebar slider does not move the camera. Reset Orientation clears only the orbit.
9. **FOV.** Slider 10–90 changes perspective while keeping the subject in frame.
10. **Backgrounds.** Solid Color (edit hex, Enter), Chroma Key (`#00FF00` default, editable), Image (choose a
    PNG and a JPEG; Fill covers, Fit letterboxes on black, Stretch distorts), Transparent (preview shows
    the dark panel behind: the texture's alpha is 0 where no avatar is drawn).
11. **Output quality.** Switch to 720p 30, 1080p 60, 4K 30 and back. Status bar and diagnostics follow;
    framing looks identical between 1080p and 4K (same aspect); no pink or black frames.
12. **Output bus.** Start Output → button turns red, status `Output: on`, diagnostics show the debug
    consumer running. Stop Output reverts. Changing quality while running keeps it running.
13. **Diagnostics.** Render FPS settles at the target (30 or 60) on an idle machine. Copy Diagnostics puts
    the report on the clipboard; paste it and confirm it contains no file paths, only the file name.
14. **UI never in output.** Take Transparent or Chroma mode and read back the texture (later: OBS). No
    sidebar or status bar pixels exist in the output frame. In the editor, inspect
    `OutputRenderTexture` in the Frame Debugger: only the avatar camera writes to it.
15. **Language.** The app starts in 繁體中文. Switch the header dropdown to English: every label, dropdown
    choice, status text and the file picker change immediately; the current selections (preset,
    background mode, quality) are preserved. Quit and relaunch: the choice is remembered. Chinese text
    renders with real glyphs, never boxes, including Chinese file names in the file picker.
16. **Command line.** `open -a VRMCast.app --args /path/to/model.vrm` loads that model at start.

## Known gaps in MVP-A

- No native open-file dialog; the in-app browser or a command-line path is used instead.
- HEIC background images are not supported (PNG/JPEG only).
- Expression driving exists in the API (`LoadedAvatar.SetExpressionWeight`) but has no UI until the
  hotkey milestone; acceptance 37.1 "expressions can be driven" is verified through the Unity Test
  Runner playmode or the editor inspector, not through the app UI.
- The output consumer is a debug counter; nothing reaches OBS until MVP-E.

## Hardware matrix (PRD 38)

Record results per machine: M3-class and M4/M5-class Mac, macOS version, Unity `6000.3.x`, and the four
VRM test models with their source.
