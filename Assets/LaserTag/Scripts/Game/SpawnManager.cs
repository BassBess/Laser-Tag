using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picks a free place to put a player: at the start of a round, after a respawn, or when someone joins.
///
/// A point is "free" when a column of physics spheres at the point overlaps no collider (wall, prop,
/// another avatar) and no other living player (or a point already handed out this frame) is closer than
/// <see cref="minDistanceToPlayers"/>. Occupied points are skipped and another one is chosen.
/// If every spawn point is taken, it tries random spots on the teleport floors, and as a last resort the
/// spawn point farthest from everyone.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [Tooltip("Spawn points to choose from. Leave empty to use every LaserTagSpawnPoint in the children.")]
    public List<LaserTagSpawnPoint> spawnPoints = new List<LaserTagSpawnPoint>();

    [Tooltip("Reviewed arena floor colliders used when every spawn point is occupied.")]
    public Collider[] fallbackFloors = new Collider[0];

    [Header("Clearance")]
    [Min(0.1f)] public float minDistanceToPlayers = 2f;
    [Min(0.05f)] public float bodyRadius = 0.35f;
    [Min(0.5f)] public float bodyHeight = 1.8f;
    [Min(0)] public int randomFallbackAttempts = 40;

    readonly List<Transform> ignore = new List<Transform>(2);
    readonly List<int> order = new List<int>();

    void Awake()
    {
        Instance = this;
        if (spawnPoints.Count == 0) GetComponentsInChildren(spawnPoints);
        spawnPoints.RemoveAll(p => p == null);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Finds a free spot for <paramref name="player"/>. <paramref name="reserved"/> are spots already given out.</summary>
    public bool TryFindSpawn(RelayCombatPlayer player, IList<Vector3> reserved, out Vector3 feet, out float yaw, bool initialOnly = false)
    {
        feet=default; yaw=0;
        TeleportClearance.LocalPlayerRoots(ignore);
        if (player != null && !ignore.Contains(player.transform)) ignore.Add(player.transform);

        // Try the predetermined points in random order.
        order.Clear();
        for (int i = 0; i < spawnPoints.Count; i++)
            if (!initialOnly || spawnPoints[i].initialSpawn) order.Add(i);
        Shuffle(order);
        foreach (int i in order)
        {
            LaserTagSpawnPoint point = spawnPoints[i];
            if (point != null && IsFree(point.transform.position, player, reserved))
            {
                yaw = point.Yaw;
                feet=point.transform.position; return true;
            }
        }

        // Every point is occupied: try random spots on the teleport floors.
        if (!initialOnly && fallbackFloors != null && fallbackFloors.Length > 0)
        {
            for (int attempt = 0; attempt < randomFallbackAttempts; attempt++)
            {
                Collider floor = fallbackFloors[Random.Range(0, fallbackFloors.Length)];
                if (floor == null) continue;
                Bounds b = floor.bounds;
                var top = new Vector3(Random.Range(b.min.x, b.max.x), b.max.y + 0.5f, Random.Range(b.min.z, b.max.z));
                if (!floor.Raycast(new Ray(top, Vector3.down), out RaycastHit hit, b.size.y + 1f)) continue;
                if (hit.normal.y < 0.85f) continue;
                if (IsFree(hit.point, player, reserved, floor))
                {
                    yaw = Random.Range(0f, 360f);
                    feet=hit.point; return true;
                }
            }
        }

        // Fail closed. The round waits, or a dead player retries respawning;
        // occupancy and wall clearance never become optional under load.
        return false;
    }

    public bool IsFree(Vector3 feet, RelayCombatPlayer player, IList<Vector3> reserved, Collider floor = null)
    {
        var rig=LocalPlayerRig.Instance;
        var settings=rig?rig.GetComponent<LeftHandArcTeleport>():null;
        var movement=player?player.GetComponent<RelayAvatarPlayer>():null;
        float height=bodyHeight;
        if(movement)height=Mathf.Max(height,(movement.ServerTrackingReady?
            movement.ServerEye.y-movement.ServerFeet.y:movement.maximumHeadHeight)+.1f);
        return settings && TeleportClearance.IsSpawnSupported(settings,feet,bodyRadius,ignore) &&
               TeleportClearance.IsFarFromPlayers(feet, minDistanceToPlayers, player, reserved) &&
               TeleportClearance.IsBodySpaceFree(feet, bodyRadius, height, floor, ignore);
    }

    static float DistanceToNearestPlayer(Vector3 feet, RelayCombatPlayer except, IList<Vector3> reserved)
    {
        float nearest = float.PositiveInfinity;
        foreach (RelayCombatPlayer p in RelayCombatPlayer.All)
            if (p != null && p != except && p.IsPresentInWorld && p.HasPose)
                nearest = Mathf.Min(nearest, Vector3.Distance(p.FeetPosition, feet));
        if (reserved != null)
            foreach (Vector3 r in reserved)
                nearest = Mathf.Min(nearest, Vector3.Distance(r, feet));
        return nearest;
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
