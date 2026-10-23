using System.Collections;
using UnityEngine;

// Fades a Third Eye Sight footprint/trap marker in on spawn and out right
// before it's destroyed, instead of popping in/out instantly. Purely local
// cosmetic polish — ThirdEyeRevealUI calls Initialize() right after
// Instantiate(), passing the same duration it uses for Destroy(marker, duration),
// so the fade-out timing always lines up with when the marker actually disappears.
public class ResidueFade : MonoBehaviour
{
    [Tooltip("How long the fade-in takes after spawning.")]
    [SerializeField] private float fadeInDuration = 0.25f;

    [Tooltip("How long the fade-out takes, timed to finish exactly when the marker is destroyed.")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    private SpriteRenderer[] spriteRenderers;
    private Color[] baseColors;

    private void Awake()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        baseColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            baseColors[i] = spriteRenderers[i].color;
        }
    }

    // Call this right after Instantiate(), with the same total lifetime
    // the caller is about to Destroy() this object after.
    public void Initialize(float totalDuration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeRoutine(totalDuration));
    }

    private IEnumerator FadeRoutine(float totalDuration)
    {
        float fadeIn = Mathf.Min(fadeInDuration, totalDuration * 0.5f);
        float fadeOut = Mathf.Min(fadeOutDuration, totalDuration * 0.5f);

        SetAlpha(0f);

        // Fade in
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            SetAlpha(Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        SetAlpha(1f);

        // Hold fully visible until it's time to start fading out.
        float holdTime = totalDuration - fadeIn - fadeOut;
        if (holdTime > 0f)
        {
            yield return new WaitForSeconds(holdTime);
        }

        // Fade out
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            SetAlpha(1f - Mathf.Clamp01(t / fadeOut));
            yield return null;
        }
        SetAlpha(0f);
    }

    private void SetAlpha(float alpha)
    {
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null) continue;
            Color c = baseColors[i];
            c.a = baseColors[i].a * alpha;
            spriteRenderers[i].color = c;
        }
    }
}