#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class LaserTagHudAdjuster : EditorWindow
{
    static readonly Vector2[] Defaults =
    {
        new Vector2(-355f, 205f),
        new Vector2(0f, 245f),
        new Vector2(355f, -205f)
    };

    static readonly string[] GroupNames =
    {
        "Health + Lives",
        "Player Count",
        "Ammo + Recharge"
    };

    static readonly string[][] ElementNames =
    {
        new[]
        {
            "Health Label",
            "Health Back",
            "Health Fill",
            "Health Value",
            "Lives"
        },
        new[]
        {
            "Connected Players"
        },
        new[]
        {
            "Ammo Label",
            "Ammo Value",
            "Ammo Max",
            "Ammo Back",
            "Ammo Fill"
        }
    };

    readonly List<RectTransform>[] elements =
    {
        new List<RectTransform>(),
        new List<RectTransform>(),
        new List<RectTransform>()
    };

    readonly List<Vector2>[] originalPositions =
    {
        new List<Vector2>(),
        new List<Vector2>(),
        new List<Vector2>()
    };

    Vector2[] positions = new Vector2[3];
    Transform attachedHud;

    const string PrefKey = "LaserTag.TempHudAdjuster.";

    [MenuItem("Tools/Laser Tag/HUD Position Adjuster")]
    public static void OpenWindow()
    {
        var window = GetWindow<LaserTagHudAdjuster>(
            "HUD Position Adjuster"
        );

        window.minSize = new Vector2(390, 440);
        window.Show();
    }

    void OnEnable()
    {
        for (int i = 0; i < 3; i++)
        {
            positions[i] = new Vector2(
                EditorPrefs.GetFloat(
                    PrefKey + i + ".x",
                    Defaults[i].x
                ),
                EditorPrefs.GetFloat(
                    PrefKey + i + ".y",
                    Defaults[i].y
                )
            );
        }

        EditorApplication.update += EditorUpdate;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
    }

    void PlayModeChanged(PlayModeStateChange state)
    {
        ClearAttachment();
        Repaint();
    }

    void ClearAttachment()
    {
        attachedHud = null;

        for (int i = 0; i < 3; i++)
        {
            elements[i].Clear();
            originalPositions[i].Clear();
        }
    }

    void EditorUpdate()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (!attachedHud)
            TryAttach();

        if (attachedHud)
            ApplyPositions();

        Repaint();
    }

    void TryAttach()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsSortMode.None
        );

        Transform hud = null;

        foreach (Canvas canvas in canvases)
        {
            if (canvas && canvas.name == "Player HUD")
            {
                hud = canvas.transform;
                break;
            }
        }

        if (!hud)
            return;

        for (int i = 0; i < 3; i++)
        {
            elements[i].Clear();
            originalPositions[i].Clear();

            foreach (string elementName in ElementNames[i])
            {
                Transform child = hud.Find(elementName);

                if (!child ||
                    !(child is RectTransform rect))
                {
                    ClearAttachment();
                    return;
                }

                elements[i].Add(rect);
                originalPositions[i].Add(
                    rect.anchoredPosition
                );
            }
        }

        attachedHud = hud;
        ApplyPositions();

        Debug.Log(
            "[HUD Adjuster] Connected to runtime Player HUD."
        );
    }

    void ApplyPositions()
    {
        if (!attachedHud)
            return;

        for (int group = 0; group < 3; group++)
        {
            Vector2 delta =
                positions[group] - Defaults[group];

            for (int i = 0; i < elements[group].Count; i++)
            {
                RectTransform rect = elements[group][i];

                if (rect)
                {
                    rect.anchoredPosition =
                        originalPositions[group][i] + delta;
                }
            }
        }
    }

    void SavePositions()
    {
        for (int i = 0; i < 3; i++)
        {
            EditorPrefs.SetFloat(
                PrefKey + i + ".x",
                positions[i].x
            );

            EditorPrefs.SetFloat(
                PrefKey + i + ".y",
                positions[i].y
            );
        }
    }

    void ResetGroup(int i)
    {
        positions[i] = Defaults[i];
        SavePositions();
        ApplyPositions();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField(
            "LIVE HUD POSITION EDITOR",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Adjust the three HUD groups during Play Mode. " +
            "All elements within a group move together. " +
            "The original PlayerHud.cs is untouched.",
            MessageType.Info
        );

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode and load the arena. " +
                "The tool will connect to the HUD automatically.",
                MessageType.Warning
            );
        }
        else if (attachedHud)
        {
            EditorGUILayout.HelpBox(
                "Connected: changes update the HUD live.",
                MessageType.Info
            );
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Waiting for the Player HUD to appear...",
                MessageType.Warning
            );
        }

        EditorGUILayout.Space(8);

        for (int i = 0; i < 3; i++)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField(
                GroupNames[i],
                EditorStyles.boldLabel
            );

            EditorGUI.BeginChangeCheck();

            Vector2 value = positions[i];

            value.x = EditorGUILayout.Slider(
                "X position",
                value.x,
                -500f,
                500f
            );

            value.y = EditorGUILayout.Slider(
                "Y position",
                value.y,
                -280f,
                280f
            );

            value = EditorGUILayout.Vector2Field(
                "Exact X / Y",
                value
            );

            if (EditorGUI.EndChangeCheck())
            {
                positions[i] = value;
                SavePositions();
                ApplyPositions();
            }

            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(
                "Reset Group",
                GUILayout.Width(110)
            ))
            {
                ResetGroup(i);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
        }

        EditorGUILayout.Space(8);

        if (GUILayout.Button(
            "RESET ALL TO CODEX DEFAULTS",
            GUILayout.Height(28)
        ))
        {
            for (int i = 0; i < 3; i++)
                positions[i] = Defaults[i];

            SavePositions();
            ApplyPositions();
        }

        if (GUILayout.Button(
            "COPY FINAL COORDINATES",
            GUILayout.Height(32)
        ))
        {
            EditorGUIUtility.systemCopyBuffer =
                CoordinateReport();

            Debug.Log(
                "[HUD Adjuster] Coordinates copied."
            );
        }

        if (GUILayout.Button(
            "PRINT COORDINATES TO CONSOLE",
            GUILayout.Height(26)
        ))
        {
            Debug.Log(CoordinateReport());
        }

        EditorGUILayout.HelpBox(
            "The coordinates are saved in Editor preferences, " +
            "so they remain available after Play Mode ends. " +
            "They are NOT permanently baked into the HUD yet.",
            MessageType.None
        );
    }

    string CoordinateReport()
    {
        return
            "FINAL HUD COORDINATES\n" +
            "Health + Lives: (" +
            positions[0].x + ", " +
            positions[0].y + ")\n" +
            "Players: (" +
            positions[1].x + ", " +
            positions[1].y + ")\n" +
            "Ammo: (" +
            positions[2].x + ", " +
            positions[2].y + ")";
    }
}
#endif
