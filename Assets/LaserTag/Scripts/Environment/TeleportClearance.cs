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
            var movement=player.GetComponent<RelayAvatarPlayer>();
            Vector3 occupied=movement && movement.OriginRevision!=0?movement.ServerFeet:player.FeetPosition;
            if (HorizontalSqrDistance(occupied, feet) < minSqr) return false;
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

    /// <summary>Occupancy follows current server-accepted feet, not a vacated historical landing.</summary>
    public static bool ConflictsWithPlayer(Vector3 candidate,Vector3 currentFeet,float separation)
    {
        return HorizontalSqrDistance(currentFeet,candidate)<separation*separation;
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

    public enum RejectionCode
    {
        None, QuerySaturated, NoArcLanding, ArcPathRangeLimit, Range, Height, Slope, SurfaceNotEligible,
        UnsupportedFootprint, SupportHeight, BodyBlocked, PlayerOccupied, SourceInsideCover, SourceOccluded
    }
    public struct Rejection
    {
        public RejectionCode Code;
        public Collider Collider;
        public Vector3? Probe;
        public Rejection(RejectionCode code,Collider collider=null,Vector3? probe=null){Code=code;Collider=collider;Probe=probe;}
        public override string ToString()
        {
            string text=Collider?$"{Code}: collider='{Hierarchy(Collider.transform)}', layer={Collider.gameObject.layer}":Code.ToString();
            return Probe.HasValue?text+$", probe={Probe.Value:F3}":text;
        }
    }

    static readonly RaycastHit[] movementHits=new RaycastHit[128];
    static readonly List<Transform> serverIgnore=new List<Transform>(4);
    static readonly HashSet<Collider> walkable=new HashSet<Collider>();
    static LeftHandArcTeleport cachedSettings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSurfaces(){walkable.Clear();cachedSettings=null;}
    public static void InvalidateSurfaces(){ResetSurfaces();}

    // Only authored environment floor/ramp/stair/platform/walkway tokens qualify
    // automatically. "Default layer" and "up-facing collider" are not eligibility.
    public static bool HasWalkableName(string name)
    {
        if(string.IsNullOrEmpty(name))return false;
        return HasToken(name,"floor") || HasToken(name,"ramp") || HasToken(name,"stair") ||
            HasToken(name,"stairs") || HasToken(name,"platform") || HasToken(name,"walkway");
    }
    static bool HasToken(string name,string token)
    {
        if(!name.StartsWith(token,System.StringComparison.OrdinalIgnoreCase))return false;
        if(name.Length==token.Length)return true;
        char next=name[token.Length];
        return next=='_' || next=='-' || next==' ' || next=='(' || char.IsDigit(next);
    }
    public static bool IsAuthoredWalkableSurface(string objectName,string meshName)
    {
        // Reusing a floor mesh for a roof/wall/prop does not opt that object in.
        // Explicit collider references remain available for intentional decks.
        if(!string.IsNullOrEmpty(objectName) &&
            (objectName.StartsWith("roof",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("ceiling",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("wall",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("cover",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("crate",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("gun",System.StringComparison.OrdinalIgnoreCase) ||
             objectName.StartsWith("weapon",System.StringComparison.OrdinalIgnoreCase)))return false;
        return HasWalkableName(objectName) || HasWalkableName(meshName);
    }
    static bool StaticEnvironment(Collider c,LeftHandArcTeleport settings)
    {
        return c && !(c is CharacterController) && !c.attachedRigidbody &&
            (!(c is MeshCollider mesh) || mesh.sharedMesh) &&
            c.gameObject.scene==settings.gameObject.scene &&
            !c.GetComponentInParent<ScoutAvatar>() && !c.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
    }
    static void EnsureSurfaces(LeftHandArcTeleport settings)
    {
        if(!settings || cachedSettings==settings)return;
        walkable.Clear();cachedSettings=settings;
        var spawns=SpawnManager.Instance;
        if(spawns && spawns.fallbackFloors!=null)
            foreach(var c in spawns.fallbackFloors)if(StaticEnvironment(c,settings))walkable.Add(c);
        if(settings.additionalWalkableSurfaces!=null)
            foreach(var c in settings.additionalWalkableSurfaces)if(StaticEnvironment(c,settings))walkable.Add(c);
        // One scan per arena, not an object search per arc frame or network pose.
        foreach(var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
        {
            if(!StaticEnvironment(c,settings))continue;
            var mesh=c as MeshCollider;
            if(IsAuthoredWalkableSurface(c.name,mesh && mesh.sharedMesh?mesh.sharedMesh.name:null))
                walkable.Add(c);
        }
    }
    public static bool IsEligibleSurface(LeftHandArcTeleport settings,Collider c)
    {
        EnsureSurfaces(settings);
        return settings && c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger &&
            c.gameObject.layer!=2 && walkable.Contains(c);
    }
    public static string Hierarchy(Transform t)
    {
        string path=t.name;
        while(t.parent){t=t.parent;path=t.name+"/"+path;}
        return path;
    }
    public static void LogSurfaceInventory(LeftHandArcTeleport settings)
    {
        InvalidateSurfaces();EnsureSurfaces(settings);
        Debug.Log($"[TeleportSurfaces] registered colliders={walkable.Count}; reviewed spawn list is a seed, not the full teleport list.",settings);
        foreach(var c in walkable)
            Debug.Log($"[TeleportSurfaces] {Hierarchy(c.transform)} layer={c.gameObject.layer} enabled={c.enabled} active={c.gameObject.activeInHierarchy} trigger={c.isTrigger} landingEligible={IsEligibleSurface(settings,c)}",c);
    }
    static void ServerRoots(RelayAvatarPlayer player)
    {
        LocalPlayerRoots(serverIgnore);
        serverIgnore.Add(player.transform);
    }
    static bool BodySolid(Collider c,IList<Transform> roots) => c && c.enabled && !c.isTrigger && !IsIgnored(c.transform,roots);
    static bool Solid(Collider c,IList<Transform> roots) => BodySolid(c,roots) && c.gameObject.layer!=2;
    static bool Nearest(Vector3 origin,Vector3 direction,float distance,IList<Transform> roots,out RaycastHit nearest,out Rejection rejection)
    {
        nearest=default;rejection=default;
        int count=Physics.RaycastNonAlloc(origin,direction,movementHits,distance,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==movementHits.Length){rejection=new Rejection(RejectionCode.QuerySaturated);return false;}
        float closest=float.PositiveInfinity;bool found=false;
        for(int i=0;i<count;i++)
            if(Solid(movementHits[i].collider,roots) && movementHits[i].distance<closest)
            {nearest=movementHits[i];closest=nearest.distance;found=true;}
        return found;
    }
    public static bool TryTraceArc(LeftHandArcTeleport settings,Vector3 controller,Vector3 forward,IList<Transform> roots,
        Vector3[] positions,out int used,out RaycastHit landing,out Rejection rejection)
    {
        used=1;landing=default;rejection=default;
        forward.Normalize();Vector3 start=controller+forward*.08f;
        Vector3 velocity=forward*settings.launchSpeed+Vector3.up*settings.upwardBoost;
        Vector3 previous=start;if(positions!=null)positions[0]=start;
        float traveled=0;
        for(int step=1;step<=Mathf.Clamp(settings.segments,8,100);step++)
        {
            float t=step*settings.stepTime;
            Vector3 next=start+velocity*t+Vector3.down*(.5f*settings.gravity*t*t);
            Vector3 delta=next-previous;float length=delta.magnitude;
            if(length<.0001f)continue;
            traveled+=length;
            if(traveled>settings.maxRange){rejection=new Rejection(RejectionCode.ArcPathRangeLimit);return false;}
            if(Nearest(previous,delta/length,length,roots,out landing,out rejection))
            {
                if(positions!=null)positions[used]=landing.point;
                used++;return true;
            }
            if(rejection.Code!=RejectionCode.None)return false;
            if(positions!=null)positions[used]=next;
            used++;previous=next;
        }
        rejection=new Rejection(RejectionCode.NoArcLanding);return false;
    }
    public static bool TryValidateSource(Vector3 eye,Vector3 controller,Vector3 forward,IList<Transform> roots,out Rejection rejection)
    {
        rejection=default;Vector3 start=controller+forward.normalized*.08f;
        int count=Physics.OverlapSphereNonAlloc(start,.03f,overlapBuffer,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==overlapBuffer.Length){rejection=new Rejection(RejectionCode.QuerySaturated);return false;}
        for(int i=0;i<count;i++)if(Solid(overlapBuffer[i],roots))
        {rejection=new Rejection(RejectionCode.SourceInsideCover,overlapBuffer[i]);return false;}
        Vector3 path=start-eye;
        if(path.sqrMagnitude>.000001f && Nearest(eye,path.normalized,path.magnitude,roots,out var hit,out rejection))
        {rejection=new Rejection(RejectionCode.SourceOccluded,hit.collider);return false;}
        return rejection.Code==RejectionCode.None;
    }
    static bool Support(LeftHandArcTeleport settings,Vector3 point,float tolerance,IList<Transform> roots,out Rejection rejection)
    {
        EnsureSurfaces(settings);
        float probeHeight=Mathf.Max(.15f,tolerance+.05f);
        var ray=new Ray(point+Vector3.up*probeHeight,Vector3.down);
        rejection=default;float closest=float.PositiveInfinity;
        foreach(var floor in walkable)
        {
            if(!IsEligibleSurface(settings,floor))continue;
            Bounds b=floor.bounds;
            if(point.x<b.min.x || point.x>b.max.x || point.z<b.min.z || point.z>b.max.z)continue;
            if(!floor.Raycast(ray,out var hit,probeHeight*2))continue;
            bool heightValid=Mathf.Abs(hit.point.y-point.y)<=tolerance;
            bool slopeValid=hit.normal.y>=Mathf.Cos(settings.maxFloorSlope*Mathf.Deg2Rad);
            if(heightValid && slopeValid){rejection=default;return true;}
            if(hit.distance<closest)
            {
                closest=hit.distance;
                rejection=new Rejection(heightValid?RejectionCode.Slope:RejectionCode.SupportHeight,floor,point);
            }
        }
        if(rejection.Code!=RejectionCode.None)return false;
        // Probe eligible geometry directly, as the old reviewed-floor check did.
        // Low trim/cover must not conceal the floor from a leaning head or a
        // footprint probe; the separate full body capsule still checks ALL solids.
        if(Nearest(ray.origin,Vector3.down,probeHeight*2,roots,out var obstruction,out rejection))
            rejection=new Rejection(RejectionCode.SurfaceNotEligible,obstruction.collider,point);
        else if(rejection.Code==RejectionCode.None)rejection=new Rejection(RejectionCode.UnsupportedFootprint,null,point);
        rejection.Probe=point;return false;
    }
    public static bool IsSpawnSupported(LeftHandArcTeleport settings,Vector3 feet,float radius,IList<Transform> roots)
    {
        if(!settings)return false;
        float tolerance=radius*Mathf.Tan(settings.maxFloorSlope*Mathf.Deg2Rad)+.08f;
        return Support(settings,feet,.08f,roots,out _) &&
            Support(settings,feet+Vector3.right*radius,tolerance,roots,out _) &&
            Support(settings,feet-Vector3.right*radius,tolerance,roots,out _) &&
            Support(settings,feet+Vector3.forward*radius,tolerance,roots,out _) &&
            Support(settings,feet-Vector3.forward*radius,tolerance,roots,out _);
    }
    public static bool TryValidateLanding(LeftHandArcTeleport settings,RaycastHit hit,Vector3 feet,float eyeHeight,
        IList<Transform> roots,RelayCombatPlayer except,out Rejection rejection)
    {
        rejection=default;
        if(!hit.collider){rejection=new Rejection(RejectionCode.NoArcLanding);return false;}
        if(Vector3.Distance(feet,hit.point)>settings.maxRange){rejection=new Rejection(RejectionCode.Range,hit.collider);return false;}
        if(Mathf.Abs(feet.y-hit.point.y)>settings.maxVerticalChange){rejection=new Rejection(RejectionCode.Height,hit.collider);return false;}
        if(hit.normal.y<Mathf.Cos(settings.maxFloorSlope*Mathf.Deg2Rad)){rejection=new Rejection(RejectionCode.Slope,hit.collider);return false;}
        if(!IsEligibleSurface(settings,hit.collider)){rejection=new Rejection(RejectionCode.SurfaceNotEligible,hit.collider);return false;}
        float tolerance=settings.bodyRadius*Mathf.Tan(settings.maxFloorSlope*Mathf.Deg2Rad)+.08f;
        if(!Support(settings,hit.point+Vector3.right*settings.bodyRadius,tolerance,roots,out rejection) ||
            !Support(settings,hit.point-Vector3.right*settings.bodyRadius,tolerance,roots,out rejection) ||
            !Support(settings,hit.point+Vector3.forward*settings.bodyRadius,tolerance,roots,out rejection) ||
            !Support(settings,hit.point-Vector3.forward*settings.bodyRadius,tolerance,roots,out rejection))return false;
        // Exact existing body radius: no extra distance-to-cover margin. Never
        // ignore the landing mesh wholesale; it may also contain a wall.
        float radius=Mathf.Max(.05f,settings.bodyRadius);
        float height=Mathf.Max(settings.bodyHeight,eyeHeight+.1f);
        Vector3 bottom=hit.point+Vector3.up*(FloorGap+radius);
        Vector3 top=hit.point+Vector3.up*Mathf.Max(FloorGap+radius,height-radius);
        int count=Physics.OverlapCapsuleNonAlloc(bottom,top,radius,overlapBuffer,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==overlapBuffer.Length){rejection=new Rejection(RejectionCode.QuerySaturated);return false;}
        for(int i=0;i<count;i++)if(BodySolid(overlapBuffer[i],roots))
        {rejection=new Rejection(RejectionCode.BodyBlocked,overlapBuffer[i]);return false;}
        foreach(var other in RelayCombatPlayer.All)
        {
            if(!other || other==except || !other.IsPresentInWorld)continue;
            var movement=other.GetComponent<RelayAvatarPlayer>();
            Vector3 occupied=movement && movement.OriginRevision!=0?movement.ServerFeet:other.FeetPosition;
            // ServerFeet moves synchronously on approval. That already protects
            // concurrent requests; a vacated old landing is not a second player.
            if(ConflictsWithPlayer(hit.point,occupied,settings.bodyRadius*2))
            {rejection=new Rejection(RejectionCode.PlayerOccupied,other.hitboxes?other.hitboxes.ColliderFor(HitboxPart.Body):null);return false;}
        }
        return true;
    }
    public static bool IsTrackedHeadPathFree(RelayAvatarPlayer player,Vector3 next,bool hasPrevious,out string reason)
    {
        reason=null;
        if(!SpawnManager.Instance)return true;
        var rig=LocalPlayerRig.Instance;var settings=rig?rig.GetComponent<LeftHandArcTeleport>():null;
        if(!settings){reason="Tracking/server teleport configuration missing";return false;}
        ServerRoots(player);
        Vector3 feet=new Vector3(next.x,player.ServerFeet.y,next.z);
        if(!Support(settings,feet,.08f,serverIgnore,out var support))
        {reason="Tracking/floor: "+support;return false;}
        const float headRadius=.10f;
        int count=Physics.OverlapSphereNonAlloc(next,headRadius,overlapBuffer,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==overlapBuffer.Length){reason="Tracking/head overlap query saturated";return false;}
        for(int i=0;i<count;i++)if(Solid(overlapBuffer[i],serverIgnore))
        {reason="Tracking/head inside cover: "+Hierarchy(overlapBuffer[i].transform);return false;}
        if(!hasPrevious)return true;
        Vector3 delta=next-player.ServerEye;if(delta.sqrMagnitude<.000001f)return true;
        count=Physics.SphereCastNonAlloc(player.ServerEye,headRadius,delta.normalized,movementHits,delta.magnitude,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if(count==movementHits.Length){reason="Tracking/head sweep query saturated";return false;}
        for(int i=0;i<count;i++)if(Solid(movementHits[i].collider,serverIgnore))
        {reason="Tracking/head crosses cover: "+Hierarchy(movementHits[i].collider.transform);return false;}
        return true;
    }
    public static bool TryValidateServerTeleport(RelayAvatarPlayer player,Vector3 proposed,out Vector3 approved,out string reason)
    {
        approved=default;reason=null;
        if(!player || !player.IsServer){reason="Server authority unavailable";return false;}
        if(!RelayAvatarPlayer.Finite(proposed)){reason="Non-finite destination";return false;}
        var rig=LocalPlayerRig.Instance;var settings=rig?rig.GetComponent<LeftHandArcTeleport>():null;
        if(!settings || !SpawnManager.Instance){reason="Server arena configuration missing";return false;}
        ServerRoots(player);
        if(!TryValidateSource(player.ServerEye,player.ServerLeftController,player.ServerLeftForward,serverIgnore,out var rejection))
        {reason=rejection.ToString();return false;}
        if(!TryTraceArc(settings,player.ServerLeftController,player.ServerLeftForward,serverIgnore,null,out _,out var landing,out rejection))
        {reason=rejection.ToString();return false;}
        if(Vector3.Distance(landing.point,proposed)>.15f)
        {reason=$"ArcMismatch: server={landing.point:F3}, error={Vector3.Distance(landing.point,proposed):F3}m, collider='{Hierarchy(landing.collider.transform)}'";return false;}
        if(!TryValidateLanding(settings,landing,player.ServerFeet,player.ServerEye.y-player.ServerFeet.y,serverIgnore,
            player.GetComponent<RelayCombatPlayer>(),out rejection))
        {reason=rejection.ToString();return false;}
        approved=landing.point;return true;
    }
}
