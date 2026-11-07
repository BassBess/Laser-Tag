using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Head-locked HUD for the local player, built entirely in code (no prefab to wire up).
///
///  - Upper left:  health bar (shrinks and turns red when hit; 0 = tagged out).
///  - Upper right: shots left, with a small recharge bar.
///  - Centre:      lobby / countdown / respawn countdown / eliminated messages.
///  - Full view:   red flash when you are hit.
///  - Game over:   scoreboard ranking every player, your own row highlighted.
///
/// VR has no "screen", so the HUD is a small world-space canvas fixed in front of the camera and drawn on
/// top of everything (it never disappears into a wall).
/// </summary>
public class PlayerHud : MonoBehaviour
{
    public static PlayerHud Instance { get; private set; }

    [Tooltip("Camera the HUD follows. Defaults to the local player's camera.")]
    public Camera targetCamera;
    [Tooltip("Distance of the HUD in front of the eyes, in metres.")]
    [Range(0.3f, 2f)] public float distance = 0.6f;
    [Tooltip("Width of the HUD in metres at that distance. Smaller = elements closer to the centre of view.")]
    [Range(0.2f, 1.5f)] public float width = 0.6f;

    [Header("Colours")]
    public Color healthHigh = new Color(0.2f, 0.95f, 0.45f);
    public Color healthLow = new Color(1f, 0.15f, 0.1f);
    public Color ammoColor = new Color(0.4f, 0.85f, 1f);
    public Color highlightColor = new Color(1f, 0.85f, 0.2f);

    const float CanvasWidth = 1000f, CanvasHeight = 560f;
    const int MaxRows = 8;

    Canvas canvas;
    Material imageMaterial, textMaterial;
    RectTransform healthFill;
    Image healthFillImage;
    TextMeshProUGUI healthText, ammoText, ammoLabel, centerText, subText, statusText;
    RectTransform ammoFill;
    Image damageFlash, deathTint;
    GameObject scoreboard;
    TextMeshProUGUI scoreTitle, scoreFooter;
    readonly List<TextMeshProUGUI[]> rows = new List<TextMeshProUGUI[]>();
    readonly List<Image> rowBackgrounds = new List<Image>();
    readonly List<NetworkPlayer> ranking = new List<NetworkPlayer>();
    float flash;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (targetCamera == null && LocalPlayerRig.Instance != null) targetCamera = LocalPlayerRig.Instance.playerCamera;
        if (targetCamera == null) targetCamera = Camera.main;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    /// <summary>Red flash over the whole view (called when the local player loses health).</summary>
    public void FlashDamage(float strength = 0.55f) => flash = Mathf.Max(flash, strength);

    // =====================================================================================
    // Building
    // =====================================================================================

    void Build()
    {
        if (targetCamera == null)
        {
            Debug.LogWarning("PlayerHud: no camera found.", this);
            return;
        }

        imageMaterial = new Material(Shader.Find("UI/Default"));
        imageMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        var root = new GameObject("Player HUD", typeof(RectTransform));
        root.layer = 5; // UI
        root.transform.SetParent(targetCamera.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, distance);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * (width / CanvasWidth);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 30000;
        ((RectTransform)root.transform).sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

        // Full-view overlays (bigger than the canvas so they cover the whole field of view).
        deathTint = MakeImage(root.transform, "Death Tint", Vector2.zero, new Vector2(6000, 4000), new Color(0.35f, 0f, 0f, 0f));
        damageFlash = MakeImage(root.transform, "Damage Flash", Vector2.zero, new Vector2(6000, 4000), new Color(1f, 0f, 0f, 0f));

        // ---- Health (upper left) ----
        var healthPos = new Vector2(-CanvasWidth * 0.5f + 175f, CanvasHeight * 0.5f - 45f);
        MakeText(root.transform, "Health Label", healthPos + new Vector2(-75f, 30f), new Vector2(200, 30), "HEALTH", 22,
            TextAlignmentOptions.Left, new Color(1, 1, 1, 0.8f));
        MakeImage(root.transform, "Health Back", healthPos, new Vector2(300, 28), new Color(0f, 0f, 0f, 0.55f));
        healthFillImage = MakeImage(root.transform, "Health Fill", healthPos, new Vector2(292, 20), healthHigh);
        healthFill = healthFillImage.rectTransform;
        healthFill.pivot = new Vector2(0f, 0.5f);
        healthFill.anchoredPosition = healthPos + new Vector2(-146f, 0f);
        healthText = MakeText(root.transform, "Health Value", healthPos + new Vector2(75f, 30f), new Vector2(150, 30), "100", 24,
            TextAlignmentOptions.Right, Color.white);

        // ---- Ammo (upper right) ----
        var ammoPos = new Vector2(CanvasWidth * 0.5f - 130f, CanvasHeight * 0.5f - 45f);
        ammoLabel = MakeText(root.transform, "Ammo Label", ammoPos + new Vector2(0f, 34f), new Vector2(220, 30), "SHOTS", 22,
            TextAlignmentOptions.Right, new Color(1, 1, 1, 0.8f));
        ammoText = MakeText(root.transform, "Ammo Value", ammoPos + new Vector2(0f, 2f), new Vector2(220, 50), "100", 44,
            TextAlignmentOptions.Right, ammoColor);
        MakeImage(root.transform, "Ammo Back", ammoPos + new Vector2(10f, -26f), new Vector2(200, 8), new Color(0f, 0f, 0f, 0.55f));
        Image ammoFillImage = MakeImage(root.transform, "Ammo Fill", ammoPos + new Vector2(10f, -26f), new Vector2(200, 8), ammoColor);
        ammoFill = ammoFillImage.rectTransform;
        ammoFill.pivot = new Vector2(1f, 0.5f);
        ammoFill.anchoredPosition = ammoPos + new Vector2(110f, -26f);

        // ---- Centre messages ----
        statusText = MakeText(root.transform, "Status", new Vector2(0f, CanvasHeight * 0.5f - 30f), new Vector2(420, 30), "", 20,
            TextAlignmentOptions.Center, new Color(1, 1, 1, 0.75f));
        centerText = MakeText(root.transform, "Center", new Vector2(0f, 60f), new Vector2(900, 120), "", 64,
            TextAlignmentOptions.Center, Color.white);
        subText = MakeText(root.transform, "Sub", new Vector2(0f, -20f), new Vector2(900, 80), "", 28,
            TextAlignmentOptions.Center, new Color(1, 1, 1, 0.9f));

        // ---- Scoreboard ----
        scoreboard = new GameObject("Scoreboard", typeof(RectTransform));
        scoreboard.layer = 5;
        scoreboard.transform.SetParent(root.transform, false);
        MakeImage(scoreboard.transform, "Panel", new Vector2(0f, -10f), new Vector2(760, 470), new Color(0.02f, 0.03f, 0.08f, 0.88f));
        scoreTitle = MakeText(scoreboard.transform, "Title", new Vector2(0f, 190f), new Vector2(700, 60), "GAME OVER", 46,
            TextAlignmentOptions.Center, Color.white);
        float[] columnX = { -300f, -150f, 80f, 180f, 280f };
        float[] columnW = { 80f, 300f, 100f, 100f, 100f };
        string[] headers = { "#", "PLAYER", "KILLS", "HITS", "DEATHS" };
        for (int r = 0; r <= MaxRows; r++)
        {
            float y = 130f - r * 40f;
            Image bg = MakeImage(scoreboard.transform, "Row " + r, new Vector2(0f, y), new Vector2(720, 36), new Color(1, 1, 1, 0f));
            var cells = new TextMeshProUGUI[columnX.Length];
            for (int c = 0; c < columnX.Length; c++)
            {
                cells[c] = MakeText(scoreboard.transform, "Cell", new Vector2(columnX[c] + columnW[c] * 0.5f - 40f, y),
                    new Vector2(columnW[c], 36), r == 0 ? headers[c] : "", r == 0 ? 20 : 26,
                    c == 1 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center,
                    r == 0 ? new Color(1, 1, 1, 0.6f) : Color.white);
            }
            if (r > 0)
            {
                rows.Add(cells);
                rowBackgrounds.Add(bg);
            }
        }
        scoreFooter = MakeText(scoreboard.transform, "Footer", new Vector2(0f, -215f), new Vector2(700, 40), "", 24,
            TextAlignmentOptions.Center, new Color(1, 1, 1, 0.8f));
        scoreboard.SetActive(false);
    }

    Image MakeImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.material = imageMaterial;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, string text, float fontSize,
        TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        if (textMaterial == null && tmp.fontSharedMaterial != null)
        {
            // Copy of the font material that ignores depth, so text stays readable in front of walls.
            textMaterial = new Material(tmp.fontSharedMaterial);
            textMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }
        if (textMaterial != null) tmp.fontSharedMaterial = textMaterial;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        return tmp;
    }

    // =====================================================================================
    // Updating
    // =====================================================================================

    void LateUpdate()
    {
        if (canvas == null) return;

        NetworkPlayer me = NetworkPlayer.Local;
        LocalPlayerRig rig = LocalPlayerRig.Instance;
        LaserTagGameManager gm = LaserTagGameManager.Instance;
        bool gameRunning = gm != null && gm.IsSpawned;

        // ---- health ----
        int maxHealth = me != null ? Mathf.Max(1, me.maxHealth) : 100;
        int health = me != null ? me.Health.Value : maxHealth;
        float healthFraction = Mathf.Clamp01((float)health / maxHealth);
        healthFill.sizeDelta = new Vector2(292f * healthFraction, 20f);
        healthFillImage.color = Color.Lerp(healthLow, healthHigh, healthFraction);
        healthText.text = health.ToString();

        // ---- ammo ----
        int ammo = rig != null ? rig.Ammo : 0;
        int maxAmmo = rig != null ? rig.maxAmmo : 100;
        ammoText.text = ammo.ToString();
        bool emptyBlink = ammo == 0 && rig != null && rig.TimeSinceEmptyClick < 0.6f;
        ammoText.color = ammo == 0 ? (emptyBlink ? Color.white : healthLow) : ammoColor;
        ammoFill.sizeDelta = new Vector2(200f * Mathf.Clamp01((float)ammo / Mathf.Max(1, maxAmmo)), 8f);
        ammoLabel.text = rig != null && rig.IsRecharging ? "RECHARGING" : "SHOTS";

        // ---- damage flash ----
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
        damageFlash.color = new Color(1f, 0f, 0f, flash);

        // ---- messages ----
        string center = "", sub = "", status = "";
        float tint = 0f;
        bool showScores = false;

        if (me == null || !gameRunning)
        {
            center = "";
            sub = LanSessionBootstrap.Status;
        }
        else
        {
            int players = gm.ConnectedPlayerCount;
            switch (gm.Phase.Value)
            {
                case GamePhase.Lobby:
                    center = "LASER TAG";
                    sub = players < gm.minPlayersToStart
                        ? $"{players} player{(players == 1 ? "" : "s")} connected - waiting for {gm.minPlayersToStart}"
                        : $"{players} players connected - press A to start";
                    break;

                case GamePhase.Countdown:
                    int count = Mathf.CeilToInt(gm.SecondsUntil(gm.PhaseEndsAt.Value));
                    center = count > 0 ? count.ToString() : "GO!";
                    sub = me.InRound.Value ? "Get ready" : "Spectating - you'll join next round";
                    break;

                case GamePhase.Playing:
                    status = me.InRound.Value
                        ? $"Lives {me.LivesLeft.Value}   |   Players left {gm.PlayersLeft}"
                        : $"Players left {gm.PlayersLeft}";
                    switch (me.LifeState.Value)
                    {
                        case PlayerLifeState.Respawning:
                            center = "TAGGED!";
                            sub = $"Respawning in {Mathf.CeilToInt(gm.SecondsUntil(me.RespawnAt.Value))}";
                            tint = 0.45f;
                            break;
                        case PlayerLifeState.Eliminated:
                            center = "ELIMINATED";
                            sub = "Explore the arena - nobody can see you";
                            tint = 0.15f;
                            break;
                        case PlayerLifeState.Spectating:
                            sub = "Round in progress - you'll join the next one";
                            break;
                    }
                    break;

                case GamePhase.GameOver:
                    showScores = true;
                    break;
            }
        }

        centerText.text = center;
        subText.text = sub;
        statusText.text = status;
        deathTint.color = new Color(0.35f, 0f, 0f, Mathf.MoveTowards(deathTint.color.a, tint, Time.deltaTime * 2f));

        if (scoreboard.activeSelf != showScores) scoreboard.SetActive(showScores);
        if (showScores) UpdateScoreboard(gm, me);
    }

    void UpdateScoreboard(LaserTagGameManager gm, NetworkPlayer me)
    {
        gm.GetRanking(ranking);
        NetworkPlayer winner = ranking.Count > 0 ? ranking[0] : null;
        scoreTitle.text = winner == null ? "GAME OVER" : (winner == me ? "YOU WIN!" : $"{winner.Name} WINS");

        for (int i = 0; i < rows.Count; i++)
        {
            TextMeshProUGUI[] cells = rows[i];
            bool used = i < ranking.Count;
            NetworkPlayer p = used ? ranking[i] : null;
            bool mine = p != null && p == me;
            cells[0].text = used ? (i + 1).ToString() : "";
            cells[1].text = used ? p.Name + (mine ? "  (you)" : "") : "";
            cells[2].text = used ? p.Kills.Value.ToString() : "";
            cells[3].text = used ? p.Hits.Value.ToString() : "";
            cells[4].text = used ? p.Deaths.Value.ToString() : "";
            Color c = mine ? highlightColor : Color.white;
            foreach (TextMeshProUGUI cell in cells) cell.color = c;
            rowBackgrounds[i].color = mine ? new Color(highlightColor.r, highlightColor.g, highlightColor.b, 0.18f) : new Color(1, 1, 1, used && i % 2 == 0 ? 0.04f : 0f);
        }
        scoreFooter.text = "Press A to play again";
    }
}
