# VR menu navigation

## Controller inputs

- LEFT controller `primary2DAxis`: move focus up/down/left/right. The dominant axis wins; equal diagonals use vertical movement.
- RIGHT controller `triggerPressed`: activate once per press. Release before selecting again.
- LEFT menu button: existing in-match options shortcut.
- Move either controller ray onto a control to return ownership to ray interaction, then use that controller's trigger. Moving the left stick returns ownership to analog focus. Stationary ray hover does not take focus away from the stick.

The menu uses enabled Input System actions on `<XRController>{LeftHand}` and `<XRController>{RightHand}`. The right ray shares the navigation trigger state; it has no separate submit binding. There is no new keyboard UI control path, input asset, EventSystem, networking system or settings store. The existing gameplay action assets and mappings remain intact.

A flick moves once. Holding waits 0.35 seconds, then repeats every 0.15 seconds. `MenuAnalogNavigation` exposes the deadzone (0.5), initial delay and repeat interval. A slow frame causes one movement rather than replaying missed movements. Direction changes respond immediately.

New pages choose the first usable control. Returning to a page restores the previously selected named control when available. Rebuilt, hidden, disabled and destroyed controls are repaired automatically. Scrollbars are excluded because selecting controls scrolls the appropriate viewport automatically. Help has a fixed Back button above its paginated instruction card.

A separate cyan border keeps the current focus visible despite ray hover. Existing Selectable selected/disabled colors remain active. Focused items are minimally scrolled into view; nested dropdown viewports scroll independently. Scrolling is immediate rather than animated to keep the entire focused control visible when navigation repeats.

## Controls and room codes

- Sliders: up/down changes control; left/right changes value by 5% of the range (one step for integer sliders). The existing live value label updates. Turn sensitivity previews through `SettingsController.TurnDegreesPerSecond`, which the existing continuous-turn code reads.
- Toggles: right trigger changes their actual value.
- Dropdowns: right trigger opens the real uGUI list. The stick moves among its actual options; right trigger applies the selected option and returns focus to the dropdown. Underlying controls are excluded while it is open.
- Buttons: submit uses the existing EventSystem submit handler and real button callbacks.
- Lobby: capacity, match duration, Ready, Start and Leave use the same generic navigation. Disabled host-only actions are skipped. Multiplayer behavior is unchanged.

Choose PLAY, then select the Room code field or ROOM CODE KEYPAD. Trigger opens the existing six-column keypad. Its A–Z and 0–9 keys support the actual `RelayRoomUI.IsValidCode` rule: exactly six uppercase ASCII letters or digits. Use BACKSPACE to delete, DONE to return with the code, or JOIN to submit directly through `MenuSessionController.Join`. CANCEL returns without submitting. Existing normalization and validation remain in place. Entries are limited to six characters.

Settings continue to use the existing draft, preview, APPLY, RESET TO DEFAULTS and PlayerPrefs behavior. Choose APPLY to save audio, graphics and control changes before going Back.

## How to Play / Help pages

How to Play and in-match Help now display **20 short instruction pages**, preserving the existing gameplay information. Only the current title and text exist as visible UI; longer topics are split at sentence boundaries. Each page has a cyan focus border, page counter, subtle clickable arrows and `LEFT STICK ◀ ▶ TO CHANGE PAGE` hint. Back remains above the card. The card and Back fit together without scrolling at the menu's 1280 × 900 VR canvas size.

- Opening Help always focuses the instruction card on page 1.
- LEFT stick RIGHT/LEFT changes the card to the next/previous page. Boundaries clamp; there is no wraparound.
- UP focuses Back; DOWN from Back returns to the card.
- RIGHT trigger on Back returns to the menu that opened Help and restores its prior focus. Trigger on the card does not change pages.
- The previous/next arrows and Back remain usable with the existing controller ray and trigger. Unavailable arrows are disabled at the boundaries.
- Horizontal pagination uses `MenuAnalogNavigation`'s existing 0.5 deadzone, 0.35-second initial delay and 0.15-second repeat interval. No additional XR input actions or input update loop were added. Other screens retain ordinary spatial navigation. Horizontal input while Back is selected leaves Back selected.

In the XR Interaction Simulator, hold Shift and tap L/J to page right/left, I to select Back, or K to return to the card. Release the letter keys while briefly retaining Shift to send a neutral stick frame; release Shift and tap T to activate Back.

The follow-up changes are limited to Help construction in `MainMenuController.cs`, the Help-specific branch in `MenuAnalogNavigation.cs`, the new `HelpInstructionViewer.cs` (and meta), expanded Editor validation and this documentation. Hosting, joining, keypad, settings, lobby, gameplay, ray input handling, `MenuVrPresentation` and Cloud configuration were not changed by this follow-up.

## Simulator configuration and exact shortcuts

Installed package: XR Interaction Toolkit **3.6.1**; Input System **1.20.0**. The project automatically instantiates the **XR Interaction Simulator** in Editor only (`Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`, `m_UseClassic: 0`). This is the new simulator, not the classic XR Device Simulator. Its lifecycle manager starts in controller mode.

Bindings were read from the imported 3.6.1 simulator action assets and checked against `XRInteractionSimulator.ProcessControlInput`:

| Task | Actual binding |
| --- | --- |
| Generate LEFT controller button/stick input | Hold **Shift** (`Left Device Actions`) |
| Move LEFT thumbstick | Hold **Shift**, use **I** up, **K** down, **J** left, **L** right |
| Generate RIGHT controller button/stick input | Release **Shift** |
| Press RIGHT trigger | **T**, with Shift released |
| Press LEFT trigger | **Shift + T** |
| LEFT menu button | **Shift + M** |
| Select left controller for pose manipulation | **[** (`Toggle Manipulate Left`) |
| Select right controller for pose manipulation | **]** (`Toggle Manipulate Right`) |
| Cycle manipulated device | **Tab** |
| Select head pose manipulation | **H** |
| Target primary thumbstick | **9** (primary is the default) |
| Target secondary axis | **0**; use **9** again before menu tests |
| Translate selected pose | **W/A/S/D**, **Q/E** vertically |
| Rotate selected pose | Arrow keys; right mouse button toggles mouse rotation |

Pose selection with brackets and button-input hand selection with Shift are separate. Selecting the left controller with `[` alone does not route I/J/K/L to the left thumbstick. Both virtual controllers exist independently, but this keyboard binding set routes hotkey button/axis input to one hand at a time using Shift. For menu tests, release I/J/K/L while keeping Shift held briefly so the left axis receives a neutral frame; then release Shift and tap T. Releasing Shift first can leave the simulator's other-hand axis state held until Shift is held again. Simultaneous left-stick and right-trigger input is not needed. The automated validation uses independent simulated XR device state events instead.

Click/focus the Game view first so the simulator receives its keyboard events. Check its UI indicates controller mode and primary-axis targeting. Menu actions enable themselves during configuration even if the gameplay rig/action manager has not spawned. Native XR display availability is not required: a simulated XRController device can initialize the tracked menu presentation. Quest/OpenXR still initializes via `XRSettings.isDeviceActive`. Controller ray poses use the same XRI Vector3/Quaternion fallback composites as the Starter Assets, preferring OpenXR pointer pose and falling back to simulated device pose.

## Exception fixes and gameplay ownership

`MaterialPropertyBlock` is allocated in `Awake`, never in the MonoBehaviour field initializer. Its previous constructor exception could prevent later fields (including the subsystem list) from being initialized. The per-frame `SubsystemManager.GetSubsystems` probe has been removed. Menu presentation waits for either an active native XR device or an Input System XR controller, supporting simulation without assuming an XR display subsystem exists. There is no blanket exception catch and XR remains enabled. Simulator placement also runs after tracked Update when there is no native display; it does not depend on a Game-view render callback. Native XR retains render-time placement and repositions when its display becomes active.

While session options are open, the existing shared gameplay gate blocks shooting, teleporting and smooth turning. Returning to gameplay also waits for the menu trigger(s) to be released and the left stick to return to neutral, preventing a held menu input from leaking into gameplay. The navigation component does not consume controls when the canvas is hidden. Gameplay retains left-stick teleportation, right-stick continuous turning and right-trigger shooting.

**Existing configuration discrepancy:** this checkout's `RelayAvatarPlayer` source and serialized player prefab have **15** teleports per burst and a 2.5-second reset/cooldown, rather than the seven stated in the request. Those gameplay settings were preserved.

## Manual Unity procedure

1. Open `Assets/Scenes/MainMenu.unity` in Unity 6000.6.2f1. Clear the Console and enter Play Mode. Confirm no MaterialPropertyBlock or subsystem exceptions and a cyan border on PLAY.
2. Use Shift + I/J/K/L and release Shift before T. Check flicks, holds, dominant diagonals and trigger holds. Keep the rays aimed elsewhere to verify analog-only use.
3. PLAY: open capacity, choose five players, confirm; navigate the room-code field and keypad. Enter six letters/digits, delete and replace one. Test DONE and direct JOIN; invalid codes must show existing validation messages.
4. SETTINGS: move down past the viewport; confirm automatic scrolling in both directions. Adjust sliders, toggle mute, open and confirm graphics quality. APPLY, Back, reopen and confirm settings persist. Test RESET TO DEFAULTS.
5. Move a controller ray deliberately onto a button, use its trigger, then use the left stick again. Confirm one click per press, stable cyan focus, visible lines/cursor and usable dropdowns in both modes.
6. With network access and another player/build, Host five slots, share code, Join, select every match duration, Ready/Unready and Start. Check disabled/hidden Start and host-only duration behavior.
7. In gameplay, verify right-stick smooth turn responds to saved sensitivity, left-stick teleport allowance/cooldown, right-trigger firing, death/respawn, round restart and options/resume. Hold inputs across resume to check release gating. Leave and return to the menu; repeat navigation. Monitor the entire Console for recurring exceptions.

## Automated validation and changed files

- `Assets/Scripts/Menu/MenuVrPresentation.cs`: lifecycle allocation, safe XR/simulator readiness, shared ray-submit ownership; existing cyan lines and cursor retained.
- `Assets/Scripts/Menu/MenuAnalogNavigation.cs` (and meta): navigation, repeat timing, focus border, dropdown handling, scroll reveal and release gating.
- `Assets/Scripts/Menu/MainMenuController.cs`: page focus notifications, code-field/keypad connection, direct validated keypad JOIN/CANCEL, gameplay release gate.
- `Assets/Scripts/Menu/Editor/MenuNavigationValidation.cs` and `MenuNavigationValidationRunner.cs` (and metas): isolated batch Play Mode smoke checks using XRSimulatedController device states and actual callbacks. Some controls are selected directly by the test to isolate callback/scroll behavior; directional tests themselves inject XR stick input.
- `Tests/VRMenuNavigation/RepeatTests.cs`: standalone assertions against production repeat/direction logic.
- This document and `docs/validation/VR_MENU_VALIDATION.txt`.

Run the Play Mode validation only in a disposable project copy, because it opens MainMenu, enters Play Mode and exits the Editor:

```sh
Unity -batchmode -force-glcore -projectPath /path/to/project-copy \
  -executeMethod MenuNavigationValidation.Run -logFile /tmp/menu-playmode-validation.log
```

The test restores the settings preference key afterward and avoids valid cloud joins. It does not prove external multiplayer connectivity or headset rendering. No scene, cloud-project, Relay environment or networking configuration was changed. The pre-existing `ProjectSettings/QualitySettings.asset` edit was left intact.

### Validation results

- **PASS:** Unity 6000.6.2f1 compiled runtime and Editor assemblies in an isolated full project copy. No C# compilation errors. Existing obsolete API/member-hiding warnings remain.
- **PASS:** final runtime sources compiled using Unity's bundled Roslyn compiler with both Editor and Android symbols; final Editor tooling also compiled. Android-symbol compilation is not an APK/IL2CPP build.
- **PASS:** 15 standalone assertions against production direction/repeat logic: deadzone, dominant axes, tied diagonals, immediate flick, initial delay, repeat interval, release, direction changes, reset and one movement per slow frame.
- **PASS:** 31 Play Mode feature assertions using independent `XRSimulatedController` states: initial focus/lifecycle initialization; held trigger transition protection; real dropdown options/value/focus return; navigation through Host and room-code field; all four keypad directions; six-character entry/deletion; real Join validation; Back focus restoration; settings sliders and actual turn-speed preview; toggle; full visibility after scrolling; APPLY and reopened persistence; retained right ray; simulated ray pose/hit/ownership; single real ray callback; stick ownership recovery; hidden/disabled focus repair.
- **PASS within exercised flow:** neither requested `MaterialPropertyBlock` constructor exception nor `SubsystemManager.GetSubsystems` null exception appeared; no menu runtime exception was observed in the final flow.
- **FAIL / external Editor issue:** the strict all-Console-errors assertion fails because Unity 6000.6.2f1 reports `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchDatabase.EnumerateAll` during startup indexing. The final batch run therefore exits 1 despite all 31 menu behavior assertions passing. Its reported two errors comprise that Search exception and the test's own failing all-Console-errors assertion. This was not hidden or filtered. Rebuilding only the copied Search cache did not resolve it. No original Editor cache or project setting was changed.
- **PASS:** `git diff --check`.

A concise record is in `docs/validation/VR_MENU_VALIDATION.txt`. Full local logs: `/tmp/menu-ray-placement-validation.log` (31 behavioral passes plus strict Console failure), `/tmp/menu-editor-compile.log`, `/tmp/menu-android-compile.log`, `/tmp/menu-tools-compile.log`. Earlier headless attempts failed inside native GTK, so Play Mode checks used the available display with `-force-glcore`. Startup logs also contain simulator point-and-click transform-discovery warnings (the menu creates its own tracked rays); key-driven virtual controller input remains available.

### Remaining manual checks and limits

No physical Quest testing, APK/IL2CPP build, valid Relay connection, five-player session, lobby-host authority exercise, gameplay scene start, death/respawn, round restart or leave-and-return lifecycle was performed. Those require the manual multiplayer/gameplay procedure above. Lobby controls use the generic, tested Selectable/dropdown path and unchanged real callbacks, but their connected behavior is not verified here. The graphical appearance, headset readability, left-ray interaction and human-operated simulator shortcuts still need a Game-view/headset review; right-ray input was verified programmatically with simulated poses. Desktop-only display/resolution controls follow the same generic dropdown path and were not present in the Editor menu.

The automated suite isolates menu behavior by sending actual simulated XR device states; it does not type direct UI keyboard shortcuts. It restores the settings PlayerPrefs key after the run. It deliberately reports any Console error as failure, including Editor tooling failures outside the menu. Full-console cleanliness is consequently **not** claimed.

### Paginated Help validation (follow-up)

Editor and Android-symbol runtime compilation and Editor tooling compilation pass. The existing 15 managed direction/repeat assertions pass. The expanded Unity Play Mode suite injects actual `XRSimulatedController` states and checks opening focus, one-page flicks, both boundaries, UP/Back/DOWN behavior, every page's exact title/body/counter and text bounds, simultaneous card/Back visibility, trigger Back and ordinary navigation after returning. Existing keypad/settings/dropdown/ray regression checks are retained. **62 Play Mode behavior assertions passed**, including all 20 instruction pages and the retained menu regressions. Results are recorded in `docs/validation/VR_HELP_VALIDATION.txt`; the full local log is `/tmp/help-playmode-final.log`.

The strict complete-Console check remains subject to the same previously recorded `UnityEditor.Search.SearchDatabase.EnumerateAll` startup indexing exception; this is not a new Help/menu exception. Physical Quest readability remains a manual check; the rendered text's layout bounds are tested at the existing VR canvas resolution.
