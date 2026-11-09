using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Tiny UDP broadcast helper so headsets on the same Wi-Fi find each other without typing IP addresses.
/// A host broadcasts "I'm hosting a laser tag game on port X" once a second; everybody listens.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    public struct Announcement
    {
        public string address;
        public ushort gamePort;
        public string token;
        public int players;
        public float receivedAt;
    }

    const string Magic = "LTAG1";

    readonly int port;
    UdpClient socket;
    Thread receiveThread;
    volatile bool running;
    readonly ConcurrentQueue<Announcement> received = new ConcurrentQueue<Announcement>();
    AndroidJavaObject multicastLock;

    public bool IsRunning => running;

    public LanDiscovery(int port)
    {
        this.port = port;
    }

    public bool Start()
    {
        if (running) return true;
        AcquireAndroidMulticastLock();
        try
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            // Allow several game instances on one computer (e.g. Multiplayer Play Mode) to listen at once.
            try { socket.ExclusiveAddressUse = false; } catch (Exception) { /* not supported on every platform */ }
            try { socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
            catch (Exception) { /* not supported on every platform */ }
            socket.EnableBroadcast = true;
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"LanDiscovery: could not open UDP port {port}: {e.Message}");
            socket?.Close();
            socket = null;
            return false;
        }

        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Laser Tag LAN discovery" };
        receiveThread.Start();
        return true;
    }

    public void Broadcast(string token, ushort gamePort, int players)
    {
        if (socket == null) return;
        byte[] data = Encoding.UTF8.GetBytes($"{Magic}|{token}|{gamePort}|{players}");
        TrySend(data, new IPEndPoint(IPAddress.Broadcast, port));
        TrySend(data, new IPEndPoint(IPAddress.Loopback, port)); // same computer
    }

    void TrySend(byte[] data, IPEndPoint target)
    {
        try { socket.Send(data, data.Length, target); }
        catch (Exception) { /* network may be down; try again next second */ }
    }

    /// <summary>Main thread: next received announcement, if any.</summary>
    public bool TryReceive(out Announcement announcement) => received.TryDequeue(out announcement);

    void ReceiveLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = socket.Receive(ref any);
                string text = Encoding.UTF8.GetString(data);
                string[] parts = text.Split('|');
                if (parts.Length < 4 || parts[0] != Magic) continue;
                if (!ushort.TryParse(parts[2], out ushort gamePort)) continue;
                int.TryParse(parts[3], out int players);
                received.Enqueue(new Announcement
                {
                    address = any.Address.ToString(),
                    token = parts[1],
                    gamePort = gamePort,
                    players = players,
                });
            }
            catch (SocketException) { if (!running) break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception) { /* ignore malformed packets */ }
        }
    }

    public void Dispose()
    {
        running = false;
        try { socket?.Close(); } catch (Exception) { }
        socket = null;
        ReleaseAndroidMulticastLock();
    }

    // Android drops incoming broadcast packets unless the app holds a Wi-Fi multicast lock.
    void AcquireAndroidMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (AndroidJavaObject wifi = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "lasertag-discovery");
                multicastLock.Call("setReferenceCounted", false);
                multicastLock.Call("acquire");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("LanDiscovery: no Wi-Fi multicast lock, discovery may not work: " + e.Message);
            multicastLock = null;
        }
#endif
    }

    void ReleaseAndroidMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { multicastLock?.Call("release"); } catch (Exception) { }
        multicastLock?.Dispose();
#endif
        multicastLock = null;
    }
}
