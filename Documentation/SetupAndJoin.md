# Setup, build and join instructions

## Exact versions

| Component | Version |
| --- | --- |
| Unity Editor | **6000.6.2f1** (with Android Build Support: OpenJDK, Android SDK & NDK) |
| Universal Render Pipeline | 17.6.0 |
| XR Interaction Toolkit | 3.6.1 (Starter Assets sample 3.5.1 imported) |
| OpenXR / Meta OpenXR / Android XR OpenXR | 1.18.0 / 2.6.1 / 1.4.0 |
| Input System | 1.20.0 |
| Netcode for GameObjects | 2.13.3 |
| Unity Transport | resolved by Netcode (2.x) |
| Target devices | Meta Quest 2 and Quest 3 |

The authoritative list is `Packages/manifest.json` and `Packages/packages-lock.json`.

## 1. Open the project
1. Clone the repository and open the folder in Unity **6000.6.2f1**. Wait for packages to import.
2. Open `Assets/LaserTag/LaserTagArena.unity`. (If it is missing, run **Tools › Laser Tag › Part 2 › Build Game Scene**.)

## 2. Build for Quest 2 / Quest 3
1. Headset in developer mode, connected by USB (accept "Allow USB debugging").
2. **File › Build Profiles › Android** → *Switch Platform* (first time).
3. Make sure `LaserTagArena` is the first scene in the list.
4. **Build And Run**. The same APK runs on Quest 2 and Quest 3.
5. Repeat *Run* (or install the APK with `adb install`) on every headset.

## 3. Join a game (in the headset)
1. Put every headset on the **same Wi-Fi** (or the same phone hotspot / router).
2. Start the app on one headset first (it becomes the host), then on the others; they join automatically
   within a few seconds.
3. The lobby shows how many players are connected.
4. When everybody is in (the lobby shows "5 players connected"), any player presses **A** to start.
5. After the round, the scoreboard appears; press **A** to play again.

## Controls

| Action | Quest | Laptop (XR simulator) |
| --- | --- | --- |
| Shoot | Trigger | Left mouse button |
| Teleport | Thumbstick forward, aim, release | Simulator thumbstick |
| Snap turn | Thumbstick left / right | Simulator thumbstick |
| Start / restart round | A (right controller) | Enter |

## Troubleshooting
- Headsets never see each other: the network blocks device-to-device traffic → use a phone hotspot or your
  own router. For a fixed setup, set *Network Manager › Lan Session Bootstrap › Mode* to *Manual* and enter
  the host's IP address.
- Safe Mode on opening: an old copy of a script is still in the project; check the Console for duplicate classes.
