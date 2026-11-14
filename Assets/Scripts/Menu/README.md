# Main menu and Relay lobby

Entry: `Assets/Scenes/MainMenu.unity`; game: `Assets/Scenes/RelayVRGame.unity`.
Use Unity 6000.6.2f1 and the checked-in manifest/lock file. Unity Services must be
linked to the team's existing project with Authentication and Relay available.

PLAY offers Host (2?8 total slots; five selected by default) and Join by room code.
All players ready up; only the host starts synchronized loading. New joins are
rejected once loading begins. A round needs at least two players. Leaving shuts
down the local connection; host departure closes the room. Rehost/rejoin does not
require restarting the app. Host migration and mid-match join are not supported.

MenuSessionController persists one NetworkManager, disables automatic player
creation, handles connection approval, synchronized loading, host spawning and
shutdown recovery. LobbyNetworkState replicates the room, capacity, member ready
states and session phase. RelayRoomUI's existing async Relay implementation is
reused; its debug UI/Editor automatic hosting are disabled on the menu manager.
The game scene contains no second manager. Registered lobby and avatar prefabs
use MenuNetworkPrefabs. RelayVR and the original avatar test scene are preserved.

MainMenuController shares actions between mouse uGUI and MenuVrPresentation's
headset world-space UI/rays and room-code keypad. Left menu opens in-game options;
trigger/ray and thumbstick scroll operate UI. Local firing and teleporting are
blocked while options/help/settings are visible; the multiplayer round continues.
VR-only How to Play includes complete default rules. Laptop controls are documented
separately in [LAPTOP_TESTING.md](../../../LAPTOP_TESTING.md).

Settings persist through PlayerPrefs: master, SFX, music/ambience, mute, quality,
and supported desktop display/simulator options. Unsupported snap-turn controls
are omitted. MenuAudio owns one fading looping source, reusing existing licensed
sample ambience in-game; audio categories preserve source gain. Loading shows real
local progress where available and honest synchronization status otherwise.

See [SUBMISSION.md](../../../SUBMISSION.md) for exact versions, authority model,
actual real-Relay local-process test evidence, contribution/license statement and
remaining physical Quest 2/3 and five-headset acceptance checks. VR rays, physical
controls, readable headset UI and measured Quest responsiveness require hardware
validation; desktop tests cannot establish those outcomes.
