using UnityEngine;

/// <summary>Which part of a player a laser hit.</summary>
public enum HitboxPart { Body, Head }

/// <summary>Everything game logic needs to know about one laser hit on a player.</summary>
public struct LaserHitInfo
{
    public LaserRay shooter;        // the gun that fired
    public PlayerHitboxes victim;   // the player that was hit
    public HitboxPart part;         // head or body
    public Vector3 point;           // exact world position of the hit
    public float distance;          // meters from the muzzle
}

/// <summary>
/// Gives a player invisible hitboxes (colliders) so lasers can detect them:
///   - a capsule around the body (child of this object, from the feet up)
///   - a sphere around the head (follows the head as it moves)
///
/// Put it on the root of the avatar prefab (the object with ScoutAvatar), or on a player's root object.
/// The hitboxes are created when the game starts; they are invisible, but you can see their green outlines
/// in the Scene view while Play mode is running.
///
/// When a LaserRay's ray reaches one of these colliders, it calls ReportHit(), which logs the hit and
/// fires the Hit / AnyPlayerHit events. Health, damage and score code should subscribe to those events.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHitboxes : MonoBehaviour
{
    [Header("Head")]
    [Tooltip("The head to follow. Leave empty to use ScoutAvatar's head, or a child object named \"Head\".")]
    public Transform head;
    [Tooltip("Radius of the head sphere, in meters.")]
    public float headRadius = 0.16f;
    [Tooltip("Offset of the sphere from the head pivot, in the head's local space.")]
    public Vector3 headCenter = Vector3.zero;

    [Header("Body")]
    [Tooltip("Radius of the body capsule, in meters.")]
    public float bodyRadius = 0.22f;
    [Tooltip("Height of the body capsule, in meters (from Body Bottom up).")]
    public float bodyHeight = 1.45f;
    [Tooltip("How far above this object's pivot (the feet) the capsule starts, in meters.")]
    public float bodyBottom = 0.05f;

    [Header("Layer (optional)")]
    [Tooltip("If a layer with this name exists (Tags & Layers), the hitboxes are put on it.")]
    public string hitboxLayer = "PlayerHitbox";

    /// <summary>Raised when THIS player is hit.</summary>
    public event System.Action<LaserHitInfo> Hit;

    /// <summary>Raised when ANY player is hit. Handy for one central game manager (scores, kill feed...).</summary>
    public static event System.Action<LaserHitInfo> AnyPlayerHit;

    /// <summary>The player's identity (id + name), if the player has a PlayerIdentity above it.</summary>
    public PlayerIdentity Identity { get; private set; }

    /// <summary>Readable name for logs: the PlayerIdentity name if there is one, otherwise the object name.</summary>
    public string PlayerName => Identity != null ? Identity.DisplayName : name;

    /// <summary>How many times this player has been hit since the game started.</summary>
    public int HitCount { get; private set; }

    SphereCollider headCollider;
    CapsuleCollider bodyCollider;

    void Start()
    {
        // Start (not Awake) so the spawner has already given the player its identity.
        Identity = GetComponentInParent<PlayerIdentity>();
        CreateHitboxes();
    }

    void CreateHitboxes()
    {
        int layer = string.IsNullOrEmpty(hitboxLayer) ? -1 : LayerMask.NameToLayer(hitboxLayer);

        // Body: capsule standing on this object's pivot.
        var bodyObject = new GameObject("Hitbox_Body");
        bodyObject.transform.SetParent(transform, false);
        bodyCollider = bodyObject.AddComponent<CapsuleCollider>();
        bodyCollider.direction = 1; // vertical (Y axis)
        bodyCollider.radius = bodyRadius;
        bodyCollider.height = bodyHeight;
        bodyCollider.center = new Vector3(0f, bodyBottom + bodyHeight * 0.5f, 0f);
        if (layer >= 0) bodyObject.layer = layer;

        // Head: sphere that follows the head transform.
        Transform headTarget = FindHead();
        if (headTarget == null)
        {
            Debug.LogWarning($"{name}: PlayerHitboxes found no head, so only the body can be hit. " +
                             "Assign the Head field.", this);
            return;
        }

        var headObject = new GameObject("Hitbox_Head");
        headObject.transform.SetParent(headTarget, false);
        headCollider = headObject.AddComponent<SphereCollider>();
        headCollider.radius = headRadius;
        headCollider.center = headCenter;
        if (layer >= 0) headObject.layer = layer;
    }

    Transform FindHead()
    {
        if (head != null) return head;

        ScoutAvatar avatar = GetComponentInChildren<ScoutAvatar>(true);
        if (avatar != null && avatar.head != null) return avatar.head;

        // Placeholder players from PlayerTestSpawner have a child called "Head".
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == "Head") return child;

        return null;
    }

    /// <summary>Is this collider one of this player's hitboxes? If so, which part?</summary>
    public bool TryGetPart(Collider collider, out HitboxPart part)
    {
        if (collider != null && collider == headCollider) { part = HitboxPart.Head; return true; }
        if (collider != null && collider == bodyCollider) { part = HitboxPart.Body; return true; }
        part = HitboxPart.Body;
        return false;
    }

    /// <summary>Called by LaserRay when its ray reaches one of this player's hitboxes.</summary>
    public void ReportHit(LaserRay shooter, HitboxPart part, RaycastHit hit)
    {
        HitCount++;

        var info = new LaserHitInfo
        {
            shooter = shooter,
            victim = this,
            part = part,
            point = hit.point,
            distance = hit.distance
        };

        Debug.Log($"{shooter.name} HIT {PlayerName} in the {part} at {hit.distance:F1} m (hit #{HitCount})", this);

        Hit?.Invoke(info);
        AnyPlayerHit?.Invoke(info);
    }
}
