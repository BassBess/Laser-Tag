using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum PlayerLifeState : byte { Alive, Respawning, Eliminated, Spectating }

// Adapted from Hend's fea6ddf NetworkPlayer combat/lifecycle code.
// RelayAvatarPlayer remains the sole pose publisher and interpolation owner.
[DefaultExecutionOrder(110), RequireComponent(typeof(RelayAvatarPlayer), typeof(PlayerHitboxes))]
public sealed class RelayCombatPlayer : NetworkBehaviour
{
    public static readonly List<RelayCombatPlayer> All = new List<RelayCombatPlayer>();
    public static RelayCombatPlayer Local { get; private set; }
    public ScoutAvatar avatar;
    public PlayerHitboxes hitboxes;
    [Header("Rules")]
    public int maxHealth = 100, damagePerHit = 10, maxAmmo = 100;
    [Min(0)] public float refillDelay = 2, refillPerSecond = 8;
    [Min(.02f)] public float shotInterval = .25f;
    [Header("Gun pose validation (hand local, authored from original guns)")]
    public Vector3 leftMuzzleOffset, rightMuzzleOffset;
    public Vector3 leftAimDirection = Vector3.forward, rightAimDirection = Vector3.forward;
    [Range(1,90)] public float aimTolerance = 35;
    [Header("Effects")]
    public Color shotColor = new Color(1,.1f,.08f), hitFlashColor = new Color(1,.08f,.05f), protectionColor = new Color(.3f,.7f,1);
    public float hitFlashSeconds = .35f;
    public NetworkVariable<FixedString32Bytes> DisplayName = new NetworkVariable<FixedString32Bytes>();
    // Server-assigned free palette slot, retained for this player's session.
    public NetworkVariable<byte> ColorSlot = new NetworkVariable<byte>();
    public NetworkVariable<int> Health = new NetworkVariable<int>(100), Ammo = new NetworkVariable<int>(100);
    public NetworkVariable<PlayerLifeState> LifeState = new NetworkVariable<PlayerLifeState>();
    public NetworkVariable<bool> InRound = new NetworkVariable<bool>();
    public NetworkVariable<int> LivesLeft = new NetworkVariable<int>(), Kills = new NetworkVariable<int>(), Hits = new NetworkVariable<int>(), Deaths = new NetworkVariable<int>(), Placement = new NetworkVariable<int>();
    public NetworkVariable<int> HeadHits = new NetworkVariable<int>(), BodyHits = new NetworkVariable<int>();
    public NetworkVariable<double> RespawnAt = new NetworkVariable<double>(), ProtectedUntil = new NetworkVariable<double>(), LastShotAt = new NetworkVariable<double>(-999);
    public NetworkVariable<uint> AcknowledgedShot = new NetworkVariable<uint>();
    [System.NonSerialized] public float serverLastHitTime = -999;
    [System.NonSerialized] public double serverEliminatedAt;
    [System.NonSerialized] public bool serverAnnounced;
    public string Name => DisplayName.Value.ToString();
    public bool IsLocalHuman => IsOwner;
    public bool IsAlive => LifeState.Value == PlayerLifeState.Alive;
    public bool IsPresentInWorld => IsSpawned && IsAlive;
    public bool HasPose => avatar && avatar.head && IsSpawned;
    public bool IsProtected => IsSpawned && NetworkManager.ServerTime.Time < ProtectedUntil.Value;
    public bool CanShoot => IsSpawned && IsAlive && InRound.Value && LaserTagGameManager.Instance && LaserTagGameManager.Instance.Phase.Value == GamePhase.Playing;
    public Vector3 FeetPosition
    {
        get
        {
            if(IsServer && TryGetComponent<RelayAvatarPlayer>(out var movement))return movement.ServerFeet;
            if(IsLocalHuman && LocalPlayerRig.Instance) return LocalPlayerRig.Instance.FeetPosition;
            var p=transform.position;var legs=avatar?avatar.GetComponent<ScoutProceduralLegs>():null;
            if(legs&&legs.IsActive)p.y=legs.CurrentFloorHeight;return p;
        }
    }
    public int PredictedAmmo => Mathf.Max(0,Ammo.Value-pending.Count);
    public bool IsRecharging => IsSpawned && IsAlive && pending.Count==0 && Ammo.Value<maxAmmo && NetworkManager.ServerTime.Time>=LastShotAt.Value+refillDelay;
    readonly List<uint> pending = new List<uint>();
    uint localSequence, serverSeenSequence, acceptedSequence;
    int localRound, acceptedRound;
    bool claimUsed;
    Vector3 acceptedFrom, acceptedEnd;
    double acceptedTime;
    // Reliable Relay packets may batch two legitimate trigger presses. A small
    // host-time token bucket tolerates that without accepting a higher sustained rate.
    readonly double[] tokenUpdatedAt = new double[2];
    readonly float[] shotTokens = new float[2];
    float rechargeProgress, flashUntil = -1;
    Renderer[] renderers;
    bool presentationVisible = true, flashing, collisionEnabled;
    readonly List<Renderer> hidden = new List<Renderer>();
    MaterialPropertyBlock[] originalBlocks;
    MaterialPropertyBlock effect;
    double nextShotDiagnostic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics(){All.Clear();Local=null;}
    void Awake()
    {
        effect=new MaterialPropertyBlock();
        if(!avatar)avatar=GetComponent<ScoutAvatar>();if(!hitboxes)hitboxes=GetComponent<PlayerHitboxes>();
        hitboxes.EnsureCreated();renderers=avatar.GetComponentsInChildren<Renderer>(true);originalBlocks=new MaterialPropertyBlock[renderers.Length];
        for(int i=0;i<originalBlocks.Length;i++)originalBlocks[i]=new MaterialPropertyBlock();
    }
    public override void OnNetworkSpawn()
    {
        All.Add(this);if(IsOwner)Local=this;
        Health.OnValueChanged+=HealthChanged;LifeState.OnValueChanged+=LifeChanged;AcknowledgedShot.OnValueChanged+=ShotAcknowledged;
        if(IsServer)
        {
            int used=0;
            foreach(var player in All)if(player!=this&&player.IsSpawned)used|=1<<player.ColorSlot.Value;
            byte slot=0;while(slot<7&&(used&(1<<slot))!=0)slot++;
            ColorSlot.Value=slot;
            var displayName=new FixedString32Bytes("Player "+(OwnerClientId+1));
            var menu=LaserTag.Menu.MenuSessionController.Instance;
            if(menu&&menu.Lobby)foreach(var member in menu.Lobby.Members)
                if(member.Id==OwnerClientId){displayName=member.Name;break;}
            DisplayName.Value=displayName;Health.Value=maxHealth;ResetAmmoServer();
        }
        if(TryGetComponent<PlayerIdentity>(out var identity))identity.Initialize((int)OwnerClientId+1,Name);
        if(IsServer&&LaserTagGameManager.Instance)LaserTagGameManager.Instance.ServerOnPlayerJoined(this);
        RefreshPresentation(true);
        var label=GetComponent<RelayPlayerPresentation>();
        if(!label)label=gameObject.AddComponent<RelayPlayerPresentation>();
        label.Initialize(this);
    }
    public override void OnNetworkDespawn()
    {
        var label=GetComponent<RelayPlayerPresentation>();if(label)Destroy(label);
        All.Remove(this);if(Local==this)Local=null;
        Health.OnValueChanged-=HealthChanged;LifeState.OnValueChanged-=LifeChanged;AcknowledgedShot.OnValueChanged-=ShotAcknowledged;
        if(IsServer&&LaserTagGameManager.Instance)LaserTagGameManager.Instance.ServerOnPlayerLeft(this);
    }
    public override void OnDestroy(){All.Remove(this);if(Local==this)Local=null;base.OnDestroy();}
    void Update()
    {
        if(!IsSpawned)return;
        if(IsServer&&IsAlive&&IsRecharging){rechargeProgress+=refillPerSecond*Time.deltaTime;int amount=(int)rechargeProgress;if(amount>0){rechargeProgress-=amount;Ammo.Value=Mathf.Min(maxAmmo,Ammo.Value+amount);}}
        RefreshPresentation(false);UpdateFlash();
    }
    public void ClearPrediction(){pending.Clear();}
    void ShotAcknowledged(uint old,uint current)
    {
        for(int i=pending.Count-1;i>=0;i--)if(pending[i]<=current)pending.RemoveAt(i);
    }
    public void ResetAmmoServer()
    {
        if(!IsServer)return;Ammo.Value=maxAmmo;rechargeProgress=0;LastShotAt.Value=-999;acceptedSequence=0;claimUsed=true;
        shotTokens[0]=shotTokens[1]=2;
        tokenUpdatedAt[0]=tokenUpdatedAt[1]=NetworkManager.ServerTime.Time;
    }
    public void OwnerFired(Vector3 from,Vector3 to,int gun)
    {
        if(!IsOwner||!IsSpawned)return;
        localSequence++;localRound=LaserTagGameManager.Instance?LaserTagGameManager.Instance.RoundNumber.Value:0;
        pending.Add(localSequence);SubmitShotRpc(localSequence,localRound,gun,from,to);
    }
    [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
    void SubmitShotRpc(uint sequence,int round,int gun,Vector3 from,Vector3 to)
    {
        if(sequence<=serverSeenSequence)return;
        serverSeenSequence=sequence;
        try
        {
            var game=LaserTagGameManager.Instance;
            if(!game||!CanShoot||round!=game.RoundNumber.Value){RejectShot("round/life state");return;}
            if(gun!=1){RejectShot("only right gun index 1 is permitted");return;}
            if(Ammo.Value<=0){RejectShot("empty ammunition");return;}
            if(!Finite(from)||!Finite(to)){RejectShot("non-finite ray");return;}
            double now=NetworkManager.ServerTime.Time;
            shotTokens[gun]=Mathf.Min(2,shotTokens[gun]+(float)System.Math.Max(0,now-tokenUpdatedAt[gun])/Mathf.Max(.02f,shotInterval));
            tokenUpdatedAt[gun]=now;
            if(shotTokens[gun]<1){RejectShot("shot rate exceeded");return;}
            Transform hand=avatar?avatar.rightHand:null;
            if(!hand){RejectShot("missing right avatar hand");return;}
            Vector3 direction=hand.rotation*rightAimDirection;
            Vector3 expected=hand.position+hand.rotation*rightMuzzleOffset;
            Vector3 ray=to-from;
            if(Vector3.Distance(expected,from)>game.gunToHandTolerance){RejectShot("muzzle away from right hand");return;}
            if(ray.magnitude>game.maxShotRange+.05f||ray.sqrMagnitude<.000001f){RejectShot("invalid ray range");return;}
            if(Vector3.Angle(direction,ray)>aimTolerance){RejectShot("aim differs from right hand");return;}
            Ammo.Value--;LastShotAt.Value=now;rechargeProgress=0;shotTokens[gun]--;
            acceptedSequence=sequence;acceptedRound=round;acceptedFrom=from;acceptedEnd=to;acceptedTime=now;claimUsed=false;
            AcceptedShotOwnerRpc();
            if(!IsOwner)ShotBeamFx.Play(from,to,shotColor);
            RemoteShotRpc(from,to);
        }
        finally
        {
            // Reconcile both accepted and rejected predictions after the ammo
            // decision. The host also observes this callback synchronously.
            AcknowledgedShot.Value=sequence;
        }
    }
    void RejectShot(string reason)
    {
        double now=NetworkManager.ServerTime.Time;
        if(now<nextShotDiagnostic)return;
        nextShotDiagnostic=now+5;
        Debug.LogWarning($"[Combat] Rejected shot from owner {OwnerClientId}: {reason}. Ammo={Ammo.Value}, life={LifeState.Value}, inRound={InRound.Value}. Further rejections suppressed for 5s.",this);
    }
    [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
    void AcceptedShotOwnerRpc(){LaserTagFeedback.PlayLocalShot();}
    [Rpc(SendTo.NotServer,InvokePermission=RpcInvokePermission.Server,Delivery=RpcDelivery.Unreliable)]
    void RemoteShotRpc(Vector3 from,Vector3 to){if(!IsOwner)ShotBeamFx.Play(from,to,shotColor);}
    public void OwnerReportHit(RelayCombatPlayer victim,Vector3 from,Vector3 point,HitboxPart part)
    {if(IsOwner&&IsSpawned&&victim&&victim.IsSpawned)ReportHitRpc(localSequence,localRound,new NetworkObjectReference(victim.NetworkObject),from,point,part);}
    [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
    void ReportHitRpc(uint sequence,int round,NetworkObjectReference target,Vector3 from,Vector3 point,HitboxPart claimedPart)
    {
        if(sequence!=acceptedSequence||round!=acceptedRound||claimUsed||NetworkManager.ServerTime.Time-acceptedTime>1.25||!Finite(point)||!Finite(from)||Vector3.Distance(from,acceptedFrom)>.02f)return;
        claimUsed=true;Vector3 ray=acceptedEnd-acceptedFrom,path=point-from;
        if(path.magnitude>ray.magnitude+.05f||Vector3.Angle(ray,path)>.5f)return;
        if(!target.TryGet(out NetworkObject obj)||!obj.TryGetComponent<RelayCombatPlayer>(out var victim)||victim==this)return;
        var game=LaserTagGameManager.Instance;
        if(game&&game.ServerValidateShot(this,victim,from,point,claimedPart,out var actualPart,out _))
        {
            int before=victim.Health.Value;game.ServerHandleHit(this,victim,actualPart,true);
            if(victim.Health.Value<before)ConfirmHitOwnerRpc(target,victim.Health.Value==0);
        }
    }
    [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
    void ConfirmHitOwnerRpc(NetworkObjectReference target,bool tagged)
    {
        LaserTagFeedback.PlayHitConfirm();if(PlayerHud.Instance){PlayerHud.Instance.ShowHitMarker();if(tagged&&target.TryGet(out var obj)&&obj.TryGetComponent<RelayCombatPlayer>(out var victim))PlayerHud.Instance.ShowKill(victim.Name);}
    }
    static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&!float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
    public void ServerTeleport(Vector3 feet,float yaw)
    {
        if(IsServer && TryGetComponent<RelayAvatarPlayer>(out var movement))movement.ServerPlace(feet,yaw);
    }
    void RefreshPresentation(bool force)
    {
        if(force||collisionEnabled!=IsPresentInWorld){collisionEnabled=IsPresentInWorld;hitboxes.SetCollisionEnabled(collisionEnabled);}
        bool visible=IsPresentInWorld&&(!IsOwner||!LocalPlayerRig.Instance);
        if(!force&&visible==presentationVisible)return;presentationVisible=visible;
        foreach(var r in renderers)if(r&&!visible&&r.enabled){r.enabled=false;hidden.Add(r);}
        if(visible){foreach(var r in hidden)if(r)r.enabled=true;hidden.Clear();}
        // Trigger hitboxes remain present on the host's own player for server ray validation.
        // They never push the local rig or get mistaken for a floor by visual leg raycasts.
    }
    void HealthChanged(int old,int value)
    {
        if(value>=old)return;
        if(IsLocalHuman){if(PlayerHud.Instance)PlayerHud.Instance.FlashDamage();if(LocalPlayerRig.Instance)LocalPlayerRig.Instance.OnDamaged();}
        else flashUntil=Time.time+hitFlashSeconds;
    }
    void LifeChanged(PlayerLifeState old,PlayerLifeState value){if(IsLocalHuman&&value==PlayerLifeState.Alive&&LocalPlayerRig.Instance)LocalPlayerRig.Instance.RefillAmmo();}
    void UpdateFlash()
    {
        bool active=presentationVisible&&(Time.time<flashUntil||IsProtected);
        if(active&&!flashing){for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].GetPropertyBlock(originalBlocks[i]);flashing=true;}
        if(!active){if(flashing){for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].SetPropertyBlock(originalBlocks[i]);flashing=false;}return;}
        bool hurt=Time.time<flashUntil;float k=hurt?Mathf.Clamp01((flashUntil-Time.time)/Mathf.Max(.01f,hitFlashSeconds)):.5f+.5f*Mathf.Sin(Time.time*12);
        Color color=Color.Lerp(Color.white,hurt?hitFlashColor:protectionColor,.35f+.45f*k);
        for(int i=0;i<renderers.Length;i++)if(renderers[i]){renderers[i].GetPropertyBlock(effect);effect.SetColor("_BaseColor",color);effect.SetColor("_Color",color);renderers[i].SetPropertyBlock(effect);}
    }
}
