using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public enum GamePhase : byte
{
    /// <summary>Waiting for players. Anyone presses Start (A / Enter) to begin.</summary>
    Lobby,
    /// <summary>Everyone was placed at a random free spawn point; 3-2-1 before shooting is allowed.</summary>
    Countdown,
    /// <summary>The round is running.</summary>
    Playing,
    /// <summary>Only one player is left. Scoreboard is shown; Start begins a new round.</summary>
    GameOver,
}

/// <summary>
/// Server-authoritative round logic (lives in the arena scene as a network object).
///
/// Rules:
///  - Round start: every connected player gets <see cref="livesPerPlayer"/> lives (default 2 = one respawn),
///    full health, and a random free spawn point.
///  - A hit removes <see cref="NetworkPlayer.damagePerHit"/> health. At 0 health the player is tagged out:
///    if they have a life left they respawn after <see cref="respawnDelay"/> seconds at a random free
///    spawn point; otherwise they are eliminated and can only explore the arena, invisible to the others.
///  - When one player (or nobody) is left, the round is over and the scoreboard is shown.
///  - Ranking: by survival (last one standing is #1, then whoever was eliminated latest), ties broken by
///    kills, then hits, then fewer deaths.
///  - Start/Restart: any player presses A on the right controller (or Enter on a keyboard).
/// </summary>
public class LaserTagGameManager : NetworkBehaviour
{
    public static LaserTagGameManager Instance { get; private set; }

    [Header("Rules")]
    [Tooltip("Players needed before a round can start. Set to 1 to test alone.")]
    [Min(1)] public int minPlayersToStart = 2;
    [Tooltip("Lives per round. 2 = one respawn, after that the player is out.")]
    [Min(1)] public int livesPerPlayer = 2;
    [Min(0f)] public float respawnDelay = 3f;
    [Min(0f)] public float startCountdown = 3f;
    [Tooltip("Server-side limit on how fast one player can land hits (anti-spam), in seconds.")]
    [Min(0f)] public float minSecondsBetweenHits = 0.15f;

    [Header("Spawn protection")]
    [Tooltip("Seconds a player cannot be hit after respawning. Landing a hit yourself ends it early.")]
    [Min(0f)] public float spawnProtection = 2f;

    [Header("Hit validation (host)")]
    [Tooltip("The reported gun position must be this close to one of the shooter's hands, in metres.")]
    [Min(0.1f)] public float gunToHandTolerance = 0.8f;
    [Tooltip("The reported hit point may be this far from the victim's body on the host (covers network delay), in metres.")]
    [Min(0.1f)] public float hitPointTolerance = 0.9f;
    [Min(1f)] public float maxShotRange = 60f;

    [Header("References")]
    public SpawnManager spawnManager;

    [Header("Input")]
    [Tooltip("Starts / restarts the round.")]
    public InputActionProperty startAction;

    public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
    /// <summary>Server time when the current countdown ends.</summary>
    public NetworkVariable<double> PhaseEndsAt = new NetworkVariable<double>(0);
    public NetworkVariable<int> RoundNumber = new NetworkVariable<int>(0);

    InputAction fallbackStartAction;
    readonly List<Vector3> reserved = new List<Vector3>();
    readonly List<NetworkPlayer> scratch = new List<NetworkPlayer>();

    public double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

    // =====================================================================================
    // Lifecycle
    // =====================================================================================

    void Awake()
    {
        Instance = this;
        if (spawnManager == null) spawnManager = FindAnyObjectByType<SpawnManager>();
    }

    void OnEnable()
    {
        InputAction action = startAction.action;
        if (action == null || action.bindings.Count == 0)
        {
            // A on the right controller, Enter on a keyboard, south button on a gamepad.
            fallbackStartAction = new InputAction("Start Round", InputActionType.Button);
            fallbackStartAction.AddBinding("<XRController>{RightHand}/primaryButton");
            fallbackStartAction.AddBinding("<Keyboard>/enter");
            fallbackStartAction.AddBinding("<Keyboard>/numpadEnter");
            fallbackStartAction.AddBinding("<Gamepad>/buttonSouth");
            fallbackStartAction.Enable();
        }
        else
        {
            action.Enable();
        }
    }

    void OnDisable()
    {
        fallbackStartAction?.Disable();
        fallbackStartAction?.Dispose();
        fallbackStartAction = null;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        RoundNumber.OnValueChanged += OnRoundChanged;
        if (IsServer)
        {
            Phase.Value = GamePhase.Lobby;
            // Players that spawned before this object did.
            foreach (NetworkPlayer p in NetworkPlayer.All) ServerOnPlayerJoined(p);
        }
    }

    public override void OnNetworkDespawn()
    {
        RoundNumber.OnValueChanged -= OnRoundChanged;
    }

    static void OnRoundChanged(int previous, int current)
    {
        // New round: everybody starts with a full gun (life state may not change if they were alive already).
        if (LocalPlayerRig.Instance != null) LocalPlayerRig.Instance.RefillAmmo();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    void Update()
    {
        if (!IsSpawned) return;

        InputAction action = fallbackStartAction ?? startAction.action;
        if (action != null && action.WasPressedThisFrame() &&
            (Phase.Value == GamePhase.Lobby || Phase.Value == GamePhase.GameOver))
            RequestStartRpc();

        if (IsServer) ServerTick();
    }

    // =====================================================================================
    // Queries (all machines)
    // =====================================================================================

    public int ConnectedPlayerCount
    {
        get
        {
            int n = 0;
            foreach (NetworkPlayer p in NetworkPlayer.All) if (p != null && p.IsSpawned) n++;
            return n;
        }
    }

    /// <summary>Players in the round who are not eliminated (respawning players still count).</summary>
    public int PlayersLeft
    {
        get
        {
            int n = 0;
            foreach (NetworkPlayer p in NetworkPlayer.All)
                if (p != null && p.IsSpawned && p.InRound.Value && p.LifeState.Value != PlayerLifeState.Eliminated) n++;
            return n;
        }
    }

    public int Participants
    {
        get
        {
            int n = 0;
            foreach (NetworkPlayer p in NetworkPlayer.All) if (p != null && p.IsSpawned && p.InRound.Value) n++;
            return n;
        }
    }

    public float SecondsUntil(double serverTime) => Mathf.Max(0f, (float)(serverTime - ServerNow));

    /// <summary>Players of the current/last round, best first. Used by the scoreboard on every machine.</summary>
    public List<NetworkPlayer> GetRanking(List<NetworkPlayer> result)
    {
        result.Clear();
        foreach (NetworkPlayer p in NetworkPlayer.All)
            if (p != null && p.IsSpawned && p.InRound.Value) result.Add(p);
        result.Sort(CompareRank);
        return result;
    }

    static int CompareRank(NetworkPlayer a, NetworkPlayer b)
    {
        // Placement 0 means "still standing" and ranks above every eliminated player.
        int pa = a.Placement.Value == 0 ? 0 : a.Placement.Value;
        int pb = b.Placement.Value == 0 ? 0 : b.Placement.Value;
        if (pa != pb) return pa.CompareTo(pb);
        if (a.Kills.Value != b.Kills.Value) return b.Kills.Value.CompareTo(a.Kills.Value);
        if (a.Hits.Value != b.Hits.Value) return b.Hits.Value.CompareTo(a.Hits.Value);
        if (a.Deaths.Value != b.Deaths.Value) return a.Deaths.Value.CompareTo(b.Deaths.Value);
        return a.OwnerClientId.CompareTo(b.OwnerClientId);
    }

    // =====================================================================================
    // Start / restart
    // =====================================================================================

    [Rpc(SendTo.Server)]
    void RequestStartRpc()
    {
        if (Phase.Value != GamePhase.Lobby && Phase.Value != GamePhase.GameOver) return;
        if (ConnectedPlayerCount < minPlayersToStart) return;
        ServerStartRound();
    }

    /// <summary>Server: reset everybody and place them at random free spawn points.</summary>
    public void ServerStartRound()
    {
        if (!IsServer) return;

        scratch.Clear();
        foreach (NetworkPlayer p in NetworkPlayer.All) if (p != null && p.IsSpawned) scratch.Add(p);
        // Random order so the same player does not always get first pick.
        for (int i = scratch.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (scratch[i], scratch[j]) = (scratch[j], scratch[i]);
        }

        // Hide everyone first so nobody blocks a spawn point with their old position.
        foreach (NetworkPlayer p in scratch) p.LifeState.Value = PlayerLifeState.Respawning;

        reserved.Clear();
        foreach (NetworkPlayer p in scratch)
        {
            p.InRound.Value = true;
            p.Health.Value = p.maxHealth;
            p.LivesLeft.Value = livesPerPlayer;
            p.Kills.Value = 0;
            p.Hits.Value = 0;
            p.Deaths.Value = 0;
            p.Placement.Value = 0;
            p.RespawnAt.Value = 0;
            p.serverLastHitTime = -999f;
            PlaceAtFreeSpawn(p);
        }
        foreach (NetworkPlayer p in scratch) p.LifeState.Value = PlayerLifeState.Alive;

        RoundNumber.Value++;
        Phase.Value = GamePhase.Countdown;
        PhaseEndsAt.Value = ServerNow + startCountdown;
    }

    void PlaceAtFreeSpawn(NetworkPlayer p)
    {
        if (spawnManager == null) return;
        Vector3 feet = spawnManager.FindSpawn(p, reserved, out float yaw);
        reserved.Add(feet);
        p.ServerTeleport(feet, yaw);
    }

    // =====================================================================================
    // Server tick
    // =====================================================================================

    void ServerTick()
    {
        switch (Phase.Value)
        {
            case GamePhase.Countdown:
                if (ServerNow >= PhaseEndsAt.Value) Phase.Value = GamePhase.Playing;
                break;

            case GamePhase.Playing:
                reserved.Clear();
                foreach (NetworkPlayer p in NetworkPlayer.All)
                {
                    if (p == null || !p.IsSpawned || !p.InRound.Value) continue;
                    if (p.LifeState.Value == PlayerLifeState.Respawning && ServerNow >= p.RespawnAt.Value)
                        ServerRespawn(p);
                }
                ServerCheckRoundOver();
                break;
        }
    }

    void ServerRespawn(NetworkPlayer p)
    {
        PlaceAtFreeSpawn(p);
        p.Health.Value = p.maxHealth;
        p.ProtectedUntil.Value = ServerNow + spawnProtection;
        p.LifeState.Value = PlayerLifeState.Alive;
    }

    void ServerCheckRoundOver()
    {
        int participants = Participants;
        int left = PlayersLeft;
        bool over = participants >= 2 ? left <= 1 : left == 0;
        if (!over) return;

        foreach (NetworkPlayer p in NetworkPlayer.All)
            if (p != null && p.IsSpawned && p.InRound.Value && p.LifeState.Value != PlayerLifeState.Eliminated)
                p.Placement.Value = 1;
        Phase.Value = GamePhase.GameOver;
    }

    // =====================================================================================
    // Hits
    // =====================================================================================

    readonly RaycastHit[] coverHits = new RaycastHit[16];

    /// <summary>
    /// Server: is a hit reported by a headset believable from the HOST's point of view?
    ///  1. the shot started at the shooter's gun (near one of their hands),
    ///  2. it is within range,
    ///  3. the hit point is on the victim's body as the host sees it (with a small allowance for network delay),
    ///  4. no wall, prop or other player is between the gun and the hit point (cover blocks shots for everyone).
    /// </summary>
    public bool ServerValidateShot(NetworkPlayer shooter, NetworkPlayer victim, Vector3 from, Vector3 point, out string reason)
    {
        PoseState pose = shooter.Pose.Value;
        float toHand = Mathf.Min(Vector3.Distance(from, pose.leftPosition), Vector3.Distance(from, pose.rightPosition));
        if (toHand > gunToHandTolerance) { reason = $"gun {toHand:F1} m from the shooter's hands"; return false; }

        Vector3 path = point - from;
        float length = path.magnitude;
        if (length > maxShotRange) { reason = $"out of range ({length:F0} m)"; return false; }

        Vector3 feet = victim.FeetPosition;
        float offBody = DistanceToSegment(point, feet + Vector3.up * 0.1f, feet + Vector3.up * 1.9f);
        if (offBody > hitPointTolerance) { reason = $"hit point {offBody:F1} m away from the victim"; return false; }

        if (length > 0.05f)
        {
            int count = Physics.RaycastNonAlloc(from, path / length, coverHits, length - 0.05f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform t = coverHits[i].collider != null ? coverHits[i].collider.transform : null;
                if (t == null || t.IsChildOf(shooter.transform) || t.IsChildOf(victim.transform)) continue;
                reason = "blocked by " + t.name;
                return false;
            }
        }
        reason = null;
        return true;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>Server: apply a validated hit (from <see cref="ServerValidateShot"/> or a server-controlled player).</summary>
    public void ServerHandleHit(NetworkPlayer shooter, NetworkPlayer victim)
    {
        if (!IsServer || shooter == null || victim == null || shooter == victim) return;
        if (Phase.Value != GamePhase.Playing) return;
        if (!shooter.InRound.Value || !victim.InRound.Value) return;
        if (!shooter.IsAlive || !victim.IsAlive) return;
        if (Time.time - shooter.serverLastHitTime < minSecondsBetweenHits) return;
        shooter.serverLastHitTime = Time.time;
        shooter.ProtectedUntil.Value = 0; // attacking ends your own spawn protection
        if (ServerNow < victim.ProtectedUntil.Value) return;

        shooter.Hits.Value++;
        int health = Mathf.Max(0, victim.Health.Value - victim.damagePerHit);
        victim.Health.Value = health;
        if (health == 0) ServerTagOut(victim, shooter);
    }

    void ServerTagOut(NetworkPlayer victim, NetworkPlayer shooter)
    {
        victim.Deaths.Value++;
        if (shooter != null) shooter.Kills.Value++;
        victim.LivesLeft.Value = Mathf.Max(0, victim.LivesLeft.Value - 1);

        if (victim.LivesLeft.Value > 0)
        {
            victim.RespawnAt.Value = ServerNow + respawnDelay;
            victim.LifeState.Value = PlayerLifeState.Respawning;
            KillFeedRpc(ShortName(shooter), ShortName(victim), false);
        }
        else
        {
            victim.LifeState.Value = PlayerLifeState.Eliminated;
            victim.serverEliminatedAt = ServerNow;
            // Placement = number of players still in, plus one. E.g. first out of 4 places 4th.
            victim.Placement.Value = PlayersLeft + 1;
            KillFeedRpc(ShortName(shooter), ShortName(victim), true);
            ServerCheckRoundOver();
        }
    }

    static FixedString32Bytes ShortName(NetworkPlayer p) => new FixedString32Bytes(p != null ? p.Name : "?");

    /// <summary>"Shooter ▸ Victim" line at the top of every player's view.</summary>
    [Rpc(SendTo.Everyone)]
    void KillFeedRpc(FixedString32Bytes shooter, FixedString32Bytes victim, bool eliminated)
    {
        if (PlayerHud.Instance != null)
            PlayerHud.Instance.AddKillFeed(shooter.ToString(), victim.ToString(), eliminated);
    }

    // =====================================================================================
    // Joining / leaving
    // =====================================================================================

    public void ServerOnPlayerJoined(NetworkPlayer p)
    {
        if (!IsServer || !IsSpawned || p == null) return;
        if (!p.serverAnnounced)
        {
            p.serverAnnounced = true;
            SystemMessageRpc(new FixedString64Bytes(p.Name + " joined"));
        }
        p.Health.Value = p.maxHealth;
        p.Placement.Value = 0;
        if (Phase.Value == GamePhase.Lobby)
        {
            p.InRound.Value = false;
            p.LifeState.Value = PlayerLifeState.Alive;
            reserved.Clear();
            PlaceAtFreeSpawn(p); // never start inside someone else
        }
        else
        {
            // Mid-round joiners watch invisibly and play from the next round.
            p.InRound.Value = false;
            p.LifeState.Value = PlayerLifeState.Spectating;
        }
    }

    public void ServerOnPlayerLeft(NetworkPlayer p)
    {
        // A headset disconnected (or quit). Its player object is removed for everyone; the round-over check
        // runs every tick, so if only one player is left the round ends normally. Tell everybody.
        if (!IsServer || !IsSpawned || NetworkManager == null || NetworkManager.ShutdownInProgress) return;
        bool midRound = Phase.Value == GamePhase.Playing && p.InRound.Value && p.LifeState.Value != PlayerLifeState.Eliminated;
        SystemMessageRpc(new FixedString64Bytes(p.Name + (midRound ? " disconnected - out of the round" : " left")));
    }

    /// <summary>Grey info line in everyone's feed (joins, disconnects).</summary>
    [Rpc(SendTo.Everyone)]
    void SystemMessageRpc(FixedString64Bytes message)
    {
        if (PlayerHud.Instance != null) PlayerHud.Instance.AddSystemMessage(message.ToString());
    }
}
