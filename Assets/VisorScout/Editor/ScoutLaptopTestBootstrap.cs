using UnityEditor;
using UnityEngine;

// This editor-only setup affects only the explicitly owned Task 1 test scene.
// It creates no serialized scene/prefab overrides and is absent from a player.
[InitializeOnLoad]
public static class ScoutLaptopTestBootstrap
{
    static ScoutLaptopTestBootstrap() { EditorApplication.playModeStateChanged += Changed; }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        foreach (var binding in Object.FindObjectsByType<ScoutLocalRigBinding>())
            if (binding.gameObject.scene.path == ScoutFirstPersonSetup.TestScene && !binding.GetComponent<ScoutLaptopHeightBounds>())
                binding.gameObject.AddComponent<ScoutLaptopHeightBounds>().binding = binding;
    }
}
