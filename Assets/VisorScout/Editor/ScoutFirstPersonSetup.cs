using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Rebuilds the separate Task 1 setup using existing visuals and XR controls.
public static class ScoutFirstPersonSetup
{
    public const string SourceAvatar = "Assets/VisorScout/GeneratedSlimSuit/PlayerAvatar_RefinedSuit.prefab";
    public const string PlayerPrefab = "Assets/PlayerAvatar/PlayerAvatar.prefab";
    public const string SourceScene = "Assets/AvatarTest.unity";
    public const string TestScene = "Assets/PlayerAvatar/AvatarFirstPersonTest.unity";

    [MenuItem("Tools/Laser Tag/Task 1/Create Avatar Test Setup")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode first."); return; }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
            { Debug.LogWarning("Save any untitled scene before opening an additive avatar test scene."); return; }
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(SourceAvatar))
            throw new InvalidOperationException("The current Scout source prefab is missing: " + SourceAvatar);
        // A submitted canonical test scene is a complete reusable template.
        // Prefer it so this tool does not require an uncertain legacy scene.
        bool copyCurrent = AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScene);
        string templateScene = copyCurrent ? TestScene : SourceScene;
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(templateScene))
            throw new InvalidOperationException("The existing AvatarTest scene is missing.");
        if (!AssetDatabase.IsValidFolder("Assets/PlayerAvatar")) AssetDatabase.CreateFolder("Assets", "PlayerAvatar");
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(SourceAvatar);
            try
            {
                if (contents.GetComponentInChildren<SkinnedMeshRenderer>(true) || contents.GetComponentInChildren<Animator>(true))
                    throw new InvalidOperationException("This setup expects the existing separate-mesh Scout, not a skeletal avatar.");
                contents.name = "PlayerAvatar";
                contents.GetComponent<ScoutAvatar>().localCamera = null;
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        string scenePath = AssetDatabase.GenerateUniqueAssetPath(TestScene);
        if (!AssetDatabase.CopyAsset(templateScene, scenePath)) throw new InvalidOperationException("Cannot copy avatar test scene.");
        if (copyCurrent)
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log("Task 1 test scene copied with its existing avatar/rig references: " + scenePath);
            return;
        }
        Scene original = SceneManager.GetActiveScene();
        Scene test = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(test);
            Transform rig = null;
            foreach (GameObject root in test.GetRootGameObjects()) if (root.name == "XR Origin (VR)" && root.activeSelf) rig = root.transform;
            if (!rig) throw new InvalidOperationException("Expected the active XR Origin (VR) root in AvatarTest.");
            Transform offset = rig.Find("Camera Offset");
            Transform camera = offset ? offset.Find("Main Camera") : null;
            Transform left = offset ? offset.Find("Left Controller") : null;
            Transform right = offset ? offset.Find("Right Controller") : null;
            if (!camera || !left || !right) throw new InvalidOperationException("The active VR rig's camera/controllers are missing.");
            Transform oldPlaceholder = rig.Find("PlayerAvatar");
            if (oldPlaceholder) oldPlaceholder.gameObject.SetActive(false);
            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab), test);
            ScoutAvatar avatar = player.GetComponent<ScoutAvatar>();
            // Only this copied test scene gets a matching standing eye height.
            // Device actions, tracking mode and controller components are intact.
            Vector3 offsetPosition = offset.localPosition; offsetPosition.y = avatar.head.localPosition.y; offset.localPosition = offsetPosition;
            foreach (MonoBehaviour component in rig.GetComponents<MonoBehaviour>())
                if (component && component.GetType().Name == "XROrigin")
                {
                    var serialized = new SerializedObject(component);
                    SerializedProperty yOffset = serialized.FindProperty("m_CameraYOffset");
                    if (yOffset != null) { yOffset.floatValue = avatar.head.localPosition.y; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                }
            ScoutLocalRigBinding binding = new GameObject("Local Avatar Binding").AddComponent<ScoutLocalRigBinding>();
            binding.avatar = avatar; binding.xrOrigin = rig; binding.trackedCamera = camera.GetComponent<Camera>();
            binding.leftController = left; binding.rightController = right;
            binding.leftGun = FitPistolGrip(left, true, out binding.leftHandPositionOffset, out binding.leftHandRotationOffset);
            binding.rightGun = FitPistolGrip(right, false, out binding.rightHandPositionOffset, out binding.rightHandRotationOffset);
            binding.ApplyPose(); avatar.UpdateArmPose();
            if (!EditorSceneManager.SaveScene(test, scenePath)) throw new InvalidOperationException("Could not save avatar test scene.");
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log("Task 1 test scene created: " + scenePath + ". Existing rig controls and the automatically loaded XR Interaction Simulator are reused.");
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (test.IsValid() && test.isLoaded) EditorSceneManager.CloseScene(test, true);
        }
    }

    // Fit the existing separate glove to the installed pistol's handle. These
    // offsets affect only the avatar's third-person hands, never the gun or input.
    static Transform FitPistolGrip(Transform controller, bool left, out Vector3 position, out Vector3 rotation)
    {
        position = rotation = Vector3.zero;
        foreach (Transform child in controller.GetComponentsInChildren<Transform>(true))
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (!source || source.name != "Sci Fi Pistol" ||
                AssetDatabase.GetAssetPath(source) != "Assets/The Developer Train/Sci Fi Guns/Prefabs/Sci Fi Pistol.prefab") continue;
            Vector3 grip = left ? ScoutLocalRigBinding.DefaultLeftGripPosition : ScoutLocalRigBinding.DefaultRightGripPosition;
            position = controller.InverseTransformPoint(child.TransformPoint(grip));
            rotation = (Quaternion.Inverse(controller.rotation) * child.rotation * Quaternion.Euler(ScoutLocalRigBinding.DefaultGripRotation)).eulerAngles;
            return child;
        }
        return null;
    }
}
