using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only inventory; never saves or replaces an open scene.
public static class ArenaGeometryAudit
{
    [Serializable] public class Record
    {
        public string path, asset, type, mesh;
        public Vector3 position, min, max, scale;
        public bool enabled, trigger;
        public int layer;
        public Vector3[] vertices;
        public int[] triangles;
    }
    [Serializable] public class Inventory { public List<Record> records = new List<Record>(); }
    [MenuItem("Tools/Laser Tag/Arena/Export Geometry Inventory")]
    public static void Export()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/RelayVRGame.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/RelayVRGame.unity", OpenSceneMode.Additive);
        try
        {
            var inventory = new Inventory();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = PathOf(t);
                foreach (var renderer in t.GetComponents<Renderer>())
                {
                    var filter=t.GetComponent<MeshFilter>();
                    var renderMesh=filter?filter.sharedMesh:null;
                    inventory.records.Add(new Record { mesh=renderMesh?renderMesh.name:"",
                        vertices=renderMesh && (path+renderMesh.name).ToLowerInvariant().Contains("stair") ? renderMesh.vertices.Select(t.TransformPoint).ToArray() : null,
                        triangles=renderMesh && (path+renderMesh.name).ToLowerInvariant().Contains("stair") ? renderMesh.triangles : null, path=path, type="Renderer", position=t.position, min=renderer.bounds.min, max=renderer.bounds.max,
                        asset=AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject)), enabled=renderer.enabled, scale=t.lossyScale });
                }
                foreach (var collider in t.GetComponents<Collider>())
                {
                    var m = collider as MeshCollider;
                    var record = new Record { path=path, type=collider.GetType().Name, position=t.position, min=collider.bounds.min, max=collider.bounds.max,
                        enabled=collider.enabled, trigger=collider.isTrigger, layer=t.gameObject.layer, scale=t.lossyScale,
                        mesh=m && m.sharedMesh ? m.sharedMesh.name : "" };
                    if (m && m.sharedMesh && (path+record.mesh).ToLowerInvariant().Contains("stair"))
                    {
                        record.vertices=m.sharedMesh.vertices.Select(t.TransformPoint).ToArray(); record.triangles=m.sharedMesh.triangles;
                    }
                    inventory.records.Add(record);
                }
                if (t.GetComponent<LaserTagSpawnPoint>()) inventory.records.Add(new Record { path=path,type="Spawn",position=t.position });
            }
            File.WriteAllText("/tmp/laser-arena-geometry.json", JsonUtility.ToJson(inventory,true));
            Debug.Log("ARENA_GEOMETRY_EXPORTED /tmp/laser-arena-geometry.json");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene,true); }
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent)+"/"+t.name : t.name;
}
