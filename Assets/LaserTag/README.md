# Laser Tag – Part 2 (gameplay + multiplayer)

Everything for Part 2 lives in `Assets/LaserTag`. The scene is generated from the Task 1 arena
(`Assets/PlayerAvatar/AvatarFirstPersonTest.unity`) by an editor tool, so it does not get merge conflicts.

## Setup (once)

1. Pull the branch and open the project. Unity installs **Netcode for GameObjects 2.13.3** (added to
   `Packages/manifest.json`). Wait for the import and compile to finish.
2. Menu **Tools › Laser Tag › Part 2 › Build Game Scene**.
   This creates `Assets/LaserTag/LaserTagArena.unity`, puts it first in Build Settings and creates
   `Assets/LaserTag/Prefabs/NetworkPlayer.prefab`. A dialog lists everything it did.
3. Open `LaserTagArena` and press Play (or build to the headsets).

After hand-editing the game scene, **Apply To Open Scene** adds anything that is missing (it never
duplicates). **Regenerate Spawn Points** and **Rebuild Network Player Prefab** redo just those parts.

## Controls

| Action | Quest controllers | Laptop (XR simulator) |
| --- | --- | --- |
| Shoot | Trigger (either gun) | simulator trigger |
| Teleport | Push thumbstick forward, aim the arc, release | simulator thumbstick |
| Snap turn | Thumbstick left/right | simulator thumbstick |
| Start / restart round | **A** on the right controller | **Enter** |

## Starting a game

Press Play (or start the app on the headset). The game connects by itself over the **local Wi-Fi**:
the first headset hosts, the others find it and join within a few seconds (UDP broadcast on port 47777,
game on port 7777). All headsets must be on the same network, and the network must allow devices to talk to
each other. If it does not (some university networks block this), use a phone hotspot or your own router.
If the host quits, the others look for a game again and one of them becomes the new host.

## Testing

- **Alone (testing only)**: *Tools › Laser Tag › Testing › Add Practice Bots* adds computer players to
  the open scene (2 by default). They walk around, shoot back with imperfect aim, can be shot, respawn and
  get eliminated like players. Press Play, wait for the lobby, then press Enter / A to start a round. *Remove Practice Bots* takes them out again. The bots live only in `Assets/LaserTag/Testing`;
  the game itself does not use them. Four bots + you is a good stand-in for a 5-player performance test.
- **Performance on Quest 2**: tick *Local Player › Player Hud › Show Fps* to see frames per second.
- **Two players on one PC**: Multiplayer Play Mode (*Window › Multiplayer › Multiplayer Play Mode*, tick Player 2),
  or a second build. They find each other automatically.
- **Headsets**: build to every Quest (File › Build Profiles › Android › Build And Run), put them on the same
  Wi-Fi and start the app on each.

## What was built (by requirement)

### 1. Environment
- **Colliders**: every visible mesh without a collider gets a `MeshCollider` (LOD0 only; light-beam
  effects skipped). Existing door/stair colliders are kept. Remote avatars have a body capsule + head
  sphere (`NetworkPlayer` prefab).
- **Teleport area** (`Laser Tag/Teleport Area`, `SafeTeleportationArea`): covers the floors. A target is
  rejected when a column of **physics spheres** (radius = *Wall Clearance*, 0.4 m) up to head height
  touches any collider, or another living player is closer than *Player Clearance* (1 m). The arc shows
  the "blocked" look and releasing does nothing.
- **Stairs**: anchors (cyan pads) at the bottom and top of each staircase (`SafeTeleportationAnchor`).
  The stairs themselves are not teleportable. Move the pads in `Laser Tag/Teleport Anchors` if needed.
- **Darker lighting**: all lights at 45 %, ambient at 40 %, reflections at 60 %.
- **Gun instead of controller**: `GunOnlyControllerVisuals` hides any controller model; only the guns
  (with lasers) are visible.

### 2. Health / ammo (HUD = `PlayerHud`, built in code, head-locked, drawn over walls)
- **Health bar** top-left, 100 HP, −10 per hit (`NetworkPlayer › Damage Per Hit`). 0 HP = tagged out.
- **Shots** top-right (`87 / 100` energy cell): 100 shots shared by both guns. 2 s after the last shot
  they refill at 8/s until full (`Local Player › Refill Delay / Refill Per Second`). Orange *LOW* under 25,
  red blinking *EMPTY* at 0, *RECHARGING* while refilling. Empty trigger = dry click + vibration.
- **Feel**: laser sound on every shot (you hear other players' shots from their guns), controller
  vibration on shots and when hit, a hit marker + ping when your laser hits someone, and *TAGGED <name>*
  when your shot takes them out.
- **Respawn**: 3 s countdown on screen, then you reappear at a random free spawn point with 2 s of
  **spawn protection** (blue glow, cannot be hit; landing a hit ends it early).
- **Kill feed** at the top of the view: `Player 1 > Bot 2`, `ELIMINATED` when it was their last life.
- **Hit feedback**: your view flashes red; everyone else sees your avatar light up red.

### 3. Game logic (`LaserTagGameManager`, server-authoritative)
- **Lobby** → press A → everyone is placed at a **random free spawn point** → 3-2-1 → play.
- **Lives**: 2 per player (= one respawn). After the last life you are **eliminated**: you can still
  teleport around and explore, but nobody sees you and you cannot shoot or be shot.
- **Game over** when one player (or nobody) is left. **Scoreboard** ranks players by survival
  (last standing = 1st, then whoever was eliminated latest), ties broken by kills, hits, fewer deaths.
  Your row is highlighted in yellow.
- **Restart**: press A on the scoreboard – new round with everyone currently connected.
- People who join mid-round spectate invisibly and play from the next round.

### 4. Avatars and network (Netcode for GameObjects, host + clients over LAN)
- Every headset spawns a `NetworkPlayer`. The owner sends head/hands/body pose 20×/s; the others drive a
  full-body Scout avatar from it (arms and legs are solved by the existing `ScoutAvatar` and
  `ScoutProceduralLegs`), with the guns in its hands.
- You see your **own** body only when you look down (the existing local avatar; your network copy is hidden).
- Synced: pose, health, lives, kills, hits, deaths, placement, life state, round phase/countdown, shots.

### 5. Shooting
- The existing `LaserRay` draws the beam from your gun to the target and detects the collider hit.
  Other players see a beam from your gun too.
- Hitting another avatar's collider sends a *hit claim* (gun position + hit point) to the host. The host
  decides: the gun must be at the shooter's hand, the target in range, the hit point on the victim's body as
  the host sees it (0.9 m allowance for network delay), and **no wall, prop or other player between gun and
  hit point**. Rejected claims are logged in the host's Console with the reason.
- **Spawning** never uses an occupied point: the spawn manager checks the same sphere clearance and keeps
  2 m from other players and from points handed out in the same moment; if every point is busy it tries
  random spots on the floor.

## Files

| Script | Purpose |
| --- | --- |
| `Network/NetworkPlayer.cs` | Synced player data, pose sync, remote avatar, hit flash, shot/hit RPCs |
| `Network/LanSessionBootstrap.cs`, `LanDiscovery.cs` | Auto host/join on the local network |
| `Game/LaserTagGameManager.cs` | Lobby, countdown, damage, lives, respawn, elimination, ranking, restart |
| `Game/LocalPlayerRig.cs` | Ammo, fire gate, hit reporting, pose capture, teleport on spawn |
| `Game/SpawnManager.cs`, `LaserTagSpawnPoint.cs` | Free spawn point selection |
| `Environment/SafeTeleportationArea.cs`, `SafeTeleportationAnchor.cs`, `TeleportClearance.cs` | Sphere-checked teleport |
| `Environment/TeleportRayVisibility.cs`, `GunOnlyControllerVisuals.cs` | Rig visuals |
| `UI/PlayerHud.cs` | Health, shots, messages, red flash, scoreboard |
| `FX/ShotBeamFx.cs` | Other players' shot beams |
| `Editor/LaserTagSceneBuilder.cs` | The Tools › Laser Tag › Part 2 menu |
| `Editor/LaserTagAndroidManifest.cs` | Adds the Wi-Fi multicast permission LAN discovery needs on Quest |
| `Testing/` (optional) | Practice bots for testing alone: `PracticeBotSpawner`, `LaserTagBot`, add/remove menu |

`Assets/Scripts/LaserRay.cs` got two small hooks: `canFire` (ammo/alive gate) and `onFired`.

## Known limitations
- The host is one of the players (listen server). If the host quits, the round ends; the others reconnect to a
  new host automatically but the match state is not migrated.
- Local Wi-Fi only: the network must allow device-to-device traffic (bring a hotspot/router if it does not).
- Hit claims are checked on the host but the first raycast happens on the shooter's headset, so a modified
  client could still claim hits it had line of sight for.

See `Documentation/` in the repository root for the technical report, setup/join guide and script guide.
