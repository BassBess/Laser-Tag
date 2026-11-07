using Unity.Netcode;
using UnityEngine;

/// <summary>
/// TESTING ONLY. Put this in a scene (Tools > Laser Tag > Testing > Add Practice Bots) to get computer
/// players to test against when you are alone. The host spawns them once the game is running; they are
/// normal networked players, so everyone connected sees them.
///
/// Nothing in the game depends on this: remove the object (or the whole Assets/LaserTag/Testing folder)
/// and the game runs with real players only.
/// </summary>
public class PracticeBotSpawner : MonoBehaviour
{
    public static PracticeBotSpawner Instance { get; private set; }

    [Range(1, 8)] public int botCount = 2;
    [Tooltip("Bots fire at human players they can see.")]
    public bool shootBack = true;
    [Tooltip("Chance that a bot shot hits (at close range; lower further away).")]
    [Range(0f, 1f)] public float accuracy = 0.35f;
    [Tooltip("Seconds between bot shots.")]
    public Vector2 fireInterval = new Vector2(0.7f, 1.4f);

    bool spawned;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer) { spawned = false; return; }
        if (spawned) return;

        // Wait until the game is running and the host's own player exists.
        LaserTagGameManager game = LaserTagGameManager.Instance;
        if (game == null || !game.IsSpawned || NetworkPlayer.Local == null) return;
        spawned = true;

        GameObject prefab = manager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("PracticeBotSpawner: no player prefab on the Network Manager.", this);
            return;
        }
        for (int i = 0; i < botCount; i++)
        {
            GameObject bot = Instantiate(prefab);
            bot.name = "Bot " + (i + 1);
            NetworkPlayer player = bot.GetComponent<NetworkPlayer>();
            player.serverBotName = "Bot " + (i + 1); // applied by NetworkPlayer once spawned
            bot.AddComponent<LaserTagBot>();
            bot.GetComponent<NetworkObject>().Spawn(true); // owned by the host; placed by the game like any player
        }
    }
}
