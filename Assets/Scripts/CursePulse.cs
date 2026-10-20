using UnityEngine;
using UnityEngine.Rendering.Universal;

// Makes the CurseMarker visual pulse continuously while it's active — a
// slow, steady heartbeat-like glow rather than a static red light, so a
// cursed player reads as "under threat" at a glance (and ties visually to
// the death timer ticking down in the background).
//
// Purely local/cosmetic. CurseStatus just SetActive(true/false)s this whole
// marker object — OnEnable/OnDisable here reset the pulse cleanly each time
// so it doesn't carry stale phase/alpha from a previous curse.
public class CursePulse : MonoBehaviour
{
    [Tooltip("How many pulses per second.")]
    [SerializeField] private float pulseSpeed = 1.5f;

    [Tooltip("Lowest intensity/alpha multiplier the pulse dips to (1 = no pulsing, 0 = fades fully out at the bottom of each cycle).")]
    [Range(0f, 1f)]
    [SerializeField] private float minMultiplier = 0.4f;

    private SpriteRenderer[] spriteRenderers;
    private Light2D[] lights;
    private Color[] baseColors;
    private float[] baseIntensities;
    private float phase;

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

    private void OnEnable()
    {
        phase = 0f;
    }

    private void Update()
    {
        phase += Time.deltaTime * pulseSpeed * Mathf.PI * 2f;

        // 0..1..0 sine wave, remapped into [minMultiplier, 1].
        float wave = (Mathf.Sin(phase) + 1f) * 0.5f;
        float multiplier = Mathf.Lerp(minMultiplier, 1f, wave);

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null) continue;
            Color c = baseColors[i];
            c.a = baseColors[i].a * multiplier;
            spriteRenderers[i].color = c;
        }

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].intensity = baseIntensities[i] * multiplier;
        }
    }
}