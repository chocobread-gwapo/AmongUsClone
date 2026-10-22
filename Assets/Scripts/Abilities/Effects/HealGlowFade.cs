using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Fades the HealGlow marker in on show and out right before it's hidden,
// instead of popping in/out instantly. Same shape as ResidueFade (Third Eye
// Sight's footprint markers) — HealGlowDisplay calls Initialize() with the
// same duration it uses before SetActive(false)ing the marker again, so the
// fade-out always finishes right as it disappears.
public class HealGlowFade : MonoBehaviour
{
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private float fadeOutDuration = 0.4f;

    private SpriteRenderer[] spriteRenderers;
    private Light2D[] lights;
    private Color[] baseColors;
    private float[] baseIntensities;

    private void Awake()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        lights = GetComponentsInChildren<Light2D>();

        baseColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            baseColors[i] = spriteRenderers[i].color;
        }

        baseIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            baseIntensities[i] = lights[i].intensity;
        }
    }

    // Call right after SetActive(true), with the same total duration the
    // marker will stay visible for before being hidden again.
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

        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            SetAlpha(Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        SetAlpha(1f);

        float holdTime = totalDuration - fadeIn - fadeOut;
        if (holdTime > 0f)
        {
            yield return new WaitForSeconds(holdTime);
        }

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

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].intensity = baseIntensities[i] * alpha;
        }
    }
}