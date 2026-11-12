using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace LaserTag.Menu
{
    [Serializable]
    public sealed class LocalPreferences
    {
        public float Master = .8f, Effects = .8f, Music = .3f, Sensitivity = .2f, SnapAngle = 45;
        public bool Mute, Fullscreen;
        public int Quality, Width = 1280, Height = 720;
    }
    public sealed class SettingsController : MonoBehaviour
    {
        const string Key = "LaserTag.Menu.Settings.v1";
        public bool SupportsSnapTurn;
        public LocalPreferences Current { get; private set; }
        public Vector2Int[] ResolutionSizes { get; private set; }
        public string[] ResolutionNames => ResolutionSizes.Select(s=>s.x+" x "+s.y).ToArray();
        // Editor Game view size is controlled by the Editor, not Screen.SetResolution.
        public static bool DesktopDisplaySupported => !Application.isEditor &&
            (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.OSXPlayer) && !UnityEngine.XR.XRSettings.isDeviceActive;
        int defaultQuality;
        void Awake()
        {
            defaultQuality=QualitySettings.GetQualityLevel();
            ResolutionSizes = Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height)).Distinct().ToArray();
            if (ResolutionSizes.Length == 0) ResolutionSizes = new[]{new Vector2Int(Mathf.Max(640,Screen.width),Mathf.Max(480,Screen.height))};
            var defaults = Defaults();
            try { Current = JsonUtility.FromJson<LocalPreferences>(PlayerPrefs.GetString(Key,JsonUtility.ToJson(defaults))) ?? defaults; }
            catch (ArgumentException) { Current = defaults; }
            Apply(Current, false); SceneManager.sceneLoaded += Loaded;
        }
        public LocalPreferences Defaults() => new LocalPreferences { Quality = defaultQuality, Width = Mathf.Max(640,Screen.width), Height = Mathf.Max(480,Screen.height), Fullscreen = Screen.fullScreen };
        public LocalPreferences Copy() => JsonUtility.FromJson<LocalPreferences>(JsonUtility.ToJson(Current));
        public int ResolutionIndex(int width,int height) {int i=Array.FindIndex(ResolutionSizes,r=>r.x==width&&r.y==height);return i<0?0:i;}
        public void Apply(LocalPreferences value, bool save = true)
        {
            value.Master=Mathf.Clamp01(value.Master);value.Effects=Mathf.Clamp01(value.Effects);value.Music=Mathf.Clamp01(value.Music);
            value.Sensitivity=Mathf.Clamp(value.Sensitivity,.05f,.5f);value.SnapAngle=Mathf.Clamp(value.SnapAngle,15,90);
            value.Quality=Mathf.Clamp(value.Quality,0,QualitySettings.names.Length-1);
            Current = JsonUtility.FromJson<LocalPreferences>(JsonUtility.ToJson(value));
            AudioListener.volume=Current.Mute?0:Current.Master;QualitySettings.SetQualityLevel(Current.Quality,true);
            if(DesktopDisplaySupported)Screen.SetResolution(Mathf.Max(640,Current.Width),Mathf.Max(480,Current.Height),Current.Fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
            ApplyInputPreferences();
            if(save){PlayerPrefs.SetString(Key,JsonUtility.ToJson(Current));PlayerPrefs.Save();}
        }
        void Loaded(Scene scene, LoadSceneMode mode) { ApplyInputPreferences(); }
        float nextScan;
        void Update(){if(Time.unscaledTime>nextScan){nextScan=Time.unscaledTime+1;ApplyInputPreferences();}}
        void ApplyInputPreferences()
        {
            foreach(var simulator in FindObjectsByType<XRInteractionSimulator>(FindObjectsSortMode.None))
            {simulator.rotateXSensitivity=Current.Sensitivity;simulator.rotateYSensitivity=Current.Sensitivity;}
            if(SupportsSnapTurn)foreach(var turn in FindObjectsByType<SnapTurnProvider>(FindObjectsSortMode.None))turn.turnAmount=Current.SnapAngle;
        }
        void OnDestroy(){SceneManager.sceneLoaded-=Loaded;}
    }
}

