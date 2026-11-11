using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Minimal owner-controlled desktop avatar for Relay smoke testing.
// The owner publishes body and head/hand poses; other peers render them.
// The server does not yet validate movement: do NOT use for authoritative hit/damage logic.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(NetworkObject), typeof(AvatarPoseDriver), typeof(ScoutAvatar))]
public sealed class RelayAvatarPlayer : NetworkBehaviour
{
    [Header("Desktop test movement")]
    public float speed = 2.2f;
    public float sprintMultiplier = 1.6f;
    public float turnSpeed = 100f;
    public float poseSendHz = 15f;

    private readonly NetworkVariable<Vector3> netBodyPosition =
        new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> netBodyRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector3> netHeadPosition =
        new NetworkVariable<Vector3>(new Vector3(0, 1.72f, 0), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> netHeadRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector3> netLeftPosition =
        new NetworkVariable<Vector3>(new Vector3(-.30f, 1.3f, .28f), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> netLeftRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector3> netRightPosition =
        new NetworkVariable<Vector3>(new Vector3(.30f, 1.3f, .28f), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Quaternion> netRightRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private AvatarPoseDriver driver;
    private ScoutAvatar avatar;
    private float nextSend;
    private float lookPitch;
    private float lookYaw;
    private float handRaise;
    private Vector3 visibleHead, visibleLeft, visibleRight;
    private Quaternion visibleHeadRot, visibleLeftRot, visibleRightRot;
    private bool firstRemoteFrame = true;

    public bool UseXRTracking { get; private set; }

    public void SetXRTracking(bool enabled)
    {
        if (!IsSpawned || !IsOwner) return;
        UseXRTracking = enabled;
        nextSend = 0f;
    }

    public override void OnNetworkSpawn()
    {
        UseXRTracking = false;
        driver = GetComponent<AvatarPoseDriver>();
        avatar = GetComponent<ScoutAvatar>();
        driver.SetPoseTargets(avatar.head, avatar.leftHand, avatar.rightHand);
        avatar.localCamera = null; // third-person desktop test; no VR camera is embedded here
        foreach (var binding in GetComponentsInChildren<ScoutLocalRigBinding>(true))
            binding.enabled = false;

        name = "NetworkPlayer_" + OwnerClientId + (IsOwner ? "_YOU" : "_REMOTE");
        if (IsOwner)
        {
            // Temporary deterministic separation only; replace with approved spawn locations later.
            transform.position = new Vector3((OwnerClientId % 5) * 2.5f - 4f, 0f,
                (OwnerClientId / 5) * 3f);
            transform.rotation = Quaternion.identity;
            var camera = FindFirstObjectByType<RelayThirdPersonCamera>();
            if (camera != null) camera.Follow(transform);
            PublishPose();
            Debug.Log("[RelayAvatar] Local player spawned: " + OwnerClientId);
        }
        else
        {
            Debug.Log("[RelayAvatar] Remote player spawned: " + OwnerClientId);
        }
    }

    private void Update()
    {
        if (!IsSpawned || driver == null) return;
        if (IsOwner)
        {
            if (!UseXRTracking)
            {
                ReadDesktopControls();
                ApplyPose(1f);
                if (Time.time >= nextSend)
                {
                    nextSend = Time.time + 1f / Mathf.Max(5f, poseSendHz);
                    PublishPose();
                }
            }
        }
        else
        {
            float blend = 1f - Mathf.Exp(-13f * Time.deltaTime);
            if (firstRemoteFrame)
            {
                transform.SetPositionAndRotation(netBodyPosition.Value, netBodyRotation.Value);
                firstRemoteFrame = false;
                blend = 1f;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, netBodyPosition.Value, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, netBodyRotation.Value, blend);
            }
            ApplyPose(blend);
        }
    }

    private void LateUpdate()
    {
        // ScoutLocalRigBinding runs first and applies the real tracked pose.
        // Publish its result, not the synthetic keyboard pose.
        if (!IsSpawned || !IsOwner || !UseXRTracking || avatar == null)
            return;

        if (Time.time < nextSend) return;
        nextSend = Time.time + 1f / Mathf.Max(5f, poseSendHz);

        netBodyPosition.Value = transform.position;
        netBodyRotation.Value = transform.rotation;

        netHeadPosition.Value = transform.InverseTransformPoint(avatar.head.position);
        netHeadRotation.Value = Quaternion.Inverse(transform.rotation) * avatar.head.rotation;

        netLeftPosition.Value = transform.InverseTransformPoint(avatar.leftHand.position);
        netLeftRotation.Value = Quaternion.Inverse(transform.rotation) * avatar.leftHand.rotation;

        netRightPosition.Value = transform.InverseTransformPoint(avatar.rightHand.position);
        netRightRotation.Value = Quaternion.Inverse(transform.rotation) * avatar.rightHand.rotation;
    }

    private void ReadDesktopControls()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;
        float dt = Time.deltaTime;
        float forward = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        float sideways = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float yaw = (k.eKey.isPressed ? 1f : 0f) - (k.qKey.isPressed ? 1f : 0f);
        Vector3 move = Vector3.ClampMagnitude(new Vector3(sideways, 0, forward), 1f);
        float moveSpeed = speed * (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed ? sprintMultiplier : 1f);
        transform.position += transform.rotation * move * moveSpeed * dt;
        transform.rotation *= Quaternion.Euler(0, yaw * turnSpeed * dt, 0);
        lookPitch = Mathf.Clamp(lookPitch +
            ((k.upArrowKey.isPressed ? 1f : 0f) - (k.downArrowKey.isPressed ? 1f : 0f)) * 65f * dt,
            -40f, 40f);
        lookYaw = Mathf.Clamp(lookYaw +
            ((k.rightArrowKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed ? 1f : 0f)) * 75f * dt,
            -55f, 55f);
        handRaise = Mathf.MoveTowards(handRaise, k.spaceKey.isPressed ? 1f : 0f, dt * 4f);
    }

    private void PublishPose()
    {
        if (!IsSpawned || !IsOwner) return;
        float height = avatar ? avatar.StandingHeadHeight : 1.72f;
        netBodyPosition.Value = transform.position;
        netBodyRotation.Value = transform.rotation;
        netHeadPosition.Value = new Vector3(0f, height, 0f);
        netHeadRotation.Value = Quaternion.Euler(-lookPitch, lookYaw, 0);
        netLeftPosition.Value = Vector3.Lerp(new Vector3(-.30f, 1.28f, .26f),
            new Vector3(-.27f, 1.44f, .54f), handRaise);
        netRightPosition.Value = Vector3.Lerp(new Vector3(.30f, 1.28f, .26f),
            new Vector3(.27f, 1.44f, .54f), handRaise);
        netLeftRotation.Value = Quaternion.Euler(-handRaise * 18f, 0, 0);
        netRightRotation.Value = Quaternion.Euler(-handRaise * 18f, 0, 0);
    }

    private void ApplyPose(float blend)
    {
        Vector3 targetHead;
        Vector3 targetLeft;
        Vector3 targetRight;
        Quaternion rotHead;
        Quaternion rotLeft;
        Quaternion rotRight;

        if (IsOwner)
        {
            // Avoid waiting for the next network tick to display local movement.
            float height = avatar ? avatar.StandingHeadHeight : 1.72f;
            targetHead = new Vector3(0, height, 0);
            targetLeft = Vector3.Lerp(new Vector3(-.30f, 1.28f, .26f), new Vector3(-.27f, 1.44f, .54f), handRaise);
            targetRight = Vector3.Lerp(new Vector3(.30f, 1.28f, .26f), new Vector3(.27f, 1.44f, .54f), handRaise);
            rotHead = Quaternion.Euler(-lookPitch, lookYaw, 0);
            rotLeft = Quaternion.Euler(-handRaise * 18f, 0, 0);
            rotRight = rotLeft;
        }
        else
        {
            targetHead = netHeadPosition.Value;
            targetLeft = netLeftPosition.Value;
            targetRight = netRightPosition.Value;
            rotHead = netHeadRotation.Value;
            rotLeft = netLeftRotation.Value;
            rotRight = netRightRotation.Value;
        }

        visibleHead = Vector3.Lerp(visibleHead, targetHead, blend);
        visibleLeft = Vector3.Lerp(visibleLeft, targetLeft, blend);
        visibleRight = Vector3.Lerp(visibleRight, targetRight, blend);
        visibleHeadRot = Quaternion.Slerp(visibleHeadRot, rotHead, blend);
        visibleLeftRot = Quaternion.Slerp(visibleLeftRot, rotLeft, blend);
        visibleRightRot = Quaternion.Slerp(visibleRightRot, rotRight, blend);

        driver.SetHeadPose(transform.TransformPoint(visibleHead), transform.rotation * visibleHeadRot);
        driver.SetLeftHandPose(transform.TransformPoint(visibleLeft), transform.rotation * visibleLeftRot);
        driver.SetRightHandPose(transform.TransformPoint(visibleRight), transform.rotation * visibleRightRot);
    }
}
