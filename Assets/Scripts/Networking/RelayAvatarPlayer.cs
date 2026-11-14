using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// The server owns the virtual origin AND every replicated pose. Owners submit
// only bounded tracking offsets; local XR tracking is never delayed by this stream.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(NetworkObject), typeof(AvatarPoseDriver), typeof(ScoutAvatar))]
public sealed class RelayAvatarPlayer : NetworkBehaviour
{
    // Version 4 adds the server-assigned player color to the combat snapshot.
    public const ushort MovementProtocolVersion = 4;
    // Retained serialized desktop settings; continuous translation stays disabled.
    public float speed = 2.2f, sprintMultiplier = 1.6f, turnSpeed = 100f, poseSendHz = 15f;
    [Header("Server tracking envelope (metres, relative to physical tracking origin)")]
    [Min(.5f)] public float roomScaleRadius = 2f;
    [Min(.5f)] public float maximumHeadHeight = 2.6f;
    [Min(.5f)] public float maximumHandReach = 1.65f;
    [Min(1f)] public float maximumHeadSpeed = 3f;
    [Tooltip("Transport/pose sampling slack, not additional locomotion range.")]
    public float trackingSlack = .15f;
    [Tooltip("Minimum interval between manual teleport requests. Round/respawn placement is exempt.")]
    public float teleportRequestInterval = .25f;

    public struct TrackingPose : INetworkSerializable, IEquatable<TrackingPose>
    {
        public Vector3 Eye, Body, Head, Left, Right, Controller;
        public Quaternion EyeRotation, BodyRotation, HeadRotation, LeftRotation, RightRotation, ControllerRotation;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Eye);s.SerializeValue(ref Body);s.SerializeValue(ref Head);
            s.SerializeValue(ref Left);s.SerializeValue(ref Right);s.SerializeValue(ref Controller);
            s.SerializeValue(ref EyeRotation);s.SerializeValue(ref BodyRotation);s.SerializeValue(ref HeadRotation);
            s.SerializeValue(ref LeftRotation);s.SerializeValue(ref RightRotation);s.SerializeValue(ref ControllerRotation);
        }
        public bool Equals(TrackingPose p) => Eye==p.Eye && Body==p.Body && Head==p.Head && Left==p.Left && Right==p.Right && Controller==p.Controller &&
            EyeRotation==p.EyeRotation && BodyRotation==p.BodyRotation && HeadRotation==p.HeadRotation && LeftRotation==p.LeftRotation && RightRotation==p.RightRotation && ControllerRotation==p.ControllerRotation;
    }
    public struct AuthorityPose : INetworkSerializable, IEquatable<AuthorityPose>
    {
        public Vector3 Origin, Landing;
        public float Yaw;
        public uint Revision;
        public TrackingPose Tracking;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Origin);s.SerializeValue(ref Landing);s.SerializeValue(ref Yaw);
            s.SerializeValue(ref Revision);s.SerializeValue(ref Tracking);
        }
        public bool Equals(AuthorityPose p) => Origin==p.Origin && Landing==p.Landing && Yaw==p.Yaw && Revision==p.Revision && Tracking.Equals(p.Tracking);
    }
    // One coherent server-written snapshot prevents mixed pre/post-teleport poses.
    readonly NetworkVariable<AuthorityPose> state = new NetworkVariable<AuthorityPose>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    AvatarPoseDriver driver;
    ScoutAvatar avatar;
    RelayCombatPlayer combat;
    ScoutLocalRigBinding localBinding;
    float nextSend, lookPitch, lookYaw, handRaise;
    uint poseSequence, serverPoseSequence, requestSequence, serverRequestSequence;
    uint localRevision, displayedRevision;
    Vector3 localOrigin;
    float localYaw;
    bool requestPending, firstRemoteFrame = true, hasServerTracking;
    uint pendingRequest;
    double lastPoseAt, nextRequestAt, tokenAt, nextDiagnostic;
    float nextTeleportDiagnostic;
    float poseTokens = 3;
    public bool UseXRTracking { get; private set; }
    public bool TeleportPending => requestPending;
    public uint OriginRevision => state.Value.Revision;
    public bool ServerTrackingReady => hasServerTracking;
    public Vector3 ServerFeet => state.Value.Origin + Rotation * Horizontal(state.Value.Tracking.Eye);
    // Kept for diagnostics/compatibility; occupancy no longer reserves this history.
    public Vector3 ReservedLanding => state.Value.Landing;
    public Vector3 ServerEye => state.Value.Origin + Rotation * state.Value.Tracking.Eye;
    public Vector3 ServerLeftController => state.Value.Origin + Rotation * state.Value.Tracking.Controller;
    public Vector3 ServerLeftForward => Rotation * state.Value.Tracking.ControllerRotation * Vector3.forward;
    Quaternion Rotation => Quaternion.Euler(0,state.Value.Yaw,0);
    static Vector3 Horizontal(Vector3 v) => new Vector3(v.x,0,v.z);
    static float TrackingYaw(Quaternion rotation)
    {
        Vector3 forward=Horizontal(rotation*Vector3.forward);
        return forward.sqrMagnitude>.001f?Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg:0f;
    }

    public void SetXRTracking(bool value) { if(IsSpawned && IsOwner)UseXRTracking=value; }
    public override void OnNetworkSpawn()
    {
        driver=GetComponent<AvatarPoseDriver>();avatar=GetComponent<ScoutAvatar>();combat=GetComponent<RelayCombatPlayer>();
        driver.SetPoseTargets(avatar.head,avatar.leftHand,avatar.rightHand);
        avatar.localCamera=null;
        foreach(var binding in GetComponentsInChildren<ScoutLocalRigBinding>(true))binding.enabled=false;
        name="NetworkPlayer_"+OwnerClientId+(IsOwner?"_YOU":"_REMOTE");
        state.OnValueChanged+=StateChanged;
        if(IsServer)
        {
            var p=DefaultTracking();
            state.Value=new AuthorityPose{Origin=transform.position,Landing=transform.position,Revision=1,Tracking=p};
            tokenAt=NetworkManager.ServerTime.Time;
        }
        if(IsOwner)
        {
            AcceptOrigin(state.Value.Origin,state.Value.Yaw,state.Value.Revision);
            var camera=FindAnyObjectByType<RelayThirdPersonCamera>();if(camera)camera.Follow(transform);
        }
        ApplyState(1);
    }
    public override void OnNetworkDespawn()
    {
        state.OnValueChanged-=StateChanged;
        requestPending=false;localBinding=null;UseXRTracking=false;
    }
    static TrackingPose DefaultTracking() => new TrackingPose{
        Eye=new Vector3(0,1.72f,0),Head=new Vector3(0,1.72f,0),
        Left=new Vector3(-.3f,1.28f,.26f),Right=new Vector3(.3f,1.28f,.26f),Controller=new Vector3(-.3f,1.28f,.26f),
        EyeRotation=Quaternion.identity,BodyRotation=Quaternion.identity,HeadRotation=Quaternion.identity,
        LeftRotation=Quaternion.identity,RightRotation=Quaternion.identity,ControllerRotation=Quaternion.identity};
    void StateChanged(AuthorityPose previous,AuthorityPose current)
    {
        if(IsOwner)AcceptOrigin(current.Origin,current.Yaw,current.Revision);
    }
    void AcceptOrigin(Vector3 origin,float yaw,uint revision)
    {
        if(!IsOwner || revision==0 || revision<localRevision)return;
        if(revision>localRevision)requestPending=false;
        localRevision=revision;localOrigin=origin;localYaw=yaw;
        if(LocalPlayerRig.Instance)LocalPlayerRig.Instance.ApplyApprovedOrigin(localOrigin,localYaw);
    }
    void Update()
    {
        if(!IsSpawned)return;
        // Correct accidental local locomotion without touching camera/controller
        // local transforms. Forged world transforms are never serialized.
        if(IsOwner && localRevision!=0 && LocalPlayerRig.Instance)
            LocalPlayerRig.Instance.ApplyApprovedOrigin(localOrigin,localYaw);
        if(!IsOwner && !IsServer)
            ApplyState(firstRemoteFrame || displayedRevision!=state.Value.Revision ? 1 : 1-Mathf.Exp(-13*Time.deltaTime));
        else if(IsOwner && !IsServer)
            ApplyState(1); // hidden owner mirror also follows validated snapshots
    }
    void LateUpdate()
    {
        if(!IsSpawned)return;
        if(IsOwner && !UseXRTracking && !LocalPlayerRig.Instance && Time.time>=nextSend)
        {
            nextSend=Time.time+1/Mathf.Clamp(poseSendHz,5,30);
            var p=DefaultTracking();var k=Keyboard.current;
            if(k!=null)
            {
                lookYaw+=((k.eKey.isPressed?1:0)-(k.qKey.isPressed?1:0))*turnSpeed*Time.deltaTime;
                lookPitch=Mathf.Clamp(lookPitch+((k.upArrowKey.isPressed?1:0)-(k.downArrowKey.isPressed?1:0))*20*Time.deltaTime,-40,40);
                handRaise=k.spaceKey.isPressed?1:0;
            }
            // Desktop diagnostic poses also pass the same server envelope.
            p.EyeRotation=p.BodyRotation=p.HeadRotation=Quaternion.Euler(-lookPitch,lookYaw,0);
            p.Left+=Vector3.up*handRaise*.15f;p.Right+=Vector3.up*handRaise*.15f;
            SubmitTrackingRpc(++poseSequence,localRevision,p);
        }
        if(IsServer)ApplyState(1); // authoritative hitboxes never interpolate
    }
    public void PublishTrackedPose(ScoutLocalRigBinding binding)
    {
        if(!IsOwner || !IsSpawned || !binding || !binding.HasValidReferences || localRevision==0)return;
        localBinding=binding;UseXRTracking=true;
        if(Time.time<nextSend || requestPending)return;
        nextSend=Time.time+1/Mathf.Clamp(poseSendHz,5,30);
        SubmitTrackingRpc(++poseSequence,localRevision,Capture(binding));
    }
    TrackingPose Capture(ScoutLocalRigBinding b)
    {
        Transform origin=b.xrOrigin;Quaternion inverse=Quaternion.Inverse(origin.rotation);
        return new TrackingPose{
            Eye=origin.InverseTransformPoint(b.trackedCamera.transform.position),EyeRotation=inverse*b.trackedCamera.transform.rotation,
            Body=origin.InverseTransformPoint(b.avatar.transform.position),BodyRotation=inverse*b.avatar.transform.rotation,
            Head=origin.InverseTransformPoint(b.avatar.head.position),HeadRotation=inverse*b.avatar.head.rotation,
            Left=origin.InverseTransformPoint(b.avatar.leftHand.position),LeftRotation=inverse*b.avatar.leftHand.rotation,
            Right=origin.InverseTransformPoint(b.avatar.rightHand.position),RightRotation=inverse*b.avatar.rightHand.rotation,
            Controller=origin.InverseTransformPoint(b.leftController.position),ControllerRotation=inverse*b.leftController.rotation};
    }
    [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner,Delivery=RpcDelivery.Unreliable)]
    void SubmitTrackingRpc(uint sequence,uint revision,TrackingPose p)
    {
        if(revision!=state.Value.Revision || sequence<=serverPoseSequence)return;
        double now=NetworkManager.ServerTime.Time;
        poseTokens=Mathf.Min(3,poseTokens+(float)(now-tokenAt)*30);tokenAt=now;
        if(poseTokens<1)return;poseTokens--;
        if(!TryAcceptTracking(sequence,p,out var reason))MovementDiagnostic(reason);
    }
    bool TryAcceptTracking(uint sequence,TrackingPose p,out string reason)
    {
        reason=null;
        if(sequence<=serverPoseSequence){reason="Tracking/stale sample sequence";return false;}
        reason=TrackingPoseRejection(p,roomScaleRadius,maximumHeadHeight,maximumHandReach);
        if(reason!=null)return false;
        double now=NetworkManager.ServerTime.Time;
        var previous=state.Value;
        if(hasServerTracking && Vector3.Distance(previous.Tracking.Eye,p.Eye)>
            maximumHeadSpeed*(float)Math.Min(2,Math.Max(0,now-lastPoseAt))+trackingSlack)
        {reason="tracking speed exceeded";return false;}
        // The first headset sample aligns the physical tracking offset to the
        // server-selected spawn, rather than displacing that spawn by a user's
        // initial stage offset. It cannot choose a new landing location.
        bool calibrate=!hasServerTracking;
        if(calibrate)
        {
            float facing=previous.Yaw+TrackingYaw(previous.Tracking.EyeRotation);
            previous.Yaw=facing-TrackingYaw(p.EyeRotation);
            previous.Origin=previous.Landing-Quaternion.Euler(0,previous.Yaw,0)*Horizontal(p.Eye);
            previous.Revision++;
        }
        // A head cannot traverse solid cover simply by claiming room-scale input.
        Vector3 nextEye=previous.Origin+Quaternion.Euler(0,previous.Yaw,0)*p.Eye;
        if(!TeleportClearance.IsTrackedHeadPathFree(this,nextEye,hasServerTracking,out reason))return false;
        previous.Tracking=p;state.Value=previous;
        serverPoseSequence=sequence;lastPoseAt=now;hasServerTracking=true;
        if(calibrate)ApprovedOriginOwnerRpc(previous.Origin,previous.Yaw,previous.Revision);
        ApplyState(1);
        return true;
    }
    public bool ValidateTracking(TrackingPose p)
        => ValidateTrackingPose(p,roomScaleRadius,maximumHeadHeight,maximumHandReach);

    public static bool ValidateTrackingPose(TrackingPose p,float roomScaleRadius=2f,float maximumHeadHeight=2.6f,float maximumHandReach=1.65f)
        => TrackingPoseRejection(p,roomScaleRadius,maximumHeadHeight,maximumHandReach)==null;

    public static string TrackingPoseRejection(TrackingPose p,float roomScaleRadius=2f,float maximumHeadHeight=2.6f,float maximumHandReach=1.65f)
    {
        if(!Finite(roomScaleRadius)||!Finite(maximumHeadHeight)||!Finite(maximumHandReach))return "Tracking/server envelope configuration is non-finite";
        if(!Finite(p.Eye))return "Tracking/non-finite eye";
        if(!Finite(p.Body))return "Tracking/non-finite body";
        if(!Finite(p.Head))return "Tracking/non-finite visual head";
        if(!Finite(p.Left))return "Tracking/non-finite left hand";
        if(!Finite(p.Right))return "Tracking/non-finite right hand";
        if(!Finite(p.Controller))return "Tracking/non-finite left controller";
        if(!ValidRotation(p.EyeRotation))return "Tracking/invalid eye rotation";
        if(!ValidRotation(p.BodyRotation))return "Tracking/invalid body rotation";
        if(!ValidRotation(p.HeadRotation))return "Tracking/invalid visual head rotation";
        if(!ValidRotation(p.LeftRotation))return "Tracking/invalid left hand rotation";
        if(!ValidRotation(p.RightRotation))return "Tracking/invalid right hand rotation";
        if(!ValidRotation(p.ControllerRotation))return "Tracking/invalid left controller rotation";
        if(Horizontal(p.Eye).magnitude>roomScaleRadius)return "Tracking/physical play-area radius exceeded";
        if(p.Eye.y<.2f || p.Eye.y>maximumHeadHeight)return "Tracking/eye height outside envelope";
        if(Horizontal(p.Body-p.Eye).magnitude>.3f)return "Tracking/body horizontal offset exceeded";
        if(p.Body.y<-.8f || p.Body.y>.3f)return "Tracking/body height outside envelope";
        if(Horizontal(p.Head-p.Eye).magnitude>.35f || Vector3.Distance(p.Head,p.Eye)>.85f)return "Tracking/visual head offset exceeded";
        if(Vector3.Distance(p.Left,p.Eye)>maximumHandReach)return "Tracking/left hand reach exceeded";
        if(Vector3.Distance(p.Right,p.Eye)>maximumHandReach)return "Tracking/right hand reach exceeded";
        if(Vector3.Distance(p.Controller,p.Eye)>maximumHandReach)return "Tracking/left controller reach exceeded";
        return null;
    }
    public string PreviewTrackingRejection()
    {
        if(localRevision==0)return "Client/origin not initialized";
        if(!localBinding)return "Client/tracking binding unavailable";
        return TrackingPoseRejection(Capture(localBinding),roomScaleRadius,maximumHeadHeight,maximumHandReach);
    }
    public static bool Finite(Vector3 p) => Finite(p.x)&&Finite(p.y)&&Finite(p.z);
    static bool Finite(float v) => !float.IsNaN(v)&&!float.IsInfinity(v);
    static bool ValidRotation(Quaternion q)
    {
        if(!Finite(q.x)||!Finite(q.y)||!Finite(q.z)||!Finite(q.w))return false;
        float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
        return Mathf.Abs(norm-1)<.01f;
    }
    public bool RequestTeleport(Vector3 feet)
    {
        string unavailable=!IsOwner?"Client/not owner":!IsSpawned?"Client/not spawned":
            requestPending?"Client/request pending":localRevision==0?"Client/origin not initialized":!localBinding?"Client/tracking binding unavailable":null;
        if(unavailable!=null){LogTeleportRejection(0,feet,unavailable,"submit");return false;}
        localBinding.ApplyPose(); // sample the release frame, including current controller aim
        requestPending=true;pendingRequest=++requestSequence;
        var game=LaserTagGameManager.Instance;
        RequestTeleportRpc(pendingRequest,localRevision,game?game.RoundNumber.Value:-1,feet,++poseSequence,Capture(localBinding));
        return true;
    }
    [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Owner)]
    void RequestTeleportRpc(uint request,uint revision,int round,Vector3 feet,uint trackingSequence,TrackingPose tracking)
    {
        if(request<=serverRequestSequence){LogTeleportRejection(request,feet,"Request/stale or duplicate sequence");return;}
        serverRequestSequence=request;
        double now=NetworkManager.ServerTime.Time;
        if(now<nextRequestAt){RejectTeleport(request,feet,"Request/rate limit");return;}
        nextRequestAt=now+Mathf.Max(.1f,teleportRequestInterval);
        var game=LaserTagGameManager.Instance;
        if(revision!=state.Value.Revision){RejectTeleport(request,feet,"Request/stale origin revision");return;}
        if(!game){RejectTeleport(request,feet,"Request/match unavailable");return;}
        if(round!=game.RoundNumber.Value){RejectTeleport(request,feet,"Request/stale round");return;}
        if(!combat){RejectTeleport(request,feet,"Request/combat player unavailable");return;}
        if(!combat.IsAlive){RejectTeleport(request,feet,$"Request/player state {combat.LifeState.Value}");return;}
        if(game.Phase.Value!=GamePhase.Lobby && game.Phase.Value!=GamePhase.Playing)
        {RejectTeleport(request,feet,$"Request/match phase {game.Phase.Value}");return;}
        if(game.Phase.Value==GamePhase.Playing && !combat.InRound.Value)
        {RejectTeleport(request,feet,"Request/not participating in current round");return;}
        if(!Finite(feet)){RejectTeleport(request,feet,"Request/non-finite destination");return;}
        Physics.SyncTransforms();
        if(!TryAcceptTracking(trackingSequence,tracking,out var reason))
        {RejectTeleport(request,feet,reason);return;}
        if(!TeleportClearance.TryValidateServerTeleport(this,feet,out var approved,out reason))
        {RejectTeleport(request,feet,reason);return;}
        // Unity executes requests sequentially. Publish authoritative feet before processing
        // another player's request, without waiting for an owner acknowledgement.
        var s=state.Value;
        SetServerOrigin(s.Origin+(approved-ServerFeet),s.Yaw,approved);
        TeleportResultRpc(request,true,"");
    }
    void RejectTeleport(uint request,Vector3 feet,string reason)
    {
        LogTeleportRejection(request,feet,reason);
        TeleportResultRpc(request,false,reason);
    }
    void LogTeleportRejection(uint request,Vector3 feet,string reason,string stage="server")
    {
        if(!Application.isEditor && !Debug.isDebugBuild)return;
        var rig=LocalPlayerRig.Instance;var settings=rig?rig.GetComponent<LeftHandArcTeleport>():null;
        if((settings && !settings.teleportDiagnostics) || Time.unscaledTime<nextTeleportDiagnostic)return;
        nextTeleportDiagnostic=Time.unscaledTime+1f;
        float radius=settings?settings.bodyRadius:0;
        float height=settings?Mathf.Max(settings.bodyHeight,ServerEye.y-ServerFeet.y+.1f):0;
        Debug.LogWarning($"[TeleportReject][{stage}] owner={OwnerClientId} request={request} revision={OriginRevision} destination={feet:F3} reason={reason}; currentFeet={ServerFeet:F3} radius={radius:F2} requiredHeight={height:F2}. Throttled 1s.",this);
    }
    public void ServerPlace(Vector3 feet,float facingYaw)
    {
        if(!IsServer || !IsSpawned || !Finite(feet) || !Finite(facingYaw))return;
        var s=state.Value;
        float yaw=facingYaw-TrackingYaw(s.Tracking.EyeRotation);
        SetServerOrigin(feet-Quaternion.Euler(0,yaw,0)*Horizontal(s.Tracking.Eye),yaw,feet);
    }
    void SetServerOrigin(Vector3 origin,float yaw,Vector3 landing)
    {
        var s=state.Value;s.Origin=origin;s.Yaw=yaw;s.Landing=landing;s.Revision++;
        state.Value=s;
        // Reliable direct response avoids waiting for the next snapshot tick.
        ApprovedOriginOwnerRpc(origin,yaw,s.Revision);
        ApplyState(1);
    }
    [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
    void ApprovedOriginOwnerRpc(Vector3 origin,float yaw,uint revision){AcceptOrigin(origin,yaw,revision);}
    [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
    void TeleportResultRpc(uint request,bool accepted,string reason)
    {
        if(request!=pendingRequest)return;
        requestPending=false;
        if(!accepted)
        {
            if(PlayerHud.Instance)PlayerHud.Instance.AddSystemMessage(reason);
            LaserTagFeedback.Haptic(LaserTagFeedback.Hand.Left,.15f,.08f);
        }
    }
    void MovementDiagnostic(string reason)
    {
        double now=NetworkManager.ServerTime.Time;if(now<nextDiagnostic)return;nextDiagnostic=now+5;
        Debug.LogWarning($"[Movement] Owner {OwnerClientId}: {reason}; keeping the last server pose (suppressed for 5s).",this);
        var s=state.Value;
        TrackingCorrectionOwnerRpc(s.Origin,s.Yaw,s.Revision,reason);
    }
    [Rpc(SendTo.Owner,InvokePermission=RpcInvokePermission.Server)]
    void TrackingCorrectionOwnerRpc(Vector3 origin,float yaw,uint revision,string reason)
    {
        if(revision<localRevision)return;
        AcceptOrigin(origin,yaw,revision);
        if(PlayerHud.Instance)PlayerHud.Instance.AddSystemMessage("Tracking not accepted: "+reason);
    }
    void ApplyState(float blend)
    {
        if(!driver || state.Value.Revision==0)return;
        var s=state.Value;var p=s.Tracking;Quaternion rotation=Quaternion.Euler(0,s.Yaw,0);
        if(displayedRevision!=s.Revision)blend=1;
        // Interpolate world targets, not child locals after moving the root.
        Vector3 head=Vector3.Lerp(avatar.head.position,s.Origin+rotation*p.Head,blend);
        Vector3 left=Vector3.Lerp(avatar.leftHand.position,s.Origin+rotation*p.Left,blend);
        Vector3 right=Vector3.Lerp(avatar.rightHand.position,s.Origin+rotation*p.Right,blend);
        Quaternion headRot=Quaternion.Slerp(avatar.head.rotation,rotation*p.HeadRotation,blend);
        Quaternion leftRot=Quaternion.Slerp(avatar.leftHand.rotation,rotation*p.LeftRotation,blend);
        Quaternion rightRot=Quaternion.Slerp(avatar.rightHand.rotation,rotation*p.RightRotation,blend);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position,s.Origin+rotation*p.Body,blend),
            Quaternion.Slerp(transform.rotation,rotation*p.BodyRotation,blend));
        driver.SetHeadPose(head,headRot);driver.SetLeftHandPose(left,leftRot);driver.SetRightHandPose(right,rightRot);
        displayedRevision=s.Revision;firstRemoteFrame=false;
    }
}
