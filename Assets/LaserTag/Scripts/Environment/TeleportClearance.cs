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
    /// solid collider. Trigger colliders and anything under <paramref name="ignoreRoots"/> are ignored.
    /// The legacy ignoreCollider argument is retained for callers, but a landing
    /// mesh is never ignored wholesale because it can contain walls as well.
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
            if(count==overlapBuffer.Length)return false; // truncated queries cannot prove clearance
            for (int c = 0; c < count; c++)
            {
                Collider other = overlapBuffer[c];
                // Do not ignore an entire landing mesh: it may also contain a
                // wall. The lowest sphere is already above a flat floor.
                if (other == null || IsIgnored(other.transform, ignoreRoots))
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
    public static bool IsFarFromPlayers(Vector3 feet, float minDistance, RelayCombatPlayer except = null,
        IList<Vector3> reserved = null)
    {
        float minSqr = minDistance * minDistance;
        foreach (RelayCombatPlayer player in RelayCombatPlayer.All)
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

    /// <summary>Keep a landing reserved even if its owner leans away between requests.</summary>
    public static bool ConflictsWithLanding(Vector3 candidate,Vector3 currentFeet,Vector3 reservedLanding,float separation)
    {
        float square=separation*separation;
        return HorizontalSqrDistance(currentFeet,candidate)<square || HorizontalSqrDistance(reservedLanding,candidate)<square;
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
        if (RelayCombatPlayer.Local != null) buffer.Add(RelayCombatPlayer.Local.transform);
        return buffer;
    }

    static readonly RaycastHit[] movementHits=new RaycastHit[128];
    static readonly List<Transform> serverIgnore=new List<Transform>(4);

    static void ServerRoots(RelayAvatarPlayer player)
    {
        // The host's scene avatar is a visual duplicate. Player occupancy is
        // checked from authoritative network feet, including the host below.
        LocalPlayerRoots(serverIgnore);
        serverIgnore.Add(player.transform);
    }
    static bool Solid(Collider c) => c && c.enabled && !c.isTrigger && c.gameObject.layer!=2 && !IsIgnored(c.transform,serverIgnore);

    /// <summary>Reviewed spawn-floor geometry is the arena whitelist, not an AABB or collider name.</summary>
    public static bool IsReviewedFloor(Vector3 point,float tolerance=.08f)
    {
        var spawns=SpawnManager.Instance;
        if(!spawns || spawns.fallbackFloors==null)return false;
        foreach(var floor in spawns.fallbackFloors)
        {
            if(!floor || !floor.enabled || !floor.gameObject.activeInHierarchy || floor.isTrigger)continue;
            Bounds bounds=floor.bounds;
            if(point.x<bounds.min.x || point.x>bounds.max.x || point.z<bounds.min.z || point.z>bounds.max.z)continue;
            // Co-located MeshCollider/BoxCollider representations of the same
            // reviewed floor are allowed, but never a prop above that floor.
            float probeHeight=Mathf.Max(.15f,tolerance+.05f);
            if(floor.Raycast(new Ray(point+Vector3.up*probeHeight,Vector3.down),out var hit,probeHeight*2) &&
                Vector3.Distance(hit.point,point)<=tolerance && hit.normal.y>=.8f)return true;
        }
        return false;
    }

    public static bool IsTrackedHeadPathFree(RelayAvatarPlayer player,Vector3 next,bool hasPrevious)
    {
        if(!SpawnManager.Instance)return true; // menu has no arena; bounds still apply
        ServerRoots(player);
        Vector3 feet=new Vector3(next.x,player.ServerFeet.y,next.z);
        if(!IsReviewedFloor(feet))return false;
        const float headRadius=.10f;
        int overlaps=Physics.OverlapSphereNonAlloc(next,headRadius,overlapBuffer,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(overlaps==overlapBuffer.Length)return false;
        for(int i=0;i<overlaps;i++)if(Solid(overlapBuffer[i]))return false;
        if(!hasPrevious)return true;
        Vector3 delta=next-player.ServerEye;
        if(delta.sqrMagnitude<.000001f)return true;
        int count=Physics.SphereCastNonAlloc(player.ServerEye,headRadius,delta.normalized,movementHits,delta.magnitude,
            Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==movementHits.Length)return false;
        for(int i=0;i<count;i++)if(Solid(movementHits[i].collider))return false;
        return true;
    }

    /// <summary>Server independently reconstructs the existing curved arc, then validates its first landing.</summary>
    public static bool TryValidateServerTeleport(RelayAvatarPlayer player,Vector3 proposed,out Vector3 approved,out string reason)
    {
        approved=default;reason="Teleport destination unavailable";
        if(!player || !player.IsServer || !RelayAvatarPlayer.Finite(proposed) || !SpawnManager.Instance)return false;
        var rig=LocalPlayerRig.Instance;
        var settings=rig?rig.GetComponent<LeftHandArcTeleport>():null;
        if(!settings){reason="Server teleport configuration missing";return false;}
        Vector3 feet=player.ServerFeet;
        if(Vector3.Distance(feet,proposed)>settings.maxRange || Mathf.Abs(feet.y-proposed.y)>settings.maxVerticalChange)
        {reason="Teleport exceeds range or floor-height limit";return false;}
        ServerRoots(player);
        Vector3 forward=player.ServerLeftForward.normalized;
        Vector3 start=player.ServerLeftController+forward*.08f;
        // A forged hand inside/beyond a wall must not start the arc on the
        // far side of cover (raycasts alone do not report an enclosing collider).
        int sourceCount=Physics.OverlapSphereNonAlloc(start,.03f,overlapBuffer,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(sourceCount==overlapBuffer.Length)return false;
        for(int i=0;i<sourceCount;i++)if(Solid(overlapBuffer[i]))
        {reason="Left-controller arc source is inside cover";return false;}
        Vector3 sourcePath=start-player.ServerEye;
        if(sourcePath.sqrMagnitude>.000001f)
        {
            int sourceHits=Physics.RaycastNonAlloc(player.ServerEye,sourcePath.normalized,movementHits,sourcePath.magnitude,
                Physics.AllLayers,QueryTriggerInteraction.Ignore);
            if(sourceHits==movementHits.Length)return false;
            for(int i=0;i<sourceHits;i++)if(Solid(movementHits[i].collider))
            {reason="Left-controller arc source is occluded by cover";return false;}
        }
        Vector3 velocity=forward*settings.launchSpeed+Vector3.up*settings.upwardBoost;
        Vector3 previous=start;
        float traveled=0;
        RaycastHit landing=default;
        bool found=false;
        int steps=Mathf.Clamp(settings.segments,8,100);
        for(int step=1;step<=steps;step++)
        {
            float t=step*settings.stepTime;
            Vector3 next=start+velocity*t+Vector3.down*(.5f*settings.gravity*t*t);
            Vector3 delta=next-previous;float length=delta.magnitude;
            if(length<.0001f)continue;
            traveled+=length;if(traveled>settings.maxRange)break;
            int count=Physics.RaycastNonAlloc(previous,delta/length,movementHits,length,Physics.AllLayers,QueryTriggerInteraction.Ignore);
            if(count==movementHits.Length){reason="Teleport physics query saturated";return false;}
            float closest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
                if(Solid(movementHits[i].collider) && movementHits[i].distance<closest)
                {landing=movementHits[i];closest=landing.distance;found=true;}
            if(found)break;
            previous=next;
        }
        // Tolerance only covers release-frame float/physics differences. The
        // actual returned landing always comes from the server's own raycast.
        if(!found || Vector3.Distance(landing.point,proposed)>.15f)
        {reason="Destination does not match the unobstructed left-hand arc";return false;}
        if(landing.normal.y<Mathf.Cos(settings.maxFloorSlope*Mathf.Deg2Rad) || !IsReviewedFloor(landing.point))
        {reason="Destination is not a reviewed walkable arena floor";return false;}
        float supportTolerance=settings.bodyRadius*Mathf.Tan(settings.maxFloorSlope*Mathf.Deg2Rad)+.08f;
        if(!IsReviewedFloor(landing.point+Vector3.right*settings.bodyRadius,supportTolerance) ||
            !IsReviewedFloor(landing.point-Vector3.right*settings.bodyRadius,supportTolerance) ||
            !IsReviewedFloor(landing.point+Vector3.forward*settings.bodyRadius,supportTolerance) ||
            !IsReviewedFloor(landing.point-Vector3.forward*settings.bodyRadius,supportTolerance))
        {reason="Destination is too close to an unsupported floor edge";return false;}
        float height=Mathf.Max(settings.bodyHeight,player.ServerEye.y-player.ServerFeet.y+.1f);
        if(!IsBodySpaceFree(landing.point,settings.bodyRadius,height,null,serverIgnore))
        {reason="Destination has insufficient body clearance";return false;}
        float separation=settings.bodyRadius*2;
        foreach(var other in RelayCombatPlayer.All)
        {
            if(!other || !other.IsPresentInWorld || other.gameObject==player.gameObject)continue;
            var movement=other.GetComponent<RelayAvatarPlayer>();
            // Last landing stays reserved while this player occupies its
            // bounded room-scale bubble, until their next approved placement.
            if(ConflictsWithLanding(landing.point,other.FeetPosition,movement?movement.ReservedLanding:other.FeetPosition,separation))
            {reason="Destination is occupied by another player";return false;}
        }
        approved=landing.point;reason="";return true;
    }
}
