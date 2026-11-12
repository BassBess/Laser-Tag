using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;

// Wires adapted team components into the existing menu scene and registered Relay actor.
// Never runs Hend's LAN builder, rewrites XR input assets or replaces the original avatar.
public static class RelayGameplayIntegration
{
    const string PlayerPath="Assets/Networking/RelayAvatarPlayer.prefab";
    [MenuItem("Tools/Laser Tag/Integration/Configure Relay Gameplay")]
    public static void Configure()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var scene=EditorSceneManager.OpenScene(TeamEnvironmentIntegration.GamePath);
        if(UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include).Length!=0)throw new InvalidOperationException("Gameplay scene must not contain a NetworkManager.");
        var b=UnityEngine.Object.FindAnyObjectByType<ScoutLocalRigBinding>();
        if(!b||!b.HasValidReferences||!b.GetComponent<RelayVRAvatarBridge>())throw new InvalidOperationException("Original XR tracking bridge is missing.");
        var left=WireGun(b.leftGun,true);var right=WireGun(b.rightGun,false);
        if(!left||!right||!left.muzzle||!right.muzzle)throw new InvalidOperationException("Original gun/muzzle references are required.");
        ConfigureActor(b,left,right);
        var local=UnityEngine.Object.FindAnyObjectByType<LocalPlayerRig>();if(!local)local=new GameObject("Relay Local Gameplay").AddComponent<LocalPlayerRig>();
        local.xrOrigin=b.xrOrigin.GetComponent<XROrigin>();local.playerCamera=b.trackedCamera;local.avatar=b.avatar;local.leftController=b.leftController;local.rightController=b.rightController;local.guns=new[]{left,right};
        var hud=local.GetComponent<PlayerHud>();if(!hud)hud=local.gameObject.AddComponent<PlayerHud>();hud.targetCamera=b.trackedCamera;
        var game=UnityEngine.Object.FindAnyObjectByType<LaserTagGameManager>();if(!game){var root=new GameObject("Relay Match");root.AddComponent<NetworkObject>();game=root.AddComponent<LaserTagGameManager>();root.AddComponent<RelayMatchCoordinator>();}
        var spawns=UnityEngine.Object.FindAnyObjectByType<SpawnManager>();if(!spawns){var root=new GameObject("Match Spawn Points");spawns=root.AddComponent<SpawnManager>();}
        game.spawnManager=spawns;game.maxShotRange=Mathf.Max(left.maxDistance,right.maxDistance);game.gunToHandTolerance=.8f;
        if(spawns.GetComponentsInChildren<LaserTagSpawnPoint>().Length==0)GenerateSpawns(spawns,b);
        else spawns.spawnPoints=spawns.GetComponentsInChildren<LaserTagSpawnPoint>().ToList();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        var main=EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var session=UnityEngine.Object.FindAnyObjectByType<LaserTag.Menu.MenuSessionController>();
        if(!session)throw new InvalidOperationException("Menu session controller is missing.");
        // Combat adds NetworkBehaviours to the registered actor. Reject old builds
        // cleanly rather than allowing peers with incompatible behaviour layouts.
        session.Manager.NetworkConfig.ProtocolVersion=2;
        var lists=session.Manager.NetworkConfig.Prefabs.NetworkPrefabsLists;
        if(lists.Count!=1||!lists[0].Contains(session.LobbyPrefab)||!lists[0].Contains(session.Manager.NetworkConfig.PlayerPrefab))throw new InvalidOperationException("Existing menu registry is incomplete.");
        EditorSceneManager.MarkSceneDirty(main);EditorSceneManager.SaveScene(main);AssetDatabase.SaveAssets();
        Debug.Log("RELAY_GAMEPLAY_CONFIGURED spawns="+spawns.spawnPoints.Count);
    }
    static LaserRay WireGun(Transform gun,bool left)
    {
        var ray=gun.GetComponentInChildren<LaserRay>(true);
        if(!ray)ray=gun.gameObject.AddComponent<LaserRay>();
        // Same existing model marker/fallback used by Hend's gun setup.
        if(!ray.muzzle)ray.muzzle=gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Cylinder")??gun;
        if(ray.fireAction.action==null)
        {
            const string path="Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
            var asset=AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);var action=asset.FindAction(left?"XRI Left Interaction/Activate":"XRI Right Interaction/Activate",true);
            var reference=AssetDatabase.LoadAllAssetsAtPath(path).OfType<InputActionReference>().FirstOrDefault(r=>r.action!=null&&r.action.id==action.id);
            if(!reference)throw new InvalidOperationException("Existing XRI trigger action reference missing.");
            ray.fireAction=new InputActionProperty(reference);
        }
        EditorUtility.SetDirty(ray);PrefabUtility.RecordPrefabInstancePropertyModifications(ray);return ray;
    }
    static void ConfigureActor(ScoutLocalRigBinding binding,LaserRay left,LaserRay right)
    {
        var root=PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var avatar=root.GetComponent<ScoutAvatar>();if(!avatar)throw new InvalidOperationException("Original Scout avatar missing from Relay prefab.");
            var boxes=root.GetComponent<PlayerHitboxes>();if(!boxes)boxes=root.AddComponent<PlayerHitboxes>();boxes.head=avatar.head;
            if(!root.GetComponent<PlayerIdentity>())root.AddComponent<PlayerIdentity>();
            var combat=root.GetComponent<RelayCombatPlayer>();if(!combat)combat=root.AddComponent<RelayCombatPlayer>();combat.avatar=avatar;combat.hitboxes=boxes;combat.shotInterval=Mathf.Max(left.cooldown,right.cooldown);
            AuthorGun(binding.leftGun,avatar.leftHand,"Left Gun Visual",binding.leftGunGripPosition,binding.leftGunGripRotation);
            AuthorGun(binding.rightGun,avatar.rightHand,"Right Gun Visual",binding.rightGunGripPosition,binding.rightGunGripRotation);
            MuzzleFrame(binding.leftGun,left.muzzle,binding.leftGunGripPosition,binding.leftGunGripRotation,out combat.leftMuzzleOffset,out combat.leftAimDirection);
            MuzzleFrame(binding.rightGun,right.muzzle,binding.rightGunGripPosition,binding.rightGunGripRotation,out combat.rightMuzzleOffset,out combat.rightAimDirection);
            PrefabUtility.SaveAsPrefabAsset(root,PlayerPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void MuzzleFrame(Transform gun,Transform muzzle,Vector3 position,Vector3 rotation,out Vector3 offset,out Vector3 direction)
    {
        Quaternion handRotation=gun.rotation*Quaternion.Euler(rotation);Vector3 handPosition=gun.TransformPoint(position);
        offset=Quaternion.Inverse(handRotation)*(muzzle.position-handPosition);direction=Quaternion.Inverse(handRotation)*muzzle.forward;
    }
    static void AuthorGun(Transform original,Transform hand,string name,Vector3 grip,Vector3 rotation)
    {
        if(hand.Find(name))return;
        var visual=UnityEngine.Object.Instantiate(original.gameObject);visual.name=name;visual.transform.SetParent(hand,false);
        foreach(var script in visual.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(script);
        foreach(var c in visual.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in visual.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in visual.GetComponentsInChildren<LineRenderer>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in visual.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in visual.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(c);
        foreach(var c in visual.GetComponentsInChildren<ParticleSystem>(true))UnityEngine.Object.DestroyImmediate(c.gameObject);
        Quaternion inverse=Quaternion.Inverse(Quaternion.Euler(rotation));
        visual.transform.localRotation=inverse;
        visual.transform.localScale=new Vector3(original.lossyScale.x/hand.lossyScale.x,original.lossyScale.y/hand.lossyScale.y,original.lossyScale.z/hand.lossyScale.z);
        visual.transform.localPosition=-(inverse*Vector3.Scale(visual.transform.localScale,grip));
    }
    static void GenerateSpawns(SpawnManager spawns,ScoutLocalRigBinding binding)
    {
        Physics.SyncTransforms();var ignore=new List<Transform>{binding.xrOrigin,binding.avatar.transform};
        // Reuses Hend's floor sampling + body-clearance + farthest-point strategy.
        // Only named arena floors are eligible; roof panels and props are excluded.
        var floors=UnityEngine.Object.FindObjectsByType<Collider>().Where(c=>c.enabled&&!c.isTrigger&&c is not CharacterController&&c.name.IndexOf("floor",StringComparison.OrdinalIgnoreCase)>=0&&!c.transform.IsChildOf(binding.xrOrigin)&&!c.transform.IsChildOf(binding.avatar.transform)).ToArray();
        spawns.fallbackFloors=floors;var candidates=new List<Vector3>();
        foreach(var floor in floors)
        {
            var box=floor.bounds;if(box.size.x<.8f||box.size.z<.8f||box.size.x>150||box.size.z>150)continue;
            for(float x=box.min.x+.5f;x<=box.max.x-.5f;x+=1)
            for(float z=box.min.z+.5f;z<=box.max.z-.5f;z+=1)
            {
                if(!floor.Raycast(new Ray(new Vector3(x,box.max.y+.5f,z),Vector3.down),out var hit,box.size.y+1)||hit.normal.y<.9f)continue;
                if(TeleportClearance.IsBodySpaceFree(hit.point,spawns.bodyRadius+.1f,spawns.bodyHeight,floor,ignore)&&!candidates.Any(p=>(p-hit.point).sqrMagnitude<.01f))candidates.Add(hit.point);
            }
        }
        if(candidates.Count<2)throw new InvalidOperationException("Insufficient clear floor spawn positions; review the collision integration.");
        Vector3 center=candidates.Aggregate(Vector3.zero,(sum,p)=>sum+p)/candidates.Count;
        var chosen=new List<Vector3>{candidates.OrderBy(p=>(p-center).sqrMagnitude).First()};
        while(chosen.Count<16){var next=candidates.OrderByDescending(p=>chosen.Min(c=>Vector3.Distance(p,c))).First();if(chosen.Min(c=>Vector3.Distance(next,c))<3)break;chosen.Add(next);}
        foreach(var point in chosen){var go=new GameObject("Spawn "+(spawns.transform.childCount+1).ToString("00"));go.transform.SetParent(spawns.transform,false);go.transform.position=point;var forward=Vector3.ProjectOnPlane(center-point,Vector3.up);go.transform.rotation=forward.sqrMagnitude>.01f?Quaternion.LookRotation(forward):Quaternion.identity;go.AddComponent<LaserTagSpawnPoint>();}
        spawns.spawnPoints=spawns.GetComponentsInChildren<LaserTagSpawnPoint>().ToList();
    }
}
