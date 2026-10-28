using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Draws a glowing laser beam from the gun's muzzle toward whatever it is pointing at.
///
/// The beam is built from two lines drawn on top of each other:
///   - a wide, soft red "glow" line (uses the LineRenderer on this object)
///   - a thin, bright, almost-white "core" line (created automatically as a child)
/// plus two soft glow sprites: one at the muzzle and one where the beam hits.
///
/// - A dimmer "aim laser" is always visible so the player can see where they aim.
/// - When the trigger is pressed, a bright, thick "shot beam" flashes and fades out.
/// The beam stops at the first collider it hits (walls/cover block it), otherwise at maxDistance.
///
/// The glow is faked with soft textures instead of Bloom post-processing, so it stays cheap on Quest 2.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LaserRay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Object at the tip of the barrel. Its blue (Z) arrow must point where the gun shoots.")]
    public Transform muzzle;

    [Tooltip("The trigger button. Right gun: XRI Right Interaction/Activate. Left gun: XRI Left Interaction/Activate.")]
    public InputActionProperty fireAction;

    [Header("Ray")]
    [Tooltip("How far the laser reaches, in meters, if it doesn't hit anything.")]
    public float maxDistance = 50f;

    [Tooltip("Which layers the laser can hit. Untick your own player's layer so you can't shoot yourself.")]
    public LayerMask hitLayers = ~0;

    [Header("Look")]
    [Tooltip("Color of the soft outer glow.")]
    public Color laserColor = new Color(1f, 0.05f, 0.05f, 1f);
    [Tooltip("Color of the bright inner core. Near-white looks hottest.")]
    public Color coreColor = new Color(1f, 0.85f, 0.85f, 1f);

    [Header("Aim laser (always on)")]
    public bool showAimLaser = true;
    [Tooltip("Width of the bright center line, in meters.")]
    public float aimCoreWidth = 0.004f;
    [Tooltip("Width of the soft red glow around it, in meters.")]
    public float aimGlowWidth = 0.025f;
    [Tooltip("0 = invisible, 1 = as bright as a shot.")]
    [Range(0f, 1f)] public float aimBrightness = 0.45f;

    [Header("Shot beam (when firing)")]
    public float shotCoreWidth = 0.015f;
    public float shotGlowWidth = 0.08f;
    [Tooltip("How long the bright beam takes to fade out after a shot, in seconds.")]
    public float shotDuration = 0.15f;
    [Tooltip("Minimum time between two shots, in seconds.")]
    public float cooldown = 0.25f;

    [Header("Glow sprites")]
    public bool showMuzzleGlow = true;
    [Tooltip("Size of the glow at the barrel tip during a shot, in meters.")]
    public float muzzleGlowSize = 0.12f;
    public bool showHitGlow = true;
    [Tooltip("Size of the glowing spot where the beam hits, in meters.")]
    public float hitGlowSize = 0.15f;

    /// <summary>
    /// Called on every shot that hits something. Game logic (damage, score) can subscribe to this later:
    ///   GetComponent&lt;LaserRay&gt;().onHit += hit =&gt; { ... };
    /// </summary>
    public System.Action<RaycastHit> onHit;

    // Shared generated textures (made once for all guns)
    static Texture2D glowTexture, coreTexture, spotTexture;

    LineRenderer glowLine;
    LineRenderer coreLine;
    Transform muzzleGlow, hitGlow;
    Material muzzleGlowMat, hitGlowMat;
    Camera viewCamera;

    float shotTimer;      // > 0 while the shot beam is visible
    float nextShotTime;   // when the gun is allowed to fire again

    bool hasHit;          // result of this frame's raycast
    RaycastHit currentHit;
    Vector3 rayEnd;

    void Awake()
    {
        if (muzzle == null)
        {
            Debug.LogWarning($"{name}: LaserRay has no muzzle assigned, using the gun's own position.", this);
            muzzle = transform;
        }

        CreateTextures();

        // Outer glow: the LineRenderer already on this object
        glowLine = GetComponent<LineRenderer>();
        SetupLine(glowLine, glowTexture, 0);

        // Inner core: a second LineRenderer on a child object, drawn on top
        var coreObject = new GameObject("Laser Core");
        coreObject.transform.SetParent(transform, false);
        coreLine = coreObject.AddComponent<LineRenderer>();
        SetupLine(coreLine, coreTexture, 1);

        muzzleGlow = CreateGlowSprite("Laser Muzzle Glow (" + name + ")", out muzzleGlowMat);
        hitGlow = CreateGlowSprite("Laser Hit Glow (" + name + ")", out hitGlowMat);
    }

    void OnEnable()
    {
        // Make sure the trigger action is listening (safe even if something else already enabled it).
        fireAction.action?.Enable();
    }

    void OnDisable()
    {
        if (glowLine) glowLine.enabled = false;
        if (coreLine) coreLine.enabled = false;
        if (muzzleGlow) muzzleGlow.gameObject.SetActive(false);
        if (hitGlow) hitGlow.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (muzzleGlow) Destroy(muzzleGlow.gameObject);
        if (hitGlow) Destroy(hitGlow.gameObject);
    }

    void Update()
    {
        CastRay();

        InputAction action = fireAction.action;
        if (action != null && action.WasPressedThisFrame() && Time.time >= nextShotTime)
            Fire();

        DrawBeam();
    }

    /// <summary>Shoots an invisible ray from the muzzle and remembers where it stops.</summary>
    void CastRay()
    {
        Vector3 origin = muzzle.position;
        Vector3 direction = muzzle.forward;

        hasHit = Physics.Raycast(origin, direction, out currentHit, maxDistance,
                                 hitLayers, QueryTriggerInteraction.Ignore);

        rayEnd = hasHit ? currentHit.point : origin + direction * maxDistance;
    }

    void Fire()
    {
        shotTimer = shotDuration;
        nextShotTime = Time.time + cooldown;

        if (hasHit)
        {
            Debug.Log($"{name} hit {currentHit.collider.name} at {currentHit.distance:F2} m", currentHit.collider);
            onHit?.Invoke(currentHit);
        }
    }

    void DrawBeam()
    {
        // How "strong" the shot is right now: 1 just after firing, fading to 0.
        float shot = 0f;
        if (shotTimer > 0f)
        {
            shotTimer -= Time.deltaTime;
            shot = Mathf.Clamp01(shotTimer / Mathf.Max(shotDuration, 0.0001f));
        }

        float aim = showAimLaser ? aimBrightness : 0f;

        // Blend between the aim look and the shot look.
        float brightness = Mathf.Max(aim, shot);
        float coreWidth = Mathf.Lerp(showAimLaser ? aimCoreWidth : 0f, shotCoreWidth, shot);
        float glowWidth = Mathf.Lerp(showAimLaser ? aimGlowWidth : 0f, shotGlowWidth, shot);

        bool visible = brightness > 0.001f;
        glowLine.enabled = visible;
        coreLine.enabled = visible;

        if (visible)
        {
            Vector3 start = muzzle.position;
            UpdateLine(glowLine, start, rayEnd, glowWidth, WithAlpha(laserColor, brightness));
            UpdateLine(coreLine, start, rayEnd, coreWidth, WithAlpha(coreColor, brightness));
        }

        if (viewCamera == null) viewCamera = Camera.main;

        // Glow at the barrel tip: only while a shot is fading out.
        bool showMuzzle = showMuzzleGlow && shot > 0.001f;
        muzzleGlow.gameObject.SetActive(showMuzzle);
        if (showMuzzle)
            PlaceGlow(muzzleGlow, muzzleGlowMat, muzzle.position, muzzleGlowSize * (0.6f + 0.4f * shot), shot);

        // Glowing spot where the beam hits a surface.
        bool showHit = showHitGlow && visible && hasHit;
        hitGlow.gameObject.SetActive(showHit);
        if (showHit)
        {
            Vector3 spot = currentHit.point + currentHit.normal * 0.01f;
            PlaceGlow(hitGlow, hitGlowMat, spot, hitGlowSize * (0.5f + 0.5f * brightness), brightness);
        }
    }

    // ---------- helpers ----------

    static Color WithAlpha(Color c, float alphaMultiplier)
    {
        c.a *= alphaMultiplier;
        return c;
    }

    static void UpdateLine(LineRenderer line, Vector3 start, Vector3 end, float width, Color color)
    {
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
    }

    void PlaceGlow(Transform glow, Material mat, Vector3 position, float size, float alpha)
    {
        glow.position = position;
        glow.localScale = Vector3.one * size;
        if (viewCamera != null)
            glow.rotation = Quaternion.LookRotation(position - viewCamera.transform.position);
        mat.color = WithAlpha(laserColor, alpha);
    }

    static Material MakeMaterial(Texture2D texture)
    {
        // "Sprites/Default" is unlit, see-through and in the project's Always Included Shaders,
        // so it renders correctly in URP and in Quest builds.
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = texture;
        return mat;
    }

    static void SetupLine(LineRenderer line, Texture2D texture, int sortingOrder)
    {
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = sortingOrder;
        line.material = MakeMaterial(texture);
        line.enabled = false;
    }

    Transform CreateGlowSprite(string objectName, out Material mat)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = objectName;
        quad.layer = 2; // "Ignore Raycast", so the laser never hits its own glow
        DestroyImmediate(quad.GetComponent<Collider>());

        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        mat = MakeMaterial(spotTexture);
        renderer.material = mat;

        quad.SetActive(false);
        return quad.transform;
    }

    /// <summary>Generates the soft textures that make the beam look like it glows.</summary>
    static void CreateTextures()
    {
        if (glowTexture != null) return;

        // Beam textures: brightness falls off from the center line to the edges (across the width).
        glowTexture = MakeBeamTexture(2.2f);  // soft, wide falloff
        coreTexture = MakeBeamTexture(0.6f);  // fuller, sharper center

        // Round soft spot for the muzzle and hit glows.
        const int size = 64;
        spotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        spotTexture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.5f);
            spotTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        spotTexture.Apply();
    }

    static Texture2D MakeBeamTexture(float falloff)
    {
        const int height = 64;
        var tex = new Texture2D(4, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;               // 0..1 across the beam
            float centered = 1f - Mathf.Abs(v * 2f - 1f); // 1 in the middle, 0 at the edges
            float a = Mathf.Pow(centered, falloff);
            for (int x = 0; x < 4; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }
}
