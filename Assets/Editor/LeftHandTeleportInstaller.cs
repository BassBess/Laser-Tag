using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LeftHandTeleportInstaller
{
    const string GameScene = "Assets/Scenes/RelayVRGame.unity";

    [MenuItem("Tools/Laser Tag/Controls/Install Left-Hand Teleport")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError(
                "Stop Play Mode before installing teleportation."
            );
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(
            GameScene,
            OpenSceneMode.Single
        );

        LocalPlayerRig local =
            Object.FindFirstObjectByType<LocalPlayerRig>();

        if (!local)
        {
            Debug.LogError(
                "[LeftTeleport] Relay Local Gameplay was not found."
            );
            return;
        }

        if (!local.xrOrigin ||
            !local.playerCamera ||
            !local.leftController)
        {
            Debug.LogError(
                "[LeftTeleport] Missing active XR rig references."
            );
            return;
        }

        if (!local.xrOrigin.gameObject.activeInHierarchy)
        {
            Debug.LogError(
                "[LeftTeleport] The assigned XR Origin is inactive."
            );
            return;
        }

        LeftHandArcTeleport teleport =
            local.GetComponent<LeftHandArcTeleport>();

        if (!teleport)
        {
            teleport =
                Undo.AddComponent<LeftHandArcTeleport>(
                    local.gameObject
                );
        }

        Undo.RecordObject(
            teleport,
            "Configure Left-Hand Teleport"
        );

        teleport.xrOrigin = local.xrOrigin;
        teleport.playerCamera = local.playerCamera;
        teleport.leftController = local.leftController;

        EditorUtility.SetDirty(teleport);
        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError(
                "[LeftTeleport] Could not save gameplay scene."
            );
            return;
        }

        Debug.Log(
            "[LeftTeleport] INSTALLED SUCCESSFULLY\n" +
            "Rig: " + local.xrOrigin.name + "\n" +
            "Controller: " + local.leftController.name + "\n" +
            "Camera: " + local.playerCamera.name + "\n" +
            "Use left thumbstick UP and release to teleport.\n" +
            "For Editor testing, hold/release F8."
        );
    }
}
