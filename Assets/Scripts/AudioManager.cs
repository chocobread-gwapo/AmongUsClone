using System.Collections;
using UnityEngine;

// Background music singleton — same DontDestroyOnLoad pattern as
// MultiplayerConnectionManager, since this needs to keep playing across the
// MainMenu -> Game scene transition instead of restarting or cutting out.
// Put this on a single persistent GameObject in your bootstrap/menu scene.
[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Tooltip("Looping background music clip (e.g. the music box track). Plays automatically on start unless Play On Awake is unchecked.")]
    [SerializeField] private AudioClip backgroundMusic;

    [Range(0f, 1f)]
    [SerializeField] private float defaultVolume = 0.5f;

    [SerializeField] private bool playOnAwake = true;

    [Tooltip("How long a fade-in/fade-out/crossfade takes, in seconds.")]
    [SerializeField] private float fadeDuration = 1.5f;

    private AudioSource audioSource;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = false; // we drive playback ourselves below
        audioSource.volume = 0f;
    }

    private void Start()
    {
        if (playOnAwake && backgroundMusic != null)
        {
            PlayMusic(backgroundMusic);
        }
    }

    // Starts (or crossfades into) a track. Safe to call with the clip
    // already playing — it just no-ops instead of restarting it.
    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource.clip == clip && audioSource.isPlaying) return;

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossfadeTo(clip));
    }

    public void StopMusic()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOutAndStop());
    }

    // Call from a volume slider (e.g. an Options menu) — takes effect
    // immediately, doesn't interrupt whatever fade is currently running.
    public void SetVolume(float volume01)
    {
        defaultVolume = Mathf.Clamp01(volume01);
        if (fadeRoutine == null)
        {
            audioSource.volume = defaultVolume;
        }
    }

    public float GetVolume() => defaultVolume;

    private IEnumerator CrossfadeTo(AudioClip clip)
    {
        // Fade the current track out first (if anything is playing).
        if (audioSource.isPlaying)
        {
            yield return Fade(audioSource.volume, 0f);
        }

        audioSource.clip = clip;
        audioSource.Play();

        yield return Fade(0f, defaultVolume);
        fadeRoutine = null;
    }

    private IEnumerator FadeOutAndStop()
    {
        yield return Fade(audioSource.volume, 0f);
        audioSource.Stop();
        fadeRoutine = null;
    }

    private IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime; // unscaled so it still fades during any future pause-menu timeScale=0
            audioSource.volume = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }
        audioSource.volume = to;
    }
}