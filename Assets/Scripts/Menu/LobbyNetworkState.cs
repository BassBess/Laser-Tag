using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LaserTag.Menu
{
    public enum SessionPhase : byte { Lobby, Loading, Playing }
    public struct LobbyMember : INetworkSerializable, IEquatable<LobbyMember>
    {
        public ulong Id;
        public bool Ready;
        public FixedString32Bytes Name;
        public bool Equals(LobbyMember other) => Id == other.Id && Ready == other.Ready && Name.Equals(other.Name);
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        { serializer.SerializeValue(ref Id); serializer.SerializeValue(ref Ready); serializer.SerializeValue(ref Name); }
    }

    // A session object, not a player: it survives the synchronized scene switch.
    public sealed class LobbyNetworkState : NetworkBehaviour
    {
        public NetworkList<LobbyMember> Members;
        public NetworkVariable<FixedString32Bytes> Code = new NetworkVariable<FixedString32Bytes>();
        public NetworkVariable<int> Capacity = new NetworkVariable<int>(4);
        public NetworkVariable<SessionPhase> Phase = new NetworkVariable<SessionPhase>();
        void Awake() { Members = new NetworkList<LobbyMember>(); }
        public override void OnNetworkSpawn()
        {
            MenuSessionController.Instance.AttachLobby(this);
            if (IsServer)
            {
                foreach (ulong id in NetworkManager.ConnectedClientsIds) AddMember(id);
                NetworkManager.OnClientConnectedCallback += AddMember;
                NetworkManager.OnClientDisconnectCallback += RemoveMember;
            }
        }
        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            { NetworkManager.OnClientConnectedCallback -= AddMember; NetworkManager.OnClientDisconnectCallback -= RemoveMember; }
        }
        void AddMember(ulong id)
        {
            foreach (var row in Members) if (row.Id == id) return;
            Members.Add(new LobbyMember { Id = id, Name = new FixedString32Bytes(id == Unity.Netcode.NetworkManager.ServerClientId ? "Host" : "Player " + id) });
        }
        void RemoveMember(ulong id)
        { for (int i = Members.Count - 1; i >= 0; i--) if (Members[i].Id == id) Members.RemoveAt(i); }
        public void ToggleLocalReady()
        { if (IsSpawned && Phase.Value == SessionPhase.Lobby) ToggleReadyRpc(); }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ToggleReadyRpc(RpcParams rpc = default)
        {
            if (Phase.Value != SessionPhase.Lobby) return;
            for (int i = 0; i < Members.Count; i++)
                if (Members[i].Id == rpc.Receive.SenderClientId)
                { var row = Members[i]; row.Ready = !row.Ready; Members[i] = row; return; }
        }
        public bool EveryoneReady
        { get { if (Members.Count == 0) return false; foreach (var row in Members) if (!row.Ready) return false; return true; } }
    }
}
