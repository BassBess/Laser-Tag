using UnityEngine;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Game-feel helpers: short generated sound effects (no audio files needed) and controller vibration.
/// </summary>
public static class LaserTagFeedback
{
    public enum Hand { Left, Right, Both }

    static AudioClip shot, empty, hitConfirm, hurt;
    // Eight short voices cover overlapping shots without allocating an object
    // or searching the scene on every trigger press. Scene unload owns cleanup.
    static readonly AudioSource[] shotVoices = new AudioSource[8];
    static int nextShotVoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        shot = empty = hitConfirm = hurt = null;
        System.Array.Clear(shotVoices,0,shotVoices.Length);
        nextShotVoice=0;
    }

    // ---------------- sounds ----------------

    /// <summary>Laser "pew" at a position (3D, so other players' shots come from their guns).</summary>
    public static void PlayShot(Vector3 at, float volume = .55f) => PlayShotVoice(at, 1f, volume);

    /// <summary>Accepted local shot, heard clearly independent of muzzle distance.</summary>
    public static void PlayLocalShot() => PlayShotVoice(Vector3.zero, 0f, .65f);

    static void PlayShotVoice(Vector3 at, float spatial, float volume)
    {
        int slot=nextShotVoice;
        for(int i=0;i<shotVoices.Length;i++)
        {
            int candidate=(nextShotVoice+i)%shotVoices.Length;
            if(!shotVoices[candidate]||!shotVoices[candidate].isPlaying){slot=candidate;break;}
        }
        nextShotVoice=(slot+1)%shotVoices.Length;
        var source=shotVoices[slot];
        if(!source)
        {
            var go=new GameObject("Laser Shot Voice");
            source=go.AddComponent<AudioSource>();
            source.playOnAwake=false;
            source.volume=1f;
            source.minDistance=1.5f;
            source.maxDistance=30f;
            source.rolloffMode=AudioRolloffMode.Linear;
            source.dopplerLevel=0;
            shotVoices[slot]=source;
            LaserTag.Menu.MenuAudio.Instance?.TrackEffectsSource(source);
        }
        source.Stop();
        source.transform.position=at;
        source.spatialBlend=spatial;
        source.pitch=Random.Range(.94f,1.06f);
        source.PlayOneShot(Shot(),volume);
    }

    /// <summary>Dry click when pulling the trigger with no shots left.</summary>
    public static void PlayEmpty(Vector3 at) => Play(Empty(), at, 0.6f, 1f);

    /// <summary>Bright ping heard by the shooter when the laser hits a player.</summary>
    public static void PlayHitConfirm() => Play2D(HitConfirm(), 0.5f);

    /// <summary>Low buzz heard by the player who got hit.</summary>
    public static void PlayHurt() => Play2D(Hurt(), 0.7f);

    static void Play(AudioClip clip, Vector3 at, float volume, float spatial)
    {
        var go = new GameObject("SFX " + clip.name);
        go.transform.position = at;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = spatial;
        source.minDistance = 1.5f;
        source.maxDistance = 30f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.pitch = Random.Range(0.94f, 1.06f); // small variation so rapid fire doesn't sound robotic
        LaserTag.Menu.MenuAudio.Instance?.TrackEffectsSource(source);
        source.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }

    static void Play2D(AudioClip clip, float volume)
    {
        Camera cam = LocalPlayerRig.Instance != null ? LocalPlayerRig.Instance.playerCamera : Camera.main;
        Play(clip, cam != null ? cam.transform.position : Vector3.zero, volume, 0f);
    }

    static AudioClip Shot() => shot != null ? shot : shot = Make("Laser Shot", 0.16f, (t, d) =>
    {
        float k = t / d;
        float freq = Mathf.Lerp(2200f, 380f, Mathf.Pow(k, 0.6f));
        float env = Mathf.Exp(-k * 5f) * Mathf.Clamp01(t / 0.004f);
        return env * (0.7f * Square(freq * t) * 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * freq * 0.5f * t));
    });

    static AudioClip Empty() => empty != null ? empty : empty = Make("Empty Click", 0.06f, (t, d) =>
    {
        float env = Mathf.Exp(-t * 90f);
        return env * (Random.value * 2f - 1f) * 0.6f + env * Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.4f;
    });

    static AudioClip HitConfirm() => hitConfirm != null ? hitConfirm : hitConfirm = Make("Hit Confirm", 0.14f, (t, d) =>
    {
        float env = Mathf.Exp(-t * 28f);
        return env * (0.6f * Mathf.Sin(2f * Mathf.PI * 1320f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 1980f * t));
    });

    static AudioClip Hurt() => hurt != null ? hurt : hurt = Make("Hurt", 0.28f, (t, d) =>
    {
        float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t / 0.01f);
        return env * (0.6f * Square(110f * t) + 0.4f * (Random.value * 2f - 1f)) * 0.7f;
    });

    static float Square(float phase) => (phase - Mathf.Floor(phase)) < 0.5f ? 1f : -1f;

    static AudioClip Make(string name, float seconds, System.Func<float, float, float> sample)
    {
        const int rate = 44100;
        int count = Mathf.CeilToInt(seconds * rate);
        var data = new float[count];
        for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample((float)i / rate, seconds), -1f, 1f) * 0.8f;
        AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------- vibration ----------------

    /// <summary>Vibrates the controller(s). Does nothing without XR controllers (e.g. in the simulator).</summary>
    public static void Haptic(Hand hand, float amplitude, float seconds)
    {
        if (hand != Hand.Right) Impulse(XRController.leftHand, amplitude, seconds);
        if (hand != Hand.Left) Impulse(XRController.rightHand, amplitude, seconds);
    }

    static void Impulse(XRController controller, float amplitude, float seconds)
    {
        if (controller is XRControllerWithRumble rumble) rumble.SendImpulse(Mathf.Clamp01(amplitude), seconds);
    }
}
