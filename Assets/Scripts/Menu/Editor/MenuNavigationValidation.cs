using UnityEditor;
using UnityEditor.SceneManagement;

// Run in an isolated project copy: enters Play Mode and sends simulated XR device states.
// Does not contact Relay or change production input bindings.
[InitializeOnLoad]
public static class MenuNavigationValidation
{
    const string Running="LaserTag.MenuNavigationValidation";
    static MenuNavigationValidation()
    {
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running,false))
                MenuNavigationValidationRunner.Begin();
        };
    }
    public static void Run()
    {
        SessionState.SetBool(Running,true);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        EditorApplication.isPlaying=true;
    }
    public static void Finish(bool success)
    {
        SessionState.SetBool(Running,false);
        EditorApplication.Exit(success?0:1);
    }
}

