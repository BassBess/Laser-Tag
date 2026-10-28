using UnityEditor;
using UnityEngine;

public class ScoutAvatarOptionsWindow : EditorWindow
{
    [SerializeField] ScoutAvatarBuilder.Options options = new ScoutAvatarBuilder.Options();
    [SerializeField] string lastFolder;

    [MenuItem("Tools/Laser Tag/Visor Scout Options...")]
    static void Open()
    {
        var window = GetWindow<ScoutAvatarOptionsWindow>("Visor Scout");
        window.minSize = new Vector2(360, 390);
    }

    void OnGUI()
    {
        if (options == null) options = new ScoutAvatarBuilder.Options();
        EditorGUILayout.LabelField("Suit appearance", EditorStyles.boldLabel);
        options.showAccentLights = EditorGUILayout.Toggle("Show LED lines", options.showAccentLights);
        using (new EditorGUI.DisabledScope(!options.showAccentLights))
        {
            options.accentColor = EditorGUILayout.ColorField(new GUIContent("LED color"), options.accentColor, true, false, false);
            using (new EditorGUILayout.HorizontalScope())
            {
                Preset("Teal", new Color(.025f, .65f, .72f));
                Preset("Blue", new Color(.08f, .32f, .90f));
                Preset("Amber", new Color(.95f, .46f, .055f));
                Preset("Violet", new Color(.47f, .17f, .86f));
            }
            options.ledIntensity = EditorGUILayout.Slider("LED brightness", options.ledIntensity, 0, 5);
        }
        EditorGUILayout.HelpBox("White/silver armor over charcoal. Only the LED lines use the selected color. LED color and brightness can also be changed on Scout Suit Appearance.", MessageType.Info);
        EditorGUILayout.Space();
        options.height = EditorGUILayout.Slider("Standing height (m)", options.height, 1.60f, 2.10f);
        options.holster = (ScoutAvatarBuilder.HolsterSide)EditorGUILayout.EnumPopup("Waist holster", options.holster);
        EditorGUILayout.HelpBox("The holster includes a GunRest attachment. Fit your gun to this socket; weapon interactions remain controlled by your gun setup.", MessageType.None);
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Create New Avatar and Studio", GUILayout.Height(32)))
            {
                string result = ScoutAvatarBuilder.Build(options);
                if (!string.IsNullOrEmpty(result)) lastFolder = result;
            }
        if (!string.IsNullOrEmpty(lastFolder))
        {
            EditorGUILayout.LabelField(lastFolder, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Select New Studio Scene"))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(lastFolder + "/ScoutSuit_Studio.unity");
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
        }
    }

    void Preset(string label, Color color)
    {
        if (GUILayout.Button(label)) options.accentColor = color;
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void DrawGunRest(ScoutAvatar avatar, GizmoType type)
    {
        if (!avatar.waistGunRest) return;
        Transform socket = avatar.waistGunRest;
        Gizmos.color = new Color(.1f, .9f, .8f);
        Gizmos.DrawWireSphere(socket.position, .025f);
        Gizmos.DrawLine(socket.position, socket.position + socket.forward * .14f);
        Handles.Label(socket.position + Vector3.up * .045f, "GunRest");
    }
}
