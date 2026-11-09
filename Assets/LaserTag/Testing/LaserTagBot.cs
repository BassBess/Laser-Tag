using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TESTING ONLY. Host-side brain for a practice bot, added by <see cref="PracticeBotSpawner"/> to a bot's
/// <see cref="NetworkPlayer"/>. The bot walks between spawn points inside the arena, pauses now and then,
/// and keeps its guns pointed where it is heading. When it can see a human player (and the
/// spawner's "Shoot Back" is on) it stops, turns to face them and fires with imperfect aim.
///
/// Everything else is the normal player code: it has health and lives, flashes red when hit, respawns,
/// gets eliminated and appears on the scoreboard, and every connected player sees it.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public class LaserTagBot : MonoBehaviour
{
    [Min(0.1f)] public float walkSpeed = 1.1f;
    [Min(10f)] public float turnSpeed = 240f;
    public Vector2 pauseSeconds = new Vector2(0.5f, 2.5f);
    [Range(0f, 1f)] public float pauseChance = 0.5f;
    [Min(10f)] public float poseRate = 20f;
    [Tooltip("How far a bot can see and shoot, in metres.")]
    [Min(1f)] public float sightRange = 18f;
    [Tooltip("Seconds a bot needs after spotting you before its first shot (reaction time).")]
    [Min(0f)] public float reactionTime = 0.6f;

    // Same grip offsets the local avatar uses (ScoutLocalRigBinding defaults).
    static readonly Vector3 LeftGunLocal = new Vector3(-0.22f, 1.28f, 0.42f);
    static readonly Vector3 RightGunLocal = new Vector3(0.22f, 1.28f, 0.42f);

    NetworkPlayer player;
    Vector3 feet;
    float yaw;
    Vector3? target;
    float waitUntil;
    float nextPose;
    bool placed;
    float lastProgressTime, lastDistance;
    NetworkPlayer enemy;
    float enemySince, nextShot, nextLook;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        player = GetComponent<NetworkPlayer>();
        player.serverTeleportHandler = TeleportTo;
    }

    /// <summary>Called by the server when the bot is (re)spawned.</summary>
    public void TeleportTo(Vector3 position, float facing)
    {
        feet = position;
        yaw = facing;
        target = null;
        waitUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
        placed = true;
        WritePose();
    }

    void Update()
    {
        if (player == null || !player.IsServer || !player.IsSpawned || !placed) return;

        LaserTagGameManager gm = LaserTagGameManager.Instance;
        PracticeBotSpawner settings = PracticeBotSpawner.Instance;
        bool combat = player.IsAlive && gm != null && settings != null && settings.shootBack &&
                      gm.Phase.Value == GamePhase.Playing;
        if (combat && Time.time >= nextLook)
        {
            nextLook = Time.time + 0.25f;
            NetworkPlayer seen = FindEnemy();
            if (seen != enemy) { enemy = seen; enemySince = Time.time; }
        }
        if (!combat) enemy = null;

        if (enemy != null) Fight(gm, settings);
        else if (player.IsAlive && Time.time >= waitUntil) Walk();

        if (Time.time >= nextPose)
        {
            nextPose = Time.time + 1f / poseRate;
            WritePose();
        }
    }

    void Walk()
    {
        if (target == null && !PickTarget()) return;

        Vector3 to = target.Value - feet;
        to.y = 0f;
        float distance = to.magnitude;
        if (distance < 0.3f)
        {
            target = null;
            if (Random.value < pauseChance) waitUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
            return;
        }

        // Stuck (something walked into the way)? Pick somewhere else.
        if (distance < lastDistance - 0.05f) { lastDistance = distance; lastProgressTime = Time.time; }
        else if (Time.time - lastProgressTime > 2f) { target = null; return; }

        Vector3 direction = to / distance;
        float wantedYaw = Quaternion.LookRotation(direction).eulerAngles.y;
        yaw = Mathf.MoveTowardsAngle(yaw, wantedYaw, turnSpeed * Time.deltaTime);
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, wantedYaw)) > 35f) return; // turn first, then walk

        float step = Mathf.Min(distance, walkSpeed * Time.deltaTime);
        if (Blocked(direction, step + 0.35f)) { target = null; return; }

        Vector3 next = feet + direction * step;
        if (!TryGround(next, out float groundY)) { target = null; return; }
        next.y = groundY;
        feet = next;
    }

    // ---------------- combat ----------------

    Vector3 Muzzle => feet + Quaternion.Euler(0f, yaw, 0f) * (RightGunLocal + new Vector3(0f, 0.18f, 0.22f));

    static Vector3 Chest(NetworkPlayer p) => p.FeetPosition + Vector3.up * 1.25f;

    NetworkPlayer FindEnemy()
    {
        NetworkPlayer best = null;
        float bestDistance = sightRange;
        foreach (NetworkPlayer p in NetworkPlayer.All)
        {
            if (p == null || p == player || p.IsBot.Value || !p.IsPresentInWorld || !p.InRound.Value || !p.HasPose) continue;
            float d = Vector3.Distance(feet, p.FeetPosition);
            if (d < bestDistance && HasLineOfSight(p)) { best = p; bestDistance = d; }
        }
        return best;
    }

    bool HasLineOfSight(NetworkPlayer target)
    {
        Vector3 from = feet + Vector3.up * 1.5f;
        Vector3 to = Chest(target);
        Vector3 dir = to - from;
        int count = Physics.RaycastNonAlloc(from, dir.normalized, hits, dir.magnitude, Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
            if (t == null || t.IsChildOf(transform) || t.IsChildOf(target.transform)) continue;
            return false; // a wall or prop is in the way
        }
        return true;
    }

    void Fight(LaserTagGameManager gm, PracticeBotSpawner settings)
    {
        Vector3 to = enemy.FeetPosition - feet;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            yaw = Mathf.MoveTowardsAngle(yaw, Quaternion.LookRotation(to).eulerAngles.y, turnSpeed * Time.deltaTime);

        bool facing = to.sqrMagnitude > 0.01f &&
                      Mathf.Abs(Mathf.DeltaAngle(yaw, Quaternion.LookRotation(to).eulerAngles.y)) < 12f;
        if (!facing || Time.time < enemySince + reactionTime || Time.time < nextShot) return;
        nextShot = Time.time + Random.Range(settings.fireInterval.x, settings.fireInterval.y);

        // Hit chance falls off with distance; misses land beside the target.
        Vector3 from = Muzzle;
        Vector3 chest = Chest(enemy);
        float distance = Vector3.Distance(from, chest);
        float chance = settings.accuracy * Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(4f, sightRange, distance));
        bool hit = Random.value < chance && !enemy.IsProtected;
        Vector3 end = chest;
        if (!hit)
        {
            Vector3 side = Vector3.Cross(Vector3.up, (chest - from).normalized);
            Vector3 miss = chest + side * Random.Range(0.45f, 0.9f) * (Random.value < 0.5f ? -1f : 1f) +
                           Vector3.up * Random.Range(-0.6f, 0.6f);
            Vector3 dir = (miss - from).normalized;
            end = Physics.Raycast(from, dir, out RaycastHit wall, sightRange * 1.5f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore) && !wall.collider.transform.IsChildOf(transform)
                ? wall.point
                : from + dir * sightRange;
        }

        player.ServerShowShot(from, end);
        if (hit) gm.ServerHandleHit(player, enemy);
    }

    /// <summary>
    /// Picks somewhere to walk to that can actually be reached in a straight line: first a spawn point with a
    /// clear path, otherwise a short random walk in a free direction (the arena is full of props).
    /// </summary>
    bool PickTarget()
    {
        SpawnManager spawns = SpawnManager.Instance;
        if (spawns != null && spawns.spawnPoints.Count > 0)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                LaserTagSpawnPoint p = spawns.spawnPoints[Random.Range(0, spawns.spawnPoints.Count)];
                if (p == null) continue;
                Vector3 candidate = p.transform.position;
                if (Mathf.Abs(candidate.y - feet.y) > 0.6f) continue; // stay on this floor; stairs are teleport-only
                if ((candidate - feet).sqrMagnitude < 1f) continue;
                if (!PathClear(candidate)) continue;
                SetTarget(candidate);
                return true;
            }
        }

        float start = Random.Range(0f, 360f);
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, start + i * 30f, 0f) * Vector3.forward;
            float length = Random.Range(1.5f, 5f);
            // Shorten the walk until the whole way is clear and has floor under it.
            for (; length >= 1f; length -= 1f)
            {
                Vector3 candidate = feet + dir * length;
                if (PathClear(candidate)) { SetTarget(candidate); return true; }
            }
        }
        waitUntil = Time.time + 1f; // boxed in: wait and try again
        return false;
    }

    void SetTarget(Vector3 point)
    {
        target = point;
        lastProgressTime = Time.time;
        lastDistance = float.PositiveInfinity;
    }

    /// <summary>No obstacle on the way and floor under every half metre of it.</summary>
    bool PathClear(Vector3 destination)
    {
        Vector3 to = destination - feet;
        to.y = 0f;
        float distance = to.magnitude;
        if (distance < 0.01f) return false;
        Vector3 dir = to / distance;
        if (Blocked(dir, distance + 0.35f)) return false;
        for (float d = 0.5f; d <= distance; d += 0.5f)
            if (!TryGround(feet + dir * d, out float y) || Mathf.Abs(y - feet.y) > 0.3f) return false;
        return true;
    }

    bool Blocked(Vector3 direction, float distance)
    {
        Vector3 origin = feet + Vector3.up * 0.9f;
        int count = Physics.SphereCastNonAlloc(origin, 0.3f, direction, hits, distance, Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (hits[i].distance <= 0f) continue; // already touching (e.g. spawned close): ignore
            return true;
        }
        return false;
    }

    bool TryGround(Vector3 at, out float y)
    {
        y = at.y;
        int count = Physics.RaycastNonAlloc(at + Vector3.up * 0.6f, Vector3.down, hits, 1.4f, Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].normal.y < 0.7f) continue;
            best = Mathf.Max(best, hits[i].point.y);
        }
        if (float.IsNegativeInfinity(best)) return false; // edge of the floor
        y = best;
        return true;
    }

    void WritePose()
    {
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
        // Aim the guns (and head) at the enemy, including up/down.
        Quaternion aim = facing;
        if (enemy != null)
        {
            Vector3 look = Chest(enemy) - (feet + Vector3.up * 1.4f);
            if (look.sqrMagnitude > 0.01f) aim = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
        // The hand sits where the avatar's gun grip is: hand = gun * grip.
        Quaternion handRotation = aim * Quaternion.Euler(ScoutLocalRigBinding.DefaultGripRotation);
        Vector3 leftGun = feet + facing * LeftGunLocal;
        Vector3 rightGun = feet + facing * RightGunLocal;

        player.Pose.Value = new PoseState
        {
            rootPosition = feet,
            rootYaw = yaw,
            headPosition = feet + Vector3.up * (player.avatar != null ? player.avatar.StandingHeadHeight : 1.68f),
            headRotation = aim,
            leftPosition = leftGun + aim * ScoutLocalRigBinding.DefaultLeftGripPosition,
            leftRotation = handRotation,
            rightPosition = rightGun + aim * ScoutLocalRigBinding.DefaultRightGripPosition,
            rightRotation = handRotation,
        };
    }
}
