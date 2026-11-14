# Gameplay and visual expansion

Implemented on `test/assil-fixes`, Unity 6000.6.2f1 / URP 17.6, targeting the existing MainMenu → RelayVRGame flow. All work remains uncommitted. No branch operations, commits, merges or pushes were performed. The pre-existing change in ProjectSettings/ProjectSettings.asset was left untouched.

## Implemented behavior

- **Right pistol aiming:** extends the existing local aiming dot, which was already enabled on the right gun. Uses the exact muzzle-forward raycast and nearest hit used by shooting, including self-collider exclusions and cover. No surface means no dot. Adds adjustable cyan color, size and brightness. The targeting distance remains the existing `maxDistance`, capped at the server's maximum shot range. The continuous beam stays disabled and temporary firing effects remain. The dot requires a local player and membership in the local rig's guns; it hides for dead players and while gameplay input is blocked. Remote avatar gun visuals do not publish dots.
- **Night sky:** dark blue gradient sky with 2,400 deterministic, naturally distributed upper-hemisphere stars in one static triangle mesh. Brightness, size and blue-white color vary, including faint stars. Uses URP-compatible stereo shaders, depth testing, no star lights or particles. Preserves existing arena lights, ambient mode and environment assets. Adds a tiny blue ambient contribution rather than rebaking lighting. Restores previous skybox/ambient colors on destruction.
- **Health pickups:** three green unlit energy orbs by default, with gentle bobbing. Each restores 5 HP, capped at the configured maximum. Full-health, dead, spectating, out-of-round and untracked players cannot consume them. Collection gives a quiet generated sound, light haptic and brief green `+N HP` feed message showing actual healing.
- **Match timer:** lobby duration selector offers 3/5/10/15 minutes, default 5. Everyone sees the selection; only the host can edit it, before loading. Uses a server-owned deadline starting after the existing countdown, and the current HUD/scoreboard. Displays `MM:SS` beside the player-count area with an amber warning in the last 30 seconds. Timeout enters the existing GameOver phase, clears pickups and labels the scoreboard footer `TIME LIMIT`. Existing early elimination, respawn, late-join and restart rules continue.

## Authority and ranking

`LobbyNetworkState.MatchDurationSeconds` is server-write, read by all clients. Its setter rejects non-server calls, non-lobby phases and values outside the four approved durations. There is no client duration RPC. The match manager captures it when Playing begins; it cannot extend an active match. Restarts reuse the selected duration and create a fresh deadline. Late arrivals receive the current deadline and remain spectators until the next round under existing rules. Disconnects still use the existing survivor check.

Timeout ranking deliberately reuses `GetRanking`: remaining participants above eliminated players; eliminated placements retain their survival order; equal placements use kills descending, hits descending, deaths ascending, then owner client ID ascending. Consequently exact statistical ties have one deterministic winner, rather than a shared-win screen. Respawning players remain participants, as in existing round rules. No new scoring system was introduced.

The existing match NetworkObject owns a server-write `NetworkList<Vector3>` of pickup slots. Clients create cosmetic spheres from this list. Inactive slots use an off-map sentinel; no individual pickup NetworkObjects, client collection requests or continuous position updates are needed. The server polls accepted tracked feet every 0.15 seconds, checks life/round/health, 3D collection distance and solid cover, marks a slot consumed **before** healing, then schedules its replacement. A second player cannot consume that slot again. Late joiners receive the complete slot snapshot. Disconnected objects are removed by the existing player lifecycle.

Spawning traces the existing teleport arc from approved initial spawn points or current server-accepted living-player positions. It checks source clearance, eligible landing surfaces, slope, height, footprint support, body clearance, player separation and pickup separation. This reuses the current registry, including `additionalWalkableSurfaces`, reviewed floors and teammate-added eligible colliders. It does not introduce another walkable whitelist or weaken teleport validation. Attempts are bounded; failed slots retry after one second instead of spawning in invalid space. A valid sparse arena may have fewer than the configured maximum pickups until a safe location is found. This is conservative sampled teleport reachability, not a full navigation/connectivity proof for the entire map.

Network protocol version changed from 2 to 3, including the existing integration tool, to reject peers with the older variable/RPC schema. Rebuild all five clients together.

## Inspector configuration

On **RelayVRGame → Relay Match**:

| Component | Setting | Default |
|---|---|---|
| HealthPickupSystem | maximumActivePickups | 3 (Inspector range 0–12) |
| | healthRestored | 5 |
| | respawnInterval | 12 seconds per consumed slot |
| | collectionRadius | 0.7 m from accepted feet |
| | minimumSpacing | 3 m |
| | minimumPlayerDistance | 2 m |
| | orbSize | 0.18 m |
| | glowColor / animate | green / enabled |
| StarryNightSky | starCount | 2,400 |
| | brightness | 0.8 |
| | ambientContribution | 0.003 |
| | seed | 297 |
| LaserTagGameManager | matchDurationSeconds | 300; fallback only when no menu lobby exists |

On the existing **Right Gun → LaserRay**: `showAimDot`, `aimDotSize` (0.035 m), `aimDotColor` (cyan), `aimDotBrightness` (0.65), and the existing `maxDistance` (50 m). Keep `showAimLaser` disabled. Gun muzzle references and server aim tolerance remain unchanged. Sky mesh, material color and ambient contribution are initialized at scene startup; adjust these settings before Play for reliable previews.

## Scene and menu changes

RelayVRGame adds HealthPickupSystem and StarryNightSky to the existing Relay Match object, with explicit serialized defaults and stable script GUIDs. Its existing gameplay camera changes from Solid Color to Skybox so the nighttime gradient can render. Original light objects, RenderSettings serialization, walkable surface assignments, colliders, gun references and the disabled left gun are preserved. The manager initializes the pickup NetworkList in Awake and supplies feature components if absent.

The existing CREW LOBBY gains a duration dropdown between the player list and Ready button, using its current Choice/uGUI/VR menu layout. No separate menu system or prefab replacement was added. The five-player default capacity remains unchanged.

## Exact files

Modified:

1. `Assets/LaserTag/Editor/RelayGameplayIntegration.cs` — protocol version only.
2. `Assets/LaserTag/Scripts/FX/LaserTagFeedback.cs` — generated healing sound.
3. `Assets/LaserTag/Scripts/Game/LaserTagGameManager.cs` — deadline, timeout and pickup state/component setup.
4. `Assets/LaserTag/Scripts/Network/RelayCombatPlayer.cs` — owner-targeted healing feedback RPC only; shot/hit validation unchanged.
5. `Assets/LaserTag/Scripts/UI/PlayerHud.cs` — timer and timeout footer.
6. `Assets/Scenes/MainMenu.unity` — protocol version only.
7. `Assets/Scenes/RelayVRGame.unity` — two feature components and camera skybox flag.
8. `Assets/Scripts/LaserRay.cs` — local configurable aiming dot and shared range cap.
9. `Assets/Scripts/Menu/LobbyNetworkState.cs` — authoritative duration setting.
10. `Assets/Scripts/Menu/MainMenuController.cs` — lobby selector.

Created:

1. `Assets/LaserTag/Scripts/Environment/StarryNightSky.cs`
2. `Assets/LaserTag/Scripts/Environment/StarryNightSky.cs.meta`
3. `Assets/LaserTag/Scripts/Game/HealthPickupSystem.cs`
4. `Assets/LaserTag/Scripts/Game/HealthPickupSystem.cs.meta`
5. `Assets/LaserTag/Resources.meta`
6. `Assets/LaserTag/Resources/LaserTagNight.shader`
7. `Assets/LaserTag/Resources/LaserTagNight.shader.meta`
8. `Assets/LaserTag/Resources/LaserTagNight.mat`
9. `Assets/LaserTag/Resources/LaserTagNight.mat.meta`
10. `Assets/LaserTag/Resources/LaserTagStars.shader`
11. `Assets/LaserTag/Resources/LaserTagStars.shader.meta`
12. `Assets/LaserTag/Resources/LaserTagStars.mat`
13. `Assets/LaserTag/Resources/LaserTagStars.mat.meta`
14. `Assets/LaserTag/Resources/LaserTagPickup.mat`
15. `Assets/LaserTag/Resources/LaserTagPickup.mat.meta`
16. `docs/FEATURE_EXPANSION.md`

`ProjectSettings/ProjectSettings.asset` remains in Git status because of its pre-existing team/user change; it was not edited by this work.

## Performance and validation performed

The star mesh contains 7,200 vertices / 2,400 triangles, generated once with a local seeded RNG. It adds one unlit draw plus a simple skybox. Three pickup spheres share one unlit material, have no physics colliders, lights, particles or individual NetworkObjects. Their transforms animate locally. Server pickup checks run about 6.7 times per second; bounded spawn searches run only for due slots. Network traffic changes only on slot creation/collection/respawn and existing phase transitions, not every animation frame. Resource materials explicitly retain shader dependencies for builds. No bloom, realtime star shadows or expensive post-processing was added.

Performed:

- C# compilation using Unity's Roslyn compiler/current project response files: gameplay Editor, gameplay with Android/IL2CPP symbols, and Editor tooling referencing the newly compiled gameplay assembly: passed, no compiler errors. Existing obsolete-API warnings remain. Outputs/logs are in `/tmp/LaserTag-expansion-{editor,android,tools}.*`.
- The already-open Unity Editor automatically imported the new shaders/materials and recompiled scripts. `Logs/Editor.log` records Netcode IL post-processors and assembly reload, without new compilation errors. This does not substitute for an Android build or multiplayer test.
- Static GUID audit: all new script and resource material shader references resolve; scene IDs are unique.
- Scene-block comparison: every pre-existing gameplay scene block is unchanged except Relay Match's component list and the gameplay camera clear flag; exactly two component blocks were added. Both persistent beam flags remain disabled and the disabled left gun's serialization is unchanged.
- Numerical muzzle audit: existing right gun scale/grip/muzzle transforms reproduce the server's rightAimDirection and rightMuzzleOffset within 0.000001. No discrepancy was found and no server combat tolerances were changed.
- `git diff --check -- Assets`: passed. Full-repository diff check still reports the pre-existing trailing whitespace in ProjectSettings.asset; that user change was preserved.

Not performed: interactive gameplay, a five-peer Relay session, visual inspection in a headset, Android shader variant/build/IL2CPP validation, on-device profiling or performance measurement. The project was already open in Unity; no competing Editor instance was started. Source compilation with Android symbols uses current Editor references and is not an Android player build.

## Unity Editor testing

1. Allow Unity 6000.6.2f1 to finish importing/recompiling. Confirm the Console has no new script, Netcode or shader errors. Open RelayVRGame and inspect Relay Match's two new components, the existing right muzzle reference, unchanged left gun disable state and Skybox camera background mode.
2. Enter through MainMenu. Host with capacity 5. In the lobby select each duration; verify default 5 minutes. Join from another build; confirm it sees changes but cannot edit the dropdown. Both builds must use protocol 3. Verify Ready/Start, room code and loading still work.
3. Start a round. Verify the original countdown occurs first and the timer begins at the selected duration only when Playing starts. Check `MM:SS`, the player-count gap, health/lives, ammo, feed, announcements and scoreboard for overlap. For quick timeout iteration, enter the arena without the persistent menu lobby and set the manager's fallback duration to 15 seconds before starting; menu sessions deliberately override this fallback.
4. Aim the right gun at near/far walls, floors, cover and exposed avatars. Confirm the cyan spot follows the barrel rather than the camera. Move the camera independently, crouch and rotate the controller. Verify dot/shot impact agreement, cover blocking, no dot at empty sky, local-only visibility and unchanged brief shot beams/audio. Verify left trigger/teleport still cannot fire.
5. Check three or fewer pickups appear on supported accessible surfaces, separated from players/other pickups. Teleport to them. Check full health does not consume; test 90→95, 98→100 and repeated contact. Spectators/dead players must not collect. Confirm green feedback only for the collector, disappearance for both peers and replacement after approximately 12 seconds.
6. Have two wounded players approach one orb together. Exactly one receives healing. Attempt collection across a thin wall and from another floor. Check no healing from remote hand extension. Repeat with latency/loss enabled in your normal network test setup; collection uses server feet, not client requests.
7. Inspect random placements near ramps, stair edges, manually approved surfaces and teammate-added colliders. Check left teleport can actually reach each sampled destination. Obstruct available areas: missing slots should retry without stacking or invalid spawns.
8. Let time expire with multiple survivors. Verify shooting stops, the scoreboard appears, rankings follow the documented policy and TIME LIMIT is shown. Restart using the existing A/Enter action; health/ammo/lives/countdown reset, old pickups disappear and the full duration restarts. Also test elimination before timeout.
9. Join during Playing, disconnect a survivor, leave/rehost, return to the menu and restart repeatedly. Late joiners should spectate, receive current timer/pickups and join the next round. No orphan pickup/star objects or stale settings should remain across scene unloads.
10. Build Android using the project's existing Quest build configuration. Check both custom shaders for errors/pink rendering and validate Netcode code generation and IL2CPP before installing. Do not rerun environment/teleport installer tools merely to enable these features.

## Quest 2 / Quest 3 testing

1. Build/install the same protocol-3 Android build on all devices. Run at least one Quest 2 and one Quest 3, ideally five simultaneous peers through Relay, with each headset serving as host in separate runs.
2. Verify both hands, avatar tracking, right-only trigger shooting, left teleport arc and server teleport rejection around cover. Check continuous joystick locomotion and the left gun remain disabled.
3. At normal IPD and headset fit, confirm the world dot is visible but small at typical combat distances. Crouch, lean within permitted bounds and aim rapidly; compare actual impacts. Verify cover/enemy silhouettes never become visible through geometry. Tune dot size/brightness before building again if necessary.
4. Look around in both eyes at the star sky, horizon and arena surfaces. Confirm stable stereo alignment, unobstructed head motion, darkness, readable cover and no distracting shimmer. Compare the tiny ambient addition against existing lighting; adjust it toward zero if needed.
5. Read the lobby duration selector and HUD timer at headset resolution. Confirm no overlap with the existing HUD or end screen. Test the 30-second amber warning and timeout/restart behavior on all five devices.
6. Repeat the wounded/full-health/contested/through-wall pickup tests while teleporting and crouching. Verify each client's pickup positions and count agree, including late arrivals and a disconnected collector.
7. Profile an actual five-player match on Quest 2 with the Unity Profiler/device metrics. Check frame time, GPU cost, draw calls, GC allocations and server spawn-search spikes. Target the project's chosen refresh rate. If necessary reduce starCount and maximumActivePickups; do not relax collision validation.

## Remaining limits

Visual sizing, illumination, menu legibility and sustained five-player Quest performance require the above device tests. Very distant physical-size aiming dots may need tuning at headset resolution. Health pickups intentionally use conservative sampled arcs and can temporarily remain below their maximum count in cramped/occupied spaces. The server is the existing host-authoritative architecture; host migration is not added. Stars are static, with no twinkling or individual light sources. The orb's glow is an unlit bright appearance rather than bloom. The selected duration remains fixed for subsequent arena restarts until the host returns to a new lobby.
