using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TESTING ONLY. Adds / removes the practice bots object in the open scene.</summary>
public static class PracticeBotsMenu
{
    const string ObjectName = "Practice Bots (testing only)";

    [MenuItem("Tools/Laser Tag/Testing/Add Practice Bots")]
    static void Add()
    {
        PracticeBotSpawner existing = Object.FindAnyObjectByType<PracticeBotSpawner>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            return;
        }
        var go = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(go, "Add Practice Bots");
        go.AddComponent<PracticeBotSpawner>();
        Selection.activeObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Practice bots added. Press Play, then Enter to start a round. Remove them with Tools > Laser Tag > Testing > Remove Practice Bots.");
    }

    [MenuItem("Tools/Laser Tag/Testing/Remove Practice Bots")]
    static void Remove()
    {
        foreach (PracticeBotSpawner spawner in Object.FindObjectsByType<PracticeBotSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            Undo.DestroyObjectImmediate(spawner.gameObject);
        }
        Debug.Log("Practice bots removed.");
    }

    [MenuItem("Tools/Laser Tag/Testing/Add Practice Bots", true)]
    [MenuItem("Tools/Laser Tag/Testing/Remove Practice Bots", true)]
    static bool NotPlaying() => !EditorApplication.isPlaying;
}
