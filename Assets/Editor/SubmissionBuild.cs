using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SubmissionBuild
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
    public static readonly string[] Scenes={"Assets/Scenes/MainMenu.unity","Assets/Scenes/RelayVRGame.unity"};
    [MenuItem("Tools/Laser Tag/Submission/Open MainMenu and Review Views")]
    public static void OpenReview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode first.");
        if(!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scenes[0]);
        float width=1280,height=800;
#if UNITY_EDITOR_WIN
        width=GetSystemMetrics(0)/EditorGUIUtility.pixelsPerPoint;height=GetSystemMetrics(1)/EditorGUIUtility.pixelsPerPoint;
#endif
        float sceneWidth=(width-40)*.36f,gameWidth=width-sceneWidth-50,viewHeight=Mathf.Max(400,height-145);
        var scene=Resources.FindObjectsOfTypeAll<SceneView>().FirstOrDefault(w=>w.name=="LaserTagSubmissionSceneView");
        if(!scene)scene=ScriptableObject.CreateInstance<SceneView>();scene.name="LaserTagSubmissionSceneView";scene.titleContent=new GUIContent("Scene — avatar outside view");scene.Show();scene.position=new Rect(15,90,sceneWidth,viewHeight);
        var gameType=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var game=Resources.FindObjectsOfTypeAll(gameType).OfType<EditorWindow>().FirstOrDefault(w=>w.name=="LaserTagSubmissionGameView");
        if(!game)game=ScriptableObject.CreateInstance(gameType) as EditorWindow;game.name="LaserTagSubmissionGameView";
        game.titleContent=new GUIContent("Game — first person / menu");game.Show();game.position=new Rect(sceneWidth+25,90,gameWidth,viewHeight);game.Focus();
    }
    [MenuItem("Tools/Laser Tag/Submission/Frame Local Avatar (Play Mode)")]
    public static void FrameAvatar()
    {
        var local=UnityEngine.Object.FindAnyObjectByType<LocalPlayerRig>();
        if(!local || !local.avatar || !SceneView.lastActiveSceneView){Debug.LogWarning("Enter the arena and open Scene view first.");return;}
        var view=SceneView.lastActiveSceneView;view.pivot=local.avatar.transform.position+Vector3.up*.9f;
        view.rotation=Quaternion.Euler(12,local.avatar.transform.eulerAngles.y+165,0);view.size=2.1f;view.orthographic=false;view.Repaint();
    }
    [MenuItem("Tools/Laser Tag/Submission/Build Quest APK")]
    public static void BuildQuest()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before building.");
        if(!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        string path=EditorUtility.SaveFilePanel("Build installable Quest APK","","LaserTag","apk");
        if(!string.IsNullOrEmpty(path))BuildQuestTo(path);
    }
    public static BuildReport BuildQuestTo(string path)
    {
        var xr=UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if(!xr || !xr.GetFeatures().Any(f=>f && f.enabled && f.GetType().Name=="MetaQuestFeature"))
            throw new InvalidOperationException("Enable Meta Quest support in Android OpenXR settings before building.");
        if(xr.GetFeatures().Any(f=>f && f.enabled && f.GetType().Assembly.GetName().Name=="Unity.XR.AndroidOpenXR"))
            throw new InvalidOperationException("This is the Quest build: disable Android XR-specific features to avoid requiring Android XR SPATIAL/hand-tracking capabilities.");
        if(PlayerSettings.Android.minSdkVersion>AndroidSdkVersions.AndroidApiLevel32)
            throw new InvalidOperationException("Minimum Android API must be 32 or lower for the intended Quest 2 baseline.");
        if(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)!=ScriptingImplementation.IL2CPP ||
            (PlayerSettings.Android.targetArchitectures&AndroidArchitecture.ARM64)==0)
            throw new InvalidOperationException("Quest requires Android IL2CPP and ARM64.");
        bool bundle=EditorUserBuildSettings.buildAppBundle;EditorUserBuildSettings.buildAppBundle=false;
        try
        {
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=Scenes,locationPathName=path,target=BuildTarget.Android,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Quest APK build failed. See Unity Console.");
            Debug.Log("Quest APK built; device installation and five-headset testing are still required.");
            return report;
        }
        finally{EditorUserBuildSettings.buildAppBundle=bundle;}
    }
    // Batch mode: -executeMethod SubmissionBuild.BuildQuestBatch -quest-output <apk>
    public static void BuildQuestBatch()
    {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-quest-output");
        if(index<0 || index+1>=args.Length)throw new ArgumentException("Provide -quest-output with an APK path outside Assets.");
        BuildQuestTo(args[index+1]);
    }
}
