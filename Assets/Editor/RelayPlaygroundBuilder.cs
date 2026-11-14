#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Creates a NEW scene and NEW prefab variant. Never modifies the teammate's base avatar.
public static class RelayPlaygroundBuilder
{
    private const string ModelPath = "Assets/PlayerAvatar/PlayerAvatar.prefab";
    private const string PrefabPath = "Assets/Networking/RelayAvatarPlayer.prefab";
    private const string ScenePath = "Assets/Scenes/RelayPlayground.unity";
    private const string PrefabListPath = "Assets/Networking/RelayNetworkPrefabs.asset";

    [MenuItem("Tools/Laser Tag/Create Relay Playground")]
    private static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Stop Play Mode before generating the Relay playground.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!basePrefab)
        {
            EditorUtility.DisplayDialog("Missing avatar", "Cannot find " + ModelPath, "OK");
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Networking"))
            AssetDatabase.CreateFolder("Assets", "Networking");

        // Create a variant of the teammate's prefab: original stays unchanged.
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = "RelayAvatarPlayer";
        if (!instance.GetComponent<NetworkObject>()) instance.AddComponent<NetworkObject>();
        if (!instance.GetComponent<AvatarPoseDriver>()) instance.AddComponent<AvatarPoseDriver>();
        if (!instance.GetComponent<RelayAvatarPlayer>()) instance.AddComponent<RelayAvatarPlayer>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);
        if (!prefab)
        {
            Debug.LogError("Could not save network player prefab.");
            return;
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (!list)
        {
            list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(list, PrefabListPath);
        }
        if (!list.Contains(prefab))
            list.Add(new NetworkPrefab { Prefab = prefab });
        EditorUtility.SetDirty(list);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var netObj = new GameObject("RelayNetworkManager");
        UnityTransport transport = netObj.AddComponent<UnityTransport>();
        NetworkManager manager = netObj.AddComponent<NetworkManager>();
        netObj.AddComponent<RelayRoomUI>();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = prefab;
        if (!manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "PlaygroundFloor";
        floor.transform.localScale = new Vector3(5, 1, 5);
        floor.transform.position = Vector3.zero;

        var cameraObj = new GameObject("Main Camera");
        cameraObj.tag = "MainCamera";
        Camera camera = cameraObj.AddComponent<Camera>();
        cameraObj.AddComponent<AudioListener>();
        cameraObj.AddComponent<RelayThirdPersonCamera>();
        camera.transform.position = new Vector3(0, 3, -7);
        camera.transform.LookAt(new Vector3(0, 1, 0));

        var lightObj = new GameObject("Directional Light");
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObj.transform.rotation = Quaternion.Euler(50, -35, 0);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("Could not save " + ScenePath);
            return;
        }
        // Put the test scene at build index 0 to prevent Windows test builds opening SampleScene.
        var oldScenes = EditorBuildSettings.scenes;
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        foreach (var entry in oldScenes)
            if (entry.path != ScenePath) scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[RelayPlayground] Created " + ScenePath + ", player prefab " + PrefabPath +
                  ". Press Play in this scene to host via Relay.");
        EditorUtility.DisplayDialog("Relay playground ready",
            "Scene and prefab created. Check that this project is linked to Unity Cloud and Multiplayer Services is installed. Then press Play in RelayPlayground to host.", "OK");
    }
}
#endif
