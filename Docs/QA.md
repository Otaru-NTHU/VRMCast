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

## Manual QA: MVP-B face tracking (PRD 42)

Prerequisite: `Scripts/setup-mediapipe.sh`, then reopen Unity so the package resolves. Test with one VRM 0.x
and one VRM 1.0 model, the built-in camera, and a standalone build.

1. **Engine present.** The TRACKING section shows no yellow setup hint and "Start Tracking" is enabled. Without
   the tarball the hint names the script and the button is disabled; nothing else breaks.
2. **Camera.** The CAMERA dropdown lists the built-in camera (and USB / Continuity cameras when present).
   Start Tracking → macOS asks for camera permission on first run; the small preview shows the webcam, mirrored
   by default; Mirror preview toggles it. Cover the camera or unplug a USB camera: the preview state reads
   "No frames, trying to recover…" and the avatar returns to neutral; plugging back in resumes within ~2 s and
   the app never switches to another camera on its own.
3. **Head.** Status reads "Tracking". Turn your head left: the avatar turns toward the same side of the screen
   (mirror). Nod: the avatar nods. Tilt: the avatar tilts. If any axis goes the wrong way, note which one and use
   the invert toggle under Advanced settings; report it so the default can be fixed.
4. **Blink / eyes / mouth.** Close your left eye: the avatar's screen-left eye (its right) closes. Look left /
   right / up / down: the avatar's eyes follow. Open your mouth: "aa" opens; smile: "happy".
5. **Calibrate.** Sit slightly turned and press Calibrate; after ~1 s "Calibration complete" and the avatar
   faces forward while you hold that pose; the status shows "calibrated". Clear Calibration restores raw.
   Calibrating with no face in view fails with a message, not a silent no-op.
6. **Rest.** With tracking on but no face, the avatar eases back to neutral over about a second, never snaps.
   With tracking off the avatar stands with arms down, not in a T-pose.
7. **Modes.** Basic vs Advanced: Advanced adds pucker/funnel/brows/frown/wide-eye expressions when the model
   defines them; Basic never triggers angry/sad/surprised.
8. **Performance.** Diagnostics: render 30 fps steady, tracking ≈ 30 fps, inference under ~25 ms on M3,
   dropped stays near 0. Output stays 1920×1080 while tracking runs.
9. **Both VRM versions.** Repeat 3–6 with the other VRM version: same behaviour, SpringBone still moves.
10. **Persistence.** Quit and relaunch: camera choice, mirror, mode and tracking on/off are remembered.

## Manual QA: MVP-C lip sync and upper body (PRD 35)

1. **Microphone.** With tracking on and Lip Sync = Hybrid, macOS asks for microphone permission; the LIP SYNC
   section lists microphones, the level bar moves when you speak and stays grey (gate closed) when the room is
   quiet. Raising the noise gate slider above the room level stops the bar from reacting to background noise.
2. **Camera mode.** Lip Sync = Camera: cover the microphone, open your mouth: the avatar's mouth opens.
3. **Microphone mode.** Lip Sync = Microphone: cover the camera or turn away, speak: the mouth opens with your
   voice and closes in pauses; no movement at all in silence.
4. **Hybrid.** Speak normally: mouth follows both; stay silent with your mouth slightly open: the avatar's mouth
   stays nearly closed. Make an "oo" shape while humming: the avatar shows ou/oh rather than aa (on models that
   define them).
5. **Body.** Body tracking = Upper Body: lean left/right, turn your shoulders, lean toward the camera; the torso
   follows the same screen side as your head (mirror), and the face does not double-rotate when the whole body
   turns. Set Off: the torso stays still while the head still moves.
6. **Calibration.** Sit slightly turned and press Calibrate: after it completes the torso reads neutral too.
6b. **Arms.** Body tracking = Upper Body + Arms, framing Half Body: raise your left hand: the avatar raises the
    arm on the *same side of the screen* (its right arm), exactly like a mirror; wave: the forearm follows; point
    at the camera: the arm comes toward the viewer. Lower your arms out of frame: the avatar's arms ease back to
    hanging within a second. Upper Body only: arms stay down. Mirror off: your left hand raises the avatar's left.
6c. **Fingers and hand-anchored arms.** Body tracking = Upper Body + Arms + Fingers, hands raised in frame with the
    palms toward the camera: open hand → fingers straight (slightly relaxed); fist → all fingers curl; index only →
    one finger stays out; thumb across the palm → the thumb folds. Closing your left hand closes the avatar's
    screen-left hand (mirror) and that hand is on the arm that follows your left hand. Move a hand slowly across
    the frame and toward the camera: the whole arm follows the palm with a natural elbow, and the palm turns with
    yours (palm to camera → palm to viewer). If the wrong side reacts for both arm and fingers, tick "Swap left and
    right (arms and fingers)"; arms and fingers must never disagree with each other. Drop the hands out of frame:
    fingers relax open within half a second and the arms fall back to the pose landmarks, then rest.
    Diagnostics shows Hands ≈ 15 fps, and the "Fingers:" line under the body mode shows the curl per hand.
6c2. **Overlay.** While tracking runs, the camera preview shows P:L / P:R at your wrists and H:L→L style tags at
    your hands. P:L must sit on your real left wrist; each hand tag's arrow side must match the P tag it sits on.
    A screenshot of this preview is what to send when sides look wrong.
6e. **Eyes.** Wink your left eye: the avatar winks the eye on the same side of the screen (its right). If the other
    eye winks, untick "Swap eye sides". Blink strength default is now 2.5; existing profiles keep their old value.
6d. **Body switches.** With "Swap body and arm sides" on (default) and every invert switch off: lean left → the
    avatar's torso top goes to screen-left (mirror); raise your right hand → P:R sits on it in the preview and the
    avatar raises its screen-right arm. The invert switches remain for unusual cameras and survive a relaunch.
7. **Persistence.** Lip mode, microphone, sensitivity, gate and body mode survive a relaunch.
8. **Performance.** Diagnostics: body ≈ 20 fps, hands ≈ 15 fps, face still ≈ 30 fps, render stays at the target.

## Manual QA: MVP-D profiles and usability (PRD 35)

1. **First launch.** A `Default` profile is created under `~/Library/Application Support/VRMCast/Profiles`.
   Load an avatar, change background, framing and tracking settings, quit, relaunch: everything is restored,
   including the avatar and calibration.
2. **Manage.** Header → Manage: New creates an empty profile, Save As / Duplicate / Rename / Delete behave as
   named; the header dropdown switches profiles and switching saves the previous one first. Turning auto-save
   off keeps changes in memory until Save.
3. **Missing file.** Move the avatar file away and relaunch: the profile stays, a red message names the missing
   path, and Relink VRM opens the file browser.
4. **Hotkeys.** Keys 1–4 toggle happy / angry / sad / surprised (where the model defines them), 5 plays neutral
   once. Click a key cell, press F6: the row now uses F6; Backspace clears. Typing in the profile name field
   never triggers expressions. Hotkeys work with tracking off and layer on top of tracking when on.
5. **Performance mode.** Header button or Tab hides all controls; the overlay shows fps / face / audio / output;
   the preview keeps rendering and the debug output keeps counting frames. Esc or Tab returns.
6. **Blink.** Advanced settings → Blink strength: at 1.8 (default) a normal blink closes the avatar's eyes fully.

## Manual QA: MVP-E camera extension spike (PRD 43)

Follow "Building and testing the spike" in `Docs/VirtualCamera.md` on a Mac with Xcode and OBS. Pass
criteria (PRD 43 definition of done, PRD 37.6):

1. `VRM Live Camera` appears in OBS › Video Capture Device after approval; `systemextensionsctl list`
   shows it `[activated enabled]`.
2. Without the host sending, OBS shows the extension's pattern: colour bars, red lower band, moving square,
   growing progress line; source properties report 1920×1080.
3. **Start Sending 1080p30** turns the band blue within a second; the counter grows by ~30/s with zero
   enqueue failures.
4. **Stop Sending** or quitting the app returns to red within ~0.5 s; never a frozen or black frame.
5. Restarting OBS keeps the device listed and streaming; no reinstall.
6. 10 minutes of continuous sending: OBS Stats stays at ~30 fps, no enqueue failures, flat memory in the
   host and the `VRMCastCameraExtension` process.
7. **Uninstall** removes the device from OBS.

Record macOS version, Xcode version, OBS version, and whether approval required a reboot.

## Manual QA: MVP-E virtual camera from VRMCast.app (PRD 37.6)

Package with `Scripts/package-macos.sh --install DEVELOPMENT_TEAM=<TeamID>` (see `Docs/VirtualCamera.md`).

1. **Install.** OUTPUT → Install / Enable Virtual Camera. The status walks through "asking macOS" → "waiting for
   approval" → after allowing in System Settings, "enabled". The Install button never silently fails: every error
   shows its code.
2. **Stream.** Load an avatar, Start Output. OBS → Video Capture Device → VRM Live Camera shows the avatar at
   1920×1080; OBS Stats reads ~30 fps for every output preset (720p/1440p/4K are scaled).
3. **Stop.** Stop Output: the camera shows the extension's red fallback pattern within half a second, never a
   frozen avatar. Start again: the avatar returns.
4. **OBS restart** while streaming: the device is still listed and live.
5. **Ten minutes** of output with tracking on: no dropped-frame growth in Copy Diagnostics beyond occasional
   single drops, flat memory.
6. **Remove.** OUTPUT → Remove: the device disappears from OBS; Install brings it back without a reboot.

## Known gaps in MVP-A

- No native open-file dialog; the in-app browser or a command-line path is used instead.
- HEIC background images are not supported (PNG/JPEG only).
- Expression driving exists in the API (`LoadedAvatar.SetExpressionWeight`) but has no UI until the
  hotkey milestone; acceptance 37.1 "expressions can be driven" is verified through the Unity Test
  Runner playmode or the editor inspector, not through the app UI.
- The output consumer inside VRMCast.app is still a debug counter; the camera extension spike is a
  separate app until the Unity bridge lands (MVP-E, `Docs/VirtualCamera.md`).

## Hardware matrix (PRD 38)

Record results per machine: M3-class and M4/M5-class Mac, macOS version, Unity `6000.3.x`, and the four
VRM test models with their source.
