using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

// Integrates Nadia's authored physics through Unity objects, not scene-YAML substitutions.
// Source scene-object IDs are preserved in the existing arena copy. Names, original
// prefab identity and world transforms are independently checked before copying a component.
public static class TeamEnvironmentIntegration
{
    public const string GamePath = "Assets/Scenes/RelayVRGame.unity";
    [MenuItem("Tools/Laser Tag/Integration/Import Team Environment Colliders")]
    public static void ChooseSource()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        string file=EditorUtility.OpenFilePanel("Select Nadia's exported outpost scene","","unity");if(string.IsNullOrEmpty(file))return;
        string temporary=AssetDatabase.GenerateUniqueAssetPath("Assets/TeamColliderSource.unity");
        File.Copy(file,temporary);AssetDatabase.ImportAsset(temporary,ImportAssetOptions.ForceSynchronousImport);
        try{Apply(temporary);}finally{AssetDatabase.DeleteAsset(temporary);}
    }
    public static string Apply(string sourcePath)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        string recovery=Path.Combine(Path.GetTempPath(),"LaserTagSceneBackups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(recovery);
        File.Copy(GamePath,Path.Combine(recovery,"RelayVRGame.unity"));File.Copy(GamePath+".meta",Path.Combine(recovery,"RelayVRGame.unity.meta"));
        var game=EditorSceneManager.OpenScene(GamePath);
        var targetObjects=game.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
        var index=targetObjects.GroupBy(t=>Id(t.gameObject)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var source=EditorSceneManager.OpenScene(sourcePath,OpenSceneMode.Additive);
        int added=0,updated=0,matched=0,empty=0,lod=0,duplicates=0,rigPhysics=0,removedRedundant=0;
        var report=new List<string>();var seen=new Dictionary<Transform,List<Collider>>();
        try
        {
            var colliders=source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).ToArray();
            foreach(var collider in colliders)
            {
                if(collider is CharacterController || collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() || collider.GetComponentInParent<ScoutAvatar>()) { rigPhysics++; continue; }
                if(collider is MeshCollider invalid&&!invalid.sharedMesh){empty++;continue;}
                if(RedundantLod(collider)){lod++;continue;}
                if(!index.TryGetValue(Id(collider.gameObject),out var candidates))throw new InvalidOperationException("No stable target for "+Hierarchy(collider.transform));
                var valid=candidates.Where(t=>SameObject(collider.transform,t)).ToArray();
                if(valid.Length!=1)throw new InvalidOperationException("Ambiguous or changed target: "+Hierarchy(collider.transform));
                var target=valid[0];
                if(!seen.TryGetValue(target,out var previous)){previous=new List<Collider>();seen.Add(target,previous);}
                if(previous.Any(c=>Equivalent(c,collider))){duplicates++;continue;}previous.Add(collider);
                var existing=target.GetComponents<Collider>().FirstOrDefault(c=>c.GetType()==collider.GetType()&&Id(c)==Id(collider));
                if(!existing)existing=target.GetComponents<Collider>().FirstOrDefault(c=>Equivalent(c,collider));
                if(existing&&Equivalent(existing,collider)){matched++;continue;}
                ComponentUtility.CopyComponent(collider);
                if(existing){if(!ComponentUtility.PasteComponentValues(existing))throw new InvalidOperationException("Cannot update collider");updated++;}
                else{if(!ComponentUtility.PasteComponentAsNew(target.gameObject))throw new InvalidOperationException("Cannot add collider");added++;}
                var copied=existing?existing:target.GetComponents<Collider>().Last(c=>c.GetType()==collider.GetType());
                PrefabUtility.RecordPrefabInstancePropertyModifications(copied);EditorUtility.SetDirty(copied);
                report.Add((existing?"updated ":"added ")+Hierarchy(target));
            }
            // Remove only a verified lower-LOD mesh collider when its high-detail
            // counterpart now supplies collision. Never remove renderers or objects.
            foreach(var original in colliders.OfType<MeshCollider>())
            {
                if(!original.sharedMesh||!RedundantLod(original)||!index.TryGetValue(Id(original.gameObject),out var nodes))continue;
                var target=nodes.SingleOrDefault(t=>SameObject(original.transform,t));if(!target)continue;
                foreach(var redundant in target.GetComponents<MeshCollider>().Where(c=>RedundantLod(c)&&Equivalent(c,original)).ToArray())
                {report.Add("removed redundant LOD collider "+Hierarchy(target));UnityEngine.Object.DestroyImmediate(redundant);removedRedundant++;}
            }
            EditorSceneManager.MarkSceneDirty(game);EditorSceneManager.SaveScene(game);
            string summary=$"source={colliders.Length}; added={added}; updated={updated}; matched={matched}; empty={empty}; redundantLOD={lod}; duplicateSource={duplicates}; excludedRigPhysics={rigPhysics}; removedRedundantLOD={removedRedundant}";
            report.Insert(0,summary);File.WriteAllLines(Path.Combine(recovery,"ColliderIntegration.txt"),report);Debug.Log("TEAM_COLLIDERS "+summary);return summary;
        }
        finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(game);}
    }
    static string Id(UnityEngine.Object obj){var id=GlobalObjectId.GetGlobalObjectIdSlow(obj);return id.targetObjectId+":"+id.targetPrefabId;}
    static bool SameObject(Transform a,Transform b)
    {
        if(a.name!=b.name||Vector3.Distance(a.position,b.position)>.003f||Quaternion.Angle(a.rotation,b.rotation)>.1f||Vector3.Distance(a.lossyScale,b.lossyScale)>.003f)return false;
        var x=PrefabUtility.GetCorrespondingObjectFromOriginalSource(a.gameObject);var y=PrefabUtility.GetCorrespondingObjectFromOriginalSource(b.gameObject);
        return x==y;
    }
    static bool RedundantLod(Collider collider)
    {
        var renderer=collider.GetComponent<Renderer>();var group=collider.GetComponentInParent<LODGroup>();if(!renderer||!group)return false;
        var levels=group.GetLODs();if(levels.Length<2||!levels.Skip(1).Any(l=>l.renderers.Contains(renderer)))return false;
        return levels[0].renderers.Any(r=>r&&r.TryGetComponent<MeshCollider>(out var c)&&c.enabled&&c.sharedMesh);
    }
    static bool Equivalent(Collider a,Collider b)
    {
        if(a.GetType()!=b.GetType()||a.enabled!=b.enabled||a.isTrigger!=b.isTrigger||a.sharedMaterial!=b.sharedMaterial||a.includeLayers!=b.includeLayers||a.excludeLayers!=b.excludeLayers||a.layerOverridePriority!=b.layerOverridePriority||Mathf.Abs(a.contactOffset-b.contactOffset)>.00001f)return false;
        if(a is MeshCollider m&&b is MeshCollider n)return m.sharedMesh==n.sharedMesh&&m.convex==n.convex&&m.cookingOptions==n.cookingOptions;
        if(a is BoxCollider x&&b is BoxCollider y)return x.center==y.center&&x.size==y.size;
        if(a is SphereCollider s&&b is SphereCollider t)return s.center==t.center&&Mathf.Approximately(s.radius,t.radius);
        if(a is CapsuleCollider p&&b is CapsuleCollider q)return p.center==q.center&&p.direction==q.direction&&Mathf.Approximately(p.radius,q.radius)&&Mathf.Approximately(p.height,q.height);
        throw new InvalidOperationException("Review unsupported collider type "+a.GetType().Name);
    }
    static string Hierarchy(Transform t){string path=t.name;while(t.parent){t=t.parent;path=t.name+"/"+path;}return path;}
}
