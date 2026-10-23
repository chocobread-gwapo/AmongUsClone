using UnityEngine;

// Tiny sound-effect player. No scene setup needed: it creates itself on game
// start and loads clips from Assets/Resources/Sfx/<name>.wav.
//   Sfx.Play2D("task");                    // UI / personal sounds
//   Sfx.PlayAt("kill", worldPosition);     // world sounds, fade out beyond your vision
public class Sfx : MonoBehaviour
{
    public static float MasterVolume = 0.8f;

    private static Sfx instance;
    private AudioSource[] sources;
    private int next;
    private readonly System.Collections.Generic.Dictionary<string, AudioClip> cache =
        new System.Collections.Generic.Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("Sfx");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Sfx>();
        instance.sources = new AudioSource[10];
        for (int i = 0; i < instance.sources.Length; i++)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            instance.sources[i] = s;
        }
    }

    private AudioClip Get(string name)
    {
        if (!cache.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>("Sfx/" + name);
            if (clip == null) Debug.LogWarning($"[Sfx] Missing clip Resources/Sfx/{name}");
            cache[name] = clip;
        }
        return clip;
    }

    private void PlayInternal(string name, float volume)
    {
        if (volume <= 0.01f) return;
        AudioClip clip = Get(name);
        if (clip == null) return;
        AudioSource s = sources[next];
        next = (next + 1) % sources.Length;
        s.clip = clip;
        s.volume = Mathf.Clamp01(volume * MasterVolume);
        s.Play();
    }

    public static void Play2D(string name, float volume = 1f)
    {
        if (instance == null) Bootstrap();
        instance.PlayInternal(name, volume);
    }

    // Volume falls off with distance from the local player, reaching zero a
    // little past their vision radius, so you can't hear kills across the map.
    public static void PlayAt(string name, Vector3 worldPos, float volume = 1f)
    {
        if (instance == null) Bootstrap();
        float v = volume;
        PlayerVision local = PlayerVision.LocalInstance;
        if (local != null)
        {
            float reach = Mathf.Max(local.CurrentVisionRadius * 1.6f, 4f);
            float d = Vector2.Distance(local.transform.position, worldPos);
            v *= Mathf.Clamp01(1f - d / reach);
        }
        instance.PlayInternal(name, v);
    }
}
