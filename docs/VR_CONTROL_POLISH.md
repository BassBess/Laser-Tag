# Retained VR improvements

The arena appearance, distant boundary and outdoor pickup eligibility use 9ca5d46
as the reference. Forest scenery, rock perimeter, hidden barriers and polygon
boundary restrictions from 920a371 have been removed. Existing floor support,
collision, headroom, occupancy and server validation remain active.

Retained improvements:
- Clearer local right-hand aiming dot/ring and its size setting.
- Smooth right-stick turning with Turn Sensitivity in Settings.
- Exact solid stair mesh and stair-specific support/lower-leg clearance handling.
- Server-authoritative teleport burst with HUD charge count/recharge countdown.

The player prefab and script defaults specify 15 teleports, a 2.5-second
partial-burst idle reset and a 2.5-second exhausted-burst cooldown. Only successful
teleports consume charges and move the deadline. Rejected requests do not extend
it. Spawn/respawn placement resets the burst. The existing request spacing remains.

## Validation

`VRControlPolishValidation.ValidateInBatch` checks turning math, burst deadlines,
script/prefab values and stair/clearance regressions in the gameplay scene.
`ArenaTeleportValidation.CompileAndroid` compiles player scripts without an APK.

For this rollback, editor and gameplay C# projects compiled with zero errors.
Static checks confirmed every baseline scene object remains, only aiming/stair
and serialized match fields differ, and pickup code matches the reference.
Unity batch physics/Android checks remain pending because licensing initialization
failed in this environment.

Headset checks: aim visibility at different ranges; turning sensitivity and pivot
stability; stair ascent/descent and blocked headroom; prior outdoor extent and
pickup spawning; 15 rapid teleports, sixteenth rejection and refill exactly 2.5
seconds after the last success, including a partially used burst. Verify with host
and remote clients, including occupancy conflicts and rejected destinations.
