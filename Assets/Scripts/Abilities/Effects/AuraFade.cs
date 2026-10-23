using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Makes a locally-spawned Aura Sense marker fade out as the VIEWING player
// gets close to it — on top of its normal timed lifetime (ReikiRevealUI
// still destroys it after the reveal duration regardless). Keeps "a fuzzy
// sense of where" from turning into a precise locator just by walking up
// and staring at the glow.
//
// Purely local/cosmetic — this script never touches the network. It just
// reads the local player's own transform to judge distance.
public class AuraFade : MonoBehaviour
{
    [Tooltip("Distance at which the aura starts fading out.")]
    [SerializeField] private float fadeStartDistance = 2.5f;

    [Tooltip("Distance at which the aura is fully invisible.")]
    [SerializeField] private float fadeEndDistance = 0.5f;

    private SpriteRenderer spriteRenderer;
    private Light2D auraLight;
    private Color baseColor;
    private float baseLightIntensity;
    private Transform localPlayerTransform;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        auraLight = GetComponentInChildren<Light2D>();

        if (spriteRenderer != null) baseColor = spriteRenderer.color;
        if (auraLight != null) baseLightIntensity = auraLight.intensity;
    }

    private void Update()
    {
        if (localPlayerTransform == null)
        {
            NetworkObject localPlayer = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClient?.PlayerObject
                : null;

            if (localPlayer == null) return;
            localPlayerTransform = localPlayer.transform;
        }

        float distance = Vector2.Distance(transform.position, localPlayerTransform.position);

        // 0 = fully faded (close), 1 = fully visible (far)
        float t = Mathf.Clamp01(Mathf.InverseLerp(fadeEndDistance, fadeStartDistance, distance));

        if (spriteRenderer != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * t;
            spriteRenderer.color = c;
        }

        if (auraLight != null)
        {
            auraLight.intensity = baseLightIntensity * t;
        }
    }
}