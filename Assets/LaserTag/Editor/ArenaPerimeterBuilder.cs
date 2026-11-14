using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds only the authored perimeter subtree and reviewed stair/ground opt-ins.</summary>
public static class ArenaPerimeterBuilder
{
    const string Folder="Assets/LaserTag/Presentation/ArenaPerimeter";
    [MenuItem("Tools/Laser Tag/Arena/Install Boundary and Stair Fix")]
    public static void Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/RelayVRGame.unity")throw new InvalidOperationException("Open RelayVRGame first.");
        var arena=scene.GetRootGameObjects().Single(g=>g.name=="arena").transform;
        var existing=scene.GetRootGameObjects().Select(g=>g.GetComponent<ArenaBoundary>()).FirstOrDefault(b=>b);
        if(!existing)
        {
            var root=new GameObject("Arena Forest Perimeter"); existing=root.AddComponent<ArenaBoundary>();
            // Inspected building footprint (-15.62..14.8,-15.62..7), outdoor grid
            // (13.98..29.98,-8..8) and every authored spawn are inside these margins.
            existing.perimeter=new[] { new Vector2(-20,-21),new Vector2(30,-21),new Vector2(35,-12),
                new Vector2(35,8),new Vector2(29,13),new Vector2(-13,13),new Vector2(-20,6) };
            existing.minimumHeight=-1;existing.maximumHeight=14;
        }
        Rebuild(existing);
        var stairs=arena.Find("stairs_big_with_emmision");
        if(!stairs)throw new InvalidOperationException("Reviewed staircase missing; inspect map before installing.");
        var stairCollider=stairs.GetComponent<MeshCollider>();
        if(!stairCollider || !stairCollider.sharedMesh)throw new InvalidOperationException("Stair mesh collider missing.");
        stairCollider.isTrigger=false;stairCollider.convex=false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(stairCollider);
        Approve(stairCollider,true,false);
        var plane=arena.Find("Plane");
        var ground=plane?plane.GetComponent<MeshCollider>():null;
        if(!ground)throw new InvalidOperationException("Reviewed ground plane missing.");
        Approve(ground,false,true);
        // The existing floor_6 plate already bridges the stair exit, at Y=4.151.
        // Reuse it; the lower floor_2 deck beneath it is not the landing surface.
        var landing=arena.Find("floor_6");
        if(!landing || !landing.GetComponent<Collider>())throw new InvalidOperationException("Reviewed upper landing floor_6 missing.");
        Approve(landing.GetComponent<Collider>(),false,true);
        // First tread is .37 above the terrain; a shallow entry plate supports
        // its front footprint without increasing every stair's rise allowance.
        var entry=arena.Find("Stair Entry Plate");
        if(!entry)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Stair Entry Plate";
            go.transform.SetParent(arena,true);go.transform.position=new Vector3(-4,.135f,7.3f);
            go.transform.localScale=new Vector3(3.6f,.1f,.8f);
            go.GetComponent<MeshRenderer>().sharedMaterial=landing.GetComponent<MeshRenderer>().sharedMaterial;
            entry=go.transform;
        }
        Approve(entry.GetComponent<Collider>(),false,false);
        TeleportClearance.InvalidateSurfaces();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("ARENA_BOUNDARY_INSTALLED: forest, continuous collision, reviewed ground and stairs, top landing");
    }
    static void Approve(Collider collider,bool stairs,bool pickups)
    {
        var marker=collider.GetComponent<ApprovedWalkableSurface>();
        if(!marker)marker=collider.gameObject.AddComponent<ApprovedWalkableSurface>();
        marker.surfaces=new[] { collider };marker.stairTreads=stairs;marker.maximumStepRise=.34f;marker.allowHealthPickups=pickups;
        EditorUtility.SetDirty(marker);PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
    }
    [MenuItem("Tools/Laser Tag/Arena/Rebuild Selected Perimeter Scenery")]
    public static void RebuildSelected()
    {
        var boundary=Selection.activeGameObject?Selection.activeGameObject.GetComponent<ArenaBoundary>():null;
        if(!boundary)throw new InvalidOperationException("Select Arena Forest Perimeter.");
        Undo.RegisterFullObjectHierarchyUndo(boundary.gameObject,"Rebuild perimeter");
        Rebuild(boundary);EditorSceneManager.MarkSceneDirty(boundary.gameObject.scene);
    }
    public static void Rebuild(ArenaBoundary boundary)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
        if(!boundary.IsValidConfiguration(out var error))throw new InvalidOperationException(error);
        var old=boundary.transform.Find("Generated Forest and Barriers");
        if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var generated=new GameObject("Generated Forest and Barriers");generated.transform.SetParent(boundary.transform,false);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/ForestVertexLit.shader");
        if(!shader)throw new InvalidOperationException("Forest shader missing.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Forest.mat");
        if(!material){material=new Material(shader){name="Night Forest"};AssetDatabase.CreateAsset(material,Folder+"/Forest.mat");}
        var random=new System.Random(boundary.forestSeed);
        for(int edge=0;edge<boundary.perimeter.Length;edge++)
        {
            Vector2 a=boundary.perimeter[edge],b=boundary.perimeter[(edge+1)%boundary.perimeter.Length];
            Vector3 start=new Vector3(a.x,0,a.y),end=new Vector3(b.x,0,b.y),along=(end-start).normalized;
            Vector3 outward=new Vector3(along.z,0,-along.x);
            var wall=new GameObject("Hidden perimeter barrier "+edge);wall.transform.SetParent(generated.transform,false);
            wall.transform.localPosition=(start+end)*.5f+Vector3.up*(boundary.barrierHeight*.5f-.5f);
            wall.transform.localRotation=Quaternion.LookRotation(along);
            var box=wall.AddComponent<BoxCollider>();box.size=new Vector3(1,boundary.barrierHeight,Vector3.Distance(start,end)+1.2f);
            wall.isStatic=true;
            var geometry=new Geometry();
            int trees=Mathf.CeilToInt(Vector3.Distance(start,end)/boundary.treeSpacing);
            for(int row=0;row<2;row++)
            for(int i=0;i<=trees;i++)
            {
                float t=Mathf.Clamp01((i+Range(random,-.35f,.35f)+(row==1?.45f:0))/trees);
                Vector3 position=Vector3.Lerp(start,end,t)+outward*(.8f+row*2.8f+Range(random,0,1.1f));
                float height=Range(random,boundary.treeHeight.x,boundary.treeHeight.y);
                geometry.Tree(position,height,Range(random,1.8f,2.8f),random);
            }
            // Continuous low irregular earth/rock ridge hides the collider base.
            int rocks=Mathf.CeilToInt(Vector3.Distance(start,end)/1.8f);
            for(int i=0;i<=rocks;i++)
            {
                var p=Vector3.Lerp(start,end,(float)i/rocks)+outward*Range(random,.1f,.8f);
                geometry.Rock(p,Range(random,1.6f,2.2f),Range(random,1.2f,2.0f),random);
            }
            var mesh=geometry.ToMesh("Forest sector "+edge);
            string assetPath=Folder+"/ForestSector"+edge+".asset";
            var stored=AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if(stored){EditorUtility.CopySerialized(mesh,stored);EditorUtility.SetDirty(stored);UnityEngine.Object.DestroyImmediate(mesh);mesh=stored;}
            else AssetDatabase.CreateAsset(mesh,assetPath);
            var chunk=new GameObject(mesh.name);chunk.transform.SetParent(generated.transform,false);chunk.isStatic=true;
            chunk.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=chunk.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            chunk.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        EditorUtility.SetDirty(boundary);AssetDatabase.SaveAssets();
    }
    static float Range(System.Random r,float min,float max)=>Mathf.Lerp(min,max,(float)r.NextDouble());
    sealed class Geometry
    {
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<Color> colors=new List<Color>();readonly List<int> triangles=new List<int>();
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
        {
            int n=vertices.Count;vertices.Add(a);vertices.Add(c);vertices.Add(b);
            colors.Add(color);colors.Add(color);colors.Add(color);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
        }
        void Cone(Vector3 p,float bottom,float top,float radius,Color color,float phase)
        {
            for(int i=0;i<7;i++)
            {
                float a=phase+i*Mathf.PI*2/7,b=phase+(i+1)*Mathf.PI*2/7;
                Triangle(p+new Vector3(Mathf.Cos(b)*radius,bottom,Mathf.Sin(b)*radius),p+Vector3.up*top,
                    p+new Vector3(Mathf.Cos(a)*radius,bottom,Mathf.Sin(a)*radius),color*(.8f+.2f*Mathf.Sin(i+1)));
            }
        }
        public void Tree(Vector3 p,float height,float radius,System.Random r)
        {
            Color bark=new Color(.24f,.19f,.14f),leaves=Color.Lerp(new Color(.12f,.25f,.19f),new Color(.2f,.32f,.28f),(float)r.NextDouble());
            float phase=Range(r,0,Mathf.PI*2);
            Cone(p,0,height*.8f,.28f,bark,phase);
            Cone(p,height*.13f,height*.7f,radius,leaves,phase);
            Cone(p,height*.38f,height*.88f,radius*.8f,leaves*1.08f,phase+.4f);
            Cone(p,height*.62f,height,radius*.55f,leaves*1.15f,phase+.8f);
        }
        public void Rock(Vector3 p,float radius,float height,System.Random r)
        {
            var center=p+new Vector3(Range(r,-.2f,.2f),height,Range(r,-.2f,.2f));
            Color color=Color.Lerp(new Color(.23f,.27f,.3f),new Color(.34f,.36f,.34f),(float)r.NextDouble());
            for(int i=0;i<7;i++)
            {
                float a=i*Mathf.PI*2/7,b=(i+1)*Mathf.PI*2/7;
                Triangle(p+new Vector3(Mathf.Cos(b)*radius,-.08f,Mathf.Sin(b)*radius),center,
                    p+new Vector3(Mathf.Cos(a)*radius,-.08f,Mathf.Sin(a)*radius),color*Range(r,.7f,1.1f));
            }
        }
        public Mesh ToMesh(string name)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
    public static void InstallInBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/RelayVRGame.unity");Install();
    }
}
