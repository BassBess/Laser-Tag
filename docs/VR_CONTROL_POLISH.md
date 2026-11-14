# VR control polish

Implemented on `feature/arena-boundary-stair-teleport`. Existing scene, boundary,
stair, pickup and build-setting edits were preserved. No scene regeneration,
commit, branch change or Git cleanup was performed.

## Changed files

- `Assets/Scripts/Networking/RelayAvatarPlayer.cs`: continuous turning, protocol 6,
  authoritative teleport burst accounting and replicated charge/deadline feedback.
- `Assets/LaserTag/Scripts/Game/LocalPlayerRig.cs`: disable competing XRI turn providers.
- `Assets/LaserTag/Scripts/Environment/LeftHandArcTeleport.cs`: charge preview/input gate.
- `Assets/LaserTag/Scripts/UI/PlayerHud.cs`: lower-left teleport count/countdown.
- `Assets/Scripts/LaserRay.cs`: distance-scaled cyan dot/ring with dark outline.
- `Assets/Scripts/Menu/SettingsController.cs`: preferences and legacy preference migration.
- `Assets/Scripts/Menu/MainMenuController.cs`: controls-first settings, live values,
  larger slider handles and updated help.
- `Assets/Scripts/Menu/MenuVrPresentation.cs`: one right-hand UI ray, clearer line,
  selectable-only endpoint, hover and press feedback; existing XRI UI interaction retained.
- `Assets/LaserTag/Editor/VRControlPolishValidation.cs` (+ metadata): production-math checks.

## Settings and decisions

**Turn Sensitivity:** 30–180 degrees/second, default 90. Right stick horizontal
raw input has a rescaled 0.18 deadzone (Inspector range 0.15–0.20). Turning is
proportional and immediate; no angular easing or continuing movement on release.
Delta time is capped at 50 ms to avoid a large turn after a hitch. A neutral stick
is required after blocked input. Left-stick teleport bindings are untouched.

The owner predicts rotation around its tracked head; camera/controller local
tracking transforms are untouched. Bounded reliable turn deltas are batched at
30 Hz and validated against server time, ownership, origin revision, sequence,
life state and the existing tracking envelope. Server allowance is 180 degrees/s
with 250 ms of transport slack and a maximum 12-degree packet. Turn and teleport
request streams have separate rate limits. Outstanding turn remainder is sent
before a teleport; reliable origin revisions reconcile server placement. Ordinary
turn snapshots update remote players without reapplying rotation to the owner.
Duplicate placement acknowledgements cannot overwrite predicted turns. All peers
must use protocol 6. Large latency, packet bursts or rejected tracking may cause
a corrective origin update; network impairment testing is still required.

**Crosshair Size:** 0.5–2×, default 1×. Inspector controls on `LaserRay` include
`aimDotSize` (reference size), `aimAngularSize` (default 0.4 degrees), minimum
0.006 m and maximum 0.4 m diameter, color, brightness, ring enable, outline width
and outline color. Texture styling is generated on component creation; restart
Play mode after changing ring/color/outline fields. Nominal diameters at 5/15/30/50 m
are about 0.035/0.105/0.209/0.349 m, before the size preference. Size is a visibility
aid, not a larger hit area. The existing muzzle raycast, first-hit filtering and
range determine the center. It is drawn on the hit surface with normal depth
testing, only for the local right gun and only on a hit. Oblique surfaces can
foreshorten the marker. Shot flashes and impact glows retain their existing size.
No additional raycasts, bloom or persistent aiming beam were added.

**Teleport burst:** server Inspector fields `teleportBurstLimit` = 7,
`teleportIdleReset` = 4 s, `teleportCooldown` = 4 s. Configure the network-player
prefab consistently for all peers. Only successful validated requests increment
usage and extend the deadline. Idle restores a partial burst; an empty burst stays
locked until its deadline. Rejections, duplicate requests and cooldown attempts
cannot extend the deadline or spend charges. Spawn and `ServerPlace` (round and
respawn placement) reset the allowance. Tracking submission continues during
cooldown. The existing 0.25 s request spacing, arena/stair support, occupancy,
source and clearance validation remain in place. HUD feedback reads server-written
usage/deadline and updates only when count or displayed tenth-second changes.

Settings reuse `LaserTag.Menu.Settings.v1`; existing audio/display/simulator
preferences and legacy snap-angle data remain. Missing new fields migrate to
defaults. Turn and crosshair sliders preview immediately; APPLY persists them.
BACK retains the preview for this session. Launch through MainMenu for the normal
persistent settings lifecycle. Gameplay input remains blocked while menus are open;
the right stick scrolls menus and the trigger operates XRI UI controls.

## Validation and acceptance

C# compilation using Unity 6000.6.2f1's Roslyn compiler and cached project references
passed for Editor, Android-symbol runtime code and Editor tools. Existing warnings
include deprecated Unity discovery APIs, a hidden `tag` member and compiler locale.
These checks are not an APK build or a replacement for Unity's complete build pipeline.
Changed-source whitespace checks passed; existing scene whitespace was left intact.

Unity batch validation uses an isolated `/tmp/vr-control-validation` copy because
the working project is open in another Editor. The Editor compiled the project and ran all 25 control-math checks successfully;
the existing arena/stair validation reported zero failures. Logs:
`/tmp/vr-polish-validation-final.log` and
`/tmp/vr-control-validation/Logs/ArenaBoundaryValidation.txt`.
Initial sandbox/Package Manager attempts failed; the successful run used the
normal Package Manager outside the sandbox. Android-symbol compiler logs are
`/tmp/vr-polish-android-compile.log`; final Editor/tools compile logs use the same
`/tmp/vr-polish-` prefix. No implementation work is blocked.
No Quest headset, five-player session or full Android build was run.

### Editor and Quest checklist

1. Open MainMenu; use the right controller to point at buttons, drag sliders,
   scroll, toggle mute and operate dropdowns. Confirm dim free pointing, green
   selectable hover, white press and an endpoint only on selectable controls.
   Confirm audio, room codes, readiness and match-duration selection still work.
2. Set turning to 30, 90 and 180; APPLY, exit and restart. Verify saved values,
   half-stick proportional speed, both directions and immediate stop on release.
   Turn repeatedly while the headset is offset from the tracking origin; the
   tracked head must not orbit. Check physical head/hand tracking and room scale.
3. Host with five players including Quest 2 and Quest 3. Check remote head/body/gun
   orientation during turning, physical movement, firing and teleporting. Repeat
   with latency/jitter and while opening menus, losing focus and respawning.
4. Aim at bright/dark surfaces and targets at 5, 15, 30 and 50 m. Compare the center
   with shot impacts in both eyes, including close range and angled surfaces.
   Check walls/cover occlude it, empty sky has no marker, remote dots are absent,
   the left gun stays disabled and no persistent beam appears.
5. Make seven valid teleports at least 0.25 s apart within the idle window. Observe
   7→0 and a 4 s lockout. Using a development client, send an eighth request directly
   to the server RPC: it must be rejected. Retry duplicates, stale revisions,
   invalid destinations and rapid requests; none may spend a charge or postpone
   recharge. Check a three-teleport burst refills four seconds after its last success.
6. Verify per-player budgets, room-scale tracking during cooldown, reset on respawn
   and new round, stairs in both directions, occupied landings, cover clearance,
   arena edges and outside destinations. Turn while aiming a teleport: the existing
   origin-change guard cancels that gesture; release and aim again.
7. Verify right-trigger combat, ammunition, health pickups, stars, health/lives HUD,
   player count, timer, lobby countdown and disconnect/rejoin behavior. Profile Quest 2.
8. Run **Tools → Laser Tag → Validate VR Control Polish** for analog/deadzone,
   repeated head-pivot/frame-rate and charge/deadline checks. Run the existing
   **Tools → Laser Tag → Arena → Validate Boundary and Stair Landings** in RelayVRGame.
   `VRControlPolishValidation.ValidateInBatch` runs both in batch mode. Finally compile
   Android player scripts/build an APK and perform the headset checks above.
