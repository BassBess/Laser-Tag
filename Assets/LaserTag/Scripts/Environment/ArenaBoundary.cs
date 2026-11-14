using UnityEngine;

/// <summary>Authored convex footprint shared by teleport preview, server movement and spawning.
/// It validates positions; it never moves a tracked camera or clamps the XR origin.</summary>
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class ArenaBoundary : MonoBehaviour
{
    public static ArenaBoundary Instance { get; private set; }
    [Tooltip("Convex polygon in this object's local X/Z plane; consecutive vertices form the closed perimeter.")]
    public Vector2[] perimeter = new Vector2[0];
    [Tooltip("Extra horizontal margin inside the scenery collision line.")]
    [Min(0)] public float safetyInset = .15f;
    public float minimumHeight = -2, maximumHeight = 14;
    public bool drawGizmos = true;
    [Header("Scenery (use Tools / Laser Tag / Arena / Rebuild Selected Perimeter Scenery)")]
    [Range(2,5)] public float treeSpacing = 3.1f;
    public Vector2 treeHeight = new Vector2(7.5f,11.5f);
    [Range(6,14)] public float barrierHeight = 10;
    public int forestSeed = 297;
    public bool IsValidConfiguration(out string reason)
    {
        reason=null;
        if(perimeter==null || perimeter.Length<3){reason="Perimeter needs at least three vertices.";return false;}
        if(Vector3.Distance(transform.lossyScale,Vector3.one)>.001f || Vector3.Dot(transform.up,Vector3.up)<.999f)
        {reason="Boundary needs unit scale and an upright transform.";return false;}
        if(!RelayAvatarPlayer.Finite(new Vector3(safetyInset,minimumHeight,maximumHeight)) || minimumHeight>=maximumHeight || safetyInset<0)
        {reason="Boundary height/inset configuration is invalid.";return false;}
        for(int i=0;i<perimeter.Length;i++)
        {
            var a=perimeter[i];var b=perimeter[(i+1)%perimeter.Length];var c=perimeter[(i+2)%perimeter.Length];
            if(!RelayAvatarPlayer.Finite(new Vector3(a.x,0,a.y)) || (b-a).sqrMagnitude<.01f || (b.x-a.x)*(c.y-b.y)-(b.y-a.y)*(c.x-b.x)<=0)
            {reason="Use a convex counter-clockwise perimeter with distinct finite vertices.";return false;}
        }
        return true;
    }
    void Awake() { Instance = this; if(!IsValidConfiguration(out var reason)) Debug.LogError("[ArenaBoundary] "+reason,this); }
    void OnDestroy() { if (Instance == this) Instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    public bool Contains(Vector3 world, float radius = 0)
    {
        if (!RelayAvatarPlayer.Finite(world) || float.IsNaN(radius) || float.IsInfinity(radius) || !IsValidConfiguration(out _)) return false;
        Vector3 p = transform.InverseTransformPoint(world);
        if (p.y < minimumHeight || p.y > maximumHeight) return false;
        float signedArea = 0;
        for (int i=0;i<perimeter.Length;i++)
        { var a=perimeter[i]; var b=perimeter[(i+1)%perimeter.Length]; signedArea += a.x*b.y-b.x*a.y; }
        if (Mathf.Abs(signedArea)<.01f) return false;
        float sign = Mathf.Sign(signedArea);
        for (int i=0;i<perimeter.Length;i++)
        {
            var a=perimeter[i]; var edge=perimeter[(i+1)%perimeter.Length]-a;
            float length=edge.magnitude;
            if (length<.01f) return false;
            float inside = sign*(edge.x*(p.z-a.y)-edge.y*(p.x-a.x))/length;
            if (inside < Mathf.Max(0,radius)+safetyInset) return false;
        }
        return true;
    }
    public static bool Allows(Vector3 world, float radius=0) => !Instance || Instance.Contains(world,radius);
    void OnDrawGizmos()
    {
        if (!drawGizmos || perimeter==null || perimeter.Length<3) return;
        Gizmos.color = new Color(.2f,.9f,.4f,.8f);
        for (int i=0;i<perimeter.Length;i++)
        {
            var a=perimeter[i]; var b=perimeter[(i+1)%perimeter.Length];
            Vector3 start=transform.TransformPoint(new Vector3(a.x,0,a.y));
            Vector3 end=transform.TransformPoint(new Vector3(b.x,0,b.y));
            Gizmos.DrawLine(start,end); Gizmos.DrawLine(start+Vector3.up*maximumHeight,end+Vector3.up*maximumHeight);
            Gizmos.DrawLine(start,start+Vector3.up*maximumHeight);
        }
    }
}
