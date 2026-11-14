using UnityEngine;
using UnityEngine.Rendering;

// One static mesh, one unlit draw, no lights, particles or frame-by-frame generation.
public sealed class StarryNightSky : MonoBehaviour
{
    [Range(100, 5000)] public int starCount = 2400;
    [Range(.1f, 2f)] public float brightness = .8f;
    [Range(0, .02f)] public float ambientContribution = .003f;
    public int seed = 297;
    Mesh mesh;
    Material material, previousSkybox;
    GameObject sky;
    Color oldSky, oldEquator, oldGround;
    void Start()
    {
        var template = Resources.Load<Material>("LaserTagStars");
        if (!template) { Debug.LogError("Missing LaserTagStars material", this); return; }
        previousSkybox = RenderSettings.skybox;
        var night = Resources.Load<Material>("LaserTagNight");
        if (night) RenderSettings.skybox = night;
        oldSky = RenderSettings.ambientSkyColor; oldEquator = RenderSettings.ambientEquatorColor; oldGround = RenderSettings.ambientGroundColor;
        Color contribution = new Color(.45f, .6f, 1f) * ambientContribution;
        RenderSettings.ambientSkyColor += contribution;
        RenderSettings.ambientEquatorColor += contribution * .5f;
        RenderSettings.ambientGroundColor += contribution * .2f;
        var random = new System.Random(seed);
        int count = Mathf.Clamp(starCount, 100, 5000);
        var vertices = new Vector3[count * 3]; var colors = new Color[count * 3]; var indices = new int[count * 3];
        for (int i = 0; i < count; i++)
        {
            float y = (float)random.NextDouble();
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            var direction = new Vector3(Mathf.Sqrt(1-y*y)*Mathf.Cos(angle), y, Mathf.Sqrt(1-y*y)*Mathf.Sin(angle));
            var right = Vector3.Cross(direction, Vector3.up).normalized;
            var up = Vector3.Cross(right, direction);
            float size = Mathf.Lerp(.00045f, .0016f, Mathf.Pow((float)random.NextDouble(), 3));
            float light = Mathf.Lerp(.08f, 1f, Mathf.Pow((float)random.NextDouble(), 2)) * brightness;
            Color color = Color.Lerp(new Color(.55f,.7f,1), Color.white, (float)random.NextDouble()) * light;
            for (int j = 0; j < 3; j++)
            {
                float a = j * Mathf.PI * 2 / 3;
                int index = i*3+j;
                vertices[index] = direction + size * (right*Mathf.Cos(a)+up*Mathf.Sin(a));
                colors[index] = color; indices[index] = index;
            }
        }
        mesh = new Mesh { name = "Static starfield" };
        mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = indices;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000);
        mesh.UploadMeshData(true);
        material = new Material(template);
        sky = new GameObject("Starfield (one draw)"); sky.transform.SetParent(transform, false);
        sky.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = sky.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    void OnDestroy()
    {
        if (sky) Destroy(sky);
        if (mesh) Destroy(mesh);
        if (material)
        {
            RenderSettings.skybox = previousSkybox;
            RenderSettings.ambientSkyColor = oldSky; RenderSettings.ambientEquatorColor = oldEquator; RenderSettings.ambientGroundColor = oldGround;
            Destroy(material);
        }
    }
}
