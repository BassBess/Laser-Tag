using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Network state belongs to the existing scene match object. Clients only render it.
public sealed class HealthPickupSystem : MonoBehaviour
{
    [Range(0, 12)] public int maximumActivePickups = 3;
    [Min(1)] public int healthRestored = 5;
    [Min(1)] public float respawnInterval = 12;
    [Range(.2f, 1.5f)] public float collectionRadius = .7f;
    [Min(.5f)] public float minimumSpacing = 3;
    [Min(0)] public float minimumPlayerDistance = 2;
    [Range(.05f,.3f)] public float orbSize = .18f;
    public Color glowColor = new Color(.15f, 1f, .35f);
    public bool animate = true;
    static readonly Vector3 Inactive = new Vector3(0, -10000, 0);
    LaserTagGameManager game;
    LeftHandArcTeleport teleport;
    readonly List<Transform> ignore = new List<Transform>();
    readonly List<Vector3> anchors = new List<Vector3>();
    readonly List<GameObject> visuals = new List<GameObject>();
    readonly List<double> respawnAt = new List<double>();
    Material material;
    double nextTick;
    bool wasPlaying;

    void Start()
    {
        game = GetComponent<LaserTagGameManager>();
        var rig = LocalPlayerRig.Instance;
        teleport = rig ? rig.GetComponent<LeftHandArcTeleport>() : null;
        var template = Resources.Load<Material>("LaserTagPickup");
        if (template) { material = new Material(template); material.SetColor("_BaseColor", glowColor); }
        else Debug.LogError("Missing LaserTagPickup material", this);
    }
    void Update()
    {
        if (!game || !game.IsSpawned) { HideAll(); return; }
        bool playing = game.Phase.Value == GamePhase.Playing;
        if (game.IsServer && game.ServerNow >= nextTick)
        {
            nextTick = game.ServerNow + .15;
            if (!playing)
            {
                if (game.HealthPickups.Count > 0) game.HealthPickups.Clear();
                respawnAt.Clear();
            }
            else
            {
                int count = Mathf.Clamp(maximumActivePickups, 0, 12);
                if (!wasPlaying || game.HealthPickups.Count != count)
                {
                    game.HealthPickups.Clear(); respawnAt.Clear();
                    for (int i=0;i<count;i++) { game.HealthPickups.Add(Inactive); respawnAt.Add(game.ServerNow); }
                }
                ServerTick();
            }
            wasPlaying = playing;
        }
        for (int i=0;i<game.HealthPickups.Count;i++)
        {
            if (i >= visuals.Count) CreateVisual();
            Vector3 feet = game.HealthPickups[i];
            bool active = playing && feet != Inactive;
            visuals[i].SetActive(active);
            if (!active) continue;
            visuals[i].transform.position = feet + Vector3.up * (.65f + (animate ? Mathf.Sin(Time.time*2+i)*.045f : 0));
            visuals[i].transform.localScale = Vector3.one * orbSize;
        }
        for (int i=game.HealthPickups.Count;i<visuals.Count;i++) visuals[i].SetActive(false);
    }
    void ServerTick()
    {
        for (int i=0;i<game.HealthPickups.Count;i++)
        {
            var feet = game.HealthPickups[i];
            if (feet == Inactive)
            {
                if (game.ServerNow >= respawnAt[i])
                {
                    if (TrySpawn(out var point)) game.HealthPickups[i] = point;
                    else respawnAt[i] = game.ServerNow + 1; // bounded retries, no accumulation
                }
                continue;
            }
            foreach (var player in RelayCombatPlayer.All)
            {
                if (!player || !player.IsSpawned || !player.InRound.Value || !player.IsAlive || player.Health.Value >= player.maxHealth) continue;
                if (!player.TryGetComponent<RelayAvatarPlayer>(out var movement) || !movement.ServerTrackingReady) continue;
                Vector3 from = movement.ServerFeet;
                if ((from-feet).sqrMagnitude > collectionRadius*collectionRadius) continue;
                // Distance alone must never heal through thin cover.
                if (Physics.Linecast(from+Vector3.up*.65f, feet+Vector3.up*.65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                game.HealthPickups[i] = Inactive; // consume before changing health or sending feedback
                respawnAt[i] = game.ServerNow + Mathf.Max(1, respawnInterval);
                int before = player.Health.Value;
                player.Health.Value = Mathf.Min(player.maxHealth, before + Mathf.Max(1, healthRestored));
                player.HealingFeedbackRpc(player.Health.Value-before);
                break;
            }
        }
    }
    bool TrySpawn(out Vector3 point)
    {
        point = default;
        if (!teleport || !game.spawnManager) return false;
        anchors.Clear();
        foreach (var spawn in game.spawnManager.spawnPoints)
            if (spawn && spawn.initialSpawn) anchors.Add(spawn.transform.position);
        foreach (var player in RelayCombatPlayer.All)
            if (player && player.IsPresentInWorld && player.TryGetComponent<RelayAvatarPlayer>(out var movement) && movement.ServerTrackingReady)
                anchors.Add(movement.ServerFeet);
        if (anchors.Count == 0) return false;
        TeleportClearance.LocalPlayerRoots(ignore);
        // Trace actual teleport arcs from reachable anchors, rather than sampling arbitrary mesh bounds.
        for (int attempt=0;attempt<12;attempt++)
        {
            Vector3 source = anchors[Random.Range(0, anchors.Count)];
            if (!TeleportClearance.IsSpawnSupported(teleport,source,teleport.bodyRadius,ignore)) continue;
            Vector3 forward = Quaternion.Euler(Random.Range(-55f,45f), Random.Range(0f,360f),0)*Vector3.forward;
            Vector3 hand = source+Vector3.up*1.2f;
            if (!TeleportClearance.TryValidateSource(source+Vector3.up*1.6f,hand,forward,ignore,out _)) continue;
            if (!TeleportClearance.TryTraceArc(teleport,hand,forward,ignore,null,out _,out var landing,out _)) continue;
            if (!TeleportClearance.TryValidateLanding(teleport,landing,source,1.7f,ignore,null,out _)) continue;
            if (!TeleportClearance.IsSpawnSupported(teleport,landing.point,teleport.bodyRadius,ignore)) continue;
            if (!TeleportClearance.IsFarFromPlayers(landing.point,minimumPlayerDistance)) continue;
            bool spaced = true;
            foreach (var other in game.HealthPickups)
                if (other != Inactive && Vector3.Distance(other,landing.point)<minimumSpacing) { spaced=false; break; }
            if (!spaced) continue;
            point = landing.point; return true;
        }
        return false;
    }
    void CreateVisual()
    {
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Health +"+healthRestored; orb.layer = 2;
        orb.transform.SetParent(transform,false);
        var collider = orb.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = orb.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        visuals.Add(orb);
    }
    void HideAll() { foreach (var visual in visuals) if (visual) visual.SetActive(false); }
    void OnDestroy() { foreach (var visual in visuals) if (visual) Destroy(visual); if (material) Destroy(material); }
}
