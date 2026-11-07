using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Connects every headset into the same game automatically.
///
/// AutoLan (default): on start, listen a couple of seconds for a game on the local network. If one is found,
/// join it; otherwise become the host and announce the game. If two headsets accidentally both become hosts,
/// the one with no guests gives up and joins the other. If the host leaves, everyone searches again.
///
/// Host / Client / Manual modes are there for testing (Manual connects to <see cref="manualAddress"/>).
/// Everybody must be on the same Wi-Fi network.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanSessionBootstrap : MonoBehaviour
{
    public enum StartMode { AutoLan, Host, Client, Manual, DoNothing }

    [Tooltip("AutoLan finds or hosts a game on the local network automatically.")]
    public StartMode mode = StartMode.AutoLan;
    [Tooltip("Game server address for Manual mode.")]
    public string manualAddress = "127.0.0.1";
    public ushort gamePort = 7777;
    public int discoveryPort = 47777;
    [Tooltip("How long to look for an existing game before hosting one, in seconds (a random extra 0-1.5 s is added).")]
    [Min(0.5f)] public float searchSeconds = 2.5f;
    [Min(0.2f)] public float announceInterval = 1f;

    /// <summary>Human-readable connection status for the HUD.</summary>
    public static string Status { get; private set; } = "";

    NetworkManager manager;
    UnityTransport transport;
    LanDiscovery discovery;
    string token;
    float nextAnnounce;
    bool searching;
    bool restarting;
    Coroutine routine;

    void Awake()
    {
        manager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
        token = System.Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    void OnEnable()
    {
        manager.OnClientStopped += OnClientStopped;
    }

    void OnDisable()
    {
        if (manager != null) manager.OnClientStopped -= OnClientStopped;
    }

    void Start()
    {
        if (transport == null)
        {
            Status = "No UnityTransport on the Network Manager";
            Debug.LogError("LanSessionBootstrap needs a UnityTransport next to the NetworkManager.", this);
            return;
        }

        switch (mode)
        {
            case StartMode.AutoLan:
                StartDiscovery();
                routine = StartCoroutine(SearchThenJoinOrHost());
                break;
            case StartMode.Host:
                StartDiscovery();
                Host();
                break;
            case StartMode.Client:
            case StartMode.Manual:
                Join(manualAddress, gamePort);
                break;
        }
    }

    void OnDestroy()
    {
        discovery?.Dispose();
        discovery = null;
    }

    void OnApplicationQuit()
    {
        discovery?.Dispose();
        discovery = null;
    }

    void StartDiscovery()
    {
        if (discovery != null) return;
        discovery = new LanDiscovery(discoveryPort);
        if (!discovery.Start())
        {
            discovery.Dispose();
            discovery = null;
        }
    }

    IEnumerator SearchThenJoinOrHost()
    {
        searching = true;
        Status = "Looking for a game on this Wi-Fi...";
        float until = Time.unscaledTime + searchSeconds + Random.Range(0f, 1.5f);
        while (Time.unscaledTime < until)
        {
            if (TryFindHost(out LanDiscovery.Announcement found))
            {
                searching = false;
                Join(found.address, found.gamePort);
                yield break;
            }
            yield return null;
        }
        searching = false;
        Host();
    }

    bool TryFindHost(out LanDiscovery.Announcement found)
    {
        found = default;
        if (discovery == null) return false;
        bool any = false;
        while (discovery.TryReceive(out LanDiscovery.Announcement a))
        {
            if (a.token == token) continue; // our own broadcast
            found = a;
            any = true;
        }
        return any;
    }

    void Host()
    {
        if (manager.IsListening) return;
        transport.SetConnectionData("0.0.0.0", gamePort, "0.0.0.0");
        if (manager.StartHost())
        {
            Status = "Hosting - other headsets will join automatically";
            Debug.Log($"Laser Tag: hosting on port {gamePort}.");
        }
        else
        {
            Status = "Could not start the game (is the port in use?)";
        }
    }

    void Join(string address, ushort port)
    {
        if (manager.IsListening) return;
        transport.SetConnectionData(address, port);
        Status = $"Joining game at {address}...";
        Debug.Log($"Laser Tag: joining {address}:{port}.");
        if (!manager.StartClient()) Status = "Could not join " + address;
    }

    void Update()
    {
        if (discovery == null || searching || restarting) return;

        if (manager.IsHost)
        {
            // Announce our game.
            if (Time.unscaledTime >= nextAnnounce)
            {
                nextAnnounce = Time.unscaledTime + announceInterval;
                discovery.Broadcast(token, gamePort, manager.ConnectedClientsIds.Count);
            }

            // Two hosts at once? The one without guests (and the "larger" token as a tie-break) joins the other.
            if (mode == StartMode.AutoLan && manager.ConnectedClientsIds.Count <= 1)
            {
                while (discovery.TryReceive(out LanDiscovery.Announcement other))
                {
                    if (other.token == token) continue;
                    if (other.players > 1 || string.CompareOrdinal(other.token, token) < 0)
                    {
                        StartCoroutine(SwitchToClient(other.address, other.gamePort));
                        break;
                    }
                }
            }
            else
            {
                while (discovery.TryReceive(out _)) { }
            }
        }
        else
        {
            while (discovery.TryReceive(out _)) { } // keep the queue small while we are a client
        }
    }

    IEnumerator SwitchToClient(string address, ushort port)
    {
        restarting = true;
        Status = "Found another game - joining it...";
        manager.Shutdown();
        while (manager.ShutdownInProgress || manager.IsListening) yield return null;
        yield return null;
        restarting = false;
        Join(address, port);
    }

    void OnClientStopped(bool wasHost)
    {
        if (wasHost || restarting || mode != StartMode.AutoLan || !isActiveAndEnabled) return;
        // Lost the host (or could not connect): search again, possibly becoming the new host.
        Status = "Lost connection - looking for a game...";
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(RetryAfterDelay());
    }

    IEnumerator RetryAfterDelay()
    {
        restarting = true;
        yield return new WaitForSecondsRealtime(1.5f);
        while (manager.ShutdownInProgress) yield return null;
        restarting = false;
        StartDiscovery();
        routine = StartCoroutine(SearchThenJoinOrHost());
    }
}
