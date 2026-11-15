# Multiplayer VR Laser Tag


A multiplayer virtual reality laser-tag game developed in Unity for **Meta Quest 2 and Quest 3**.

Players can teleport, take cover, shoot opponents, collect health pickups and compete in timed matches with synchronized health, ammunition, lives and scores.

## Requirements

- Unity **6000.6.2f1** with Android Build Support
- Meta Quest 2 or Quest 3
- Internet connection for Unity Relay multiplayer

**Main packages:** XR Interaction Toolkit 3.6.1, OpenXR 1.18.0, Netcode for GameObjects 2.13.3, Unity Multiplayer Services 2.3.3 and Universal Render Pipeline 17.6.0.

See [`Packages/manifest.json`](Packages/manifest.json) for the complete dependency list.

## Setup and Installation

1. Clone the repository and open it in Unity Hub.
2. Use Unity 6000.6.2f1 and allow package installation to complete.
3. Open `Assets/Scenes/MainMenu.unity`.
4. Ensure the existing Unity Services project is linked and Relay/Authentication are available.
5. Switch the build target to Android.
6. Select **Tools → Laser Tag → Submission → Build Quest APK**, or use Unity's Build and Run with a connected Quest headset.
7. Install and launch the APK on the headset.

## How to Play

1. Select **Play** and choose Host or Join.
2. The host creates a lobby and shares the six-character room code.
3. Other players enter the code to join.
4. Players ready up, and the host starts the match.
5. Teleport around the arena, use cover and eliminate opponents.

| VR Control | Action |
|---|---|
| Left thumbstick (menu) | Navigate menu |
| Right trigger (menu) | Select |
| Left thumbstick (gameplay) | Aim and release to teleport |
| Right thumbstick | Smooth turning |
| Right trigger (gameplay) | Shoot |

The game includes ammunition regeneration, limited teleport charges, health pickups, respawning, timed rounds and a synchronized scoreboard.

## Multiplayer Architecture

The game uses **Netcode for GameObjects, Unity Relay and Unity Authentication**.

It follows a **host-authoritative model**: clients submit movement and shooting requests, while the host validates actions, hit detection, damage and game state. Player positions, health, scores and round information are synchronized across connected clients.

Relay provides connectivity between players; it does not independently simulate gameplay.

## Testing and Limitations

Editor compilation, simulated XR menu navigation and local multiplayer tests have been performed.

Physical Quest gameplay, mixed Quest 2/3 compatibility and five simultaneous headset connections still require final hardware verification.

Host migration and joining during an active match are not supported.

See [`SUBMISSION.md`](SUBMISSION.md) for technical details and [`docs/VR_MENU_NAVIGATION.md`](docs/VR_MENU_NAVIGATION.md) for simulated XR testing.

## Credits and Acknowledgments

Developed as a team project for **CMPS297AG — Introduction to XR** at the American University of Beirut.

The project uses Unity XR tools and third-party 3D assets. ChatGPT/Codex and assisted with implementation, debugging, testing and documentation.

Individual contributions, source acknowledgments and asset licensing information should be finalized in the submission documentation.
