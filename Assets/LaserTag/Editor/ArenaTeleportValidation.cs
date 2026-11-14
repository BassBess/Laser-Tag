using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArenaTeleportValidation
{
    [MenuItem("Tools/Laser Tag/Arena/Validate Stair Landings")]
    public static void Validate()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/RelayVRGame.unity")throw new InvalidOperationException("Open RelayVRGame first.");
        var objects=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
        var settings=objects.Select(t=>t.GetComponent<LeftHandArcTeleport>()).First(s=>s);
        var stair=objects.Single(t=>t.name=="stairs_big_with_emmision").GetComponent<MeshCollider>();
        var ground=objects.Single(t=>t.name=="Plane" && t.GetComponent<MeshCollider>()).GetComponent<MeshCollider>();
        var landing=objects.Single(t=>t.name=="floor_6").GetComponent<Collider>();
        var ignore=objects.Where(t=>t.GetComponent<Unity.XR.CoreUtils.XROrigin>() || t.GetComponent<ScoutAvatar>()).ToList();
        Physics.SyncTransforms();TeleportClearance.InvalidateSurfaces();
        var report=new StringBuilder();int failures=0;
        Action<bool,string> check=(valid,label)=>{report.AppendLine((valid?"PASS ":"FAIL ")+label);if(!valid)failures++;};
        check(!stair.isTrigger && !stair.convex,"stair uses exact solid nonconvex mesh");
        check(TeleportClearance.IsEligibleSurface(settings,stair),"stair explicitly registered");
        var treadPoints=new List<Vector3>();
        for(int step=0;step<13;step++)
        {
            float z=6.78f-step*.304f;
            for(int lane=-1;lane<=1;lane++)
            for(int offset=-1;offset<=1;offset++)
            {
                Vector3 origin=new Vector3(-4+lane*.7f,7,z+offset*.07f);
                bool hitTread=stair.Raycast(new Ray(origin,Vector3.down),out var hit,10);
                check(hitTread,"tread ray "+step+" lane="+lane+" offset="+offset);
                if(!hitTread)continue;
                bool valid=TeleportClearance.TryValidateLanding(settings,hit,new Vector3(-4,Mathf.Max(0,hit.point.y-1),z+1.2f),1.7f,ignore,null,out var rejection);
                check(valid,$"tread {step} lane={lane} offset={offset}: {hit.point:F3} {rejection}");
                if(lane==0 && offset==0)treadPoints.Add(hit.point);
            }
        }
        check(landing.Raycast(new Ray(new Vector3(-4,7,2.45f),Vector3.down),out var topHit,10) &&
            TeleportClearance.TryValidateLanding(settings,topHit,new Vector3(-4,3.4f,3.9f),1.7f,ignore,null,out _),"upper landing body/support clearance");
        // Search genuine controller pitches for progressive upward/downward travel.
        var route=new List<Vector3>{new Vector3(-4,0,7.8f)};
        route.AddRange(treadPoints.Where((p,i)=>i%3==0));route.Add(new Vector3(-4,4.151014f,2.45f));route.Add(new Vector3(-4,4.151014f,-1.5f));
        for(int direction=0;direction<2;direction++)
        {
            if(direction==1)route.Reverse();
            for(int i=0;i<route.Count-1;i++)
            {
                Vector3 source=route[i],target=route[i+1]; bool reached=false;string last="no arc";
                float yaw=target.z<source.z?180:0;
                for(int pitch=-25;pitch<=88;pitch++)
                {
                    Vector3 forward=Quaternion.Euler(pitch,yaw,0)*Vector3.forward;
                    if(!TeleportClearance.TryValidateSource(source+Vector3.up*1.7f,source+Vector3.up*1.2f,forward,ignore,out var sourceRejection)){last=sourceRejection.ToString();continue;}
                    if(!TeleportClearance.TryTraceArc(settings,source+Vector3.up*1.2f,forward,ignore,null,out _,out var hit,out var rejection)) {last=rejection.ToString();continue;}
                    if(Vector3.Distance(hit.point,target)>.55f)continue;
                    if(!TeleportClearance.TryValidateLanding(settings,hit,source,1.7f,ignore,null,out rejection)) {last=rejection.ToString();continue;}
                    reached=true;break;
                }
                check(reached,$"actual arc {(direction==0?"up":"down")} leg {i}: {source:F2} -> {target:F2} ({last})");
            }
        }
        foreach(var spawn in objects.Select(t=>t.GetComponent<LaserTagSpawnPoint>()).Where(s=>s))
        {
            if(spawn.initialSpawn)
            {
                bool free=TeleportClearance.IsSpawnSupported(settings,spawn.transform.position,.35f,ignore) &&
                    TeleportClearance.IsBodySpaceFree(spawn.transform.position,.35f,2.7f,null,ignore);
                check(free,"initial spawn clearance: "+spawn.name);
            }
        }
        // Ordinary open floor and outdoor tiles, not just the special staircase.
        foreach(Vector3 point in new[]{new Vector3(15.98f,.187f,6),new Vector3(23.98f,.187f,6),new Vector3(-9.5f,.192f,-12.5f),new Vector3(-4,4.151014f,1.5f),new Vector3(-4,4.151014f,-1.5f),new Vector3(13.48f,4.004f,1.5f),new Vector3(-13.5f,4f,1.5f)})
        {
            bool supported=TeleportClearance.IsSpawnSupported(settings,point,.25f,ignore);
            check(supported && TeleportClearance.IsBodySpaceFree(point,.25f,1.7f,null,ignore),"ordinary floor/ground/platform footprint and body: "+point);
        }
        // Low cover and headroom remain blocking even on approved stair treads.
        var testTarget=treadPoints[6];
        stair.Raycast(new Ray(testTarget+Vector3.up,Vector3.down),out var targetHit,2);
        foreach(float height in new[]{.2f,1.5f})
        {
            var obstacle=new GameObject("Temporary clearance validation");
            try
            {
                obstacle.transform.position=testTarget+Vector3.up*height;
                var box=obstacle.AddComponent<BoxCollider>();box.size=new Vector3(.15f,.2f,.15f);
                Physics.SyncTransforms();
                bool accepted=TeleportClearance.TryValidateLanding(settings,targetHit,testTarget-Vector3.forward,1.7f,ignore,null,out var rejection);
                check(!accepted && rejection.Code==TeleportClearance.RejectionCode.BodyBlocked,"stair obstacle/headroom y="+height+": "+rejection);
            }
            finally { UnityEngine.Object.DestroyImmediate(obstacle);Physics.SyncTransforms(); }
        }
        report.AppendLine("Failures: "+failures);
        string path=Path.GetFullPath("Logs/ArenaTeleportValidation.txt");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,report.ToString());

        Debug.Log("ARENA_VALIDATION failures="+failures+" report="+path);
        if(failures>0)throw new InvalidOperationException("Arena validation failed; see "+path);
    }
    [MenuItem("Tools/Laser Tag/Arena/Compile Android Player Scripts")]
    public static void CompileAndroid()
    {
        var settings=new UnityEditor.Build.Player.ScriptCompilationSettings
        {
            target=BuildTarget.Android,group=BuildTargetGroup.Android,
            options=UnityEditor.Build.Player.ScriptCompilationOptions.None
        };
        string output="Temp/laser-arena-android-assemblies";Directory.CreateDirectory(output);
        var result=UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings,output);
        if(result.assemblies==null || result.assemblies.Count==0)throw new InvalidOperationException("Android player script compilation produced no assemblies.");
        Debug.Log("ARENA_ANDROID_COMPILE_PASSED assemblies="+result.assemblies.Count);
    }
    public static void ValidateAndCompileAndroidInBatch()
    {
        ValidateInBatch();
        CompileAndroid();
    }
    public static void ValidateInBatch() { EditorSceneManager.OpenScene("Assets/Scenes/RelayVRGame.unity"); Validate(); }
}
