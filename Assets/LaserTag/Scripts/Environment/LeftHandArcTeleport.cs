using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// Teleport-only movement for the active XR Origin.
/// Left thumbstick UP: show aiming arc.
/// Release thumbstick: request a server-approved landing on a safe solid surface.
/// Editor fallback: hold and release F8.
/// Does not use or move the inactive XR Hands sample rig.
/// </summary>
public sealed class LeftHandArcTeleport : MonoBehaviour
{
    [Header("Active Gameplay Rig")]
    public XROrigin xrOrigin;
    public Camera playerCamera;
    public Transform leftController;

    [Header("Input")]
    [Range(0.3f, 0.95f)]
    public float activateThreshold = 0.65f;

    [Range(0.05f, 0.5f)]
    public float releaseThreshold = 0.25f;

    [Header("Aiming Arc")]
    public float launchSpeed = 8f;
    public float upwardBoost = 1.2f;
    public float gravity = 9.81f;
    public int segments = 48;
    public float stepTime = 0.065f;
    public float maxRange = 18f;

    [Header("Safe Destinations")]
    [Range(0f, 80f)]
    public float maxFloorSlope = 35f;

    public float bodyRadius = 0.25f;
    public float bodyHeight = 1.7f;
    public float maxVerticalChange = 4f;

    InputAction leftStick;
    LineRenderer arc;
    Material arcMaterial;
    Material markerMaterial;
    GameObject marker;

    bool aiming;
    bool validDestination;
    Vector3 destination;
    Vector2Control aimingStick;
    bool needsNeutral = true;
    Vector3 aimOrigin;
    RelayAvatarPlayer networkMovement;
#if UNITY_EDITOR
    bool editorAim;
#endif

    readonly RaycastHit[] hits = new RaycastHit[48];
    readonly List<Transform> ignoreRoots = new List<Transform>();

    void Awake()
    {
        if (!xrOrigin || !playerCamera || !leftController ||
            !playerCamera.transform.IsChildOf(xrOrigin.transform) ||
            !leftController.IsChildOf(xrOrigin.transform) ||
            (LocalPlayerRig.Instance && leftController == LocalPlayerRig.Instance.rightController))
        {
            Debug.LogError(
                "[LeftTeleport] Missing rig, camera or left controller.",
                this
            );
            enabled = false;
            return;
        }

        // XRI 3.6.1 simulated controllers and OpenXR expose this usage.
        // Never bind simulator keyboard actions or an unqualified XRController.
        leftStick = new InputAction(
            "LeftTeleportThumbstick",
            InputActionType.Value,
            "<XRController>{LeftHand}/{Primary2DAxis}",
            expectedControlType: "Vector2"
        );

        CreateVisuals();
    }

    void OnEnable()
    {
        leftStick?.Enable();
        needsNeutral = true;
    }

    void OnDisable()
    {
        leftStick?.Disable();
        CancelAim();
    }

    void OnDestroy()
    {
        leftStick?.Dispose();

        if (arcMaterial)
            Destroy(arcMaterial);

        if (markerMaterial)
            Destroy(markerMaterial);
    }

    void CreateVisuals()
    {
        GameObject arcObject = new GameObject("Left Teleport Arc");
        arcObject.transform.SetParent(transform, false);

        arc = arcObject.AddComponent<LineRenderer>();
        arc.useWorldSpace = true;
        arc.widthMultiplier = 0.025f;
        arc.numCapVertices = 4;
        arc.positionCount = 0;
        arc.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        arc.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");

        if (!shader)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (!shader)
        {
            Debug.LogError("[LeftTeleport] No suitable line shader.");
            enabled = false;
            return;
        }

        arcMaterial = new Material(shader);
        arc.material = arcMaterial;
        arc.enabled = false;

        marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Teleport Destination Marker";
        marker.layer = 2; // Ignore Raycast
        marker.transform.SetParent(transform, false);

        Collider markerCollider = marker.GetComponent<Collider>();

        if (markerCollider)
        {
            markerCollider.enabled = false;
            Destroy(markerCollider);
        }

        markerMaterial = new Material(shader);
        marker.GetComponent<Renderer>().material = markerMaterial;
        marker.SetActive(false);
    }

    void Update()
    {
        if (!CanTeleport())
        {
            CancelAim();
            return;
        }

        // A respawn/server placement while aiming must never commit the old aim.
        if (aiming && (xrOrigin.transform.position - aimOrigin).sqrMagnitude > 0.0001f)
        {
            CancelAim();
            return;
        }

#if UNITY_EDITOR
        // Separate diagnostic source; production builds have no keyboard path.
        bool f8 = Keyboard.current != null && Keyboard.current.f8Key.isPressed;
        if (editorAim)
        {
            UpdateArc();
            if (!f8) FinishAim();
            return;
        }
        if (!aiming && f8)
        {
            editorAim = true;
            BeginAim(null);
            UpdateArc();
            return;
        }
#endif

        // Pin the originating LEFT device for the whole gesture. Device loss,
        // usage changes and focus loss cancel rather than acting like release.
        if (aiming)
        {
            if (!UsableLeftStick(aimingStick))
            {
                CancelAim();
                return;
            }
            UpdateArc();
            if (aimingStick.ReadValue().magnitude <= releaseThreshold)
                FinishAim();
            return;
        }

        Vector2Control candidate = null;
        bool allNeutral = true;
        bool hasTrackedStick = false;
        foreach (var control in leftStick.controls)
        {
            if (!(control is Vector2Control axis) || !UsableLeftStick(axis)) continue;
            hasTrackedStick = true;
            Vector2 value = axis.ReadValue();
            if (value.magnitude > releaseThreshold) allNeutral = false;
            if (value.y >= activateThreshold && Mathf.Abs(value.x) < value.y)
                candidate = axis;
        }
        if (needsNeutral)
        {
            if (hasTrackedStick && allNeutral) needsNeutral = false;
            return;
        }
        if (candidate != null)
        {
            BeginAim(candidate);
            UpdateArc();
        }
    }

    static bool UsableLeftStick(Vector2Control axis)
    {
        if (axis == null || !axis.device.added || !axis.device.enabled ||
            !axis.device.usages.Contains(new InternedString("LeftHand")) ||
            axis.device.usages.Contains(new InternedString("RightHand"))) return false;
        var tracked = axis.device.TryGetChildControl<ButtonControl>("isTracked");
        return tracked != null && tracked.isPressed;
    }

    bool CanTeleport()
    {
        var player = RelayCombatPlayer.Local;
        var game = LaserTagGameManager.Instance;
        if(player && !networkMovement)networkMovement=player.GetComponent<RelayAvatarPlayer>();
        return Application.isFocused && xrOrigin && xrOrigin.gameObject.activeInHierarchy &&
            playerCamera && leftController && leftController.gameObject.activeInHierarchy &&
            player && player.IsOwner && player.IsSpawned && player.IsAlive && game &&
            networkMovement && !networkMovement.TeleportPending &&
            (game.Phase.Value == GamePhase.Lobby ||
             (game.Phase.Value == GamePhase.Playing && player.InRound.Value));
    }

    void BeginAim(Vector2Control axis)
    {
        aiming = true;
        aimingStick = axis;
        aimOrigin = xrOrigin.transform.position;
    }

    void FinishAim()
    {
        // UpdateArc was run on this release frame: never use a stale destination.
        if (validDestination && CanTeleport()) Teleport();
        CancelAim();
    }

    void CancelAim()
    {
        aiming = false;
        validDestination = false;
        aimingStick = null;
        needsNeutral = true;
#if UNITY_EDITOR
        editorAim = false;
#endif
        HideVisuals();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelAim();
    }

    bool IgnoreCollider(Collider collider)
    {
        if (!collider || !collider.enabled || collider.isTrigger)
            return true;

        if (collider.gameObject.layer == 2)
            return true;

        Transform t = collider.transform;

        if (t.IsChildOf(xrOrigin.transform))
            return true;

        var localRig = LocalPlayerRig.Instance;

        if (localRig && localRig.avatar &&
            t.IsChildOf(localRig.avatar.transform))
            return true;

        var networkPlayer = RelayCombatPlayer.Local;

        if (networkPlayer &&
            t.IsChildOf(networkPlayer.transform))
            return true;

        return false;
    }

    bool FindNearestHit(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out RaycastHit nearest)
    {
        nearest = default;

        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            hits,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        float bestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];

            if (IgnoreCollider(hit.collider))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            nearest = hit;
            bestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    bool SafeDestination(RaycastHit hit)
    {
        if (hit.collider == null)
            return false;

        // No teleporting onto walls or steep slopes.
        float minUp = Mathf.Cos(maxFloorSlope * Mathf.Deg2Rad);

        if (hit.normal.y < minUp)
            return false;

        Vector3 currentFeet = new Vector3(
            playerCamera.transform.position.x,
            xrOrigin.transform.position.y,
            playerCamera.transform.position.z
        );

        if (Vector3.Distance(currentFeet, hit.point) > maxRange)
            return false;

        if (Mathf.Abs(hit.point.y - currentFeet.y) >
            maxVerticalChange)
            return false;

        TeleportClearance.LocalPlayerRoots(ignoreRoots);

        return TeleportClearance.IsFarFromPlayers(hit.point, bodyRadius * 2f, RelayCombatPlayer.Local) &&
            TeleportClearance.IsBodySpaceFree(
            hit.point,
            bodyRadius,
            bodyHeight,
            hit.collider,
            ignoreRoots
        );
    }

    void UpdateArc()
    {
        if (!arc)
            return;

        Vector3 forward = leftController.forward.normalized;
        Vector3 start =
            leftController.position + forward * 0.08f;

        Vector3 velocity =
            forward * launchSpeed +
            Vector3.up * upwardBoost;

        Vector3 previous = start;

        int steps = Mathf.Clamp(segments, 8, 100);

        Vector3[] positions = new Vector3[steps + 1];
        positions[0] = start;

        int used = 1;
        float traveled = 0f;

        validDestination = false;
        bool reachedSurface = false;
        RaycastHit surfaceHit = default;

        for (int i = 1; i <= steps; i++)
        {
            float t = i * stepTime;

            Vector3 next =
                start +
                velocity * t +
                Vector3.down * (0.5f * gravity * t * t);

            Vector3 difference = next - previous;
            float distance = difference.magnitude;

            if (distance < 0.0001f)
                continue;

            traveled += distance;

            if (traveled > maxRange)
                break;

            if (FindNearestHit(
                previous,
                difference / distance,
                distance,
                out RaycastHit hit))
            {
                positions[used++] = hit.point;
                surfaceHit = hit;
                reachedSurface = true;
                break;
            }

            positions[used++] = next;
            previous = next;
        }

        if (reachedSurface && SafeDestination(surfaceHit))
        {
            validDestination = true;
            destination = surfaceHit.point;
        }

        arc.positionCount = used;

        for (int i = 0; i < used; i++)
            arc.SetPosition(i, positions[i]);

        Color color = validDestination
            ? Color.cyan
            : Color.red;

        arc.startColor = color;
        arc.endColor = color;
        arc.enabled = true;

        if (marker)
        {
            marker.SetActive(validDestination);

            if (validDestination)
            {
                marker.transform.position =
                    destination + surfaceHit.normal * 0.025f;

                marker.transform.rotation =
                    Quaternion.FromToRotation(
                        Vector3.up,
                        surfaceHit.normal
                    );

                marker.transform.localScale =
                    new Vector3(0.25f, 0.008f, 0.25f);

                markerMaterial.color = Color.cyan;
            }
        }
    }

    void Teleport()
    {
        // Both the left stick and Editor F8 reach this exact request path.
        // No speculative rig movement: only a server-approved origin can move it.
        if(networkMovement)networkMovement.RequestTeleport(destination);
    }

    void HideVisuals()
    {
        if (arc)
            arc.enabled = false;

        if (marker)
            marker.SetActive(false);
    }
}
