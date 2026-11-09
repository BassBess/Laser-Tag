# Script guide (for explaining and modifying the code)

Every member must be able to explain their part, including AI-assisted code. Suggested split for 5 members
(adjust to who actually worked on what), with the questions an examiner is likely to ask and a small
"live modification" each person should be able to do.

## Member 1 – Networking & sessions
`Scripts/Network/LanSessionBootstrap.cs`, `LanDiscovery.cs`

- **What**: starts Netcode automatically. Each headset listens ~2.5–4 s for a UDP broadcast "I'm hosting"
  (port 47777); if it hears one it joins that host (game port 7777), otherwise it becomes the host and starts
  broadcasting. Two accidental hosts: the one without guests joins the other. Same-PC tests use a heartbeat file.
- **Disconnect**: `OnClientStopped` → wait 1.5 s → search again (another headset may become the host).
- **Try**: change the search time, the game port, or switch *Mode* to *Manual* and type an IP.

## Member 2 – Player sync & avatars
`Scripts/Network/NetworkPlayer.cs`, `Scripts/Game/LocalPlayerRig.cs` (pose part)

- **What**: one `NetworkPlayer` per headset. Owner writes `Pose` (root, head, hands) 20×/s;
  others apply it to the Scout avatar (`ApplyRemotePose`), which solves arms/legs itself.
- **Visibility**: own network copy hidden (you see your local body when looking down); dead/eliminated players hidden;
  colliders follow visibility.
- **Owner vs server data**: `Pose`/`Ammo` owner-write; health, lives, scores server-write.
- **Try**: change `poseSendRate`, or the hit flash colour.

## Member 3 – Game rules & authority
`Scripts/Game/LaserTagGameManager.cs`

- **What**: server-authoritative state machine Lobby → Countdown → Playing → GameOver.
- **Hits**: `ServerValidateShot` (hand, range, on-body, **cover raycast on the host**) then `ServerHandleHit`
  (damage, spawn protection, tag-out → respawn or eliminate) — explain why the host decides.
- **Simultaneous firing**: claims processed in order; second claim rejected because the shooter is dead.
- **Try**: change lives, damage (`NetworkPlayer.damagePerHit`), respawn delay, or ranking order (`CompareRank`).

## Member 4 – Movement, spawning & arena
`Scripts/Environment/*`, `Scripts/Game/SpawnManager.cs`, `LaserTagSpawnPoint.cs`, `Editor/LaserTagSceneBuilder.cs`

- **What**: teleport validity with physics spheres (`TeleportClearance.IsBodySpaceFree` + `IsFarFromPlayers`),
  stairs anchors, spawn selection that skips occupied points, the scene builder (colliders, floors, lighting).
- **Try**: change wall clearance (0.4 m) or player clearance (1 m); move or add a spawn point.

## Member 5 – Weapons, HUD & feedback
`Assets/Scripts/LaserRay.cs` (Task 1, with `canFire`/`onFired` hooks), `Scripts/Game/LocalPlayerRig.cs` (ammo part),
`Scripts/UI/PlayerHud.cs`, `Scripts/FX/*`

- **What**: energy-cell ammo (100, refill after 2 s at 8/s), fire gate, hit claims, head-locked HUD (health,
  shots, kill feed, scoreboard), sounds generated in code, controller vibration, other players' beams.
- **Try**: change max ammo / refill speed, the LOW threshold, or HUD colours.

## Optional testing tools
`Testing/PracticeBotSpawner.cs`, `Testing/LaserTagBot.cs` — computer players for solo tests. Not part of the game.
