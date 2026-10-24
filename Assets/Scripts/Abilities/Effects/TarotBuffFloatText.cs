using System.Collections;
using TMPro;
using UnityEngine;

// Floating "+50% Speed" / "-50% Vision" style text that rises from the local
// player's head and fades out — the world-space counterpart to the screen
// popup's card reveal, showing the actual effect that just landed. Same
// permanent-child-toggle pattern as HealGlowDisplay/CurseMarker: a child
// TextMeshPro (3D, world space) sits on the Player prefab, disabled by
// default, and this just drives its position/alpha/color/text each time
// it's triggered.
//
// Client-local only — no networking involved. TarotCardPopupUI calls this
// directly on the local player's own object at the moment its card popup
// starts fading away, so only the Tarot User who drew the card ever sees
// this, same as the popup itself.
public class TarotBuffFloatText : MonoBehaviour
{
    [Tooltip("Child TextMeshPro (3D) object, positioned at/near the player's head. Disabled by default.")]
    [SerializeField] private TextMeshPro floatText;

    [Header("Colors")]
    [SerializeField] private Color buffColor = new Color(0.25f, 0.85f, 0.3f);   // green — good
    [SerializeField] private Color debuffColor = new Color(0.85f, 0.2f, 0.2f);  // red — bad

    [Header("Motion")]
    [Tooltip("Local Y offset (relative to this object's own position) the text starts at — tune this against your actual character height, measured from wherever this component sits (likely the player root's origin, often the feet).")]
    [SerializeField] private float startYOffset = -0.5f;
    [Tooltip("Local Y offset the text rises TO before holding/fading — its 'resting' height, just above the head.")]
    [SerializeField] private float endYOffset = -0.05f;
    [SerializeField] private float riseDuration = 0.35f;
    [Tooltip("How long the text stays fully visible at its resting height before it starts fading.")]
    [SerializeField] private float holdDuration = 1f;
    [Tooltip("Extra drift upward while fading, on top of endYOffset.")]
    [SerializeField] private float driftDuringFade = 0.25f;
    [SerializeField] private float fadeDuration = 0.45f;

    [Tooltip("Extra height added to every position above so the text clears the nametag stack (name, color, role).")]
    [SerializeField] private float extraYOffset = 0.9f;

    private Coroutine activeRoutine;
    private Vector3 baseLocalPosition;

    private void Awake()
    {
        if (floatText != null)
        {
            baseLocalPosition = floatText.transform.localPosition + Vector3.up * extraYOffset;
            floatText.gameObject.SetActive(false);
        }
    }

    public void ShowBuffText(string text, bool isGood)
    {
        if (floatText == null) return;

        floatText.text = text;
        floatText.color = isGood ? buffColor : debuffColor;

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(FloatRoutine());
    }

    private IEnumerator FloatRoutine()
    {
        floatText.gameObject.SetActive(true);

        Vector3 startPos = baseLocalPosition + Vector3.up * startYOffset;
        Vector3 restPos = baseLocalPosition + Vector3.up * endYOffset;
        Vector3 driftPos = restPos + Vector3.up * driftDuringFade;

        floatText.transform.localPosition = startPos;
        Color c = floatText.color;
        c.a = 1f;
        floatText.color = c;

        // Rise from the head up to its resting height, easing out.
        float t = 0f;
        while (t < riseDuration)
        {
            t += Time.deltaTime;
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / riseDuration), 3f); // ease-out cubic
            floatText.transform.localPosition = Vector3.Lerp(startPos, restPos, eased);
            yield return null;
        }
        floatText.transform.localPosition = restPos;

        // Hold fully visible at the resting height before fading — without
        // this it reads as way too fast, since rise+fade alone is under a second.
        yield return new WaitForSeconds(holdDuration);

        // Fade out while drifting a little further up, for a gentle finish.
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / fadeDuration);

            floatText.transform.localPosition = Vector3.Lerp(restPos, driftPos, progress);
            c.a = 1f - progress;
            floatText.color = c;
            yield return null;
        }

        floatText.gameObject.SetActive(false);
        activeRoutine = null;
    }
}