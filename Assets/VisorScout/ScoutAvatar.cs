using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

// Visuals only. A local binding or the team's AvatarPoseDriver owns tracked poses.
[DefaultExecutionOrder(100)]
public class ScoutAvatar : MonoBehaviour
{
    public Transform head, leftHand, rightHand;
    public Transform leftUpperArm, leftForearm, rightUpperArm, rightForearm;
    [Tooltip("Optional waist attachment. Gun local +Z points down the holster; local +Y faces forward.")]
    public Transform waistGunRest;
    [Header("Body measurements (set by the prefab builder)")]
    [Tooltip("Authored head-pivot height in metres. Zero uses the original Scout proportions for older prefabs.")]
    [Min(0)] public float standingHeadHeight;
    public float StandingHeadHeight => standingHeadHeight > 0 ? standingHeadHeight : shoulderOffset.y * (1.65f / 1.40f);
    public Vector3 shoulderOffset = new Vector3(.245f, 1.40f, 0);
    [Min(.1f)] public float armSegmentLength = .34f;
    [Min(.01f)] public float upperArmWidth = .13f;
    [Min(.01f)] public float forearmWidth = .115f;
    [Header("Hand wrist attachments")]
    [Tooltip("Wrist position in the left hand's local space. Zero preserves legacy hand pivots. The arm ends here, while the hand pivot follows the gun grip.")]
    public Vector3 leftWristOffset;
    [Tooltip("Wrist position in the right hand's local space.")]
    public Vector3 rightWristOffset;
    public Vector3 LeftWristPosition => leftHand ? leftHand.TransformPoint(leftWristOffset) : transform.position;
    public Vector3 RightWristPosition => rightHand ? rightHand.TransformPoint(rightWristOffset) : transform.position;
    public bool LeftArmReachLimited { get; private set; }
    public bool RightArmReachLimited { get; private set; }
    [Header("First-person visibility")]
    [Tooltip("Hide arm segments from the local camera. Hands stay visible, including when parented beneath an arm. Outside cameras retain the full avatar.")]
    public bool hideArmsInFirstPerson = true;
    [Tooltip("Local camera only: hide the helmet and, optionally, arm segments. Hands, controller guns and torso stay visible. Leave empty on remote avatars.")]
    public Camera localCamera;
    private Renderer[] firstPersonHiddenRenderers;
    private bool[] previousVisibility;
    private bool hiding;
    private bool cachedHideArms;
    private readonly Transform[] cachedParts = new Transform[9];
    struct CameraFrame
    {
        public Camera camera;
        public bool previouslyHiding;
    }
    readonly List<CameraFrame> cameraFrames = new List<CameraFrame>(4);

    void OnEnable()
    {
        RefreshFirstPersonRenderers();
        Camera.onPreCull += BeforeCamera;
        Camera.onPostRender += AfterCamera;
        RenderPipelineManager.beginCameraRendering += BeginSRP;
        RenderPipelineManager.endCameraRendering += EndSRP;
    }
    void OnDisable()
    {
        RestoreVisibility();
        Camera.onPreCull -= BeforeCamera;
        Camera.onPostRender -= AfterCamera;
        RenderPipelineManager.beginCameraRendering -= BeginSRP;
        RenderPipelineManager.endCameraRendering -= EndSRP;
    }
    void BeginSRP(ScriptableRenderContext context, Camera camera) { BeforeCamera(camera); }
    void EndSRP(ScriptableRenderContext context, Camera camera) { AfterCamera(camera); }
    void BeforeCamera(Camera camera)
    {
        if (!localCamera || !camera) { RestoreVisibility(); return; }
        if (!CachedPartsMatch() || firstPersonHiddenRenderers == null) RefreshFirstPersonRenderers();
        if (cameraFrames.Count == 0)
        {
            for (int i = 0; i < firstPersonHiddenRenderers.Length; i++)
                previousVisibility[i] = firstPersonHiddenRenderers[i] && firstPersonHiddenRenderers[i].enabled;
        }
        cameraFrames.Add(new CameraFrame { camera = camera, previouslyHiding = hiding });
        // A nested observer/Scene camera must see the complete avatar while the
        // local camera is rendering. No permanent layer or renderer changes.
        SetFirstPersonHidden(camera == localCamera);
    }
    void AfterCamera(Camera camera)
    {
        if (cameraFrames.Count == 0) return;
        int last = cameraFrames.Count - 1;
        CameraFrame frame = cameraFrames[last];
        if (frame.camera != camera) { RestoreVisibility(); return; }
        cameraFrames.RemoveAt(last);
        SetFirstPersonHidden(frame.previouslyHiding);
    }
    // Keep the existing public refresh entry point for callers that replace a helmet.
    public void RefreshHeadRenderers()
    {
        RefreshFirstPersonRenderers();
    }
    public void RefreshFirstPersonRenderers()
    {
        RestoreVisibility();
        cachedParts[0] = head; cachedParts[1] = leftHand; cachedParts[2] = rightHand;
        cachedParts[3] = leftUpperArm; cachedParts[4] = leftForearm;
        cachedParts[5] = rightUpperArm; cachedParts[6] = rightForearm;
        cachedParts[7] = transform.Find("NeckSeal");
        cachedParts[8] = transform.Find("Collar");
        cachedHideArms = hideArmsInFirstPerson;
        var renderers = new HashSet<Renderer>();
        for (int i = 0; i < cachedParts.Length; i++)
        {
            // Hands are deliberately excluded from the hidden set. Filtering
            // descendants also protects hands nested beneath an arm segment.
            if (i == 1 || i == 2 || (i >= 3 && i <= 6 && !hideArmsInFirstPerson)) continue;
            Transform part = cachedParts[i];
            if (part)
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                    if (renderer.transform.IsChildOf(transform) &&
                        !(leftHand && renderer.transform.IsChildOf(leftHand)) &&
                        !(rightHand && renderer.transform.IsChildOf(rightHand))) renderers.Add(renderer);
        }
        firstPersonHiddenRenderers = new Renderer[renderers.Count];
        renderers.CopyTo(firstPersonHiddenRenderers);
        previousVisibility = new bool[firstPersonHiddenRenderers.Length];
    }
    bool CachedPartsMatch()
    {
        return cachedHideArms == hideArmsInFirstPerson && cachedParts[0] == head && cachedParts[1] == leftHand && cachedParts[2] == rightHand &&
            cachedParts[3] == leftUpperArm && cachedParts[4] == leftForearm &&
            cachedParts[5] == rightUpperArm && cachedParts[6] == rightForearm && cachedParts[7] == transform.Find("NeckSeal") && cachedParts[8] == transform.Find("Collar");
    }
    void SetFirstPersonHidden(bool hidden)
    {
        if (firstPersonHiddenRenderers != null)
            for (int i = 0; i < firstPersonHiddenRenderers.Length; i++)
                if (firstPersonHiddenRenderers[i]) firstPersonHiddenRenderers[i].enabled = !hidden && previousVisibility[i];
        hiding = hidden;
    }
    void RestoreVisibility()
    {
        if (hiding || cameraFrames.Count > 0) SetFirstPersonHidden(false);
        cameraFrames.Clear();
    }
    void LateUpdate()
    {
        UpdateArmPose();
    }
    // Also called after the binding's before-render pose update, avoiding a
    // one-frame gap between the tracked hands and the rendered arm segments.
    public void UpdateArmPose()
    {
        LeftArmReachLimited = Arm(-1, leftHand, leftUpperArm, leftForearm, leftWristOffset);
        RightArmReachLimited = Arm(1, rightHand, rightUpperArm, rightForearm, rightWristOffset);
    }
    bool Arm(float side, Transform hand, Transform upper, Transform lower, Vector3 wristOffset)
    {
        if (!hand || !upper || !lower) return false;
        Vector3 shoulder = transform.TransformPoint(new Vector3(side * shoulderOffset.x, shoulderOffset.y, shoulderOffset.z));
        Vector3 wrist = hand.TransformPoint(wristOffset);
        Vector3 delta = wrist - shoulder;
        float distance = delta.magnitude;
        Vector3 direction = distance < .0001f ? -transform.up : delta / distance;
        // The grip remains on the gun even for an impossible tracked target.
        // Limit the arm endpoint instead of stretching armor or moving a gun.
        float limitedDistance = Mathf.Clamp(distance, .02f, armSegmentLength * 1.998f);
        bool limited = Mathf.Abs(limitedDistance - distance) > .0001f;
        distance = limitedDistance; delta = direction * distance; wrist = shoulder + delta;
        Vector3 bend = Vector3.ProjectOnPlane(transform.right * side - transform.up, direction);
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(transform.forward, direction);
        // Equal fixed-length segments; elbow bends out/down.
        float segment = armSegmentLength;
        float elbowOffset = Mathf.Sqrt(Mathf.Max(0, segment * segment - distance * distance * .25f));
        Vector3 elbow = shoulder + delta * .5f + bend.normalized * elbowOffset;
        Segment(upper, shoulder, elbow, upperArmWidth);
        Segment(lower, elbow, wrist, forearmWidth);
        return limited;
    }
    static void Segment(Transform part, Vector3 a, Vector3 b, float width)
    {
        part.position = (a + b) * .5f;
        part.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        // Segment meshes span local Y -1..+1. Avatar and ancestors must have scale 1.
        part.localScale = new Vector3(width, Vector3.Distance(a, b) * .5f, width);
    }
}
