using UnityEngine;

// Per-avatar LED overrides. Armor stays neutral white/silver and charcoal.
[ExecuteAlways, DisallowMultipleComponent]
public class ScoutSuitAppearance : MonoBehaviour
{
    // Retain old serialized fields/API names without exposing or applying armor paint.
    [HideInInspector] public bool showSuitColor;
    [HideInInspector] public Color suitColor;
    [Header("LED lines")]
    public bool showAccentLights = true;
    [InspectorName("LED Color"), ColorUsage(false)] public Color accentColor = new Color(.42f, .10f, .85f);
    [Range(0, 5), InspectorName("LED Brightness")] public float ledIntensity = 2.2f;

    [HideInInspector] public Renderer[] colorPanels = new Renderer[0];
    [HideInInspector] public Renderer[] accentStrips = new Renderer[0];
    [HideInInspector] public Texture2D paintResponse;

    Renderer[] cachedStrips;
    MaterialPropertyBlock[] originalStrips;
    MaterialPropertyBlock scratch;
    bool pendingRefresh;

    void OnEnable() { ApplyColors(); }
    // Validation can occur during asset loading. Do renderer work on the next update.
    void OnValidate() { pendingRefresh = true; }
    void Update() { if (pendingRefresh) ApplyColors(); }
    void OnDisable() { Restore(cachedStrips, originalStrips); }

    [ContextMenu("Apply Suit Colors")]
    public void ApplyColors()
    {
        if (!isActiveAndEnabled) return;
        pendingRefresh = false;
        Cache(accentStrips, ref cachedStrips, ref originalStrips);
        if (scratch == null) scratch = new MaterialPropertyBlock();
        for (int i = 0; i < cachedStrips.Length; i++)
        {
            Renderer strip = cachedStrips[i];
            if (!strip) continue;
            strip.GetPropertyBlock(scratch);
            Color color = showAccentLights ? accentColor : new Color(.025f, .028f, .035f);
            scratch.SetColor("_BaseColor", color);
            scratch.SetColor("_Color", color);
            scratch.SetColor("_EmissionColor", showAccentLights ? accentColor * Mathf.Clamp(ledIntensity, 0, 5) : Color.black);
            strip.SetPropertyBlock(scratch);
        }
    }

    static void Cache(Renderer[] source, ref Renderer[] cached, ref MaterialPropertyBlock[] originals)
    {
        bool same = cached != null && cached.Length == (source == null ? 0 : source.Length);
        if (same) for (int i = 0; i < cached.Length; i++) if (cached[i] != source[i]) { same = false; break; }
        if (same) return;
        Restore(cached, originals);
        cached = source == null ? new Renderer[0] : (Renderer[])source.Clone();
        originals = new MaterialPropertyBlock[cached.Length];
        for (int i = 0; i < cached.Length; i++)
        {
            originals[i] = new MaterialPropertyBlock();
            if (cached[i]) cached[i].GetPropertyBlock(originals[i]);
        }
    }
    static void Restore(Renderer[] renderers, MaterialPropertyBlock[] originals)
    {
        if (renderers == null || originals == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i]) renderers[i].SetPropertyBlock(originals[i]);
    }
}
