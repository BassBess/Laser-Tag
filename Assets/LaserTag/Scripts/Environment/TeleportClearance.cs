using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared "is there room for a player here?" checks, used by teleport targets and by player spawning.
///
/// A standing player is approximated by a column of physics spheres from the feet up to head height.
/// A point is free when none of those spheres overlap a solid collider (walls, props, doors, other
/// avatars) and no other living player stands closer than a minimum distance.
/// </summary>
public static class TeleportClearance
{
    /// <summary>Gap between the floor and the lowest sphere, so the floor itself never counts as blocking.</summary>
    public const float FloorGap = 0.1f;

    static readonly Collider[] overlapBuffer = new Collider[64];

    /// <summary>
    /// True when a body of the given radius and height fits at <paramref name="feet"/> without touching any
    /// solid collider. Trigger colliders, <paramref name="ignoreCollider"/> (usually the floor that was hit)
    /// and anything under one of <paramref name="ignoreRoots"/> are ignored.
    /// </summary>
    public static bool IsBodySpaceFree(Vector3 feet, float radius, float height, Collider ignoreCollider = null,
        IList<Transform> ignoreRoots = null)
    {
        radius = Mathf.Max(0.05f, radius);
        float lowest = FloorGap + radius;
        float highest = Mathf.Max(lowest, height - radius);

        // Spheres are spaced one radius apart so the column has no gaps.
        int steps = Mathf.Max(1, Mathf.CeilToInt((highest - lowest) / radius));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 center = feet + Vector3.up * Mathf.Lerp(lowest, highest, (float)i / steps);
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int c = 0; c < count; c++)
            {
                Collider other = overlapBuffer[c];
                if (other == null || other == ignoreCollider || IsIgnored(other.transform, ignoreRoots) ||
                    other.GetComponentInParent<SafeTeleportationAnchor>() != null)
                    continue;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// True when every living, visible player (except <paramref name="except"/>) is at least
    /// <paramref name="minDistance"/> metres away horizontally. Also checks <paramref name="reserved"/>
    /// points, used while several players are being placed in the same frame.
    /// </summary>
    public static bool IsFarFromPlayers(Vector3 feet, float minDistance, NetworkPlayer except = null,
        IList<Vector3> reserved = null)
    {
        float minSqr = minDistance * minDistance;
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player == except || !player.IsPresentInWorld || !player.HasPose) continue;
            if (HorizontalSqrDistance(player.FeetPosition, feet) < minSqr) return false;
        }
        if (reserved != null)
            foreach (Vector3 point in reserved)
                if (HorizontalSqrDistance(point, feet) < minSqr)
                    return false;
        return true;
    }

    static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        // Players on another floor (more than ~a storey apart) never block each other.
        if (Mathf.Abs(a.y - b.y) > 2f) return float.PositiveInfinity;
        return dx * dx + dz * dz;
    }

    static bool IsIgnored(Transform t, IList<Transform> roots)
    {
        if (roots == null) return false;
        for (int i = 0; i < roots.Count; i++)
            if (roots[i] != null && t.IsChildOf(roots[i]))
                return true;
        return false;
    }

    /// <summary>Roots that belong to the local player (XR rig and own avatar); these never block the local player.</summary>
    public static List<Transform> LocalPlayerRoots(List<Transform> buffer)
    {
        buffer.Clear();
        LocalPlayerRig rig = LocalPlayerRig.Instance;
        if (rig != null)
        {
            if (rig.xrOrigin != null) buffer.Add(rig.xrOrigin.transform);
            if (rig.avatar != null) buffer.Add(rig.avatar.transform);
        }
        if (NetworkPlayer.Local != null) buffer.Add(NetworkPlayer.Local.transform);
        return buffer;
    }
}
