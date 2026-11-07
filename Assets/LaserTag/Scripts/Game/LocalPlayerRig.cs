using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// The player wearing THIS headset: XR rig, camera, local avatar and guns.
///
/// - Ammunition: <see cref="maxAmmo"/> shots shared by both guns. After <see cref="rechargeDelay"/> seconds
///   without shooting, shots refill at <see cref="rechargePerSecond"/> until full.
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
    [Min(0f)] public float rechargeDelay = 1.5f;
    [Tooltip("Shots refilled per second once recharging.")]
    [Min(0.1f)] public float rechargePerSecond = 12f;

    public int Ammo { get; private set; }
    public bool IsRecharging => Ammo < maxAmmo && Time.time - lastShotTime >= rechargeDelay;
    /// <summary>Seconds since the last "out of ammo" click; used by the HUD to blink.</summary>
    public float TimeSinceEmptyClick => Time.time - lastEmptyClick;

    float lastShotTime = -999f;
    float lastEmptyClick = -999f;
    float rechargeProgress;

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
        foreach (LaserRay gun in guns)
        {
            if (gun == null) continue;
            gun.canFire = CanFire;
            gun.onFired += OnFired;
            gun.onHit += OnHit;
        }
    }

    void OnDisable()
    {
        foreach (LaserRay gun in guns)
        {
            if (gun == null) continue;
            if (gun.canFire == (System.Func<bool>)CanFire) gun.canFire = null;
            gun.onFired -= OnFired;
            gun.onHit -= OnHit;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Ammo >= maxAmmo) { rechargeProgress = 0f; return; }
        if (Time.time - lastShotTime < rechargeDelay) return;
        rechargeProgress += rechargePerSecond * Time.deltaTime;
        int whole = Mathf.FloorToInt(rechargeProgress);
        if (whole > 0)
        {
            rechargeProgress -= whole;
            Ammo = Mathf.Min(maxAmmo, Ammo + whole);
        }
    }

    // ---------------- shooting ----------------

    bool CanFire()
    {
        NetworkPlayer me = NetworkPlayer.Local;
        if (me != null && !me.CanShoot) return false;
        if (Ammo <= 0)
        {
            lastEmptyClick = Time.time;
            return false;
        }
        return true;
    }

    void OnFired(Vector3 from, Vector3 to, bool hit)
    {
        Ammo = Mathf.Max(0, Ammo - 1);
        lastShotTime = Time.time;
        rechargeProgress = 0f;
        if (NetworkPlayer.Local != null) NetworkPlayer.Local.OwnerFired(from, to);
    }

    void OnHit(RaycastHit hit)
    {
        NetworkPlayer me = NetworkPlayer.Local;
        if (me == null || hit.collider == null) return;
        NetworkPlayer victim = hit.collider.GetComponentInParent<NetworkPlayer>();
        if (victim != null && victim != me) me.OwnerReportHit(victim);
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
