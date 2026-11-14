# Multiplayer VR Laser Tag  -  submission report

Entry scene: `Assets/Scenes/MainMenu.unity`. Canonical arena: `Assets/Scenes/RelayVRGame.unity`.
Both players must use the same build; the current network protocol is 4. The original
`RelayVR.unity` and `AvatarFirstPersonTest.unity` remain regression scenes.

## Setup and play

Use Unity **6000.6.2f1** with Android Build Support, SDK/NDK and OpenJDK. The project
uses Netcode for GameObjects **2.13.3**, UGS Multiplayer **2.3.3** (Relay and
Authentication **3.7.4**, Services Core **1.18.0**), Unity Transport **6.6.0**, XRI **3.6.1**, Input System **1.20.0**, URP **17.6.0**, OpenXR
**1.18.0**, Meta OpenXR **2.6.1** and XR Management **4.7.0**. Preserve the manifest
and lock file. Imported XRI assets include older 3.5.1 samples.

Internet access and the team's existing Unity Services project with anonymous
Authentication/Relay are required. Do not insert service credentials into source.
Open MainMenu, select Play, host with five total slots, share the room code, and
have everyone join and mark Ready. Only the host starts. At least two players are
needed for a round. After synchronized loading, wait for the countdown.

VR controls and complete default rules are in **How to Play**, also accessible
from in-game options. Left thumbstick forward aims teleport; release requests a
clear floor landing. Right trigger fires. Turn/look physically. Left menu opens
options; a controller ray and trigger operate the menu. Right A requests a new
round after game over. Returning to menu leaves the room; the host leaving closes
it. No host migration or joining after loading starts is supported.

Each headset needs its own clear physical play area, with its Quest boundary
enabled. Everyone uses the same virtual arena coordinates; no physical colocation,
shared anchors or passthrough is required. Do not physically walk through cover.
Laptop testing controls are separate in [LAPTOP_TESTING.md](LAPTOP_TESTING.md).

## Rules and authority

Defaults: 100 health, 10 damage for either head or body, 100 ammunition, one shot
per trigger press, 0.25-second cooldown, 50-metre range. After two seconds without
an accepted shot, ammunition regenerates at eight per second. Two lives allow one
respawn after three seconds. Respawn protection lasts two seconds; landing a
validated hit ends it. Blocked spawns delay respawning until a clear location is
available. Last survivor wins; ranking then compares kills, hits and fewer deaths.

`MenuSessionController` owns one persistent NetworkManager and Relay connection.
Connection approval disables automatic player creation. `LobbyNetworkState`
replicates members/readiness and loading phase; the host uses Netcode synchronized
scene management, then spawns the registered existing player prefab. The arena
does not contain another NetworkManager. LAN discovery/bootstrap is not used.

`ScoutLocalRigBinding` reads the original XR camera/controllers/guns. It updates
visual grips late and before camera rendering. `ScoutAvatar` solves separate arm
segments and hides only the local helmet/optional arm segments from its camera.
`ScoutProceduralLegs` handles restrained visual posture, crouch and foot resets.
The editor-only Q/E adapter filters synthetic input; real headset camera tracking
is never clamped by avatar anatomical limits.

`RelayVRAvatarBridge` submits bounded tracking offsets to `RelayAvatarPlayer`.
The host owns virtual origin, teleport approval and replicated poses. It validates
sequence/revision, finite values, tracking envelope and cover before publishing.
The local tracked avatar remains immediate; its network mirror is hidden locally.
Remote avatars use independent replicated targets. `RelayCombatPlayer` validates
owner-only shot requests, sequence, eligibility, muzzle/aim, rate and ammunition.
Host raycasts select hits against cover and `PlayerHitboxes`; clients cannot
directly apply health changes. Hit claims are checked and duplicate claims rejected.
Health, ammo, lives, scores and round state are server-written network variables.

`TeleportClearance` shares client/server source, arc, surface, footprint, slope,
height, body-clearance and occupancy checks. Spawn placement also requires floor
support and free space. Approved origins update immediately, reserving occupancy
before another request can be processed. Physical camera/controller local tracking
is preserved. A simultaneous conflict should approve at most one occupied landing.

Server-assigned free color slots give up to eight participants distinct LED
colors without repainting white armor. `RelayPlayerPresentation` applies the same
color to the local tracked suit and remote mirror, and displays depth-tested
name tags only over living remote players. Color is not the sole identifier.

## Requirements and evidence  -  14 November 2026

These statuses distinguish automated laptop evidence from required headset tests.
No grade or five-headset performance claim is made.

| Instructor requirement | Main implementation | Evidence/status |
|---|---|---|
| Same arena/coordinates, predetermined spawns, cover | RelayVRGame, SpawnManager, TeleportClearance | Implemented/tested (laptop): five real Relay processes had matching arena mesh-coordinate hashes, five separated spawns and one manager. Native cover blocked shots. |
| Visible head/hands/body, local first-person view | ScoutAvatar, ScoutLocalRigBinding, RelayAvatarPlayer/Bridge | Implemented/tested (laptop): hidden owner mirror, four visible remote avatars per peer; local helmet hidden and hands visible during offscreen camera rendering. Moving right grips/elbow/wrist continuity checked in four poses. Physical visual inspection pending. |
| Controller teleport to valid unoccupied floor | LeftHandArcTeleport, TeleportClearance, RelayAvatarPlayer | Implemented/tested (laptop): left-controller arc and server request API, blocked/unsupported/forged landing rejection, camera-local pose preservation and immediate visual foot reset. Physical stick gesture, stairs/elevation routes and simultaneous competing requests need headset/manual testing. |
| Controller weapon, ammunition, solid cover | LaserRay, LocalPlayerRig, RelayCombatPlayer | Implemented/tested (laptop): actual existing gun component API, ammo depletion/regeneration, own exclusion/source inspection, local and host cover rejection. Physical trigger/haptics pending. |
| Synchronized shots/hits/health/scores/ammo; feedback | RelayCombatPlayer, LaserTagGameManager, PlayerHitboxes, LaserTagFeedback, ShotBeamFx | Implemented/tested (laptop): real Relay body/head hits, damage, ammo, duplicate/covered claim rejection, concurrent firing, protection, respawn, elimination and ranking. Listening/haptics on hardware pending. |
| Join/start/win/restart; death/respawn; authority | MenuSessionController, LobbyNetworkState, LaserTagGameManager | Implemented/tested (laptop): ready/start/load, two-player full round/restart, blocked-spawn respawn delay, leave/rehost/rejoin and host shutdown recovery. |
| Five simultaneous Quest 2/3 players, mixed hardware, responsive Quest 2 | Android/OpenXR configuration, registered prefabs, 5-player lobby/HUD | Implemented/unverified on hardware: five real processes on one laptop passed, not five headsets. Physical mixed Quest 2/3, five-headset session and responsiveness/profile remain unverified. |
| Source + installable Quest build + versions/instructions | Repository, SubmissionBuild, this report | Implemented/tested build: source/versions/instructions provided. Signed ARM64 Quest APK built and verified; installation/device test pending. |
| Contributions, reuse, licenses, AI verification; live explanation | Statement below | Missing final team evidence: draft supplied; team sign-off, license receipts and individual live-debug demonstration pending. |

### Actual validation and limits

Tests ran on a Windows laptop (Intel Core Ultra 7 155U / Intel Graphics), not a
Quest. Native Unity compilation, Windows Mono validation builds and the final Android IL2CPP/ARM64 release APK build passed. APK signature v2 verified; manifest minimum API 32, target API 34, Quest device support and required VR head tracking were checked. Android XR-specific required SPATIAL/hand-tracking capabilities were removed by disabling only that platform's three enabled features; Meta Quest/controller features remain enabled.
Hidden Editor Play mode exercised MainMenu loading, synthetic Q/E limits, blocked
WASD head movement, grip continuity, visual foot resets, local visibility and
return-to-menu. Real Relay used five separate local processes for the lobby,
arena/color/tag/spawn checks and two for combat/lifecycle/reconnect. Controlled
test poses and gun APIs were used; this is not physical trigger input testing.
The temporary automation is excluded from submission. The last five-process run additionally checked every suit's applied LED-emission property blocks and remote gun visibility/hand parenting. Name text measured about 10.5 cm tall, and an offscreen cover render hid it. A hidden Play-mode check confirmed persistent shot effects were cleared on session exit. Native audits found zero missing scripts/references, 24 clear spawn points and 301 mesh colliders in the final arena.

Offscreen runtime menu captures were reviewed at 1280x720 and 1920x1080. Settings
mute/master/SFX gain, a single gameplay ambience source and preference serialization were checked, restoring the
prior local preferences afterwards. A hidden render-order fixture also verified late-camera menu placement. VR menu rays/keypad, in-headset HUD/text size,
actual sound balance and controller haptics still need manual testing. Offscreen
arena previews are not a Quest performance measurement. No connected ADB device
was available. Five local processes do not prove five computers or headsets.

Before submission, demonstrate the complete flow with five mixed Quest 2/3
headsets: physical trigger/teleport input, clear/occupied/wall/edge landings,
stairs, aiming/grips, cover from both sides, simultaneous shooting, ammo/health,
death/respawn, winning/restart, loading/client/host disconnect and reconnect.
Measure Quest 2 frame timing, draw calls, memory and network traffic using Unity
Profiler/Meta tools; no invented timing or performance numbers are supplied.

## Contributions, starting material and AI use

The following follows the team's attribution and the integrated branch history.
Each member must confirm their name, exact scope and any additional external
sources before submission; do not treat this draft as a signed contribution log.

| Contributor | Contribution/adaptation | Live-debug practice |
|---|---|---|
| Assil | Scout procedural avatar/visibility/grips/pose limits; main menu, lobby, settings and loading; final integration review | Trace camera -> local binding -> bridge -> host snapshot; adjust a grip/LED or menu setting and explain camera-only head hiding. |
| BassBess | Relay host/join and original XR-to-network avatar bridge, including e270e56 | Explain connection approval, prefab registration, owner/server authority and disconnect cleanup. |
| Hend | Gameplay rules, ammo/health, lives/respawn, HUD, scoring and feedback from fea6ddf, adapted to Relay instead of a second LAN player system | Trace trigger -> shot request -> host validation -> damage -> respawn/round state; change a rule value and verify synchronization. |
| Mohammad | Body/head hitboxes, own-player exclusion and hit events from 67875be, combined with shot callbacks | Trace head versus body collider, cover ray selection, replay rejection and host damage. |
| Nadia | Desert collider improvements from fec122a, selectively integrated onto existing corresponding objects | Explain floor/cover collision and LOD collider selection; debug a blocked or unsupported landing. |

Starting materials include the Unity XR template and XRI Starter/Hands/Simulator
samples; the existing Sci-Fi Styled Modular Pack desert environment; The Developer
Train Sci Fi Guns; existing props; Unity Netcode/Relay/Transport and URP. Original
avatar geometry is generated by project code, not a purchased human skeleton.
The final outdoor addition reuses existing floor/wall prefabs rather than copying
the entire desert scene. Existing licensed XRI sample ambient audio is reused;
no arbitrary downloaded music was added. XRI package license/third-party notices
remain in the package. Team action: verify Asset Store entitlements, exact asset
authors/versions and any required attribution/redistribution restrictions before
publishing source; do not claim every vendor license has been independently cleared.

AI assistance (ChatGPT/Codex) was used for code generation/adaptation, procedural
avatar work, integration fixes, audit fixtures and documentation. Human/team review,
native compilation, hidden Play mode and real Relay process tests provide the
verification described above. AI output does not establish hardware compatibility
or replace each member's ability to explain and modify their own contribution.

Submission email subject, exactly: **CMPS297AG_final_project**.
