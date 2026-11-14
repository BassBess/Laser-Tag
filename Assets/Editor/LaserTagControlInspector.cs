using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LaserTagControlInspector
{
    static bool IsRelevant(string name)
    {
        string n = name.ToLowerInvariant();

        return n.Contains("moveprovider")
            || n.Contains("teleport")
            || n.Contains("locomotion")
            || n.Contains("turnprovider")
            || n.Contains("simulator")
            || n.Contains("laserray")
            || n.Contains("inputaction")
            || n.Contains("xrorigin");
    }

    static string ObjectPath(Transform t)
    {
        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + " / " + path;
        }

        return path;
    }

    [MenuItem("Tools/Laser Tag/Inspect Movement Components")]
    public static void Inspect()
    {
        Scene scene = SceneManager.GetActiveScene();

        var report = new StringBuilder();

        report.AppendLine("SCENE: " + scene.path);
        report.AppendLine();

        int matches = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            report.AppendLine("ROOT: " + root.name);

            var components =
                root.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour component in components)
            {
                if (component == null)
                    continue;

                string type = component.GetType().Name;

                if (!IsRelevant(type))
                    continue;

                matches++;

                report.AppendLine(
                    "  " + ObjectPath(component.transform)
                );

                report.AppendLine(
                    "    Component: " + type
                );

                report.AppendLine(
                    "    Enabled: " + component.enabled
                );
            }
        }

        report.AppendLine();
        report.AppendLine("Matching components: " + matches);

        string output = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            ),
            "Downloads",
            "LaserTag-Live-Controls.txt"
        );

        File.WriteAllText(output, report.ToString());

        Debug.Log(
            "CONTROL INSPECTION COMPLETE\n" +
            report +
            "\nSaved to: " + output
        );
    }

    [MenuItem("Tools/Laser Tag/Disable Continuous Movement")]
    public static void DisableMovement()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!EditorUtility.DisplayDialog(
            "Disable Continuous Movement?",
            "Disable MoveProvider components in " +
            scene.name +
            "?\nTeleportation and turning will remain enabled.",
            "Disable Movement",
            "Cancel"))
            return;

        int count = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var components =
                root.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour component in components)
            {
                if (component == null)
                    continue;

                string type = component.GetType().Name;

                if (!type.Contains("MoveProvider"))
                    continue;

                if (!component.enabled)
                    continue;

                Undo.RecordObject(
                    component,
                    "Disable Continuous Movement"
                );

                component.enabled = false;

                PrefabUtility
                    .RecordPrefabInstancePropertyModifications(
                        component
                    );

                Debug.Log(
                    "Disabled movement: " +
                    ObjectPath(component.transform) +
                    " [" + type + "]"
                );

                count++;
            }
        }

        if (count > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            "Movement providers disabled: " + count +
            ". Teleportation and turning were not changed."
        );
    }
}
