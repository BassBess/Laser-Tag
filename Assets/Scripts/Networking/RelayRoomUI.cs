using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

// Small prototype room UI. Editor auto-hosts; a standalone build can join with a code.
[RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
public sealed class RelayRoomUI : MonoBehaviour
{
    [Tooltip("In the Editor, pressing Play creates a Relay room and spawns the host player.")]
    public bool autoHostInEditor = true;
    public bool showDebugUI = true;
    public string JoinCode => joinCode;
    public string Status => status;
    public bool Busy => busy;
    [Range(1, 7)] public int maxJoiningPlayers = 3;

    private NetworkManager manager;
    private UnityTransport transport;
    private string joinCode = "";
    private string enteredCode = "";
    private string status = "Ready";
    private bool busy;

    private void Start()
    {
        manager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
        manager.OnClientConnectedCallback += OnConnected;
        manager.OnClientDisconnectCallback += OnDisconnected;
#if UNITY_EDITOR
        if (autoHostInEditor)
            HostRoom();
#endif
    }

    private void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientConnectedCallback -= OnConnected;
        manager.OnClientDisconnectCallback -= OnDisconnected;
    }

    private void OnConnected(ulong id)
    {
        status = "Player " + id + " connected";
        Debug.Log("[RelayRoom] " + status);
    }

    private void OnDisconnected(ulong id)
    {
        status = "Player " + id + " disconnected";
        Debug.Log("[RelayRoom] " + status);
    }

    private async void HostRoom()
    {
        try { await HostAsync(maxJoiningPlayers); }
        catch (Exception e) { status = "Host failed: " + e.Message; Debug.LogException(e, this); }
    }

    private async void JoinRoom()
    {
        try { await JoinAsync(enteredCode); }
        catch (Exception e) { status = "Join failed: " + e.Message; Debug.LogException(e, this); }
    }

    // Shared by the preserved playground and the menu. UI/session lifecycle lives outside this service.
    public async Task<string> HostAsync(int joiningPlayers, CancellationToken cancellation = default)
    {
        PrepareOperation();
        if (joiningPlayers < 1 || joiningPlayers > 7) throw new ArgumentOutOfRangeException(nameof(joiningPlayers));
        busy = true;
        try
        {
            status = "Authenticating with Unity Services...";
            await Timed(SignIn(), cancellation); CheckActive(cancellation);
            status = "Allocating Relay room...";
            Allocation allocation = await Timed(RelayService.Instance.CreateAllocationAsync(joiningPlayers), cancellation);
            CheckActive(cancellation);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            string code = await Timed(RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId), cancellation);
            CheckActive(cancellation);
            if (!manager.StartHost()) throw new InvalidOperationException("Could not start host. Check the transport and player prefab.");
            joinCode = code;
            status = "Room created.";
            return code;
        }
        finally { busy = false; }
    }

    public async Task JoinAsync(string roomCode, CancellationToken cancellation = default)
    {
        PrepareOperation();
        string code = NormalizeCode(roomCode);
        if (!IsValidCode(code)) throw new ArgumentException("Enter the six-letter or digit room code supplied by the host.");
        busy = true;
        try
        {
            status = "Authenticating with Unity Services...";
            await Timed(SignIn(), cancellation); CheckActive(cancellation);
            status = "Joining Relay allocation...";
            JoinAllocation allocation = await Timed(RelayService.Instance.JoinAllocationAsync(code), cancellation);
            CheckActive(cancellation);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            if (!manager.StartClient()) throw new InvalidOperationException("Could not start client. Please retry.");
            joinCode = code;
            status = "Connecting to host...";
        }
        finally { busy = false; }
    }

    public static string NormalizeCode(string value) => (value ?? "").Trim().ToUpperInvariant();
    public static bool IsValidCode(string code)
    {
        if (code == null || code.Length != 6) return false;
        foreach (char c in code) if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
        return true;
    }
    private void PrepareOperation()
    {
        manager = GetComponent<NetworkManager>(); transport = GetComponent<UnityTransport>();
        if (busy || manager.IsListening) throw new InvalidOperationException("A connection is already active. Leave it before connecting again.");
    }
    private void CheckActive(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (this == null || !isActiveAndEnabled || manager == null) throw new OperationCanceledException();
    }
    private static async Task Timed(Task request, CancellationToken cancellation)
    {
        if (await Task.WhenAny(request, Task.Delay(30000, cancellation)) != request)
        { cancellation.ThrowIfCancellationRequested(); throw new TimeoutException("Unity Services request timed out."); }
        await request;
    }
    private static async Task<T> Timed<T>(Task<T> request, CancellationToken cancellation)
    {
        if (await Task.WhenAny(request, Task.Delay(30000, cancellation)) != request)
        { cancellation.ThrowIfCancellationRequested(); throw new TimeoutException("Unity Services request timed out."); }
        return await request;
    }
    private static async System.Threading.Tasks.Task SignIn()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void OnGUI()
    {
        if (!showDebugUI) return;
        GUILayout.BeginArea(new Rect(14, 14, 365, 305), GUI.skin.box);
        GUILayout.Label("LASER TAG | RELAY PLAYGROUND");
        if (manager == null)
        {
            GUILayout.Label("Waiting for NetworkManager...");
        }
        else if (manager.IsListening)
        {
            GUILayout.Label(manager.IsHost ? "HOST" :
                (manager.IsConnectedClient ? "CLIENT CONNECTED" : "CLIENT CONNECTING"));
            if (manager.IsHost)
            {
                GUILayout.Label("JOIN CODE: " + (joinCode == "" ? "Loading..." : joinCode));
                GUILayout.Label("Players: " + manager.ConnectedClientsIds.Count);
            }
            if (GUILayout.Button("DISCONNECT / STOP"))
            {
                manager.Shutdown();
                joinCode = "";
                status = "Disconnected";
            }
        }
        else
        {
            GUI.enabled = !busy;
            if (GUILayout.Button("HOST NEW ROOM")) HostRoom();
            GUILayout.Label("Join code from host:");
            enteredCode = GUILayout.TextField(enteredCode, 15);
            if (GUILayout.Button("JOIN ROOM")) JoinRoom();
            GUI.enabled = true;
        }
        GUILayout.Space(5);
        GUILayout.Label(status);
        GUILayout.Space(6);
        GUILayout.Label("Desktop: WASD move | Q/E turn | arrows look");
        GUILayout.Label("Space: raise hands | Shift: run");
        GUILayout.EndArea();
    }
}
