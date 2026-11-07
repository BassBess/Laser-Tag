using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short glowing beam drawn when ANOTHER player fires, so everyone sees the shot travel from their gun
/// to what it hit. (The shooter's own beam is drawn by <see cref="LaserRay"/>.) Beams are pooled.
/// </summary>
public class ShotBeamFx : MonoBehaviour
{
    static readonly Stack<ShotBeamFx> pool = new Stack<ShotBeamFx>();
    static Material sharedMaterial;
    static Texture2D beamTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => pool.Clear();

    LineRenderer line;
    float duration, age;
    Color color;
    float width;

    public static void Play(Vector3 from, Vector3 to, Color color, float width = 0.03f, float duration = 0.18f)
    {
        ShotBeamFx beam = pool.Count > 0 ? pool.Pop() : Create();
        if (beam == null) beam = Create();
        beam.color = color;
        beam.width = width;
        beam.duration = Mathf.Max(0.02f, duration);
        beam.age = 0f;
        beam.line.SetPosition(0, from);
        beam.line.SetPosition(1, to);
        beam.gameObject.SetActive(true);
        beam.Apply(1f);
    }

    static ShotBeamFx Create()
    {
        var go = new GameObject("Remote Shot Beam");
        go.layer = 2; // Ignore Raycast
        DontDestroyOnLoad(go);
        var beam = go.AddComponent<ShotBeamFx>();
        beam.line = go.AddComponent<LineRenderer>();
        beam.line.positionCount = 2;
        beam.line.useWorldSpace = true;
        beam.line.alignment = LineAlignment.View;
        beam.line.numCapVertices = 4;
        beam.line.textureMode = LineTextureMode.Stretch;
        beam.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.line.receiveShadows = false;
        beam.line.sharedMaterial = SharedMaterial();
        return beam;
    }

    static Material SharedMaterial()
    {
        if (sharedMaterial != null) return sharedMaterial;
        // Soft falloff across the beam so it looks like it glows.
        const int h = 32;
        beamTexture = new Texture2D(2, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float v = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            Color c = new Color(1f, 1f, 1f, Mathf.Pow(v, 1.6f));
            beamTexture.SetPixel(0, y, c);
            beamTexture.SetPixel(1, y, c);
        }
        beamTexture.Apply();
        sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = beamTexture };
        return sharedMaterial;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = 1f - age / duration;
        if (k <= 0f)
        {
            gameObject.SetActive(false);
            pool.Push(this);
            return;
        }
        Apply(k);
    }

    void Apply(float k)
    {
        Color c = color;
        c.a *= k;
        line.startColor = line.endColor = c;
        line.startWidth = line.endWidth = width * (0.4f + 0.6f * k);
    }
}
