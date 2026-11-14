using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaserTag.Menu
{
    // Small generated ambience/click clips have no external audio dependencies.
    // Other sources are treated as effects; their authored volume is retained as the gain baseline.
    public sealed class MenuAudio : MonoBehaviour
    {
        public static MenuAudio Instance { get; private set; }
        [Tooltip("Existing licensed Unity sample ambience; keep below weapon and hit cues.")]
        public AudioClip gameplayAmbience;
        AudioSource music, click;
        AudioClip musicClip, clickClip, gameplayMusicClip;
        SettingsController settings;
        struct Gain { public float Authored, Applied; }
        readonly Dictionary<AudioSource,Gain> gains = new Dictionary<AudioSource,Gain>();
        readonly List<AudioSource> expired = new List<AudioSource>();
        readonly List<AudioSource> sources = new List<AudioSource>();
        float nextScan;
        void Awake(){if(GetComponent<MenuSessionController>()==MenuSessionController.Instance)Instance=this;}
        void Start()
        {
            if(GetComponent<MenuSessionController>()!=MenuSessionController.Instance)return;
            Instance=this;
            settings=GetComponent<SettingsController>();
            music=gameObject.AddComponent<AudioSource>();music.playOnAwake=false;music.loop=true;music.volume=0;musicClip=Tone(true);music.clip=musicClip;music.spatialBlend=0;music.Play();
            gameplayMusicClip=CreateGameplayMusic();
            click=gameObject.AddComponent<AudioSource>();click.playOnAwake=false;click.volume=.3f;clickClip=Tone(false);click.clip=clickClip;
            TrackEffectsSource(click);SceneManager.sceneLoaded+=Loaded;ScanEffects();
        }
        public void Click(){if(click)click.Play();}
        // Short gameplay sounds must respect SFX gain before their first sample,
        // rather than waiting for the periodic discovery pass.
        public void TrackEffectsSource(AudioSource source)
        {
            if(!source||!settings||settings.Current==null||gains.ContainsKey(source))return;
            var gain=new Gain{Authored=source.volume,Applied=source.volume*settings.Current.Effects};
            source.volume=gain.Applied;gains.Add(source,gain);
        }
        void Update()
        {
            var session=MenuSessionController.Instance;
            if(!session||!settings||settings.Current==null||!music)return;
            bool playing=session.InGame;
            AudioClip desired=playing?gameplayMusicClip:musicClip;
            float target=session.Leaving?0:(playing?.42f:.24f)*settings.Current.Music;
            if(music.clip!=desired)
            {
                music.volume=Mathf.MoveTowards(music.volume,0,Time.unscaledDeltaTime*.5f);
                if(music.volume<=.0001f){music.Stop();music.clip=desired;music.Play();}
            }
            else music.volume=Mathf.MoveTowards(music.volume,target,Time.unscaledDeltaTime*.5f);
            if(Time.unscaledTime<nextScan)return;nextScan=Time.unscaledTime+.1f;
            expired.Clear();foreach(var pair in gains)if(!pair.Key)expired.Add(pair.Key);
            foreach(var source in expired)gains.Remove(source);
            sources.Clear();sources.AddRange(gains.Keys);
            foreach(var source in sources)
            {
                if(source==music)continue;
                if(!gains.TryGetValue(source,out var gain))gain=new Gain{Authored=source.volume,Applied=source.volume};
                else if(Mathf.Abs(source.volume-gain.Applied)>.001f)gain.Authored=source.volume;
                gain.Applied=gain.Authored*settings.Current.Effects;source.volume=gain.Applied;gains[source]=gain;
            }
        }
        void Loaded(Scene scene,LoadSceneMode mode){ScanEffects();}
        void ScanEffects(){foreach(var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))if(source!=music)TrackEffectsSource(source);}
        // Original procedural retro loop. No samples or existing game melodies.
        static AudioClip CreateGameplayMusic()
        {
            const int rate=22050; const float beat=.5f; const int beats=32;
            var data=new float[(int)(rate*beat*beats)];
            int[] bass={45,41,48,43}; // A minor, F, C, G
            int[] steps={0,7,12,3,7,10,12,7};
            for(int i=0;i<data.Length;i++)
            {
                float t=(float)i/rate;
                int bar=(int)(t/(beat*8)), step=(int)(t/(beat*.5f));
                float local=t%(beat*.5f), env=Mathf.Min(1,local/.008f)*Mathf.Exp(-local*14);
                float hz=440*Mathf.Pow(2,(bass[bar]+24+steps[step%8]-69)/12f);
                float lead=(Mathf.Sin(2*Mathf.PI*hz*t)+.2f*Mathf.Sin(6*Mathf.PI*hz*t))*.10f*env;
                float b=440*Mathf.Pow(2,(bass[bar]-69)/12f), pulse=t%beat;
                float low=Mathf.Sin(2*Mathf.PI*b*t)*.12f*Mathf.Min(1,pulse/.012f)*Mathf.Exp(-pulse*5);
                float kick=.10f*Mathf.Sin(2*Mathf.PI*55*pulse)*Mathf.Exp(-pulse*30);
                // End fades inside the final sixteenth; beginning starts at zero.
                float seam=Mathf.Min(1,t/.02f)*Mathf.Min(1,(beat*beats-t)/.04f);
                data[i]=(lead+low+kick)*seam;
            }
            var clip=AudioClip.Create("Original laser arena retro loop",data.Length,1,rate,false);
            clip.SetData(data,0);return clip;
        }
        static AudioClip Tone(bool ambient)
        {
            int sampleRate=22050, length=ambient?sampleRate*8:sampleRate/12;var data=new float[length];
            for(int i=0;i<length;i++){float t=(float)i/sampleRate;if(ambient)data[i]=.035f*(Mathf.Sin(2*Mathf.PI*110*t)+.5f*Mathf.Sin(2*Mathf.PI*165*t)+.3f*Mathf.Sin(2*Mathf.PI*220*t));else data[i]=.14f*Mathf.Sin(2*Mathf.PI*(620-220*t)*t)*Mathf.Pow(1-(float)i/length,3);}
            var clip=AudioClip.Create(ambient?"Menu ambience":"Menu confirmation",length,1,sampleRate,false);clip.SetData(data,0);return clip;
        }
        void OnDestroy(){SceneManager.sceneLoaded-=Loaded;if(Instance==this)Instance=null;foreach(var pair in gains)if(pair.Key)pair.Key.volume=pair.Value.Authored;if(musicClip)Destroy(musicClip);if(clickClip)Destroy(clickClip);if(gameplayMusicClip)Destroy(gameplayMusicClip);}
    }
}
