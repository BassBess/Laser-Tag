using System;
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
        if (busy || manager.IsListening) return;
        busy = true;
        status = "Authenticating with Unity Services...";
        try
        {
            await SignIn();
            if (this == null || manager == null || transport == null || !isActiveAndEnabled) return;
            status = "Allocating Relay room...";
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxJoiningPlayers);
            if (this == null || manager == null || transport == null || !isActiveAndEnabled) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (this == null || manager == null || transport == null || !isActiveAndEnabled) return;
            if (!manager.StartHost())
                throw new InvalidOperationException("NetworkManager.StartHost failed. Check Console / Player Prefab.");
            joinCode = code;
            status = "Room created. Share the code below.";
            Debug.Log("[RelayRoom] Host started. Join code: " + joinCode);
        }
        catch (Exception e)
        {
            status = "Host failed: " + e.Message;
            Debug.LogException(e, this);
        }
        finally { busy = false; }
    }

    private async void JoinRoom()
    {
        if (busy || manager.IsListening) return;
        string code = enteredCode.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) { status = "Enter a join code."; return; }
        busy = true;
        status = "Authenticating with Unity Services...";
        try
        {
            await SignIn();
            if (this == null || manager == null || transport == null || !isActiveAndEnabled) return;
            status = "Joining Relay allocation...";
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(code);
            if (this == null || manager == null || transport == null || !isActiveAndEnabled) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            if (!manager.StartClient())
                throw new InvalidOperationException("NetworkManager.StartClient failed. Check Console.");
            status = "Connecting to host...";
        }
        catch (Exception e)
        {
            status = "Join failed: " + e.Message;
            Debug.LogException(e, this);
        }
        finally { busy = false; }
    }

    private static async System.Threading.Tasks.Task SignIn()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void OnGUI()
    {
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
