using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opts the owned avatar and menu gameplay scenes into laptop height bounds.
// The adapter independently guards against physical XR devices and displays.
[InitializeOnLoad]
public static class ScoutLaptopTestBootstrap
{
    const string MenuGameScene = "Assets/Scenes/RelayVRGame.unity";

    static ScoutLaptopTestBootstrap()
    {
        EditorApplication.playModeStateChanged += Changed;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            ConfigureLoadedBindings();
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isPlaying && IsSupportedScene(scene.path))
            ConfigureLoadedBindings();
    }

    static bool IsSupportedScene(string path)
    {
        return path == ScoutFirstPersonSetup.TestScene || path == MenuGameScene;
    }

    static void ConfigureLoadedBindings()
    {
        foreach (var binding in Object.FindObjectsByType<ScoutLocalRigBinding>())
        {
            if (!IsSupportedScene(binding.gameObject.scene.path))
                continue;

            var bounds = binding.GetComponent<ScoutLaptopHeightBounds>();
            if (!bounds)
                bounds = binding.gameObject.AddComponent<ScoutLaptopHeightBounds>();

            bounds.binding = binding;
        }
    }
}
