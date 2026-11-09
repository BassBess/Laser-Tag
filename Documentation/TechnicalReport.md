# Multiplayer VR Laser Tag – Technical Report (draft)

> Draft prepared from the code. Fill in the **[TODO]** parts (team names, test results, screenshots) before submitting.

Course: CMPS297AG – Introduction to XR · Team: **[TODO]** · Members: **[TODO]**

## 1. Overview

Up to five players wearing Meta Quest 2 or Quest 3 headsets enter the same virtual arena, move by teleporting,
take cover and tag each other with laser guns. Each player has 2 lives; the last player standing wins, and the
round can be restarted from the scoreboard.

| Item | Choice |
| --- | --- |
| Engine | Unity 6000.6.2f1, URP 17.6.0 |
| XR | OpenXR 1.18 (Meta OpenXR 2.6.1), XR Interaction Toolkit 3.6.1, Input System 1.20 |
| Networking | Netcode for GameObjects 2.13.3 over Unity Transport (client–host / listen server) |
| Connectivity | Local Wi-Fi auto-discovery (UDP broadcast) – first headset hosts, others join automatically |
| Arena | `Assets/LaserTag/LaserTagArena.unity`, generated from the Task 1 arena by `Tools › Laser Tag › Part 2 › Build Game Scene` |

## 2. Architecture

```
            ┌────────────── every headset ───────────────┐
 XR rig ──► LocalPlayerRig ──► NetworkPlayer (owner) ──────┼──► host
 (camera,    ammo, fire gate,   pose (20 Hz), ammo,         │      LaserTagGameManager (server-authoritative):
  guns)      hit claims,        hit claims (RPC)            │       phases, damage, lives, respawn, ranking,
             teleport on spawn                              │       hit validation, spawn selection
 PlayerHud ◄── reads NetworkPlayer + GameManager state ◄────┘      SpawnManager + TeleportClearance
```

| Layer | Scripts | Responsibility |
| --- | --- | --- |
| Session | `LanSessionBootstrap`, `LanDiscovery` | Find or host a game on the local network, start Netcode, reconnect when the host leaves |
| Players | `NetworkPlayer` | Synced player data, pose sync, remote full-body avatar, hit flash, shot/hit RPCs |
| Local player | `LocalPlayerRig` | Ammo/energy, fire gate, hit claims, pose capture, server-requested teleports |
| Game rules | `LaserTagGameManager` | Lobby → countdown → playing → game over; damage, lives, respawn, elimination, ranking, restart |
| World | `SafeTeleportationArea`, `SafeTeleportationAnchor`, `TeleportClearance`, `SpawnManager`, `LaserTagSpawnPoint` | Valid/unoccupied teleport targets and spawn points |
| Presentation | `PlayerHud`, `ShotBeamFx`, `LaserTagFeedback`, `GunOnlyControllerVisuals`, `TeleportRayVisibility`, `LaserRay` | HUD, beams, sound, vibration, rig visuals |
| Tools | `LaserTagSceneBuilder` (editor), `Testing/*` (practice bots, optional) | Scene setup; solo testing |

## 3. Authority model (who decides what)

We use a **client–host model**: one headset is the host (server + player), the others are clients.

| Decision | Who decides | Why |
| --- | --- | --- |
| Own head/hand pose | Owner headset (`NetworkVariable` with owner write permission) | Tracking only exists on that headset; lowest latency for looking/aiming |
| Teleport destination validity | Owner headset (sphere checks) | Must feel instant; the host still re-places players at spawn |
| Ammo / energy | Owner headset | Purely local resource with instant feedback; shown on the owner's HUD |
| **Whether a shot hits** | **Host** (claim from shooter, validated on host) | Single source of truth; cover must block shots identically for everyone |
| Damage, health, lives, kills, deaths, placement | **Host** (`NetworkVariable`, server write) | Prevents conflicting scores between headsets |
| Round phase, countdown, respawn time, winner | **Host** | Same game state on every device |
| Spawn positions | **Host** (`SpawnManager`) | Guarantees two players never get the same free point |

### Hit decision in detail
1. The shooter's `LaserRay` raycasts from the gun muzzle; walls, props, doors and avatars all have colliders,
   so the beam stops at the first one (visible cover).
2. If the collider belongs to another player, the shooter sends a **hit claim** RPC: victim, gun position, hit point.
3. The host (`LaserTagGameManager.ServerValidateShot`) accepts the claim only if
   - the gun position is within 0.8 m of one of the shooter's synced hands,
   - the distance is within 60 m,
   - the hit point lies within 0.9 m of the victim's body **as the host sees it** (tolerance for ~100 ms of movement),
   - a raycast on the host from the gun to the hit point is not blocked by any collider except the two players
     involved (**cover is enforced by the host**).
4. Then `ServerHandleHit` applies the rules: round must be running, both players alive and in the round, the
   victim not spawn-protected, max one hit per 0.15 s per shooter. Health −10; at 0 the victim is tagged out.
5. Results reach everyone through `NetworkVariable`s (health, kills…) and RPCs (kill feed).

Rejected claims are logged on the host with the reason (e.g. `blocked by glass_panel_1`).

## 4. Synchronization

| Data | Mechanism | Rate / reliability |
| --- | --- | --- |
| Pose (body root, head, both hands) | `NetworkVariable<PoseState>` (owner write, memcpy struct, 100 bytes) | 20 Hz, only when changed; remote avatars interpolate and snap on teleports > 1.25 m |
| Health, lives, kills, hits, deaths, placement, life state, in-round, respawn time, spawn protection | `NetworkVariable` (server write) | On change, reliable |
| Ammo | `NetworkVariable<int>` (owner write) | On change |
| Phase, countdown end, round number | `NetworkVariable` on the in-scene Game Manager | On change |
| Shot beams | `Rpc(SendTo.NotMe)`, unreliable | Per shot (cosmetic) |
| Hit claims | `Rpc(SendTo.Server)`, reliable, owner only | Per hit |
| Server teleport (spawn/respawn) | `Rpc(SendTo.Owner)` | Per event; the owner moves its XR rig through the `TeleportationProvider` |
| Kill feed / join-leave messages | `Rpc(SendTo.Everyone)` | Per event |

Timers (countdown, respawn) are stored as **server time stamps** and every client computes the remaining
seconds from `NetworkManager.ServerTime`, so countdowns are identical on all headsets.

### One virtual world, independent physical play areas
Every headset loads the same scene and positions avatars from **virtual-world coordinates** sent by the owner.
A player's physical guardian area only moves their XR rig locally; teleporting moves the rig in the virtual
world. Example from the brief: if A and B are 3 m apart in the arena with a box 1 m from A, every headset renders
the box 1 m from A and 2 m from B, because all positions are in the shared arena coordinates (no colocation or
spatial anchors needed).

## 5. Movement and teleport validity

- Movement is teleport-only (thumbstick forward, aim, release) plus snap turn; players stay in their own guardian.
- `SafeTeleportationArea` covers the arena floors. A target is **rejected** (arc shows the blocked style) when a
  column of physics spheres (radius 0.4 m, up to 1.8 m) overlaps any collider (walls, props, avatars) or another
  living player is within 1 m.
- Stairs are not teleportable; anchors at the bottom and top move players between floors and are blocked while
  someone stands on them.

## 6. Match flow

`Lobby → (A / Enter) → Countdown (3 s, players placed at random free spawn points) → Playing → GameOver → (A) → Countdown…`

- Lives: 2 (one respawn). Respawn after 3 s at a random free spawn point with 2 s spawn protection.
- Eliminated players can teleport around but are invisible, cannot shoot and cannot be shot.
- Game over when one player (or nobody) is left. Ranking: survival order, then kills, hits, fewer deaths.
- Late joiners spectate until the next round.

## 7. Edge cases required by the brief

**Simultaneous firing.** Hit claims are processed by the host one at a time in arrival order. If A and B tag each
other "at the same time", the claim that reaches the host first tags its victim out; the second claim is then
rejected because its shooter is no longer alive. Each hit is applied exactly once and all headsets see the same
result. Both players still see their own beam immediately (local feedback); only the authoritative outcome
waits for the host.

**Player disconnect.**
- *Client leaves*: Netcode removes its player object on every headset; the host posts "Player N left"
  (or "disconnected – out of the round") in everyone's feed; the round-over check runs every tick, so if only one
  player remains the round ends normally.
- *Host leaves*: the session ends for everyone; the clients search the network again and one of them becomes
  the new host, the others join it (back in the lobby). The match state is not migrated (see limitations).

## 8. Performance on Quest 2

- Avatars do not cast shadows; HUD text is only rebuilt when it changes; pose sync is 20 Hz and change-only.
- Teleport clearance checks only run while aiming; spawn checks only on the host.
- Arena colliders are static mesh colliders (LOD0 only); light-beam effects have no colliders.
- FPS counter: *Local Player › Player Hud › Show Fps*.
- **[TODO] measured FPS on Quest 2 with 5 players:** ___ fps (target 72).

## 9. Testing

| Test | Setup | Expected | Result |
| --- | --- | --- | --- |
| 2 headsets join | Quest 2 + Quest 3 on the same Wi-Fi | Both see each other, same arena | **[TODO]** |
| 5 headsets join | 5 Quests | All spawn at different points, see each other | **[TODO]** |
| Cover blocks shots | Shoot at a player behind a wall | No damage; host log "blocked by …" | **[TODO]** |
| Ammo | Fire 100 shots | EMPTY, refill after 2 s at 8/s | **[TODO]** |
| Death / respawn | Reduce a player to 0 HP | 3 s countdown, respawn at free point, 2 s protection | **[TODO]** |
| Elimination / win | Use both lives | Invisible spectator; last player wins, scoreboard | **[TODO]** |
| Simultaneous firing | Two players shoot each other together | Exactly one tag-out applied | **[TODO]** |
| Client disconnect | Quit one client mid-round | "left" message, round continues/ends correctly | **[TODO]** |
| Host disconnect | Quit the host | Clients regroup under a new host | **[TODO]** |
| Teleport validity | Aim at walls, props, near players | Arc blocked, no teleport | **[TODO]** |
| Quest 2 performance | 5 players (or 1 + 4 practice bots) | ≥ 72 fps | **[TODO]** |

## 10. Known limitations

- Listen server: the host is also a player; no host migration.
- The first raycast is done by the shooter's headset; the host validates position, range and cover, but a
  modified client could still claim hits it genuinely had line of sight for.
- Ammo is owner-authoritative (simple, instant); a modified client could fire without ammo.
- Local Wi-Fi discovery needs a network that allows broadcast between devices (bring a hotspot/router otherwise).

## 11. Starting material, adaptations and contributions

| Starting material | Source / licence | How it was adapted |
| --- | --- | --- |
| XR Interaction Toolkit Starter Assets | Unity, Unity Companion Licence | Teleport interactor prefab, input actions |
| Sci-Fi Styled Modular Pack, 3D Sprite Sci-Fi Props | Asset Store **[TODO licence]** | Arena geometry; colliders added |
| Sci Fi Guns (The Developer Train) | Asset Store **[TODO licence]** | Gun models |
| Scout avatar, LaserRay | Our Task 1 work | Full-body avatar reused for remote players; LaserRay given fire gate/fired event |
| Netcode for GameObjects | Unity | Networking layer |

Original contributions: see `Documentation/ContributionStatement.md`.
