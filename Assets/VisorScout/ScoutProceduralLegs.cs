using System;
using UnityEngine;

// Separate-mesh visual IK only. Never writes tracking, locomotion or collisions.
[DefaultExecutionOrder(150), DisallowMultipleComponent]
public sealed class ScoutProceduralLegs : MonoBehaviour
{
    [Serializable] public sealed class Leg
    {
        public Transform thigh, shin, knee, boot;
        public Vector3 hipLocal;
    }
    [Header("Visual motion")]
    [InspectorName("Enable Procedural Legs")] public bool enableProceduralLegs = true;
    public ScoutAvatar avatar;
    public Transform pelvis;
    public Leg left = new Leg(), right = new Leg();
    [Header("Authored measurements (metres)")]
    [Min(.01f)] public float thighLength = .43f, shinLength = .35f, ankleHeight = .145f;
    [Min(0)] public float standingDrop = .012f;
    [Header("Restrained posture")]
    [Range(0, .015f)] public float idleShift = .001f;
    [Min(1)] public float idlePeriod = 4.8f;
    [Range(75, 125)] public float maximumKneeBend = 110;
    public bool restrainWhileAiming = true;
    [Header("Small corrective steps")]
    [Range(15, 70)] public float turnStepAngle = 45;
    [Range(.06f, .25f)] public float stepDistance = .18f;
    [Range(.15f, .5f)] public float stepDuration = .28f;
    [Range(.002f, .05f)] public float stepLift = .006f;
    [Header("Ground placement (read only)")]
    [Tooltip("Optional floor-height reference. The local binding supplies XR Origin when empty; this transform is never moved.")]
    public Transform groundReference;
    public LayerMask groundLayers = ~0;
    [Tooltip("Floor search above the read-only reference. Use walkable floor layers to avoid treating cover as a floor. No camera pose is changed.")]
    [Min(.2f)] public float floorSearchAboveReference = 2;
    [Min(.5f)] public float floorSearchBelowReference = 4;
    [Range(0, .25f)] public float maximumGroundStep = .15f;
    [Range(0, 25)] public float maximumSlope = 12;
    public Vector3 soleExtents = new Vector3(.067f, .18f, .085f); // half width, toe, heel
    [Header("Discontinuities")]
    [Min(.3f)] public float teleportDistance = .65f;
    // Retained for older prefab serialization. Height changes alone are not
    // teleports: they can be crouches; grounding/reference changes identify lifts.
    [HideInInspector] public float verticalResetDistance = .45f;
    [Range(0, .015f)] public float settlingDepth = .003f;
    [Range(.2f, .7f)] public float settlingDuration = .38f;

    sealed class Foot
    {
        public Vector3 planted, from, to, current;
        public Quaternion rotation, fromRotation, toRotation;
        public float progress;
        public bool stepping;
    }
    struct RestPose { public Transform part; public Vector3 position, scale; public Quaternion rotation; }
    readonly Foot leftFoot = new Foot(), rightFoot = new Foot();
    readonly RaycastHit[] hits = new RaycastHit[32];
    RestPose[] rest;
    Transform trackingOrigin;
    bool initialized, restored = true, nextLeft = true;
    int advancedFrame = -1;
    Vector3 lastRoot;
    float floorY, clock, settleAge = 1, cooldown;
    bool floorInitialized, resetPending;
    Vector3 lastGroundPoint, lastReferencePosition;

    // Derived from this prefab's actual unequal segment lengths and knee limit.
    // Leave a small margin for idle sway/settling instead of reaching a hard stop.
    public float MaximumCrouchDrop
    {
        get
        {
            if (!IsConfigured) return 0;
            float shortest = Mathf.Sqrt(thighLength * thighLength + shinLength * shinLength +
                2 * thighLength * shinLength * Mathf.Cos(maximumKneeBend * Mathf.Deg2Rad));
            return Mathf.Max(0, Mathf.Min(left.hipLocal.y, right.hipLocal.y) - ankleHeight - shortest - standingDrop - settlingDepth - .01f);
        }
    }
    public float CurrentFloorHeight => floorY;

    public bool IsConfigured => avatar && avatar.transform == transform && Owned(pelvis) && Valid(left) && Valid(right);
    public bool IsActive => isActiveAndEnabled && enableProceduralLegs && IsConfigured;
    public Vector3 LeftFootTarget => leftFoot.current;
    public Vector3 RightFootTarget => rightFoot.current;
    public bool LeftFootStepping => leftFoot.stepping;
    public bool RightFootStepping => rightFoot.stepping;
    bool Owned(Transform part) => part && part != transform && part.IsChildOf(transform);
    bool Valid(Leg leg) => leg != null && Owned(leg.thigh) && Owned(leg.shin) && Owned(leg.knee) && Owned(leg.boot);

    void OnEnable() { initialized = false; floorInitialized = false; CaptureRest(); }
    void OnDisable() { RestoreRest(); }
    void LateUpdate() { RefreshPose(); }
    public void SetTrackingOrigin(Transform origin) { trackingOrigin = origin; }

    // Called before binding height limits and again after late tracking. Floor
    // height comes from colliders/reference, never the camera's look or height.
    public float PrepareGrounding(Vector3 point)
    {
        Transform reference = groundReference ? groundReference : trackingOrigin;
        Vector3 referencePosition = reference ? reference.position : (floorInitialized ? lastReferencePosition : transform.position);
        float referenceY = referencePosition.y;
        float measuredFloor = ProbeFloor(point, referenceY);
        bool jump = floorInitialized && (Vector3.ProjectOnPlane(point - lastGroundPoint, Vector3.up).magnitude > teleportDistance ||
            Vector3.Distance(referencePosition, lastReferencePosition) > teleportDistance || Mathf.Abs(referencePosition.y - lastReferencePosition.y) > .02f ||
            Mathf.Abs(measuredFloor - floorY) > maximumGroundStep);
        if (jump) resetPending = true;
        floorY = measuredFloor; lastGroundPoint = point; lastReferencePosition = referencePosition; floorInitialized = true;
        return floorY;
    }

    float ProbeFloor(Vector3 point, float referenceY)
    {
        point.y = referenceY + floorSearchAboveReference;
        int count = Physics.RaycastNonAlloc(point, Vector3.down, hits, floorSearchAboveReference + floorSearchBelowReference, groundLayers, QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            if (ValidGround(hits[i])) highest = Mathf.Max(highest, hits[i].point.y);
        return float.IsNegativeInfinity(highest) ? referenceY : highest;
    }

    bool ValidGround(RaycastHit hit)
    {
        Transform target = hit.collider.transform;
        Transform excluded = trackingOrigin ? trackingOrigin : groundReference;
        return !target.IsChildOf(transform) && !(excluded && target.IsChildOf(excluded)) && hit.normal.y >= Mathf.Cos(maximumSlope * Mathf.Deg2Rad);
    }

    void CaptureRest()
    {
        if (!IsConfigured || rest != null) return;
        Transform[] parts = { pelvis, left.thigh, left.shin, left.knee, left.boot, right.thigh, right.shin, right.knee, right.boot };
        rest = new RestPose[parts.Length];
        for (int i = 0; i < parts.Length; i++) rest[i] = new RestPose { part = parts[i], position = parts[i].localPosition, rotation = parts[i].localRotation, scale = parts[i].localScale };
    }
    void RestoreRest()
    {
        if (rest != null && !restored)
            foreach (RestPose pose in rest)
                if (pose.part) { pose.part.SetLocalPositionAndRotation(pose.position, pose.rotation); pose.part.localScale = pose.scale; }
        restored = true; initialized = false;
    }

    [ContextMenu("Reset Planted Feet")]
    public void ResetFootTargets() { initialized = false; if (Application.isPlaying) RefreshPose(); }

    // Safe to call repeatedly for late tracking and multiple cameras. Time and
    // step progress advance once per frame; discontinuities reset immediately.
    public void RefreshPose()
    {
        if (!Application.isPlaying) return;
        if (!IsActive) { RestoreRest(); return; }
        CaptureRest(); restored = false;
        Vector3 root = transform.position;
        PrepareGrounding(root);
        if (!initialized) ResetPlants(false);
        Vector3 delta = root - lastRoot;
        if (resetPending || Vector3.ProjectOnPlane(delta, Vector3.up).magnitude > teleportDistance ||
            Vector3.ProjectOnPlane(root - (leftFoot.current + rightFoot.current) * .5f, Vector3.up).magnitude > .5f)
        {
            ResetPlants(true); resetPending = false;
        }
        lastRoot = root;
        if (advancedFrame != Time.frameCount)
        {
            advancedFrame = Time.frameCount;
            float dt = Mathf.Clamp(Time.deltaTime, 0, .05f);
            clock += dt; settleAge += dt; cooldown = Mathf.Max(0, cooldown - dt);
            AdvanceFoot(leftFoot, dt); AdvanceFoot(rightFoot, dt);
            if (!leftFoot.stepping && !rightFoot.stepping && cooldown == 0) MaybeStep();
        }
        ApplyPose();
    }
    Quaternion Heading => Quaternion.Euler(0, transform.eulerAngles.y, 0);
    bool Aiming
    {
        get
        {
            if (!restrainWhileAiming) return false;
            return HandAiming(avatar.leftHand) || HandAiming(avatar.rightHand);
        }
    }
    bool HandAiming(Transform hand)
    {
        if (!hand) return false;
        Vector3 local = transform.InverseTransformPoint(hand.position);
        return local.z > .15f && local.y > avatar.shoulderOffset.y - .24f;
    }
    Vector3 DesiredFoot(Leg leg)
    {
        Vector3 point = transform.TransformPoint(new Vector3(leg.hipLocal.x, 0, leg.hipLocal.z));
        point.y = GroundHeight(point, Heading);
        return point;
    }
    void ResetPlants(bool settle)
    {
        ResetFoot(leftFoot, DesiredFoot(left)); ResetFoot(rightFoot, DesiredFoot(right));
        lastRoot = transform.position; initialized = true; cooldown = .12f;
        settleAge = settle ? 0 : settlingDuration;
    }
    void ResetFoot(Foot foot, Vector3 position)
    {
        foot.planted = foot.from = foot.to = foot.current = position;
        foot.rotation = foot.fromRotation = foot.toRotation = Heading;
        foot.stepping = false; foot.progress = 0;
    }
    float Error(Foot foot, Vector3 desired)
    {
        float distance = Vector3.ProjectOnPlane(desired - foot.planted, Vector3.up).magnitude / Mathf.Max(.01f, stepDistance);
        float angle = Quaternion.Angle(foot.rotation, Heading) / Mathf.Max(1, turnStepAngle);
        return Mathf.Max(distance, angle);
    }
    void MaybeStep()
    {
        Vector3 l = DesiredFoot(left), r = DesiredFoot(right);
        float le = Error(leftFoot, l), re = Error(rightFoot, r);
        if (Mathf.Max(le, re) < 1) return;
        bool chooseLeft = Mathf.Abs(le - re) < .15f ? nextLeft : le > re;
        Foot foot = chooseLeft ? leftFoot : rightFoot;
        foot.from = foot.current; foot.to = chooseLeft ? l : r;
        foot.fromRotation = foot.rotation; foot.toRotation = Heading;
        foot.progress = 0; foot.stepping = true; nextLeft = !chooseLeft;
    }
    void AdvanceFoot(Foot foot, float dt)
    {
        if (!foot.stepping) { foot.current = foot.planted; return; }
        foot.progress = Mathf.Min(1, foot.progress + dt / (stepDuration * (Aiming ? 1.15f : 1)));
        float t = foot.progress, eased = t * t * t * (10 + t * (-15 + 6 * t));
        foot.current = Vector3.Lerp(foot.from, foot.to, eased);
        foot.rotation = Quaternion.Slerp(foot.fromRotation, foot.toRotation, eased);
        float lift = Mathf.Sin(Mathf.PI * t); lift *= lift * stepLift * (Aiming ? .65f : 1);
        foot.current.y = Mathf.Max(foot.current.y + lift, GroundHeight(foot.current, foot.rotation));
        if (t == 1) { foot.planted = foot.current = foot.to; foot.rotation = foot.toRotation; foot.stepping = false; cooldown = .08f; }
    }
    float GroundHeight(Vector3 point, Quaternion rotation)
    {
        float highest = float.NegativeInfinity;
        for (int sample = 0; sample < 5; sample++)
        {
            Vector3 offset = sample == 0 ? Vector3.zero : new Vector3(sample % 2 == 0 ? soleExtents.x : -soleExtents.x, 0, sample <= 2 ? soleExtents.y : -soleExtents.z);
            Vector3 origin = point + rotation * offset; origin.y = floorY + maximumGroundStep + .1f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, maximumGroundStep * 2 + .5f, groundLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!ValidGround(hit)) continue;
                if (Mathf.Abs(hit.point.y - floorY) > maximumGroundStep + .001f) continue;
                highest = Mathf.Max(highest, hit.point.y);
            }
        }
        // No collider/ground layer information: use the supplied origin's floor
        // plane. This is intentionally not a navigation or collision controller.
        return float.IsNegativeInfinity(highest) ? floorY : highest;
    }
    void ApplyPose()
    {
        float motion = Aiming ? .35f : 1;
        float sway = Mathf.Sin(clock * Mathf.PI * 2 / idlePeriod) * idleShift * motion;
        float settle = settleAge < settlingDuration ? Mathf.Pow(Mathf.Sin(Mathf.PI * settleAge / settlingDuration), 2) * settlingDepth * motion : 0;
        Vector3 offset = new Vector3(sway, -standingDrop - settle, 0);
        Vector3 lAnkle = leftFoot.current + Vector3.up * ankleHeight, rAnkle = rightFoot.current + Vector3.up * ankleHeight;
        float reach = (thighLength + shinLength) * .98f;
        // A rapid late horizontal update can make old plants unreachable. Reset
        // visuals locally rather than ever stretching a segment toward them.
        if (Vector3.ProjectOnPlane(transform.TransformPoint(left.hipLocal + offset) - lAnkle, Vector3.up).magnitude > reach ||
            Vector3.ProjectOnPlane(transform.TransformPoint(right.hipLocal + offset) - rAnkle, Vector3.up).magnitude > reach)
        {
            ResetPlants(true);
            lAnkle = leftFoot.current + Vector3.up * ankleHeight; rAnkle = rightFoot.current + Vector3.up * ankleHeight;
        }
        // A common pelvis correction enforces both legs' reach envelopes. It
        // changes only pelvis/leg visuals and bounds very deep crouching.
        float minimum = float.NegativeInfinity, maximum = float.PositiveInfinity;
        ConstrainPelvis(left, lAnkle, offset, ref minimum, ref maximum);
        ConstrainPelvis(right, rAnkle, offset, ref minimum, ref maximum);
        float correction = minimum <= maximum ? Mathf.Clamp(0, minimum, maximum) : (minimum + maximum) * .5f;
        offset.y += correction;
        pelvis.localPosition = offset;
        Solve(left, lAnkle, leftFoot.rotation, offset); Solve(right, rAnkle, rightFoot.rotation, offset);
    }
    void ConstrainPelvis(Leg leg, Vector3 ankle, Vector3 offset, ref float minimum, ref float maximum)
    {
        Vector3 hip = transform.TransformPoint(leg.hipLocal + offset);
        float horizontal = Vector3.ProjectOnPlane(hip - ankle, Vector3.up).sqrMagnitude;
        float longest = (thighLength + shinLength) * .999f;
        float shortestSquared = thighLength * thighLength + shinLength * shinLength + 2 * thighLength * shinLength * Mathf.Cos(maximumKneeBend * Mathf.Deg2Rad);
        float low = Mathf.Sqrt(Mathf.Max(0, shortestSquared - horizontal));
        float high = Mathf.Sqrt(Mathf.Max(.0001f, longest * longest - horizontal));
        minimum = Mathf.Max(minimum, ankle.y + low - hip.y);
        maximum = Mathf.Min(maximum, ankle.y + high - hip.y);
    }
    void Solve(Leg leg, Vector3 ankle, Quaternion footRotation, Vector3 pelvisOffset)
    {
        Vector3 hip = transform.TransformPoint(leg.hipLocal + pelvisOffset), delta = ankle - hip;
        float distance = Mathf.Max(.0001f, delta.magnitude);
        Vector3 direction = delta / distance;
        float along = (thighLength * thighLength - shinLength * shinLength + distance * distance) / (2 * distance);
        float height = Mathf.Sqrt(Mathf.Max(0, thighLength * thighLength - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(transform.forward, direction).normalized;
        if (bend.sqrMagnitude < .5f) bend = transform.forward;
        Vector3 knee = hip + direction * along + bend * height;
        Quaternion upper = SegmentRotation(hip, knee), lower = SegmentRotation(knee, ankle);
        leg.thigh.SetPositionAndRotation(hip, upper); leg.shin.SetPositionAndRotation(knee, lower);
        leg.knee.SetPositionAndRotation(knee, Quaternion.Slerp(upper, lower, .5f));
        leg.boot.SetPositionAndRotation(ankle, footRotation);
    }
    Quaternion SegmentRotation(Vector3 top, Vector3 bottom)
    {
        Vector3 up = (top - bottom).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (forward.sqrMagnitude < .1f) forward = Vector3.ProjectOnPlane(transform.right, up).normalized;
        return Quaternion.LookRotation(forward, up);
    }
}
