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

    [Header("Walkable environment")]
    [Tooltip("Explicit opt-in for walkable static colliders with generic names. Reviewed spawn floors and authored floor/ramp/stair/platform/walkway assets are included automatically.")]
    public Collider[] additionalWalkableSurfaces = new Collider[0];
    [Header("Temporary teleport diagnostics")]
    [Tooltip("Throttled rejection logs in Editor/Development builds only. Disable after arena validation.")]
    public bool teleportDiagnostics;

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

    readonly Vector3[] arcPositions = new Vector3[101];
    readonly List<Transform> ignoreRoots = new List<Transform>();
    TeleportClearance.Rejection previewRejection;
    string previewTrackingRejection;
    RaycastHit previewHit;
    Vector3 attemptedDestination;
    float nextPreviewDiagnostic;

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
        if(aiming)LogPreviewRejection("Client/component disabled");
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
        if (!CanTeleport(out var unavailable))
        {
            if(aiming)LogPreviewRejection(unavailable);
            CancelAim();
            return;
        }

        // A respawn/server placement while aiming must never commit the old aim.
        if (aiming && (xrOrigin.transform.position - aimOrigin).sqrMagnitude > 0.0001f)
        {
            LogPreviewRejection("Client/origin changed while aiming");
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
                LogPreviewRejection("Client/left tracking or input device lost");
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

    bool CanTeleport() => CanTeleport(out _);
    bool CanTeleport(out string reason)
    {
        var player = RelayCombatPlayer.Local;
        var game = LaserTagGameManager.Instance;
        if(player && !networkMovement)networkMovement=player.GetComponent<RelayAvatarPlayer>();
        reason=null;
        if(LaserTag.Menu.MainMenuController.GameplayInputBlocked)reason="Client/options open";
        else if(!Application.isFocused)reason="Client/application not focused";
        else if(!xrOrigin || !xrOrigin.gameObject.activeInHierarchy)reason="Client/active rig unavailable";
        else if(!playerCamera || !leftController || !leftController.gameObject.activeInHierarchy)reason="Client/camera or left controller unavailable";
        else if(!player || !player.IsOwner || !player.IsSpawned)reason="Client/owned player unavailable";
        else if(!player.IsAlive)reason="Client/player not alive";
        else if(!game || !networkMovement)reason="Client/match or movement unavailable";
        else if(networkMovement.TeleportPending)reason="Client/request already pending";
        else if(!(game.Phase.Value==GamePhase.Lobby || (game.Phase.Value==GamePhase.Playing && player.InRound.Value)))reason="Client/match phase does not permit teleport";
        return reason==null;
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
        if(CanTeleport(out var reason))
        {
            if(validDestination)Teleport();
            else LogPreviewRejection(previewTrackingRejection??previewRejection.ToString());
        }
        else LogPreviewRejection(reason);
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
        if (!focused){if(aiming)LogPreviewRejection("Client/application focus lost");CancelAim();}
    }

    void UpdateArc()
    {
        if(!arc)return;
        TeleportClearance.LocalPlayerRoots(ignoreRoots);
        Vector3 forward=leftController.forward.normalized;
        bool reached=TeleportClearance.TryTraceArc(this,leftController.position,forward,ignoreRoots,arcPositions,
            out int used,out previewHit,out previewRejection);
        attemptedDestination=reached?previewHit.point:arcPositions[Mathf.Max(0,used-1)];
        // Preview and server run the same source, surface, support, clearance and
        // occupancy rules. Only the server may approve and move the origin.
        bool sourceValid=TeleportClearance.TryValidateSource(playerCamera.transform.position,leftController.position,forward,
            ignoreRoots,out var sourceRejection);
        Vector3 feet=new Vector3(playerCamera.transform.position.x,xrOrigin.transform.position.y,playerCamera.transform.position.z);
        previewTrackingRejection=networkMovement?networkMovement.PreviewTrackingRejection():"Client/network movement unavailable";
        validDestination=false;
        if(!sourceValid)previewRejection=sourceRejection;
        else if(reached)
            validDestination=TeleportClearance.TryValidateLanding(this,previewHit,feet,
                playerCamera.transform.position.y-feet.y,ignoreRoots,RelayCombatPlayer.Local,out previewRejection);
        if(previewTrackingRejection!=null)validDestination=false;
        if(validDestination)destination=previewHit.point;
        arc.positionCount=used;
        for(int i=0;i<used;i++)arc.SetPosition(i,arcPositions[i]);
        Color color=validDestination?Color.cyan:Color.red;
        arc.startColor=color;arc.endColor=color;arc.enabled=true;
        if(marker)
        {
            marker.SetActive(validDestination);
            if(validDestination)
            {
                marker.transform.position=destination+previewHit.normal*.025f;
                marker.transform.rotation=Quaternion.FromToRotation(Vector3.up,previewHit.normal);
                marker.transform.localScale=new Vector3(.25f,.008f,.25f);
                markerMaterial.color=Color.cyan;
            }
        }
    }
    void LogPreviewRejection(string reason)
    {
        if(!Application.isEditor && !Debug.isDebugBuild)return;
        if(!teleportDiagnostics || Time.unscaledTime<nextPreviewDiagnostic)return;
        nextPreviewDiagnostic=Time.unscaledTime+1f;
        string surface=previewHit.collider?TeleportClearance.Hierarchy(previewHit.collider.transform):"none";
        Debug.LogWarning($"[TeleportReject][preview] destination={attemptedDestination:F3} reason={reason}; hit='{surface}' radius={bodyRadius:F2} eyeHeight={(playerCamera&&xrOrigin?playerCamera.transform.position.y-xrOrigin.transform.position.y:0):F2}. Throttled 1s.",this);
    }
    [ContextMenu("Log Teleport Surface Inventory (Editor/Development)")]
    void LogSurfaceInventory()
    {
        if(Application.isEditor || Debug.isDebugBuild)TeleportClearance.LogSurfaceInventory(this);
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
