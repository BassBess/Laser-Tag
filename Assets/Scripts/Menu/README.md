# Menu and Relay lobby

Open `Assets/Scenes/MainMenu.unity` and enter Play mode. The menu does not start a connection automatically.

1. Select PLAY. Choose a maximum of 2–8 total players, then HOST GAME.
2. Share the room code. On another instance choose PLAY, enter the code and select JOIN GAME. Scroll the panel if needed.
3. Each participant, including the host, selects TOGGLE READY. Only the host can START GAME, after everyone is ready. A solo host may also start for testing.
4. Netcode loads `Assets/Scenes/RelayVRGame.unity` on all participants. Players spawn after the connected group completes loading. Escape opens the in-game session menu; RETURN TO MENU disconnects.

SETTINGS includes master/SFX/menu music volume, mute, quality and simulator look sensitivity. Desktop builds also expose fullscreen/windowed mode and available resolutions. APPLY persists preferences; RESET TO DEFAULTS applies and saves defaults. Display controls are omitted in the Editor and while an XR display is active. Simulator sensitivity uses XRI's existing pitch/yaw sensitivity properties; it also affects keyboard look and never overrides headset tracking. Comfort angle is exposed only if the integration scene has an active supported snap-turn provider. The current networking integration has turn providers only on the disabled Hands rig, so that control is omitted.

The project must be linked to Unity Services with Relay and anonymous Authentication available. Hosting and joining use `RelayRoomUI`'s shared async API. Its existing debug UI and Editor auto-host defaults remain available in the original playground scenes; the menu disables them on its own manager only.

`MenuSessionController` persists one NetworkManager, approves connections without automatic player creation, enforces capacity and phase restrictions, and owns shutdown/recovery. `LobbyNetworkState` is a separate server-owned session object with sender-validated ready RPCs. The existing `RelayAvatarPlayer` prefab and `RelayVRAvatarBridge` continue publishing XR poses without changes. The copied game scene contains the original rig/avatar/guns/bridge and no NetworkManager. The original RelayVR and avatar-test scenes remain unchanged.

Loading progress is the actual local Unity AsyncOperation progress when available. Synchronization uses status text, without an invented percentage. Loading timeouts/rejections return to a disconnected menu. New connections are rejected once loading starts. Late joining and host migration are not supported.

The scene-generation menu is **Tools > Laser Tag > Menu > Create Menu and Integration Scenes**. It refuses to overwrite existing generated assets. MainMenu and RelayVRGame are first in the build list; existing regression scenes remain listed. Runtime uGUI is mouse/keyboard driven, with separate session/settings APIs available for a future VR ray UI.

The integrated game now adapts the team's combat, HUD, rounds and collision contributions to this same Relay foundation; see `Assets/LaserTag/README.md` for ownership, rules and validation limits. Native desktop testing may expose the base project's AR Simulation camera-background error when no headset is present; that XR/package configuration is preserved. Two-computer and physical-headset acceptance testing remains required.
