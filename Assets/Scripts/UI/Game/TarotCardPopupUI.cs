using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Screen-space popup showing which Tarot card the LOCAL player just drew —
// only the Tarot User who drew it ever sees this (TarotUserAbility only
// sends the triggering ClientRpc to its own owner). Singleton, same pattern
// as AbilityCooldownUI/GameOverUI/MeetingUI.
//
// Reveal animation: a classic 2D "card flip" — since a Screen Space Overlay
// Canvas has no real perspective, a true 3D spin can't be faked with camera
// depth. Instead the card's RectTransform scales horizontally (1 -> 0 -> 1),
// swapping from the back sprite to the drawn card's front sprite at the
// midpoint (when the card is edge-on and invisible), plus a small z-axis
// tilt wobble for a bit of flair — the same illusion the referenced AE rig
// produces, just done the 2D-UI way instead of with real 3D rotation.
public class TarotCardPopupUI : MonoBehaviour
{
    public static TarotCardPopupUI Instance { get; private set; }

    [Header("References")]
    [Tooltip("Root panel object: gets faded in/out via the CanvasGroup below.")]
    [SerializeField] private CanvasGroup popupGroup;
    [Tooltip("The single card Image: shows the back sprite, then flips to the drawn card's front sprite.")]
    [SerializeField] private Image cardImage;
    [SerializeField] private TextMeshProUGUI cardNameText;
    [SerializeField] private TextMeshProUGUI orientationText;
    [SerializeField] private TextMeshProUGUI effectText;

    [Header("Card Art")]
    [Tooltip("Shown before/during the first half of the flip, and briefly at the very start.")]
    [SerializeField] private Sprite backSprite;
    [Tooltip("Card name (must match what TarotUserAbility sends, e.g. \"The Fool\") mapped to its front-face sprite.")]
    [SerializeField] private CardArtEntry[] cardArt;

    [System.Serializable]
    private struct CardArtEntry
    {
        public string cardName;
        public Sprite sprite;
    }

    [Header("Colors")]
    [Tooltip("Orientation text color when the draw is Upright (a buff).")]
    [SerializeField] private Color uprightColor = new Color(0.85f, 0.7f, 0.2f); // gold
    [Tooltip("Orientation text color when the draw is Reversed (a debuff).")]
    [SerializeField] private Color reversedColor = new Color(0.75f, 0.15f, 0.15f); // blood red

    [Header("Flip Animation")]
    [Tooltip("Total time for the back-to-front flip (shrink + swap + grow).")]
    [SerializeField] private float flipDuration = 0.5f;
    [Tooltip("Small z-axis tilt (degrees) the card wobbles through during the flip, for a bit of life: 0 disables it.")]
    [SerializeField] private float flipTiltDegrees = 6f;

    [Header("Timing")]
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private float holdDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.2f;

    private Coroutine activeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (popupGroup != null)
        {
            popupGroup.alpha = 0f;
            popupGroup.gameObject.SetActive(false);
        }
    }

    public void ShowCard(string cardName, string orientation, string effectDescription, bool upright)
    {
        if (popupGroup == null || cardImage == null) return;
        Sfx.Play2D("tarot");

        if (cardNameText != null) cardNameText.text = cardName;
        if (orientationText != null)
        {
            orientationText.text = orientation;
            orientationText.color = upright ? uprightColor : reversedColor;
        }
        if (effectText != null) effectText.text = effectDescription;

        // Must activate BEFORE starting the coroutine — Unity refuses to
        // start a coroutine on a MonoBehaviour whose GameObject is inactive,
        // so doing this inside RevealRoutine() itself is too late.
        popupGroup.gameObject.SetActive(true);

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(RevealRoutine(FindCardArt(cardName), upright, effectDescription));
    }

    private Sprite FindCardArt(string cardName)
    {
        if (cardArt == null) return null;

        foreach (var entry in cardArt)
        {
            if (entry.cardName == cardName) return entry.sprite;
        }

        Debug.LogWarning($"[TarotCardPopupUI] No front-face art mapped for card \"{cardName}\": check the Card Art list in the Inspector.");
        return null;
    }

    private IEnumerator RevealRoutine(Sprite frontSprite, bool upright, string effectDescription)
    {
        RectTransform cardRect = cardImage.rectTransform;
        cardImage.sprite = backSprite;
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;

        // Fade the whole popup in first, showing the card back.
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            popupGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        popupGroup.alpha = 1f;

        // The flip itself: shrink scale.x to 0 (card turning edge-on), swap
        // to the front sprite at the midpoint, then grow back to full width.
        // A reversed draw lands the front sprite pre-rotated 180° so it
        // reveals already upside-down, same as a real reversed tarot card.
        float finalRotation = upright ? 0f : 180f;
        float half = flipDuration * 0.5f;

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / half); // 0 -> 1 across the shrink
            float eased = Mathf.Sin(progress * Mathf.PI * 0.5f); // ease-out, snappy near the pinch

            cardRect.localScale = new Vector3(1f - eased, 1f, 1f);
            if (flipTiltDegrees > 0f)
            {
                cardRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, flipTiltDegrees, progress));
            }
            yield return null;
        }

        // Edge-on — swap the face and lock in the final (possibly reversed) rotation.
        cardImage.sprite = frontSprite != null ? frontSprite : backSprite;
        cardRect.localScale = new Vector3(0f, 1f, 1f);

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / half); // 0 -> 1 across the grow
            float eased = 1f - Mathf.Cos(progress * Mathf.PI * 0.5f); // ease-in, accelerating back to full width

            cardRect.localScale = new Vector3(eased, 1f, 1f);
            if (flipTiltDegrees > 0f)
            {
                float tilt = Mathf.Lerp(flipTiltDegrees, 0f, progress);
                cardRect.localRotation = Quaternion.Euler(0f, 0f, finalRotation == 0f ? tilt : finalRotation - tilt);
            }
            yield return null;
        }

        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.Euler(0f, 0f, finalRotation);

        yield return new WaitForSeconds(holdDuration);

        // Hand off to the floating buff/debuff text above the player's head
        // right as the card itself starts fading away.
        NetworkObject localPlayerObj = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject
            : null;
        localPlayerObj?.GetComponent<TarotBuffFloatText>()?.ShowBuffText(effectDescription, upright);

        t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            popupGroup.alpha = 1f - Mathf.Clamp01(t / fadeOutDuration);
            yield return null;
        }
        popupGroup.alpha = 0f;

        popupGroup.gameObject.SetActive(false);
        activeRoutine = null;
    }
}