# Final submission file inventory

Foundation: `origin/feature/final-game` at `21254b7`. Branch: `final/submission-fixes-assil`.
All listed changes were made in the separate final worktree during this audit. Existing shared files are modified only by the listed final-fix diff; this does not claim authorship of their original implementation. No teammates' branch is rewritten.

| Status | Exact project-relative path | Purpose |
|---|---|---|
| Added | `Assets/Editor/SubmissionBuild.cs` | Canonical Quest build/validation and manual review-view commands; no automatic UI opening. |
| Added | `Assets/Editor/SubmissionBuild.cs.meta` | Required Unity asset/folder GUID metadata. |
| Added | `Assets/LaserTag/Presentation.meta` | Required Unity asset/folder GUID metadata. |
| Added | `Assets/LaserTag/Presentation/LaserTagBloom.asset` | Restrained game-scene bloom profile. |
| Added | `Assets/LaserTag/Presentation/LaserTagBloom.asset.meta` | Required Unity asset/folder GUID metadata. |
| Added | `Assets/LaserTag/Presentation/Materials.meta` | Required Unity asset/folder GUID metadata. |
| Added | `Assets/LaserTag/Presentation/Materials/hologram_particle_d37e8378_URP.mat` | Scoped URP replacement for the existing legacy particle material. |
| Added | `Assets/LaserTag/Presentation/Materials/hologram_particle_d37e8378_URP.mat.meta` | Required Unity asset/folder GUID metadata. |
| Modified | `Assets/LaserTag/README.md` | Current integration overview; replaces contradictory historical instructions. |
| Modified | `Assets/LaserTag/Scripts/Environment/LeftHandArcTeleport.cs` | Block teleport input while local options are open; disable diagnostic spam. |
| Modified | `Assets/LaserTag/Scripts/Environment/TeleportClearance.cs` | Use current authoritative occupancy and validate spawn floor support. |
| Modified | `Assets/LaserTag/Scripts/FX/LaserTagFeedback.cs` | Route new SFX through the cached category-volume controller. |
| Modified | `Assets/LaserTag/Scripts/FX/ShotBeamFx.cs` | Own and clear persistent pooled shot effects on session exit. |
| Modified | `Assets/LaserTag/Scripts/Game/LaserTagGameManager.cs` | Wait for safe spawn space; preserve multiplayer round participant count; UI input gate. |
| Modified | `Assets/LaserTag/Scripts/Game/LocalPlayerRig.cs` | Keep right-only firing and block shots while options own trigger input. |
| Modified | `Assets/LaserTag/Scripts/Game/SpawnManager.cs` | Reject occupied/unsupported/blocked placements instead of unsafe fallback spawning. |
| Modified | `Assets/LaserTag/Scripts/Network/RelayCombatPlayer.cs` | Server-assigned unique LED color; reuse lobby names; attach/remove presentation. |
| Modified | `Assets/Networking/RelayAvatarPlayer.prefab` | Explicit authored tracking-envelope defaults; existing references preserved. |
| Modified | `Assets/PlayerAvatar/ScoutLaptopHeightBounds.cs` | Preserve floor-relative Editor Q/E bounds; block FPS/HMD WASD only. |
| Modified | `Assets/Scenes/MainMenu.unity` | Assign existing licensed gameplay ambience. |
| Modified | `Assets/Scenes/RelayVRGame.unity` | Outdoor floor/cover/spawns, navy lighting/bloom, scoped broken-reference repairs. |
| Modified | `Assets/Scripts/LaserRay.cs` | Prevent inactive gun components from firing through the public API. |
| Modified | `Assets/Scripts/Menu/MainMenuController.cs` | VR-only rules, in-game help/settings, code keypad, UI firing gate and readable sliders. |
| Modified | `Assets/Scripts/Menu/MenuAudio.cs` | One fading menu/game ambience source, SFX gain cache and cleanup. |
| Modified | `Assets/Scripts/Menu/MenuSessionController.cs` | Clear shot-effect pool after network shutdown. |
| Added | `Assets/Scripts/Menu/MenuVrPresentation.cs` | Headset world UI/rays, tracked menu camera, late placement and action/material cleanup. |
| Added | `Assets/Scripts/Menu/MenuVrPresentation.cs.meta` | Required Unity asset/folder GUID metadata. |
| Modified | `Assets/Scripts/Menu/README.md` | Accurate setup, menu behavior and validation limits. |
| Modified | `Assets/Scripts/Menu/SettingsController.cs` | Hide simulator-only controls when unavailable or in a headset. |
| Modified | `Assets/Scripts/Networking/RelayAvatarPlayer.cs` | Protocol 4 for color-state layout; expose tracking readiness for safe spawning. |
| Added | `Assets/Scripts/Networking/RelayPlayerPresentation.cs` | Per-player LED overrides and small depth-tested remote name tags; no duplicate avatar. |
| Added | `Assets/Scripts/Networking/RelayPlayerPresentation.cs.meta` | Required Unity asset/folder GUID metadata. |
| Modified | `Assets/XR/Settings/OpenXRPackageSettings.asset` | Disable only three Android XR-specific features for the Quest target. |
| Added | `LAPTOP_TESTING.md` | Separate simulator testing controls; excluded from in-game How to Play. |
| Modified | `ProjectSettings/ProjectSettings.asset` | Minimum Android API 32; preserve target 34, ARM64 and IL2CPP. |
| Added | `SUBMISSION.md` | Technical report, requirements evidence, setup, contributions and limitations. |
| Added | `SUBMISSION_FILES.md` | Exact public submission change inventory. |

No tracked file was deleted or moved. Temporary fixtures, reports, screenshots, APKs, builds, private notes, backups and generated machine/settings noise are excluded. The original checkout?s pre-existing changes are outside this list and preserved. Unity/package versions are unchanged.
