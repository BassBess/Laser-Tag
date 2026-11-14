# Arena boundary and stair teleportation

Implemented on `feature/arena-boundary-stair-teleport`, created from clean `main`. All work is uncommitted. No merge, reset, push or stash operations were performed.

## Map and implementation

The inspected outpost occupies approximately X −15.62…14.8, Z −15.62…7; the outdoor floor/spawn grid occupies X 13.98…29.98, Z −8…8. The 577 m ground plane and distant scattered props were excluded when choosing the combat footprint. All 24 spawn positions are inside the new perimeter.

**Arena Forest Perimeter** encloses these areas with two staggered rows of conifers, varied heights/widths/positions, and an overlapping irregular rock ridge. Default geometry: 130 trees, seven static mesh sectors, 13,083 vertices, 4,361 triangles, one shared vertex-lit URP material that respects the existing fog. Seven static mesh colliders follow the scenery; seven overlapping hidden BoxColliders close edges and corners. There are no per-tree scripts, rigidbodies, lights or particles.

The convex polygon's counter-clockwise local X/Z vertices are:

`(-20,-21), (30,-21), (35,-12), (35,8), (29,13), (-13,13), (-20,6)`.

The staircase is **arena/stairs_big_with_emmision**, near `(-4,0,7)`, ascending toward negative Z. Its MeshCollider was a **convex trigger**, so teleport rays ignored it and its individual treads were not represented physically. It now uses the original solid, nonconvex mesh on the original layer.

Its approximately 0.307 m rises exceeded ordinary footprint tolerances, and the next tread intersected the standing capsule. A specific `ApprovedWalkableSurface` profile now allows up to 0.34 m of neighboring tread height/contact on this stair mesh. Four footprint probes remain required. Only this collider receives the lower-leg allowance; another capsule checks its upper body/railings, and all other obstacles retain the full-height check. Global slope, headroom, occupancy, range and vertical limits remain unchanged.

The first tread is approximately 0.37 m above terrain. A visible **Stair Entry Plate**, center `(-4,0.135,7.3)`, size `(3.6,0.1,0.8)`, supports its front footprint without increasing global tolerances. It reuses the existing landing material. The original **floor_6** upper landing and adjacent **floor_6 (1)** deck, both at Y≈4.151, are reused; no replacement upper platform remains.

The actual ground MeshCollider and existing landing receive explicit walkable approval. Existing floor/ramp/platform/stair discovery, fallback floors and manually assigned `additionalWalkableSurfaces` remain intact. Arbitrary walls, props, roofs and colliders are not automatically approved.

## Server authority and preservation

The shared `TeleportClearance` path checks the boundary at the arc source, every arc segment endpoint, final body footprint, server-request destination, accepted tracked feet and spawn footprint. With a convex polygon, accepted segment endpoints cannot conceal a crossing outside. The server still retraces the authoritative controller arc and validates the claimed endpoint; aiming over trees or submitting an outside destination cannot bypass the boundary.

SpawnManager inherits the boundary through its existing `IsSpawnSupported` checks. Health pickups use the same bounded landing checks and additionally exclude stair treads and the entry plate. Forest colliders are always ineligible, including if renamed or placed in an explicit surface list. Pickups on the existing landing still require normal support, clearance and spacing.

The boundary never clamps the camera, snaps an XR origin or forces movement. Existing room-scale rejection behavior is retained; physical headset movement is not stopped by virtual colliders. No new RPC, NetworkObject, controller binding or joystick locomotion was added.

No original scene IDs were removed. Only five existing scene blocks changed: scene roots, arena children, ground components, upper-landing prefab components and staircase prefab overrides. Existing match manager, sky/pickup settings and both gun components are preserved byte-for-byte. Lighting, spawn transforms, existing surface references and networking objects are retained.

## Configuration

1. Select **Arena Forest Perimeter**. Green gizmos show its polygon and height envelope. Defaults: safety inset 0.15 m, accepted height −1…14 m, tree spacing 3.1 m, tree heights 7.5…11.5 m, barrier height 10 m, seed 297. Keep unit scale, an upright transform and convex counter-clockwise vertices.
2. After changing the polygon or scenery settings, run **Tools → Laser Tag → Arena → Rebuild Selected Perimeter Scenery**, then save. It rebuilds only `Generated Forest and Barriers` and its mesh assets. Keep manually authored objects outside that generated child. Leave room for paths, stairs and spawn/body footprints.
3. For another reviewed generic ground/deck, add `ApprovedWalkableSurface` and assign its particular static solid collider(s) to `surfaces`. Leave `stairTreads` off for ordinary floors. For measured stair geometry only, enable it and set `maximumStepRise` (hard capped at 0.35 m). Never apply the stair allowance to a combined building/wall collider. Use `allowHealthPickups` to exclude unsuitable surfaces.
4. Existing explicit surface lists remain usable. Fix missing collision before approving a surface; do not opt roofs, trees, thin ledges or decorative objects in merely because they have colliders. All peers should run the updated build; no network schema change was required.

## Validation performed

Headless Unity 6000.6.2f1 checks ran in an isolated `/tmp/laser-arena-validation` copy. The final suite passed with **zero failures**:

- 588 barrier rays across seven edges, endpoints and multiple heights; sampled outside destinations rejected by shared landing validation; all forest colliders ineligible.
- 117 tread samples: 13 steps × three width positions × three forward/back offsets; existing upper landing validated.
- 14 real source/arc/landing legs, ascending and descending between ground, stairs, landing and adjacent deck.
- Low cover and headroom obstacles on a stair landing correctly rejected as `BodyBlocked`.
- All 24 spawns inside; all seven initial spawns retained full standing clearance.
- Eight ordinary floor/ground/platform support-and-body checks; 276 spawn-to-spawn sightlines unobstructed by new perimeter collision.

Unity Editor compilation and forest shader import passed. Unity's **Android player-script compilation produced 78 assemblies**, including gameplay and Netcode IL post-processing. Separate current-project C# checks also passed for Editor gameplay, Editor tools and Android/IL2CPP-symbol paths. Scene/material/mesh/shader GUIDs resolve; `git diff --check` passes.

Reports: `/tmp/laser-arena-validation-results.txt`, `/tmp/laser-arena-verified-final.log`, and `/tmp/LaserTag-arena-{editor,android,tools}.log`. Running the Editor test menu writes `Logs/ArenaBoundaryValidation.txt`.

**Not verified:** interactive Relay play, headset visuals/comfort, Android shader variants, a complete APK/IL2CPP build, or measured Quest performance. Managed Android compilation is not an APK build.

## Unity and Quest test steps

1. Let assets/scripts import and reopen `Assets/Scenes/RelayVRGame.unity` to load the saved additions. Inspect the forest from ground and deck: density, night visibility, material appearance and entry-plate alignment.
2. Run **Tools → Laser Tag → Arena → Validate Boundary and Stair Landings** and inspect its report. Enable `LeftHandArcTeleport.teleportDiagnostics` for throttled preview/server rejection reasons, including `OutsideArena` and collider/probe details. **Export Geometry Inventory** and **Compile Android Player Scripts** are additional diagnostic menu actions.
3. Host/join through MainMenu with five players. Try every perimeter edge/corner and aim over the trees. Preview and server must reject outside destinations. Crouch/lean near the boundary and confirm there is no camera snapping.
4. Teleport progressively up/down lower, middle and upper treads at different widths, then onto the existing landing and adjoining deck. Railings, side edges, occupied destinations, low cover and insufficient headroom must remain invalid. Compare host/client logs under latency.
5. Recheck both hands, left teleport input, right-only shooting, cover blocking, ammo, respawn, pickups, timer, scoreboard, restart and late joining. Pickups must stay inside and off trees/treads/entry plate; full health must not consume them.
6. Build identical Android APKs using existing Quest settings. Check forest shader variants and IL2CPP output. Repeat on Quest 2 and Quest 3 with five peers; inspect both eyes and profile Quest 2 CPU/GPU, draw calls, physics queries and GC. Tune tree spacing/height or polygon placement based on actual device results, preserving collision validation.

## Files

Modified: `Assets/LaserTag/Scripts/Environment/TeleportClearance.cs`, `Assets/LaserTag/Scripts/Game/HealthPickupSystem.cs`, `Assets/Scenes/RelayVRGame.unity`.

New runtime components: `ArenaBoundary.cs`, `ApprovedWalkableSurface.cs` under `Assets/LaserTag/Scripts/Environment/`.

New Editor tools: `ArenaGeometryAudit.cs`, `ArenaPerimeterBuilder.cs`, `ArenaTeleportValidation.cs` under `Assets/LaserTag/Editor/`.

Scenery assets: `Assets/LaserTag/Presentation/ArenaPerimeter/` contains seven `ForestSector*.asset` meshes, `Forest.mat`, `ForestVertexLit.shader` and metadata. New scripts/folder also have stable `.meta` files.
