using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public class MultiplayerConnectionUI : MonoBehaviour
{
    private NetworkManager manager;
    private UnityTransport transport;

    private string address = "127.0.0.1";
    private string portText = "7777";
    private string message = "Ready to connect";

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(
            new Rect(20, 20, 350, 250),
            GUI.skin.box
        );

        GUILayout.Label("LASER TAG MULTIPLAYER");

        if (!manager.IsListening)
        {
            GUILayout.Label("Host IP address:");
            address = GUILayout.TextField(address);

            GUILayout.Label("Port:");
            portText = GUILayout.TextField(portText);

            if (GUILayout.Button("START HOST"))
                StartConnection(true);

            if (GUILayout.Button("JOIN AS CLIENT"))
                StartConnection(false);
        }
        else
        {
            string mode = manager.IsHost
                ? "HOST RUNNING"
                : manager.IsConnectedClient
                    ? "CLIENT CONNECTED"
                    : "CLIENT CONNECTING";

            GUILayout.Label("Status: " + mode);

            if (manager.IsServer)
            {
                GUILayout.Label(
                    "Connected players: " +
                    manager.ConnectedClientsIds.Count
                );
            }

            if (GUILayout.Button("DISCONNECT / STOP"))
            {
                manager.Shutdown();
                message = "Disconnected";
            }
        }

        GUILayout.Label(message);
        GUILayout.EndArea();
    }

    private void StartConnection(bool host)
    {
        if (!ushort.TryParse(portText, out ushort port)
            || port == 0)
        {
            message = "Invalid port";
            return;
        }

        if (!host && string.IsNullOrWhiteSpace(address))
        {
            message = "Enter a host IP address";
            return;
        }

        transport.SetConnectionData(
            host ? "127.0.0.1" : address.Trim(),
            port,
            host ? "0.0.0.0" : null
        );

        bool started = host
            ? manager.StartHost()
            : manager.StartClient();

        message = started
            ? host ? "Host started" : "Connecting..."
            : "Failed to start networking";

        Debug.Log("[Multiplayer] " + message);
    }
}
