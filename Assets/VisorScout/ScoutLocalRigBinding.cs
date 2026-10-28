using UnityEngine;
using UnityEngine.Rendering;

// Standalone local-avatar adapter. No spawning, identity, device simulation,
// locomotion or networking. Disable when AvatarPoseDriver owns this avatar.
[DefaultExecutionOrder(50), DisallowMultipleComponent]
public class ScoutLocalRigBinding : MonoBehaviour
{
    public ScoutAvatar avatar;
    [Tooltip("The active XR Origin (VR) root. Its existing input stack owns the tracked poses.")]
    public Transform xrOrigin;
    public Camera trackedCamera;
    public Transform leftController, rightController;
    [Header("Avatar gun grips (both views)")]
    [Tooltip("Existing gun transforms, read only. Hands follow their current pose, including offsets from the controller. Leave empty to use the controller offsets below.")]
    public Transform leftGun, rightGun;
    public static readonly Vector3 DefaultLeftGripPosition = new Vector3(-.020f, -.10f, -.18f);
    public static readonly Vector3 DefaultRightGripPosition = new Vector3(.020f, -.10f, -.18f);
    public static readonly Vector3 DefaultGripRotation = new Vector3(110, 0, 0);
    [InspectorName("Left Grip Position (Gun Local)")]
    public Vector3 leftGunGripPosition = DefaultLeftGripPosition;
    [InspectorName("Right Grip Position (Gun Local)")]
    public Vector3 rightGunGripPosition = DefaultRightGripPosition;
    [InspectorName("Left Grip Rotation (Degrees)")]
    public Vector3 leftGunGripRotation = DefaultGripRotation;
    [InspectorName("Right Grip Rotation (Degrees)")]
    public Vector3 rightGunGripRotation = DefaultGripRotation;
    [Header("Tracked-pose offsets")]
    [Tooltip("Avatar-body offset in horizontal heading space; does not move the XR rig.")]
    public Vector3 bodyOffset = new Vector3(0, 0, -.06f);
    public Vector3 headPositionOffset, headRotationOffset;
    public Vector3 leftHandPositionOffset, leftHandRotationOffset;
    public Vector3 rightHandPositionOffset, rightHandRotationOffset;

    [Header("Standing-height calibration (visual avatar only)")]
    public bool calibrateStandingHeightOnStart = true;
    [Tooltip("Standing tracked eye height above the current floor, in metres. Zero uses the model's authored height until calibration. Stand upright and use Calibrate Standing Height in Play mode.")]
    [Min(0)] public float standingEyeHeight;
    [Tooltip("Minimum VISUAL head-pivot height above the floor. The actual leg lengths and knee limit can require a higher value. Never changes the XR camera.")]
    [Min(.5f)] public float minimumCrouchHeight = 1.35f;
    [Header("Visual neck limits relative to torso (degrees)")]
    [Range(0, 70)] public float maximumNeckPitchUp = 35;
    [Range(0, 70)] public float maximumNeckPitchDown = 50;
    [Range(0, 45)] public float maximumNeckRoll = 20;
    [Range(0, 90)] public float maximumNeckYaw = 70;
    [Tooltip("Small head translations relative to its neutral neck connection. Torso tracking still allows normal horizontal movement and bounded crouching.")]
    [Range(0, .12f)] public float maximumHeadHorizontalOffset = .075f;
    [Range(0, .06f)] public float maximumHeadVerticalOffset = .025f;

    public float CalibratedStandingHeight => standingEyeHeight > 0 ? standingEyeHeight : (avatar ? avatar.StandingHeadHeight : 1.65f);
    public float EffectiveMinimumCrouchHeight => avatar ? avatar.StandingHeadHeight - MaximumCrouchDrop() : minimumCrouchHeight;
    public bool VisualHeadLimited { get; private set; }
    bool calibrated;
    Quaternion lastHeading = Quaternion.identity;
    Transform neck;
    Vector3 neckRestPosition;
    Quaternion neckRestRotation;

    ScoutAvatar cameraOwner;
    Camera previousCamera, assignedCamera;

    public bool HasValidReferences => avatar && xrOrigin && trackedCamera && leftController && rightController &&
        avatar.head && avatar.leftHand && avatar.rightHand &&
        avatar.head.IsChildOf(avatar.transform) && avatar.leftHand.IsChildOf(avatar.transform) && avatar.rightHand.IsChildOf(avatar.transform) &&
        !trackedCamera.transform.IsChildOf(avatar.transform) &&
        !leftController.IsChildOf(avatar.transform) && !rightController.IsChildOf(avatar.transform) &&
        (!leftGun || !leftGun.IsChildOf(avatar.transform)) && (!rightGun || !rightGun.IsChildOf(avatar.transform));

    void OnEnable()
    {
        calibrated = false;
        if (!HasValidReferences)
            Debug.LogWarning("Assign the avatar, active XR Origin, Main Camera and both controllers on Scout Local Rig Binding. Tracking sources must be outside the avatar hierarchy.", this);
        Application.onBeforeRender += BeforeRender;
        Camera.onPreCull += BeforeCamera;
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
    }
    void OnDisable()
    {
        Application.onBeforeRender -= BeforeRender;
        Camera.onPreCull -= BeforeCamera;
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RestoreCameraOwner();
    }
    void LateUpdate() { ApplyPose(); }

    [BeforeRenderOrder(1000)]
    void BeforeRender()
    {
        ApplyPose();
        if (HasValidReferences) avatar.UpdateArmPose();
    }
    void BeginCameraRendering(ScriptableRenderContext context, Camera camera) { BeforeCamera(camera); }
    void BeforeCamera(Camera camera)
    {
        // Desktop simulation may have no active XR display/before-render event.
        // Refresh for the actual Game/observer camera too, after late gun motion.
        if (!isActiveAndEnabled || !HasValidReferences) return;
        ApplyPose();
        avatar.UpdateArmPose();
    }

    [ContextMenu("Apply Tracked Avatar Pose")]
    public void ApplyPose()
    {
        if (!HasValidReferences) return;
        if (cameraOwner != avatar)
        {
            RestoreCameraOwner();
            cameraOwner = avatar; previousCamera = avatar.localCamera;
        }
        assignedCamera = trackedCamera; avatar.localCamera = trackedCamera;

        // Read sources before moving any avatar object. These transforms belong
        // to the existing rig and remain entirely controlled by its input stack.
        Transform cameraTransform = trackedCamera.transform;
        Vector3 headPosition = cameraTransform.position;
        Quaternion headRotation = cameraTransform.rotation;
        Vector3 leftPosition = leftGun ? leftGun.TransformPoint(leftGunGripPosition) : leftController.TransformPoint(leftHandPositionOffset);
        Vector3 rightPosition = rightGun ? rightGun.TransformPoint(rightGunGripPosition) : rightController.TransformPoint(rightHandPositionOffset);
        Quaternion leftRotation = leftGun ? leftGun.rotation * Quaternion.Euler(leftGunGripRotation) : leftController.rotation * Quaternion.Euler(leftHandRotationOffset);
        Quaternion rightRotation = rightGun ? rightGun.rotation * Quaternion.Euler(rightGunGripRotation) : rightController.rotation * Quaternion.Euler(rightHandRotationOffset);

        // Horizontal heading is independent of height. At the vertical-look
        // singularity retain the previous heading instead of inventing a turn.
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (forward.sqrMagnitude > .0025f) lastHeading = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion heading = lastHeading;
        Vector3 bodyPosition = headPosition + heading * bodyOffset;
        bool hasLegs = avatar.TryGetComponent<ScoutProceduralLegs>(out var legs) && legs.IsConfigured;
        if (hasLegs) legs.SetTrackingOrigin(xrOrigin);
        float floor = hasLegs ? legs.PrepareGrounding(bodyPosition) : xrOrigin.position.y;
        // Give the existing simulator time to apply its startup camera offset.
        // Recalibration never runs on teleports or changes of floor elevation.
        float measuredHeight = headPosition.y - floor;
        if (Application.isPlaying && calibrateStandingHeightOnStart && !calibrated && Time.timeSinceLevelLoad > 1.25f && measuredHeight >= 1.1f && measuredHeight <= 2.4f)
            CalibrateStandingHeight();
        float drop = Mathf.Clamp(CalibratedStandingHeight - measuredHeight, 0, MaximumCrouchDrop());
        bodyPosition.y = Mathf.Clamp(floor - drop + bodyOffset.y, floor - MaximumCrouchDrop(), floor);
        avatar.transform.SetPositionAndRotation(bodyPosition, heading);

        Quaternion localHead = Quaternion.Inverse(heading) * headRotation * Quaternion.Euler(headRotationOffset);
        Vector3 f = localHead * Vector3.forward;
        float pitch = Mathf.Atan2(-f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;
        float yaw = new Vector2(f.x, f.z).sqrMagnitude > .0001f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0;
        Quaternion noRoll = Quaternion.Euler(pitch, yaw, 0);
        float roll = Vector3.SignedAngle(noRoll * Vector3.up, localHead * Vector3.up, f);
        Quaternion limitedRotation = Quaternion.Euler(Mathf.Clamp(pitch, -maximumNeckPitchUp, maximumNeckPitchDown), Mathf.Clamp(yaw, -maximumNeckYaw, maximumNeckYaw), Mathf.Clamp(roll, -maximumNeckRoll, maximumNeckRoll));
        Vector3 desiredHead = headPosition + headRotation * headPositionOffset;
        desiredHead.y += avatar.StandingHeadHeight - CalibratedStandingHeight;
        Vector3 neutral = bodyPosition + Vector3.up * avatar.StandingHeadHeight;
        Vector3 headDelta = desiredHead - neutral;
        Vector3 limitedDelta = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(headDelta, Vector3.up), maximumHeadHorizontalOffset) +
            Vector3.up * Mathf.Clamp(headDelta.y, -maximumHeadVerticalOffset, maximumHeadVerticalOffset);
        VisualHeadLimited = (headDelta - limitedDelta).sqrMagnitude > .000001f || Quaternion.Angle(localHead, limitedRotation) > .1f;
        // The helmet pivot is near its centre. Rotate it about the authored
        // neck connection so pitching does not tear the neck ribs off the collar.
        float neckLength = avatar.StandingHeadHeight * (.135f / 1.65f);
        Vector3 neckArc = heading * (limitedRotation * Vector3.up * neckLength) - Vector3.up * neckLength;
        avatar.head.SetPositionAndRotation(neutral + limitedDelta + neckArc, heading * limitedRotation);
        ApplyNeck(limitedRotation, heading, limitedDelta);
        avatar.leftHand.SetPositionAndRotation(leftPosition, leftRotation);
        avatar.rightHand.SetPositionAndRotation(rightPosition, rightRotation);
        // Optional separate-mesh leg visuals use the origin only as a read-only
        // floor reference. Reapply after late root tracking, without advancing
        // their planted-foot state more than once per frame.
        if (hasLegs) legs.RefreshPose();
    }

    float MaximumCrouchDrop()
    {
        if (!avatar) return 0;
        float drop = Mathf.Max(0, avatar.StandingHeadHeight - minimumCrouchHeight);
        if (avatar.TryGetComponent<ScoutProceduralLegs>(out var legs) && legs.IsConfigured)
            return legs.IsActive ? Mathf.Min(drop, legs.MaximumCrouchDrop) : 0;
        return 0; // No known leg measurements: upright stance is safer than collapsing.
    }

    [ContextMenu("Calibrate Standing Height (Stand Upright)")]
    public void CalibrateStandingHeight()
    {
        if (!HasValidReferences) return;
        float floor = xrOrigin.position.y;
        if (avatar.TryGetComponent<ScoutProceduralLegs>(out var legs) && legs.IsConfigured)
        {
            legs.SetTrackingOrigin(xrOrigin);
            floor = legs.PrepareGrounding(trackedCamera.transform.position);
        }
        float height = trackedCamera.transform.position.y - floor;
        if (height < 1.1f || height > 2.4f) return;
        standingEyeHeight = height; calibrated = true;
    }

    void ApplyNeck(Quaternion localHead, Quaternion heading, Vector3 headDelta)
    {
        // Setup/menu pose previews must not bake an animated neck into a scene.
        if (!Application.isPlaying) return;
        if (!neck)
        {
            neck = avatar.transform.Find("NeckSeal");
            if (!neck) return;
            neckRestPosition = neck.localPosition; neckRestRotation = neck.localRotation;
        }
        // The authored neck mesh has baked Y coordinates. Rotate around its
        // actual collar connection, not the object's origin at the feet.
        Vector3 pivot = Vector3.up * (avatar.StandingHeadHeight * (1.49f / 1.65f));
        Quaternion rotation = Quaternion.Slerp(Quaternion.identity, localHead, .35f);
        neck.localPosition = pivot + rotation * (neckRestPosition - pivot) + Quaternion.Inverse(heading) * headDelta * .35f;
        neck.localRotation = rotation * neckRestRotation;
    }

    void RestoreCameraOwner()
    {
        if (neck) neck.SetLocalPositionAndRotation(neckRestPosition, neckRestRotation);
        neck = null;
        if (cameraOwner && cameraOwner.localCamera == assignedCamera) cameraOwner.localCamera = previousCamera;
        cameraOwner = null; previousCamera = assignedCamera = null;
    }
}
