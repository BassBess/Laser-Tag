using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// The player wearing THIS headset: XR rig, camera, local avatar and guns.
///
/// - Ammunition: <see cref="maxAmmo"/> shots shared by both guns. After <see cref="refillDelay"/> seconds
///   without shooting, shots refill at <see cref="refillPerSecond"/> until full.
/// - Fire gate: guns only fire with ammo, while alive and outside the start countdown.
/// - Hits: when a laser hits another player's avatar, the server is asked to apply damage.
/// - Pose: leaves tracking and pose publication to the original Scout/Relay avatar stack.
/// - Teleport: lets the server place this player (round start, respawn).
/// </summary>
[DefaultExecutionOrder(-100)]
public class LocalPlayerRig : MonoBehaviour
{
    public static LocalPlayerRig Instance { get; private set; }

    [Header("Rig")]
    public XROrigin xrOrigin;
    public Camera playerCamera;
    [Tooltip("The local full-body avatar (driven by ScoutLocalRigBinding). Optional.")]
    public ScoutAvatar avatar;
    public TeleportationProvider teleportationProvider;
    [Tooltip("Left/right controllers, used for the hand pose when there is no avatar.")]
    public Transform leftController, rightController;

    [Header("Guns")]
    public LaserRay[] guns = new LaserRay[0];

    [Header("Ammunition (server configuration lives on RelayCombatPlayer)")]
    public int lowAmmoWarning = 25;
    public int maxAmmo => RelayCombatPlayer.Local ? RelayCombatPlayer.Local.maxAmmo : 100;
    [Header("Feel")]
    [Range(0f, 1f)] public float shotVibration = 0.25f;
    [Range(0f, 1f)] public float hitVibration = 0.8f;

    public int Ammo => RelayCombatPlayer.Local ? RelayCombatPlayer.Local.PredictedAmmo : 0;
    public bool IsRecharging => RelayCombatPlayer.Local && RelayCombatPlayer.Local.IsRecharging;
    /// <summary>Seconds since the last "out of ammo" click; used by the HUD to blink.</summary>
    public float TimeSinceEmptyClick => Time.time - lastEmptyClick;

    /// <summary>The player most recently hit by our laser, and when (used for "TAGGED" kill confirms).</summary>
    public RelayCombatPlayer LastVictim { get; private set; }
    public float LastVictimTime { get; private set; } = -999f;
    public float AmmoFraction => Mathf.Clamp01((float)Ammo / Mathf.Max(1, maxAmmo));


    Vector3 lastShotFrom;
    float lastEmptyClick = -999f;

    System.Func<bool>[] gates = new System.Func<bool>[0];
    System.Action<Vector3, Vector3, bool>[] fireHandlers = new System.Action<Vector3, Vector3, bool>[0];

    void Awake()
    {
        Instance = this;
        RelayCombatPlayer.Local?.ClearPrediction();
        if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
        if (playerCamera == null && xrOrigin != null) playerCamera = xrOrigin.Camera;
        if (playerCamera == null) playerCamera = Camera.main;
        if (teleportationProvider == null) teleportationProvider = FindAnyObjectByType<TeleportationProvider>();
        if (guns == null || guns.Length == 0) guns = FindObjectsByType<LaserRay>(FindObjectsSortMode.None);
    }

    void OnEnable()
    {
        gates = new System.Func<bool>[guns.Length];
        fireHandlers = new System.Action<Vector3, Vector3, bool>[guns.Length];
        for (int i = 0; i < guns.Length; i++)
        {
            LaserRay gun = guns[i];
            if (gun == null) continue;
            LaserTagFeedback.Hand hand = HandOf(gun);
            gates[i] = () => CanFire(gun, hand);
            fireHandlers[i] = (from, to, hit) => OnFired(from, to, hand);
            gun.canFire = gates[i];
            gun.onFired += fireHandlers[i];
            gun.onHit += OnHit;
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < guns.Length && i < gates.Length; i++)
        {
            LaserRay gun = guns[i];
            if (gun == null) continue;
            if (gun.canFire == gates[i]) gun.canFire = null;
            gun.onFired -= fireHandlers[i];
            gun.onHit -= OnHit;
        }
    }

    LaserTagFeedback.Hand HandOf(LaserRay gun)
    {
        if (leftController != null && gun.transform.IsChildOf(leftController)) return LaserTagFeedback.Hand.Left;
        if (rightController != null && gun.transform.IsChildOf(rightController)) return LaserTagFeedback.Hand.Right;
        return gun.name.ToLowerInvariant().Contains("left") ? LaserTagFeedback.Hand.Left : LaserTagFeedback.Hand.Right;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        var me = RelayCombatPlayer.Local;
        foreach (var gun in guns) if (gun)
        {
            gun.owner = xrOrigin ? xrOrigin.transform : transform;
            gun.ignoredAvatar = avatar ? avatar.transform : null;
            gun.ignoredNetworkPlayer = me ? me.transform : null;
        }
    }
    // ---------------- shooting ----------------

    bool CanFire(LaserRay gun, LaserTagFeedback.Hand hand)
    {
        RelayCombatPlayer me = RelayCombatPlayer.Local;
        if (me == null || !me.CanShoot) return false;
        if (Ammo <= 0)
        {
            // Empty attempts provide feedback without consuming ammo or delaying server regeneration.
            lastEmptyClick = Time.time;
            LaserTagFeedback.PlayEmpty(gun.muzzle != null ? gun.muzzle.position : gun.transform.position);
            LaserTagFeedback.Haptic(hand, 0.15f, 0.04f);
            return false;
        }
        return true;
    }

    void OnFired(Vector3 from, Vector3 to, LaserTagFeedback.Hand hand)
    {


        lastShotFrom = from;

        LaserTagFeedback.PlayShot(from);
        LaserTagFeedback.Haptic(hand, shotVibration, 0.06f);
        if (RelayCombatPlayer.Local != null) RelayCombatPlayer.Local.OwnerFired(from, to, hand == LaserTagFeedback.Hand.Left ? 0 : 1);
    }

    void OnHit(RaycastHit hit)
    {
        RelayCombatPlayer me = RelayCombatPlayer.Local;
        if (me == null || hit.collider == null) return;
        RelayCombatPlayer victim = hit.collider.GetComponentInParent<RelayCombatPlayer>();
        if (victim == null || victim == me || !victim.IsPresentInWorld) return;
        var boxes = victim.GetComponent<PlayerHitboxes>();
        if (!boxes || !boxes.TryGetPart(hit.collider, out var part)) return;
        me.OwnerReportHit(victim, lastShotFrom, hit.point, part); // the host re-checks this shot
        LastVictim = victim;
        LastVictimTime = Time.time;

    }

    /// <summary>Called when the local player loses health.</summary>
    public void OnDamaged()
    {
        LaserTagFeedback.PlayHurt();
        LaserTagFeedback.Haptic(LaserTagFeedback.Hand.Both, hitVibration, 0.18f);
    }

    public void RefillAmmo()
    {
        RelayCombatPlayer.Local?.ClearPrediction();

    }

    // ---------------- pose ----------------

    /// <summary>Feet position on the floor (avatar root, or the camera dropped to the rig's floor).</summary>
    public Vector3 FeetPosition
    {
        get
        {
            if (avatar != null) { var pos = avatar.transform.position; var legs = avatar.GetComponent<ScoutProceduralLegs>(); if (legs && legs.IsActive) pos.y = legs.CurrentFloorHeight; return pos; }
            Vector3 cam = playerCamera != null ? playerCamera.transform.position : transform.position;
            float floor = xrOrigin != null ? xrOrigin.transform.position.y : 0f;
            return new Vector3(cam.x, floor, cam.z);
        }
    }

    // ---------------- teleport ----------------

    /// <summary>Moves the player so their feet land on <paramref name="feet"/>, looking towards <paramref name="yaw"/>.</summary>
    public void TeleportTo(Vector3 feet, float yaw)
    {
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
        if (teleportationProvider != null && teleportationProvider.isActiveAndEnabled)
        {
            var request = new TeleportRequest
            {
                destinationPosition = feet,
                destinationRotation = facing,
                matchOrientation = MatchOrientation.TargetUpAndForward,
                requestTime = Time.time,
            };
            if (teleportationProvider.QueueTeleportRequest(request)) return;
        }

        // Fallback without a locomotion system: move the rig directly.
        if (xrOrigin == null || playerCamera == null) return;
        Transform rig = xrOrigin.transform;
        Transform cam = playerCamera.transform;
        Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        float camYaw = camForward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(camForward).eulerAngles.y : rig.eulerAngles.y;
        rig.RotateAround(cam.position, Vector3.up, Mathf.DeltaAngle(camYaw, yaw));
        Vector3 currentFeet = new Vector3(cam.position.x, rig.position.y, cam.position.z);
        rig.position += feet - currentFeet;
    }
}
