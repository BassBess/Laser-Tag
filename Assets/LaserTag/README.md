# Integrated Relay Laser Tag

Open `Assets/Scenes/MainMenu.unity`; it loads `Assets/Scenes/RelayVRGame.unity`.
The source foundation is `feature/final-game` at 21254b7, containing the menu,
Relay/XR networking and adapted team combat/environment contributions. The final
fixes retain this architecture. Network protocol **4** includes synchronized
player color slots; every peer must use the same build.

The current technical report, instructor-requirement checklist, contribution
statement, test evidence and remaining hardware checks are in
[SUBMISSION.md](../../SUBMISSION.md). Laptop simulator controls are deliberately
separate in [LAPTOP_TESTING.md](../../LAPTOP_TESTING.md). VR controls, ammo, health,
lives, cover, winning, restart and leaving are explained inside How to Play.

- MenuSessionController owns one persistent Relay/Netcode connection. No LAN
  bootstrap or second gameplay NetworkManager is used. Connection approval
  disables automatic players; synchronized loading completes before host spawning.
- RelayAvatarPlayer owns server-authorized virtual origin and pose snapshots.
  RelayVRAvatarBridge reads the original locally tracked Scout representation.
  Owner mirrors are hidden; remote head/hand targets are independent.
- RelayCombatPlayer adapts the team's health, ammo, hit validation and lifecycle
  onto that existing network player. LaserTagGameManager owns round state,
  respawn, elimination, ranking and restart; PlayerHud presents the result.
- LeftHandArcTeleport uses the left stick only. The right gun retains its original
  controller, muzzle and trigger action. Continuous avatar travel is disabled;
  headset/controller tracking remains physical. The Editor bounds synthetic head
  height and blocks WASD head translation without disabling controller adjustment.
- TeleportClearance validates source, arc, authored/explicit floor eligibility,
  support footprint, slope, range/elevation, body space and current occupancy.
  SpawnManager fails closed when no supported unoccupied placement is available.
- Player colors affect existing LED strips, keeping white armor and gripping hands.
  Living remote players have small matching depth-tested name tags; no self tag.
- The original desert remains; an added outdoor deck reuses 16 existing floor
  prefabs and six staggered cover walls, with eight extra spawns. No whole-scene
  environment duplication was used. Added floor references support teleporting.

The arena uses navy ambient/fog lighting, restrained cyan/violet lights and subtle
bloom; this does not prove Quest performance. Existing licensed Unity sample
ambience shares the persistent music source; effects retain their own gain.
Audio preferences and mute apply locally. Native rendering, local Relay process
checks and Editor fixtures are distinct from physical headset acceptance tests.

Original RelayVR and AvatarFirstPersonTest scenes remain available. Existing
editor generators are retained for reproducibility; do not regenerate a reviewed
scene casually. `Tools > Laser Tag > Submission > Build Quest APK` builds only
MainMenu and RelayVRGame. Device installation and mixed five-headset testing
remain required even after a successful APK build.
