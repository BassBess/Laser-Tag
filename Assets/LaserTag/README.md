# Integrated Relay game

## Teleport surface/navigation fix and temporary diagnostics

This section supersedes the original strict whitelist and persistent landing
reservation descriptions below. The authority protocol, tracking bounds and
input controls remain unchanged.

The navigation regression came from using SpawnManager.fallbackFloors as a
complete teleport whitelist. That 76-reference list was generated for spawn
fallbacks, and at least three current floor_2_LOD1 MeshColliders (scene IDs
601001652, 1236449459 and 1625145987) are missing. The server also checked source
visibility, four footprint probes, taller headset clearance and floor membership
while the client preview omitted those rules. An old landing remained occupied
even after its owner had moved away through accepted room-scale tracking.
These are code/scene findings; their relative impact has not been measured in Unity.

TeleportClearance now registers the reviewed references, plus static colliders
whose own object/mesh name starts with an authored floor/ramp/stair(s)/platform/
walkway token. Token boundaries distinguish `floor_2` from `FloorLamp`, for
example. Generic prop, wall, roof and ceiling colliders do not qualify merely
because their top faces upward. Semantic roof/wall/cover/crate object names also
prevent automatic opt-in through reuse of a floor mesh. CharacterControllers, rigidbody objects, avatar
and XR-rig geometry, triggers, inactive/disabled/empty colliders and Ignore
Raycast landing surfaces are excluded. Default is not a dedicated floor layer:
the inspected arena floors and other geometry share layer 0. Walls/cover on
other layers remain collision blockers even when they are not eligible landings.

For an intentionally walkable collider with a generic name, explicitly assign
it to LeftHandArcTeleport.additionalWalkableSurfaces. Do not add walls/props or
change Default into a catch-all walkable layer. The registry is cached once per
arena. After editing this list or adding colliders during Play Mode, use the
component context menu **Log Teleport Surface Inventory (Editor/Development)**
to rebuild it. Run this on both peers when making temporary Play Mode changes.
The menu prints registered hierarchy paths, layers and collider enablement.

Client preview and server validation use the same tracking-envelope function, curved-arc tracing, source
clearance/visibility, surface membership, slope, range/height, four support probes,
body capsule and current-player occupancy checks. The 18m range, 4m vertical
change, 35-degree slope, 0.25m radius, minimum 1.7m body height, 0.15m arc-match
tolerance and tracking envelope have not been increased. The capsule uses the
configured body radius with no extra distance-to-cover margin; valid space next
to cover is usable, but intersecting it still fails. Taller tracked eyes require
their existing extra head clearance on both preview and server.

Occupancy now follows the current authoritative feet from the replicated
snapshot, rather than an interpolated avatar or a permanently reserved historical
landing. Approval updates those feet synchronously before the next request can
be handled, so simultaneous overlapping requests still cannot both succeed.
The snapshot's Landing field remains for initial stage/spawn alignment.
Preview remains provisional: another player, moving geometry, tracking sample,
round transition or network delay can change the result before server processing.

### All teleport rejection gates

| Stage | Rejection conditions |
| --- | --- |
| Client startup/input | Missing rig/camera/left references or shader; focus loss; inactive rig/controller; no spawned local owner; dead player; missing match/movement; pending request; disallowed phase/participation; left tracking/device loss; server origin change while aiming. Neutral rearming and forward/release thresholds remain required. |
| Submission | Not owner/spawned; pending request; origin revision not initialized; tracking binding unavailable. |
| Server admission | Netcode owner permission; stale/duplicate request; request rate; stale origin revision; missing match; stale round; player not alive; disallowed phase/participation; non-finite candidate. |
| Tracking data | Stale sample; non-finite position; invalid/non-unit rotation; play-area radius, eye height, body horizontal/vertical offset, visual-head offset or individual hand/controller reach outside their existing bounds; excessive head speed. |
| Tracking geometry | Missing configuration; no eligible support at the projected feet/current floor height; head inside/crossing cover; saturated head/floor query. |
| Arc source | Source overlap query saturated; controller source inside solid cover; source occluded from the eye; source ray query saturated. |
| Arc | No solid landing within the segment window; path length exhausted; saturated query; candidate differs from the server's first arc hit by more than 0.15m. |
| Landing | Feet-to-point range; vertical change; slope; surface ineligible; missing footprint support, excessive support height difference or unsupported slope; saturated support/body query; occupied body capsule; another living player's current position within the existing separation distance. |

Permission failures are rejected by Netcode before the request handler. Other
server request failures now retain their specific reason instead of returning a
generic "Tracking sample rejected". Preview geometry uses an allocation-free
rejection value; diagnostics are formatted only when releasing a rejected aim.

### Reading the diagnostics and testing in Unity

1. Use matching updated code on host and client. Enter through MainMenu, load the
   arena and inspect the existing LeftHandArcTeleport on Relay Local Gameplay.
   Leave **Teleport Diagnostics** enabled initially. Filter Console for
   `[TeleportReject]`. Temporary warnings are emitted only in Editor/Development
   builds, at most once per second per preview/server player. Wait over one
   second between deliberate rejection tests to avoid suppression. Disable the
   checkbox after validation; existing throttled `[Movement]` security logs remain.
2. Invoke **Log Teleport Surface Inventory (Editor/Development)** in Play Mode.
   Confirm the floor_2_LOD1 colliders above register despite absence from the
   spawn list. Review any unexpected surface names before the demonstration.
3. In simulator controller mode press 9, hold Shift+I, then release I before
   Shift; alternatively hold/release F8. Sweep previously rejected floor areas,
   seams and different levels. A cyan arc should agree with server geometry
   checks on an unchanged scene; confirm the second peer sees each approval.
4. `[preview]` means local rules rejected the released aim; no request was sent.
   `[submit]` means ownership/binding/origin/pending prevented submission.
   `[server]` gives owner, request, revision, attempted destination, exact reason
   and current feet. `BodyBlocked`, `SourceInsideCover` and `SourceOccluded`
   include the blocking hierarchy/layer. Support failures include the failed
   probe point. `ArcMismatch` includes the server hit and measured discrepancy.
   `Tracking/...` identifies the tracking gate, not a destination range failure.
5. For `SurfaceNotEligible`, inspect the logged collider. If it is intentionally
   walkable static environment geometry, opt in that collider explicitly and
   rebuild the registry on both peers. A wall, roof or small prop should stay
   ineligible. Do not increase tolerances to hide a missing/incorrect collider.
6. Test a flat floor next to cover: approximately 0.26–0.35m from a vertical
   face should fit the 0.25m body radius if headroom/support are clear; 0.20m
   should fail with BodyBlocked. Also test walls, prop tops, real edges, slopes
   over 35 degrees, low ceilings and occupied destinations: all must remain
   blocked for the appropriate reason.
7. Have one player occupy a point: the other must be rejected. Move the first
   player's simulated head/feet about 1m away within the approved tracking
   envelope and wait for accepted updates: the vacated point should become free.
   Then make simultaneous requests for one free point: exactly one must succeed.
8. Verify death/countdown/respawn cancellation, remote positions, both hands,
   left-only teleport, right-only shooting, ammo/recharge, audio and HUD. Repeat
   representative floor/cover cases in a Quest Development build before relying
   on the change for the demonstration.

Native Unity physics/gameplay and headset validation have not been run here.
The installed Editor cannot start in this environment because required shared
libraries are missing. Managed compilation/tests do not establish arena coverage.
Validation completed: 57 managed checks passed against the runtime tracking,
occupancy, surface-name/mesh classification and rejection-reason functions.
Cached-reference C# compilation passed for Editor runtime, Editor tools and
Android/IL2CPP conditional code, with no new runtime compiler warnings.
Static checks confirmed the shared client/server rules, unchanged authority and
limits, and all 76 reviewed references. Scene/prefab/gameplay asset hashes and
`git diff --check` passed. No native physics, transport or Android build was run.

## Server-authoritative locomotion (protocol 3)

This section supersedes the historical owner-pose limitation and local-teleport
descriptions below. Use the same updated build on every headset. RelayRoomUI
sets protocol 3 before host/join, rejecting the incompatible protocol-2 builds.
The existing menu supports capacities 2–8; select **5** for the five-headset demo.
No scene, prefab, XR settings, gun, sound or HUD asset changes are required.

### Authority and tracking

Previously RelayAvatarPlayer exposed eight owner-writable world body/head/hand
pose variables. The bridge copied scene transforms directly into the network
mirror, and teleport moved the XR Origin locally. There is no NetworkTransform
on the registered player or its base avatar. Adding a request RPC alone would
have left the owner-pose bypass open.

Now a single server-written AuthorityPose holds an authorized origin, origin yaw,
revision, reserved landing and accepted relative TrackingPose. The owner submits
only relative tracking samples, at the existing 15 Hz by default. The server
rejects invalid samples before publishing any pose or moving network hitboxes.
The scene Scout avatar and XR camera/controllers continue tracking locally every
frame; the server mirror is separate. The bridge no longer moves that mirror.
Server hitboxes use accepted poses immediately; observing clients interpolate
within a revision and snap all body/head/hand targets together on a new revision.

The configured server tracking envelope allows a 2m horizontal radius in the
physical tracking coordinate system, eye height 0.2–2.6m, hands/controllers up to
1.65m from the eye, body within 0.3m horizontally of the eye and body y from
-0.8m to 0.3m. The existing calibrated visual head may differ from the raw eye by
up to 0.85m overall and 0.35m horizontally. Rotations must be finite unit quaternions. Head translation is bounded
by 3m/s plus 0.15m sampling slack; elapsed-time allowance caps at 2 seconds so
tracking can resume anywhere within the same bounded play area after a pause.
These are host-authored tracking plausibility settings on RelayAvatarPlayer,
not a change to the 18m teleport range. Review them against the actual safe play
areas and users before the demonstration. Local physical tracking is never
clamped or held waiting for a server response. Rejected samples retain the last
accepted network pose; corrections/feedback are throttled to once per five seconds.

The server checks head endpoints and sweeps a 0.1m head sphere against solid cover,
and requires the projected feet to stay on the reviewed floor at the authorized
floor height. Crouching/leaning are not tested as a full standing capsule on every
tracking update. A first valid headset sample is aligned to the server-selected
spawn, so initial physical stage offsets cannot displace the spawn. No client
world root, origin, yaw or teleport delta is accepted as tracking data.

### Approval, collision and concurrent requests

Stick/F8 release sends request sequence, origin revision, round number, candidate
landing and a fresh relative tracking sample. It does **not** move the rig.
The owner-only request RPC first checks freshness, state and request rate (one
request per 0.25s by default), then validates the tracking sample independently.
Periodic tracking inputs also have a 30 Hz server token budget with a three-packet burst.

Server physics reconstructs the same 48-segment curved arc from the accepted
left-controller pose and server scene settings: speed 8, upward boost 1.2, gravity
9.81, 0.065s step, 18m path/range limit, 4m vertical change and 35-degree slope.
The source must be clear and visible from the head, preventing a forged controller
inside/beyond cover from starting an arc on its far side. The first solid arc hit
must match the candidate within 0.15m; only the server hit is used for placement.

The existing SpawnManager.fallbackFloors array defines reviewed arena geometry.
Landing and footprint-support probes must hit those actual collider surfaces;
a broad bounds box, a floor-like object name or a prop top is insufficient.
Co-located mesh/box representations of the same reviewed surface are accepted.
The body clearance uses the existing 0.25m teleport radius and at least the 1.7m
configured height (increased for a taller tracked eye). The shared clearance
utility now fails closed on saturated queries and never ignores an entire
landing mesh, since that mesh may contain walls as well as floor triangles.

Destination separation remains twice the teleport body radius (0.5m by default);
the existing 2m spawn separation remains a separate SpawnManager rule. A successful
request updates the server snapshot and reserved landing synchronously before
another request is handled. Both current authoritative feet and reserved landing
block competing requests. Reservations last until the next placement or despawn;
dead/invisible actors do not reserve gameplay space. There is no wait for client
acknowledgement and no race involving clients' locally interpolated transforms.

Only a spawned living player in Lobby, or an in-round living player in Playing,
can manually teleport. Countdown, GameOver, respawn, eliminated and spectator
states remain blocked, matching the existing arc input gate. The old eliminated
HUD wording about exploring does not grant free movement. Server round/respawn
placement remains driven by SpawnManager through RelayCombatPlayer.ServerTeleport,
but now changes the same authoritative frame rather than sending a local-only
move command. Server-selected placements are exempt from manual request limits.

Every placement increments the revision. Requests and pose samples from old
revisions, old rounds or old sequences cannot restore the previous location.
Reliable owner approvals apply absolute origin position/yaw, so receiving both
an RPC and a snapshot cannot apply a delta twice. Manual approval preserves yaw
and tracked height. Round/respawn placement retains the requested facing yaw.
The owner also reapplies its last approved origin if unrelated local code moves
the rig, without modifying tracked child transforms. Rejection leaves that origin
unchanged and gives HUD/left-haptic feedback. Natural head/hand movement continues.

### Security boundary and verification status

A modified client cannot publish arbitrary arena root/head/hand world positions
or write the snapshot. It can still fabricate plausible physical tracking within
the configured envelope: hardware tracking cannot be attested by this protocol.
This is bounded movement validation, not complete anti-cheat or lag compensation.
The host/server and its authored scene/configuration remain trusted. A physical
user outside the envelope can see a local/network discrepancy until returning;
local tracking is deliberately retained for VR comfort. Shots still use the
existing server-validated right-hand poses and anti-spam/cover checks.

The reviewed floor list and colliders are the navigable-arena definition; there
is no existing baked NavMesh/connectivity graph. Arc reachability is enforced,
including cover, rather than inventing a straight-line path restriction or a new
navigation rule. Moving-platform parenting and arbitrary recenter/snap-turn of
the virtual origin are not supported by this fixed-origin protocol; physical
head/body turning remains supported. Existing scene continuous movement stays off.
The pre-existing SpawnManager emergency crowded-spawn fallback remains server
controlled and may use the least-crowded authored point when all points are busy.

Managed checks executed: 22 tests of the actual runtime functions: 16 tracking-envelope tests
(standing/crouching/leaning/independent hands accepted; non-finite data, radius,
height, root/head/hand/controller forgery and invalid rotations rejected), plus
six landing-separation checks (same landing, persistent reservation, separate
landing/storey, minimum distance and exact distance boundary).
Cached-reference managed compilations passed for runtime code with Editor
defines, Editor tools against the updated runtime, and Android/IL2CPP conditional
runtime code. No new runtime compiler warnings were introduced. These checks do
not run Netcode IL postprocessing or produce an Android player build.
Native Unity startup was attempted but missing shared libraries prevent running
the Editor in this environment. Physics, Netcode IL postprocessing, transport,
two-client gameplay and Quest behavior therefore still require the tests below.

### Two-client Unity acceptance procedure

1. Open MainMenu with this code on both peers, allow Unity compilation/Netcode
   postprocessing to finish, select capacity 5, host and join the same Relay code.
   Verify one manager per process, distinct owners and the existing active rig,
   both visible hands, disabled Hands rig/left gun and unchanged HUD placement.
2. In simulator controller mode press 9, hold Shift+I to aim, release I **before**
   Shift. F8 hold/release is an alternative Editor diagnostic using the same
   request. Expect one approved teleport, stable headset height/yaw, and one
   matching remote relocation (no lerp across the arena or second delta).
3. Repeat separately as host and joining client. Aim at cover, occupied space,
   prop/roof tops, floor edges, excessive range, a different level beyond 4m,
   steep surfaces and low headroom. Rejection must leave the XR Origin unchanged
   and show feedback. A locally cyan candidate is provisional; server rejection
   can occur if geometry/occupancy changed before the request arrived.
4. Have both peers request the same free landing at nearly the same time.
   Exactly one succeeds; the other gets occupied-destination feedback. Repeat
   while delaying a client/network tick and while one player physically leans
   away from the landing. Inspect server ReservedLanding and ServerFeet.
5. In Play Mode move the owner's XR Origin transform manually by 10m: it should
   be restored to its approved frame and must not move the remote avatar. Move
   the simulated head/root far outside the physical envelope: the server pose
   stays bounded, reports a throttled correction and recovers when tracking is
   plausible. Replaying old request/pose sequence or revision values through a
   development debugger must not cause another relocation or restore an old pose.
6. Aim, then die or enter countdown/respawn before releasing. Try F8 and the stick
   while respawning, eliminated or spectating. Server placement wins; old requests
   fail. Restart, disconnect/rejoin via the lobby and confirm no reservation or
   pending state is retained from a destroyed player. Late joining during play
   remains unsupported by the existing menu admission rules.
7. Crouch, lean and move hands independently; watch the other client's avatar.
   Fire the right pistol before and after teleports: verify damage/cover, ammo
   consumption/recharge, one accepted-shot sound, lives, respawn, hit markers,
   kill feed and scoreboard. Test rapid aiming under realistic Relay latency.

### Quest 2/Quest 3 and five-player acceptance

1. Build the same Android/IL2CPP version for all five devices. Set capacity 5
   before creating the Relay room. Use independently safe physical play areas.
2. Verify initial stage alignment, standing height, crouch/lean, both tracked
   hands and full head/body turning on both headset models. Inspect server
   tracking rejections and adjust only host tracking settings for measured users.
3. Independently teleport all five players repeatedly using left-stick forward
   and neutral release. Test simultaneous conflicting landings, inter-floor arc
   travel, nearby cover, floor edges and low ceilings. Right inputs never teleport.
4. Kill/respawn/restart while requests are in flight. Confirm consistent remote
   positions, no double move, no old-location hitboxes, correct shooting/audio/
   ammo/HUD/scoreboard and correct player counts after disconnects.
5. Test headset tracking loss/resume, application focus changes and Relay jitter.
   Check comfort, both-eye HUD visibility and Quest frame time/bandwidth with five
   players. Do not treat managed compilation as an Android build or hardware test.

## Right-pistol ammunition, sound and HUD

The ammo/sound regression was a scene reference error: LocalPlayerRig.guns
contained disabled Left Gun LaserRay 2116677072, leaving active Right Gun
LaserRay 46920595 unbound. LaserRay permits local visual firing without a
canFire delegate; without LocalPlayerRig's onFired subscription no shot RPC,
ammo prediction or shooting sound ran. The array now references only 46920595.
A startup error detects a missing active right gun. The left gun remains off.

Right TriggerButton -> LaserRay.TryFire -> LocalPlayerRig.CanFire/OnFired ->
RelayCombatPlayer.OwnerFired (gun 1) -> owner-only SubmitShotRpc -> server
Ammo/LastShotAt -> acknowledged prediction -> existing PlayerHud is the pipeline.
The immediate beam is predicted feedback, not proof of server acceptance.
Server checks still enforce round/life state, gun 1, finite ray, ammo, token
bucket rate, right muzzle offset, range and aim. Rejections log their category
at most once per five seconds per player. Acknowledgement follows the decision
for both accepted and rejected requests. No validation tolerance was increased.
The authored right muzzle frame matches the current half-scale pistol/grip.

Maximum ammo remains 100, refill delay 2 seconds, refill rate 8/second and shot
interval 0.25 seconds on RelayAvatarPlayer. Only accepted shots consume one unit
and reset recharge; the existing server recharge and round/respawn resets remain.
Pending requests predict consumption, then reconcile against replicated ammo and
acknowledgements. The recharge label is suppressed while a request is pending.

No shooting audio file is present; the existing project-owned generated Laser
Shot clip is reused. Accepted shots send one reliable owner audio event (clear
2D local sound). Remote ShotBeamFx already plays spatial audio, retained as the
single remote path, including on the host. Eight reusable audio voices avoid
per-shot GameObject creation and respect existing master/mute/SFX settings.
Remote range remains 30m with linear attenuation. Local acceptance sound follows
network latency; remote effects use the existing unreliable shot event.

The existing 0.6m-wide, camera-relative world-space canvas remains 0.6m ahead:
health/value and lives at top left, connected players/replicated lobby capacity
at top center, ammunition and recharge at bottom right. Numeric text updates
only when values change. Hit markers, damage/death overlays, kill feed,
announcements and scoreboard remain on the same HUD.

Validation performed: cached-reference C# compilation with Editor defines and
with Editor defines removed; both succeeded with no new compiler warnings.
Static scene/prefab checks verified right-gun wiring, disabled left gun, one HUD,
ammo/recharge defaults and right muzzle/aim frame agreement. This does not run
Unity's Netcode IL postprocessing, a player build, audio output or XR hardware.

### Shooting/ammo/audio/HUD acceptance steps

1. Use the same updated code on both peers. Enter through MainMenu, choose a
   room capacity, host/join, ready and start. Wait for Playing. Confirm top-left
   health `100/100` and configured lives, top-center actual players/capacity,
   and bottom-right ammo `100/100`; there must be one HUD.
2. In simulator controller mode, T exercises the right trigger (as bound in
   the imported controller controls). On Quest use the right trigger. Fire
   individual shots, observing 100 -> 99 -> 98, one local sound per accepted
   shot and one remote beam/spatial sound. Test separately as host and client.
   Shift+T/left trigger must neither shoot nor consume ammo. Countdown must block
   firing. A `[Combat]` warning identifies server rejection rather than hiding it.
3. Fire ten shots without a two-second pause. Expect 90 ammo. Stop: no refill
   before the configured delay, then approximately eight units/second to 100,
   with the recharge indicator. A new accepted shot restarts the delay. Fire
   into clear air, cover and a player: all accepted shots cost exactly one;
   hit/damage validation remains independent of whether ammo was consumed.
4. To exercise empty ammo with the default regeneration, temporarily set only
   the runtime server player's refillDelay to 60 in Play Mode, fire at the
   allowed interval to zero, then attempt another trigger: dry click, no shot
   sound/damage or negative ammo. Restore delay to 2; verify recharge resumes.
   Do not save this diagnostic override. Also verify excess-rate/replayed,
   wrong-gun and invalid-pose requests do not consume server ammo or play an
   acceptance sound; rejected client predictions must recover.
5. Take damage, lose a life and respawn: verify health/lives update, ammo resets,
   hit confirmations/kill feed/countdown/protection and scoreboard still work.
   Restart the round and verify resets again. Disconnect the joining peer and
   confirm the connected count changes without changing room capacity.
6. Verify SFX/master volume and mute controls. From the other peer, compare near,
   left/right and distant gunfire, with no doubled host sound. On Quest check
   both-eye legibility, HUD edges, aiming/teleport clearance and head tracking.
   Verify left-thumbstick teleport still works and both hands remain visible.

Runtime firing/reconciliation, audible output, two-client effects and physical
headset layout/controls require these manual tests; they have not been verified
by the static checks above.

## Left-hand teleport input (current final-game controls)

Only the right gun is enabled. Both tracked hands remain visible. The active
`XR Origin (VR)` is the teleport root; keep `XR Origin Hands (XR Rig)` disabled.

The installed XRI 3.6.1 simulator is automatically instantiated in the Editor
(`Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`). Its controller
controls asset binds Axis 2D to I/J/K/L and its simulator controls asset binds
Left Device Actions to Shift and Toggle Primary 2D Axis Target to 9.
`XRInteractionSimulator.ProcessControlInput` routes axes to the left controller
while Shift is held, regardless of which controller is selected for pose
manipulation. Without Shift those keys drive the right controller. Releasing
Shift first stops updating the left axis; `ClearControllerButtonInput` clears
buttons, not axes. Release I before Shift to publish a neutral left stick.
This is different from the classic XR Device Simulator controls.

`<XRController>{LeftHand}/{Primary2DAxis}` is a valid usage binding in this
version, matching the imported 3.5.1 Starter Assets. The 3.6.1 simulated
controller declares `primary2DAxis` with usage `Primary2DAxis` and aliases
`thumbstick`/`joystick`; the binding spelling was not the cause. The scene assigns
origin 282227457, camera 467642849 and left-controller transform 404321707.
The left controller uses a TrackedPoseDriver, not an ActionBasedController.

The custom action is independent of XRI locomotion action-map enablement.
Teleport pins a tracked, exclusively LeftHand device through each gesture,
starts above the forward threshold, and commits only within the neutral release
threshold. Right input is never a teleport source. Focus/tracking loss, disabled
rig, origin displacement, death or phase changes cancel aiming. Start from
neutral after cancellation. F8 is a separate `UNITY_EDITOR` diagnostic source
and follows the same movement and destination gates.

Manual movement is allowed for a spawned, living local owner in the lobby,
or an in-round player during Playing. Countdown, respawn, elimination,
spectating and GameOver block manual teleport. The release frame recomputes the
existing arc, range, slope, height and body clearance checks, and checks spacing
from living players. Server round/respawn placements retain their existing path.

Synchronization follows the existing stack: moving the active origin updates
ScoutLocalRigBinding (LateUpdate order 50), RelayVRAvatarBridge (75), then
RelayAvatarPlayer (100) publishes body/head/both-hand poses at poseSendHz (15 Hz
by default). Remote avatars interpolate those poses. This is a source audit,
not a verified two-client or headset runtime result.

**Server-validation limitation:** pose NetworkVariables are owner-writable.
These client gates prevent ordinary control use during restricted states but
cannot constrain a modified client. Server-authoritative movement still needs
an owner-only teleport request, server-owned round/life checks, approved source
and destination validation (range, floor, slope, clearance, arena boundaries,
player spacing and request rate), acceptance/correction sequencing for respawn
and round transitions, and rejection of pose jumps without an accepted request.
Room-scale tracking needs a separate bounded allowance. Validating only a new
teleport RPC would leave the existing owner-writable pose bypass open.

### Acceptance steps (not executed on VR hardware)

1. Enter through MainMenu, host/join the same Relay room and reach RelayVRGame.
   Confirm only XR Origin (VR) is active, both hands are visible, left gun and
   remote Left Gun Visual are disabled, and continuous move remains disabled.
2. In the Editor Game view, ensure the runtime XR Interaction Simulator is in
   **controller** mode. Press 9, allow a neutral frame, hold Shift, then hold I.
   Aim the left controller at a clear floor using the simulator UI. Expect a
   cyan valid arc/marker or red invalid arc. Release I while holding Shift:
   expect one teleport. Then release Shift. Selecting left pose manipulation
   alone is insufficient. If input fails, inspect Input Debugger's simulated
   LeftHand device: isTracked must be true and primary2DAxis must change from
   (0,0) to (0,1) and back. Verify the scene teleport action resolves that device.
3. Use I/J/K/L without Shift, right trigger and right stick controls. None should
   start or commit an arc. Test right inputs while holding a left aim as well.
   Hold/release F8 separately; it should use the same arc in the Editor only.
4. On Quest, start neutral, push **left thumbstick forward**, aim and release to
   center. Expect one teleport with headset height/yaw preserved. Ease the stick
   below 0.65 but above 0.25: aiming must continue. Sideways/backward motion must
   not begin aiming. Exercise every right control: none may activate teleport.
5. Reject walls, steep surfaces, blocked headroom, occupied destinations and
   destinations beyond range/height limits. While aiming, lose tracking, change
   focus, die, enter countdown or receive a respawn placement: no stale teleport
   should occur. Restore tracking/focus and return the stick to neutral to rearm.
6. With two clients, teleport separately as host and joining client; confirm the
   other view follows body and both hands after network ticks. While respawning,
   eliminated, spectating or in countdown, try both stick and Editor F8. Confirm
   server spawn placement wins, HUD/health/ammo/rounds remain correct, right gun
   fires, Shift+T does not fire a left gun, and physical headset tracking remains.

Open `Assets/Scenes/MainMenu.unity`. The canonical arena is `Assets/Scenes/RelayVRGame.unity`.
Both computers must use this same integrated build (network protocol version 2). The integration base is `feature/menu-lobby-assil` at `985cbe0`, containing networking commit `e270e56`.

1. Host a Relay room, join its code from another instance, and mark both players ready.
2. The host starts the match. Netcode loads the arena and the menu spawns the existing registered Relay player prefab. With at least two players, the round begins after a three-second countdown.
3. Aim and trigger either original controller gun. Each accepted shot consumes one shared ammunition unit. Defaults: 100 health, 10 damage for either head/body hit, 100 ammo, 8 ammo/second regeneration after two seconds without an accepted shot, two lives, three-second respawn and two-second spawn protection. Landing a hit ends the attacker's protection.
4. The last remaining player wins. The HUD shows rankings, kills, hits and deaths. Right-controller A, Enter or gamepad South requests a new round only while waiting or after game over. Escape opens the session menu; Return to Menu disconnects. Host departure closes the room. Host migration and late joining are unsupported.

## Contribution map

- Assil's menu/Relay foundation remains responsible for the persistent NetworkManager, lobby, approval, synchronized scene loading and player spawning. No second manager or LAN connection system runs in the arena. `MenuNetworkPrefabs.asset` already registers both MenuLobby and the original RelayAvatarPlayer prefab; no extra default registration is needed.
- BassBess's `RelayAvatarPlayer` remains the only network pose publisher/interpolator. `RelayVRAvatarBridge`, Scout tracking, arm/leg solvers, first-person visibility and original controller inputs remain in place. The local tracked avatar is visible as before; its owned network mirror is hidden. Remote mirrors include inert gun visuals aligned from the existing hand grips, without shooting scripts or colliders.
- Hend's `fea6ddf` round manager, spawn selection, local gun callbacks, head-locked HUD, feedback, beam pool and clearance checks are adapted under `Assets/LaserTag`. `RelayCombatPlayer` extracts her combat/lifecycle role onto the existing Relay player NetworkObject, without adding another pose system. `RelayMatchCoordinator` waits for menu-controlled player spawning before starting the first countdown.
- Mohammad's `67875be` hitbox component and ray selection are combined with Hend's fire gate and fired/hit callbacks in `LaserRay`. Hitboxes are invisible triggers, excluded from physical locomotion and floor placement. Local hit events are observations, never an authority to apply health changes.
- Nadia's `fec122a` desert scene is the source for reviewed collider additions. Stable Unity scene/prefab IDs are independently checked against object name, world transform and original prefab source. The import added 193 usable colliders to existing objects, changed no environment geometry, and is idempotent. Source inspection found 436 MeshCollider components including inherited prefab components; 87 empty MeshColliders, 52 redundant lower-LOD entries, 38 duplicate source entries and the source rig's CharacterController were excluded. Empty source components have no usable mesh and cannot provide collision. The retained editor tool can import an exported source scene with a backup outside Assets; it does not require that source scene at runtime.

## Authority and configuration

`RelayCombatPlayer` validates owner RPC permissions, sequence/round, per-gun host-time fire-rate budget (at most two queued shots to tolerate Relay packet batching), shared host ammo, finite endpoints, range and proximity/direction relative to the tracked gun. A hit claim must reference a recent accepted shot and may be consumed once. The host rechecks target shape tolerance, nearest player/solid cover and life/protection state before changing health, lives or scores. Remote hit-confirm feedback is sent only after damage is accepted. Both head/body hits currently deal the same configured damage; their statistics are separate.

Existing avatar poses are still owner-published. This is bounded host validation, not movement anti-cheat or historical lag compensation. Fast movement and Relay latency need multiplayer tuning; invalid/outdated claims fail without damage. Death disables firing and network hitboxes; eliminated players may still use the existing XR locomotion to observe the arena.

Configure health/ammo/damage/cooldown and muzzle validation on `Assets/Networking/RelayAvatarPlayer.prefab` (`RelayCombatPlayer`). Configure round/lives/respawn/protection on `Relay Match` in RelayVRGame (`LaserTagGameManager`). `Match Spawn Points` contains 16 reviewed floor positions plus fallback floor sampling and occupancy checks. Regeneration settings live on the combat component, not the local HUD.

`Tools > Laser Tag > Integration > Configure Relay Gameplay` wires the canonical scene and existing network prefab through Unity APIs. It adds gun firing components using the existing XRI Activate actions, leaving gun transforms and input assets intact. Preserve original model scale when changing grip settings; rerun wiring only after reviewing scene changes. The original RelayVR and AvatarFirstPersonTest scenes are not regenerated.

The existing editor-only floor-relative Q/E filter and its menu scene-load activation are preserved. It never clamps real headset tracking. Safe teleport targets, LAN discovery/bootstrap, Hend's alternate NetworkPlayer/prefab/arena, practice bots, unrelated project settings and package changes were deliberately not imported. Spawn/respawn relocation reuses Hend's rig placement logic; optional teleport locomotion is deferred.

## Validation

Native Unity compilation and local Editor Play checks passed for menu-to-game loading, first-person head hiding with hands visible, one manager/owned mirror, active floor-relative Q/E limits and immediate return from either limit, first round startup and return-to-menu cleanup. Environment import was repeated without additional changes. The final fixture-free Unity compilation exited successfully (code 0).

Two local standalone Mono processes completed the real-Relay flow: host/join, synchronized ready/loading, two actors with one manager, owner mirror hiding, head/body classification and damage, shared ammo exhaustion/regeneration, cover and replay rejection, a bounded queued-shot rate test, respawn and protection, elimination/rankings, restart, simultaneous host dual-gun damage to the client, leaving, a new Relay allocation, rejoining and host-disconnect recovery. The final complete run used the submission runtime code without diagnostic logging. Temporary fixtures controlled the existing controller transforms and invoked the guns' shared firing API; this was not physical XR-trigger testing or a bot test. Source and prefab audits found no new missing scripts/references and preserved all 844 existing scene objects' transforms, meshes and materials. The scene now has 403 colliders, including 263 MeshColliders.

The Windows IL2CPP build module is not installed on the test machine. Validation used a temporary Mono build and restored the project's backend; no backend/package/project-setting changes are submitted. Temporary fixtures, source branch scenes, build output, reports and backups are not runtime dependencies or submission assets.

Two-computer acceptance, physical XR trigger/haptics, headset tracking, visual grip inspection while moving, rapid aiming under variable latency, Quest build/performance, floor/wall traversal and full arena collision coverage require manual testing. An earlier synthetic post-turn hit was rejected; subsequent complete runs passed. The bounded host checks may reject outdated poses; no historical lag compensation or movement anti-cheat is implemented. A collider count is not proof of exhaustive coverage. The native audit found ten broken references already present in the foundation: eight simulator UI sprite references and two door Animator avatar references. No new broken references or missing scripts were introduced by the integration; these inherited references were left unchanged. The editor has pre-existing Search-index exceptions; desktop without an HMD may emit the base project's OpenXR/AR Simulation warnings. Those package configurations are not replaced here.

Existing stack: Unity 6000.6.2f1, Netcode for GameObjects 2.13.3, Unity Services Multiplayer 2.3.3 (Relay/Authentication), Unity Transport, XRI 3.6.1 with the project's existing imported Starter Assets, Input System 1.20.0, URP 17.6.0 and TextMeshPro UI. No package changes are required by this integration.
