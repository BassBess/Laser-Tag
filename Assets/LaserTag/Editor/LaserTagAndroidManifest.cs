#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

/// <summary>
/// Adds the Android permissions LAN discovery needs (receiving Wi-Fi broadcast packets) to the generated
/// Gradle project, without replacing the project's main AndroidManifest.
/// </summary>
public class LaserTagAndroidManifest : IPostGenerateGradleAndroidProject
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    static readonly string[] Permissions =
    {
        "android.permission.INTERNET",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.ACCESS_WIFI_STATE",
        "android.permission.CHANGE_WIFI_MULTICAST_STATE",
    };

    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath)) return;

        var doc = new XmlDocument();
        doc.Load(manifestPath);
        XmlElement manifest = doc.DocumentElement;
        if (manifest == null) return;

        bool changed = false;
        foreach (string permission in Permissions)
        {
            bool present = false;
            foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
                if (node is XmlElement e && e.GetAttribute("name", AndroidNs) == permission) present = true;
            if (present) continue;

            XmlElement element = doc.CreateElement("uses-permission");
            element.SetAttribute("name", AndroidNs, permission);
            manifest.PrependChild(element);
            changed = true;
        }
        if (changed) doc.Save(manifestPath);
    }
}
#endif
