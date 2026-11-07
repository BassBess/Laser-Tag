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
/// - Pose: provides the pose that <see cref="NetworkPlayer"/> sends to the others.
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

    [Header("Ammunition")]
    [Min(1)] public int maxAmmo = 100;
    [Tooltip("Seconds without shooting before shots start to refill.")]
    [Min(0f)] public float refillDelay = 2f;
    [Tooltip("Shots refilled per second once recharging.")]
    [Min(0.1f)] public float refillPerSecond = 8f;
    [Tooltip("At or below this many shots the HUD warns LOW.")]
    [Min(0)] public int lowAmmoWarning = 25;

    [Header("Feel")]
    [Range(0f, 1f)] public float shotVibration = 0.25f;
    [Range(0f, 1f)] public float hitVibration = 0.8f;

    public int Ammo { get; private set; }
    public bool IsRecharging => Ammo < maxAmmo && Time.time - lastShotTime >= refillDelay;
    /// <summary>Seconds since the last "out of ammo" click; used by the HUD to blink.</summary>
    public float TimeSinceEmptyClick => Time.time - lastEmptyClick;

    /// <summary>The player most recently hit by our laser, and when (used for "TAGGED" kill confirms).</summary>
    public NetworkPlayer LastVictim { get; private set; }
    public float LastVictimTime { get; private set; } = -999f;
    public float AmmoFraction => Mathf.Clamp01((Ammo + rechargeProgress) / Mathf.Max(1, maxAmmo));

    float lastShotTime = -999f;
    Vector3 lastShotFrom;
    float lastEmptyClick = -999f;
    float rechargeProgress;
    System.Func<bool>[] gates = new System.Func<bool>[0];
    System.Action<Vector3, Vector3, bool>[] fireHandlers = new System.Action<Vector3, Vector3, bool>[0];

    void Awake()
    {
        Instance = this;
        Ammo = maxAmmo;
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
        if (Ammo >= maxAmmo) { rechargeProgress = 0f; return; }
        if (Time.time - lastShotTime < refillDelay) return;
        rechargeProgress += refillPerSecond * Time.deltaTime;
        int whole = Mathf.FloorToInt(rechargeProgress);
        if (whole > 0)
        {
            rechargeProgress -= whole;
            Ammo = Mathf.Min(maxAmmo, Ammo + whole);
        }
    }

    // ---------------- shooting ----------------

    bool CanFire(LaserRay gun, LaserTagFeedback.Hand hand)
    {
        NetworkPlayer me = NetworkPlayer.Local;
        if (me != null && !me.CanShoot) return false;
        if (Ammo <= 0)
        {
            // Empty: dry click + double tap, and pressing again restarts the recharge wait (like real games).
            lastEmptyClick = Time.time;
            LaserTagFeedback.PlayEmpty(gun.muzzle != null ? gun.muzzle.position : gun.transform.position);
            LaserTagFeedback.Haptic(hand, 0.15f, 0.04f);
            return false;
        }
        return true;
    }

    void OnFired(Vector3 from, Vector3 to, LaserTagFeedback.Hand hand)
    {
        Ammo = Mathf.Max(0, Ammo - 1);
        lastShotTime = Time.time;
        lastShotFrom = from;
        rechargeProgress = 0f;
        LaserTagFeedback.PlayShot(from);
        LaserTagFeedback.Haptic(hand, shotVibration, 0.06f);
        if (NetworkPlayer.Local != null) NetworkPlayer.Local.OwnerFired(from, to);
    }

    void OnHit(RaycastHit hit)
    {
        NetworkPlayer me = NetworkPlayer.Local;
        if (me == null || hit.collider == null) return;
        NetworkPlayer victim = hit.collider.GetComponentInParent<NetworkPlayer>();
        if (victim == null || victim == me || !victim.IsPresentInWorld) return;
        me.OwnerReportHit(victim, lastShotFrom, hit.point); // the host re-checks this shot
        LastVictim = victim;
        LastVictimTime = Time.time;
        LaserTagFeedback.PlayHitConfirm();
        if (PlayerHud.Instance != null) PlayerHud.Instance.ShowHitMarker();
    }

    /// <summary>Called when the local player loses health.</summary>
    public void OnDamaged()
    {
        LaserTagFeedback.PlayHurt();
        LaserTagFeedback.Haptic(LaserTagFeedback.Hand.Both, hitVibration, 0.18f);
    }

    public void RefillAmmo()
    {
        Ammo = maxAmmo;
        rechargeProgress = 0f;
    }

    // ---------------- pose ----------------

    /// <summary>Feet position on the floor (avatar root, or the camera dropped to the rig's floor).</summary>
    public Vector3 FeetPosition
    {
        get
        {
            if (avatar != null) return avatar.transform.position;
            Vector3 cam = playerCamera != null ? playerCamera.transform.position : transform.position;
            float floor = xrOrigin != null ? xrOrigin.transform.position.y : 0f;
            return new Vector3(cam.x, floor, cam.z);
        }
    }

    public bool TryCapturePose(out PoseState pose)
    {
        pose = default;
        if (playerCamera == null) return false;
        Transform cam = playerCamera.transform;

        if (avatar != null && avatar.head != null && avatar.leftHand != null && avatar.rightHand != null)
        {
            // The local avatar has already been fitted to the tracked head and guns by ScoutLocalRigBinding.
            pose.rootPosition = avatar.transform.position;
            pose.rootYaw = avatar.transform.eulerAngles.y;
            pose.headPosition = avatar.head.position;
            pose.headRotation = avatar.head.rotation;
            pose.leftPosition = avatar.leftHand.position;
            pose.leftRotation = avatar.leftHand.rotation;
            pose.rightPosition = avatar.rightHand.position;
            pose.rightRotation = avatar.rightHand.rotation;
            return true;
        }

        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        pose.rootPosition = FeetPosition;
        pose.rootYaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward).eulerAngles.y : 0f;
        pose.headPosition = cam.position;
        pose.headRotation = cam.rotation;
        Transform l = leftController != null ? leftController : cam;
        Transform r = rightController != null ? rightController : cam;
        pose.leftPosition = l.position; pose.leftRotation = l.rotation;
        pose.rightPosition = r.position; pose.rightRotation = r.rotation;
        return true;
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
