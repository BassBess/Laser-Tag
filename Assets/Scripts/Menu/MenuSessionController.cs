using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaserTag.Menu
{
    [DefaultExecutionOrder(-31000)]
    public sealed class MenuSessionController : MonoBehaviour
    {
        public const string MenuScene = "MainMenu", GameScene = "RelayVRGame";
        public static MenuSessionController Instance { get; private set; }
        public NetworkManager Manager;
        public RelayRoomUI Relay;
        public GameObject LobbyPrefab;
        public LobbyNetworkState Lobby { get; private set; }
        public string Status { get; private set; } = "Choose Host or Join to connect.";
        public bool Connecting { get; private set; }
        public bool Leaving { get; private set; }
        public float RealLoadProgress { get; private set; } = -1;
        public int MaximumPlayers { get; private set; } = 4;
        CancellationTokenSource operation;
        float connectDeadline, loadDeadline;
        bool expectedDisconnect;
        string pendingCode;
        AsyncOperation sceneLoad;
        readonly HashSet<ulong> admitted = new HashSet<ulong>();

        void Awake()
        {
            if (Instance != null && Instance != this) { gameObject.SetActive(false); Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            Relay.autoHostInEditor = false; Relay.showDebugUI = false;
            Manager.NetworkConfig.ConnectionApproval = true;
            Manager.ConnectionApprovalCallback = Approve;
            Manager.OnClientConnectedCallback += Connected;
            Manager.OnClientDisconnectCallback += Disconnected;
            Manager.OnTransportFailure += TransportFailed;
            Manager.OnServerStopped += ServerStopped;
        }
        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool allowed = !Leaving && (!Lobby || Lobby.Phase.Value == SessionPhase.Lobby) &&
                admitted.Count < MaximumPlayers;
            if (allowed) admitted.Add(request.ClientNetworkId);
            response.Approved = allowed; response.CreatePlayerObject = false; response.Pending = false;
            response.Reason = allowed ? "" : "This room is full or the match has already started.";
        }
        public async void Host(int capacity)
        {
            if (!BeginConnection()) return;
            MaximumPlayers = Mathf.Clamp(capacity, 2, 8);
            try
            {
                pendingCode = await Relay.HostAsync(MaximumPlayers - 1, operation.Token);
                if (Leaving || !Manager.IsHost) return;
                FinishHostConnection(pendingCode);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { FailConnection("Hosting failed. " + FriendlyError(e)); }
        }
        void FinishHostConnection(string code)
        {
            if (!Manager.IsHost) throw new InvalidOperationException("Only the connected host can create the lobby.");
            HookSceneEvents();
            var obj = Instantiate(LobbyPrefab); var state = obj.GetComponent<LobbyNetworkState>();
            obj.GetComponent<NetworkObject>().Spawn(false);
            state.Code.Value = new Unity.Collections.FixedString32Bytes(code);
            state.Capacity.Value = MaximumPlayers;
            Connecting = false; Status = "Room connected. Mark yourself ready.";
        }
        public async void Join(string code)
        {
            code = RelayRoomUI.NormalizeCode(code);
            if (!RelayRoomUI.IsValidCode(code)) { Status = "Enter a six-character room code (letters and digits)."; return; }
            if (!BeginConnection()) return;
            pendingCode = code;
            try
            {
                await Relay.JoinAsync(code, operation.Token);
                if (Leaving) return;
                HookSceneEvents(); connectDeadline = Time.realtimeSinceStartup + 35;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { FailConnection("Joining failed. Check the code; the room may have expired or closed. " + FriendlyError(e)); }
        }
        bool BeginConnection()
        {
            if (Connecting || Leaving || Manager.IsListening || Relay.Busy) return false;
            admitted.Clear();
            operation?.Dispose(); operation = new CancellationTokenSource();
            Connecting = true; expectedDisconnect = false; connectDeadline = Time.realtimeSinceStartup + 65; Status = "Connecting..."; return true;
        }
        static string FriendlyError(Exception e)
        {
            // Service messages can contain request identifiers or endpoints: show a useful category instead.
            if (e is ArgumentException) return "Check the room code and player limit.";
            if (e.GetType().Name.Contains("Authentication")) return "Unity Services sign-in failed. Check your internet connection and project services.";
            return "Check internet access and Unity Relay availability, then retry.";
        }
        void FailConnection(string reason) { Leave(reason); }
        void Connected(ulong id)
        { if (id == Manager.LocalClientId) { Status = "Connected. Synchronizing lobby..."; } }
        void Disconnected(ulong id)
        {
            admitted.Remove(id);
            if (expectedDisconnect || Leaving) return;
            if (id == Manager.LocalClientId || (!Manager.IsServer && !Manager.IsConnectedClient))
                Leave(string.IsNullOrEmpty(Manager.DisconnectReason) ? "The host closed the room or the connection was lost." : Manager.DisconnectReason);
        }
        void TransportFailed() { if (!Leaving) Leave("Network transport failed. You can host or join again."); }
        void ServerStopped(bool wasHost) { admitted.Clear(); if (!expectedDisconnect && !Leaving) Leave("The session stopped. You can connect again."); }
        public void AttachLobby(LobbyNetworkState state)
        { Lobby = state; Connecting = false; Status = "Room connected. Mark yourself ready."; }
        public bool CanStart => Manager.IsHost && Lobby && Lobby.IsSpawned && Lobby.Phase.Value == SessionPhase.Lobby && Lobby.EveryoneReady && !Leaving;
        public void StartMatch()
        {
            if (!CanStart) return;
            Lobby.Phase.Value = SessionPhase.Loading;
            loadDeadline = Time.realtimeSinceStartup + 125;
            Status = "Loading arena...";
            var result = Manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
            if (result != SceneEventProgressStatus.Started) Leave("Could not start synchronized arena loading: " + result);
        }
        void HookSceneEvents()
        { if (Manager.SceneManager != null) { Manager.SceneManager.OnSceneEvent -= SceneEvent; Manager.SceneManager.OnSceneEvent += SceneEvent; } }
        void SceneEvent(SceneEvent e)
        {
            if (e.SceneName != GameScene) return;
            if (e.SceneEventType == SceneEventType.Load)
            { Status = "Loading arena..."; loadDeadline = Time.realtimeSinceStartup + 125; RealLoadProgress = -1; }
            if (e.SceneEventType == SceneEventType.Load && e.AsyncOperation != null) sceneLoad = e.AsyncOperation;
            if (e.SceneEventType == SceneEventType.LoadComplete) { sceneLoad = null; RealLoadProgress = -1; Status = "Synchronizing players..."; }
            if (e.SceneEventType == SceneEventType.LoadEventCompleted && Manager.IsServer)
            {
                if (e.ClientsThatTimedOut != null && e.ClientsThatTimedOut.Count > 0)
                { Leave("A player timed out loading the arena. Please recreate the room."); return; }
                foreach (ulong id in Manager.ConnectedClientsIds)
                {
                    var client = Manager.ConnectedClients[id];
                    if (client.PlayerObject != null) continue;
                    var player = Instantiate(Manager.NetworkConfig.PlayerPrefab);
                    player.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);
                }
                Lobby.Phase.Value = SessionPhase.Playing;
            }
        }
        void Update()
        {
            if (sceneLoad != null) RealLoadProgress = sceneLoad.progress;
            if (Connecting) Status = Relay.Busy ? Relay.Status : Status;
            if (Connecting && !Relay.Busy && Time.realtimeSinceStartup > connectDeadline) Leave("Connection timed out. Check the room code and host connection.");
            if (loadDeadline > 0 && Time.realtimeSinceStartup > loadDeadline) Leave("Arena synchronization timed out. Please reconnect.");
            if (InGame) { loadDeadline = 0; Status = "Connected to arena."; }
        }
        public bool InGame => Lobby && Lobby.Phase.Value == SessionPhase.Playing && Manager.LocalClient?.PlayerObject != null &&
            Manager.LocalClient.PlayerObject.GetComponent<RelayAvatarPlayer>().UseXRTracking;
        public void Leave(string message = "Left the room.")
        { if (!Leaving) StartCoroutine(LeaveRoutine(message)); }
        IEnumerator LeaveRoutine(string message)
        {
            Leaving = true; expectedDisconnect = true; Connecting = false; loadDeadline = 0;
            operation?.Cancel(); Status = "Disconnecting...";
            if (Manager.SceneManager != null) Manager.SceneManager.OnSceneEvent -= SceneEvent;
            Manager.Shutdown();
            while (Manager.ShutdownInProgress || Manager.IsListening || Relay.Busy) yield return null;
            Lobby = null; RealLoadProgress = -1; sceneLoad = null;
            if (SceneManager.GetActiveScene().name != MenuScene)
            { var load = SceneManager.LoadSceneAsync(MenuScene); while (load != null && !load.isDone) yield return null; }
            Status = message; Leaving = false; expectedDisconnect = false;
        }
        void OnDestroy()
        {
            operation?.Cancel(); operation?.Dispose();
            if (Instance != this) return;
            Instance = null;
            if (Manager != null)
            {
                Manager.OnClientConnectedCallback -= Connected; Manager.OnClientDisconnectCallback -= Disconnected;
                Manager.OnTransportFailure -= TransportFailed; Manager.OnServerStopped -= ServerStopped;
                Manager.ConnectionApprovalCallback = null;
            }
        }
    }
}



