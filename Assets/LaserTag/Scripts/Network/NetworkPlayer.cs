using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Where a player is in the round.</summary>
public enum PlayerLifeState : byte
{
    /// <summary>In the arena: visible, can shoot and be shot.</summary>
    Alive,
    /// <summary>Just got tagged out and waiting a few seconds to respawn. Invisible, cannot shoot.</summary>
    Respawning,
    /// <summary>Used up every life. Can explore the arena, but is invisible and cannot shoot or be shot.</summary>
    Eliminated,
    /// <summary>Joined while a round was running. Invisible until the next round.</summary>
    Spectating,
}

/// <summary>Head, hands and body pose of a player, sent by the owner ~20 times per second.</summary>
public struct PoseState : INetworkSerializeByMemcpy
{
    public Vector3 rootPosition;
    public float rootYaw;
    public Vector3 headPosition;
    public Quaternion headRotation;
    public Vector3 leftPosition;
    public Quaternion leftRotation;
    public Vector3 rightPosition;
    public Quaternion rightRotation;
}

/// <summary>
/// The networked side of one player. One of these is spawned for every connected headset.
///
/// - The owner (the player wearing the headset) copies the pose of their local XR avatar into
///   <see cref="Pose"/>; everyone else drives a full-body avatar from it.
/// - The server owns the game data: health, lives, kills, hits, deaths, placement and life state.
/// - The owner's own copy of the avatar is hidden (the local rig already shows your body when you look down).
/// - Remote avatars have colliders so lasers and teleport checks can hit them, and flash red when hit.
/// </summary>
[DefaultExecutionOrder(-50)]
public class NetworkPlayer : NetworkBehaviour
{
    /// <summary>Every spawned player on this machine, including the local one.</summary>
    public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();

    /// <summary>The player object that belongs to this headset, or null before connecting.</summary>
    public static NetworkPlayer Local { get; private set; }

    [Header("Avatar (set up by the Laser Tag builder)")]
    public ScoutAvatar avatar;
    [Tooltip("Colliders lasers hit. Enabled only while the player is alive and visible.")]
    public Collider[] hitColliders = new Collider[0];
    [Tooltip("Optional extra visuals hidden together with the avatar (for example the guns in its hands).")]
    public GameObject[] extraVisuals = new GameObject[0];

    [Header("Rules")]
    public int maxHealth = 100;
    public int damagePerHit = 10;

    [Header("Network")]
    [Range(5, 60)] public float poseSendRate = 20f;
    [Tooltip("Remote avatars further than this from their new pose snap instead of sliding (teleports).")]
    public float snapDistance = 1.25f;
    [Range(1, 40)] public float smoothing = 18f;

    [Header("Effects")]
    public Color hitFlashColor = new Color(1f, 0.08f, 0.05f, 1f);
    [Min(0.05f)] public float hitFlashSeconds = 0.35f;
    public Color shotColor = new Color(1f, 0.1f, 0.08f, 1f);
    public Color protectionColor = new Color(0.3f, 0.7f, 1f, 1f);

    // ---------- synced data ----------
    public NetworkVariable<PoseState> Pose = new NetworkVariable<PoseState>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> Ammo = new NetworkVariable<int>(100,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public NetworkVariable<FixedString32Bytes> DisplayName = new NetworkVariable<FixedString32Bytes>();
    public NetworkVariable<int> Health = new NetworkVariable<int>(100);
    public NetworkVariable<PlayerLifeState> LifeState = new NetworkVariable<PlayerLifeState>(PlayerLifeState.Alive);
    public NetworkVariable<bool> InRound = new NetworkVariable<bool>(false);
    public NetworkVariable<int> LivesLeft = new NetworkVariable<int>(0);
    public NetworkVariable<int> Kills = new NetworkVariable<int>(0);
    public NetworkVariable<int> Hits = new NetworkVariable<int>(0);
    public NetworkVariable<int> Deaths = new NetworkVariable<int>(0);
    /// <summary>Final position in the round: 1 = winner. 0 = still playing / not in the round.</summary>
    public NetworkVariable<int> Placement = new NetworkVariable<int>(0);
    /// <summary>Server time at which a <see cref="PlayerLifeState.Respawning"/> player comes back.</summary>
    public NetworkVariable<double> RespawnAt = new NetworkVariable<double>(0);
    /// <summary>
    /// Controlled by the server instead of a headset. The game itself never creates these; the optional
    /// testing tools (Assets/LaserTag/Testing) use it for practice bots.
    /// </summary>
    public NetworkVariable<bool> IsBot = new NetworkVariable<bool>(false);

    /// <summary>Server-controlled players: how to move them when the game places them (set by whatever controls them).</summary>
    [System.NonSerialized] public System.Action<Vector3, float> serverTeleportHandler;

    /// <summary>Server: set before spawning to make this a server-controlled player with this name.</summary>
    [System.NonSerialized] public string serverBotName;
    /// <summary>Server time until which this player cannot be hit (spawn protection).</summary>
    public NetworkVariable<double> ProtectedUntil = new NetworkVariable<double>(0);

    // ---------- server-only bookkeeping ----------
    [System.NonSerialized] public float serverLastHitTime = -999f;
    [System.NonSerialized] public double serverEliminatedAt;
    [System.NonSerialized] public bool serverAnnounced;

    // ---------- local state ----------
    float nextPoseSend;
    bool hasTarget;
    PoseState shown;
    Renderer[] avatarRenderers = new Renderer[0];
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    bool presentationVisible = true;
    MaterialPropertyBlock flashBlock;
    float flashUntil = -1f;

    // Statics survive between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        Local = null;
    }

    public string Name => DisplayName.Value.ToString();

    /// <summary>The player wearing THIS headset. Bots are owned by the host but are not the host's player.</summary>
    public bool IsLocalHuman => IsOwner && !IsBot.Value;

    /// <summary>True while spawn protection is active.</summary>
    public bool IsProtected => IsSpawned && NetworkManager != null && NetworkManager.ServerTime.Time < ProtectedUntil.Value;
    public bool IsAlive => LifeState.Value == PlayerLifeState.Alive;

    /// <summary>Alive and visible to the others (also true in the lobby).</summary>
    public bool IsPresentInWorld => IsSpawned && LifeState.Value == PlayerLifeState.Alive;

    /// <summary>True once a real pose has been received/sent.</summary>
    public bool HasPose => IsLocalHuman ? LocalPlayerRig.Instance != null : IsValidRotation(Pose.Value.headRotation);

    /// <summary>Where this player's feet are, as best this machine knows.</summary>
    public Vector3 FeetPosition
    {
        get
        {
            if (IsLocalHuman && LocalPlayerRig.Instance != null) return LocalPlayerRig.Instance.FeetPosition;
            return Pose.Value.rootPosition;
        }
    }

    /// <summary>Whether the local player is allowed to fire right now.</summary>
    public bool CanShoot
    {
        get
        {
            if (!IsAlive) return false;
            LaserTagGameManager gm = LaserTagGameManager.Instance;
            return gm == null || gm.Phase.Value != GamePhase.Countdown;
        }
    }

    // =====================================================================================
    // Lifecycle
    // =====================================================================================

    void Awake()
    {
        if (avatar == null) avatar = GetComponentInChildren<ScoutAvatar>(true);
        if (avatar != null)
        {
            avatar.localCamera = null; // remote copies always show the full body
            foreach (ScoutLocalRigBinding binding in avatar.GetComponentsInChildren<ScoutLocalRigBinding>(true))
                binding.enabled = false;
            avatarRenderers = avatar.GetComponentsInChildren<Renderer>(true);
            // Performance (Quest 2): up to 5 avatars made of many parts; dynamic shadows from them are costly.
            foreach (Renderer r in avatarRenderers)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (hitColliders == null || hitColliders.Length == 0)
            hitColliders = GetComponentsInChildren<Collider>(true);
        flashBlock = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this)) All.Add(this);

        Health.OnValueChanged += OnHealthChanged;
        LifeState.OnValueChanged += OnLifeStateChanged;

        if (IsServer)
        {
            // Network variables may only be written once spawned, so bot settings are applied here.
            if (!string.IsNullOrEmpty(serverBotName))
            {
                IsBot.Value = true;
                DisplayName.Value = new FixedString32Bytes(serverBotName);
            }
            if (!IsBot.Value) DisplayName.Value = new FixedString32Bytes("Player " + (OwnerClientId + 1));
            Health.Value = maxHealth;
        }

        if (IsLocalHuman)
        {
            Local = this;
            gameObject.name = "Local Network Player";
            SendPose(force: true);
        }
        else
        {
            gameObject.name = IsBot.Value ? Name : "Remote Player " + (OwnerClientId + 1);
        }

        if (IsServer && LaserTagGameManager.Instance != null)
            LaserTagGameManager.Instance.ServerOnPlayerJoined(this);

        RefreshPresentation(force: true);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Health.OnValueChanged -= OnHealthChanged;
        LifeState.OnValueChanged -= OnLifeStateChanged;
        if (Local == this) Local = null;
        if (IsServer && LaserTagGameManager.Instance != null)
            LaserTagGameManager.Instance.ServerOnPlayerLeft(this);
    }

    public override void OnDestroy()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        base.OnDestroy();
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsLocalHuman)
        {
            if (Time.unscaledTime >= nextPoseSend) SendPose(force: false);
        }
        else
        {
            ApplyRemotePose();
        }

        RefreshPresentation(force: false);
        UpdateFlash();
    }

    // =====================================================================================
    // Pose sync
    // =====================================================================================

    void SendPose(bool force)
    {
        nextPoseSend = Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);
        LocalPlayerRig rig = LocalPlayerRig.Instance;
        if (rig == null) return;
        if (rig.TryCapturePose(out PoseState pose))
        {
            // Write only on change so idle players do not use bandwidth.
            if (force || !SamePose(pose, Pose.Value)) Pose.Value = pose;
        }
        if (Ammo.Value != rig.Ammo) Ammo.Value = rig.Ammo;
    }

    static bool SamePose(in PoseState a, in PoseState b)
    {
        return (a.headPosition - b.headPosition).sqrMagnitude < 1e-6f &&
               (a.leftPosition - b.leftPosition).sqrMagnitude < 1e-6f &&
               (a.rightPosition - b.rightPosition).sqrMagnitude < 1e-6f &&
               Quaternion.Dot(a.headRotation, b.headRotation) > 0.99999f &&
               Quaternion.Dot(a.leftRotation, b.leftRotation) > 0.99999f &&
               Quaternion.Dot(a.rightRotation, b.rightRotation) > 0.99999f;
    }

    void ApplyRemotePose()
    {
        if (avatar == null) return;
        PoseState latest = Pose.Value;
        if (!IsValidRotation(latest.headRotation)) return; // nothing received yet

        if (!hasTarget || (latest.rootPosition - shown.rootPosition).sqrMagnitude > snapDistance * snapDistance)
        {
            shown = latest; // first pose or teleport: snap
            hasTarget = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            shown.rootPosition = Vector3.Lerp(shown.rootPosition, latest.rootPosition, k);
            shown.rootYaw = Mathf.LerpAngle(shown.rootYaw, latest.rootYaw, k);
            shown.headPosition = Vector3.Lerp(shown.headPosition, latest.headPosition, k);
            shown.headRotation = Quaternion.Slerp(Safe(shown.headRotation), Safe(latest.headRotation), k);
            shown.leftPosition = Vector3.Lerp(shown.leftPosition, latest.leftPosition, k);
            shown.leftRotation = Quaternion.Slerp(Safe(shown.leftRotation), Safe(latest.leftRotation), k);
            shown.rightPosition = Vector3.Lerp(shown.rightPosition, latest.rightPosition, k);
            shown.rightRotation = Quaternion.Slerp(Safe(shown.rightRotation), Safe(latest.rightRotation), k);
        }

        // Same targets the existing AvatarPoseDriver uses: body root, head and both hands.
        // ScoutAvatar solves the arms and ScoutProceduralLegs plants the feet afterwards.
        avatar.transform.SetPositionAndRotation(shown.rootPosition, Quaternion.Euler(0f, shown.rootYaw, 0f));
        if (avatar.head) avatar.head.SetPositionAndRotation(shown.headPosition, Safe(shown.headRotation));
        if (avatar.leftHand) avatar.leftHand.SetPositionAndRotation(shown.leftPosition, Safe(shown.leftRotation));
        if (avatar.rightHand) avatar.rightHand.SetPositionAndRotation(shown.rightPosition, Safe(shown.rightRotation));
    }

    static Quaternion Safe(Quaternion q) => IsValidRotation(q) ? q : Quaternion.identity;

    /// <summary>A default (all-zero) quaternion means "no pose received yet". Note: Quaternion == cannot detect it.</summary>
    static bool IsValidRotation(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f;

    // =====================================================================================
    // Visibility / colliders
    // =====================================================================================

    void RefreshPresentation(bool force)
    {
        // Your own network copy is never drawn: the local rig shows your body.
        bool visible = !IsLocalHuman && IsSpawned && LifeState.Value == PlayerLifeState.Alive && IsValidRotation(Pose.Value.headRotation);
        if (!force && visible == presentationVisible) return;
        presentationVisible = visible;

        foreach (Renderer r in avatarRenderers)
        {
            if (r == null) continue;
            // Remember renderers we turned off so ScoutAvatar's own first-person hiding is not disturbed.
            if (!visible && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
        }
        if (visible)
        {
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
        }
        foreach (GameObject go in extraVisuals)
            if (go != null) go.SetActive(visible);

        // Colliders follow visibility: invisible players cannot be hit and do not block teleports/spawns.
        foreach (Collider c in hitColliders)
            if (c != null) c.enabled = visible;
    }

    // =====================================================================================
    // Hits and effects
    // =====================================================================================

    void OnHealthChanged(int previous, int current)
    {
        if (current >= previous) return;
        if (IsLocalHuman)
        {
            if (PlayerHud.Instance != null) PlayerHud.Instance.FlashDamage();
            if (LocalPlayerRig.Instance != null) LocalPlayerRig.Instance.OnDamaged();
        }
        else
        {
            flashUntil = Time.time + hitFlashSeconds; // everybody else sees the avatar light up red

            // Our shot finished them off: kill confirm.
            LocalPlayerRig rig = LocalPlayerRig.Instance;
            if (current == 0 && rig != null && rig.LastVictim == this && Time.time - rig.LastVictimTime < 1.5f &&
                PlayerHud.Instance != null)
                PlayerHud.Instance.ShowKill(Name);
        }
    }

    void OnLifeStateChanged(PlayerLifeState previous, PlayerLifeState current)
    {
        if (IsLocalHuman && current == PlayerLifeState.Alive && LocalPlayerRig.Instance != null)
            LocalPlayerRig.Instance.RefillAmmo();
    }

    void UpdateFlash()
    {
        bool flashing = Time.time < flashUntil && presentationVisible;
        bool shielded = presentationVisible && IsProtected;
        if (!flashing && !shielded && flashUntil < 0f) return;

        if (!flashing && shielded)
        {
            // Spawn protection: soft blue pulse.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            Color c = Color.Lerp(Color.white, protectionColor, 0.4f + 0.4f * pulse);
            flashBlock.Clear();
            flashBlock.SetColor("_BaseColor", c);
            flashBlock.SetColor("_Color", c);
            flashBlock.SetColor("_EmissionColor", protectionColor * pulse);
            foreach (Renderer r in avatarRenderers) if (r != null) r.SetPropertyBlock(flashBlock);
            flashUntil = 0f; // so the tint is cleared when protection ends
            return;
        }

        if (flashing)
        {
            float k = Mathf.Clamp01((flashUntil - Time.time) / hitFlashSeconds);
            Color c = Color.Lerp(Color.white, hitFlashColor, 0.35f + 0.65f * k);
            flashBlock.Clear();
            flashBlock.SetColor("_BaseColor", c);
            flashBlock.SetColor("_Color", c);
            flashBlock.SetColor("_EmissionColor", hitFlashColor * (2f * k));
            foreach (Renderer r in avatarRenderers) if (r != null) r.SetPropertyBlock(flashBlock);
        }
        else
        {
            foreach (Renderer r in avatarRenderers) if (r != null) r.SetPropertyBlock(null);
            flashUntil = -1f;
        }
    }

    // =====================================================================================
    // Shooting (called by LocalPlayerRig on the owner)
    // =====================================================================================

    /// <summary>Owner fired: show the beam to everybody else.</summary>
    public void OwnerFired(Vector3 from, Vector3 to)
    {
        if (IsOwner && IsSpawned) ShotFxRpc(from, to);
    }

    /// <summary>Owner's laser hit another player's collider: ask the server to apply damage.</summary>
    public void OwnerReportHit(NetworkPlayer victim, Vector3 from, Vector3 point)
    {
        if (IsOwner && IsSpawned && victim != null && victim != this && victim.IsSpawned)
            ReportHitRpc(new NetworkObjectReference(victim.NetworkObject), from, point);
    }

    [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
    void ShotFxRpc(Vector3 from, Vector3 to)
    {
        ShotBeamFx.Play(from, to, shotColor);
    }

    /// <summary>Server: show a shot fired by a server-controlled player to everybody.</summary>
    public void ServerShowShot(Vector3 from, Vector3 to)
    {
        if (!IsServer) return;
        ShotBeamFx.Play(from, to, shotColor);
        ServerShotFxRpc(from, to);
    }

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
    void ServerShotFxRpc(Vector3 from, Vector3 to)
    {
        ShotBeamFx.Play(from, to, shotColor);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ReportHitRpc(NetworkObjectReference victimRef, Vector3 from, Vector3 point)
    {
        if (!victimRef.TryGet(out NetworkObject victimObject)) return;
        if (!victimObject.TryGetComponent(out NetworkPlayer victim)) return;
        LaserTagGameManager gm = LaserTagGameManager.Instance;
        if (gm == null) return;
        // The shooter's headset only CLAIMS a hit; the host decides (see ServerValidateShot).
        if (gm.ServerValidateShot(this, victim, from, point, out string reason)) gm.ServerHandleHit(this, victim);
        else Debug.Log($"[Laser Tag] Hit by {Name} on {victim.Name} rejected: {reason}");
    }

    // =====================================================================================
    // Server helpers
    // =====================================================================================

    /// <summary>Server: move this player's headset to a spot (round start, respawn, join).</summary>
    public void ServerTeleport(Vector3 feet, float yaw)
    {
        if (!IsServer) return;
        if (IsBot.Value)
        {
            serverTeleportHandler?.Invoke(feet, yaw);
            return;
        }
        TeleportOwnerRpc(feet, yaw);
    }

    [Rpc(SendTo.Owner)]
    void TeleportOwnerRpc(Vector3 feet, float yaw)
    {
        if (LocalPlayerRig.Instance != null) LocalPlayerRig.Instance.TeleportTo(feet, yaw);
    }
}
