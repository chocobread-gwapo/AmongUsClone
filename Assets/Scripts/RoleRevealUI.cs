using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Full-screen "you are X" reveal shown once at the start of a match —
// replaces the old plain Debug.Log reveal. Singleton, same pattern as
// every other UI manager (MeetingUI, GameOverUI, etc.).
//
// PlayerRole's two separate owner-only ClientRpcs (role, then survivor
// class) can both fire for the same player within the same match start —
// ShowRole() opens the panel, and if a survivor class reveal follows
// shortly after, AppendSurvivorClass() updates the SAME open panel instead
// of popping a second one on top of it.
public class RoleRevealUI : MonoBehaviour
{
    public static RoleRevealUI Instance { get; private set; }

    [Header("Panel")]
    [Tooltip("The whole reveal panel — shown/hidden as a group.")]
    [SerializeField] private GameObject root;
    [Tooltip("Optional full-panel background image whose color gets tinted per role (red for Maligno, a calmer color for Villagers).")]
    [SerializeField] private Image backgroundTint;
    [Tooltip("Shows the player's own character sprite — their idle down-facing frame, grabbed off CharacterSpriteAnimator at reveal time.")]
    [SerializeField] private Image portraitImage;
    [Tooltip("Wide horizontal glow (GlowStreak.png) sitting behind the portrait, tinted red for Maligno / blue for Villager. Use a plain WHITE source image here — color comes entirely from the tint applied in code, not from the asset itself.")]
    [SerializeField] private Image glowStreak;
    [Tooltip("Radial vignette (Vignette.png) overlaid above everything else — transparent in the middle, fades to black at the edges. Purely decorative framing, not tinted per role.")]
    [SerializeField] private Image vignette;

    [Header("Text")]
    [Tooltip("Big header: \"YOU ARE MALIGNO\" / \"YOU ARE A VILLAGER\".")]
    [SerializeField] private TextMeshProUGUI headerText;
    [Tooltip("The specific role/class name: \"Tikbalang\", \"Third Eye Sight\", etc. Blank for a plain Villager with no class.")]
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("One or two sentences describing the ability, or a generic line for a plain Villager.")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Timing")]
    [SerializeField] private float autoHideDelay = 5f;

    [Header("Colors")]
    [Tooltip("Fully opaque near-black tints by design — this panel is meant to fully cover the HUD behind it, not let it bleed through.")]
    [SerializeField] private Color malignoColor = new Color(0.12f, 0.02f, 0.02f, 1f);
    [SerializeField] private Color villagerColor = new Color(0.03f, 0.05f, 0.06f, 1f);
    [Tooltip("Vivid tint applied to glowStreak (which should be a plain white source image) — this is where the actual glow color comes from.")]
    [SerializeField] private Color malignoGlowColor = new Color(1f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color villagerGlowColor = new Color(0.1f, 0.55f, 0.85f, 1f);

    private Coroutine hideRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (root != null) root.SetActive(false);
    }

    // Called from PlayerRole's role reveal. isImpostor/malignoType come
    // straight from the enum values — all the display text/color lookup
    // happens in here so flavor text only lives in one place. portrait is
    // the player's own idle sprite (PlayerRole.ReceiveRoleClientRpc grabs
    // it off CharacterSpriteAnimator on the same GameObject, since this
    // RPC only ever runs on that player's own owning client).
    public void ShowRole(bool isImpostor, MalignoType malignoType, Sprite portrait)
    {
        if (root != null) root.SetActive(true);
        if (backgroundTint != null) backgroundTint.color = isImpostor ? malignoColor : villagerColor;
        if (glowStreak != null) glowStreak.color = isImpostor ? malignoGlowColor : villagerGlowColor;
        if (headerText != null) headerText.text = isImpostor ? "YOU ARE MALIGNO" : "YOU ARE A VILLAGER";

        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (isImpostor)
        {
            (string name, string desc) = MalignoInfo(malignoType);
            SetNameAndDescription(name, desc);
        }
        else
        {
            // A plain Villager with no Survivor Class never gets a follow-up
            // AppendSurvivorClass() call, so show the generic line right away;
            // if a class reveal DOES follow, it overwrites this a moment later.
            SetNameAndDescription("", "No special ability — finish tasks, find the Maligno.");
        }

        RestartAutoHide();
    }

    // Called from PlayerRole's survivor class reveal, shortly after
    // ShowRole() for the same match start. Updates the same panel rather
    // than opening a second one.
    public void AppendSurvivorClass(SurvivorClass survivorClass)
    {
        if (survivorClass == SurvivorClass.None) return; // nothing to add — ShowRole()'s generic line already covers this

        if (root == null || !root.activeSelf)
        {
            // Role reveal wasn't shown (UI not wired up in time, etc.) —
            // fall back to a standalone reveal rather than silently dropping it.
            if (root != null) root.SetActive(true);
            if (backgroundTint != null) backgroundTint.color = villagerColor;
            if (glowStreak != null) glowStreak.color = villagerGlowColor;
            if (headerText != null) headerText.text = "YOU ARE A VILLAGER";
        }

        (string name, string desc) = SurvivorClassInfo(survivorClass);
        SetNameAndDescription(name, desc);
        RestartAutoHide();
    }

    private void SetNameAndDescription(string name, string description)
    {
        if (nameText != null) nameText.text = name;
        if (descriptionText != null) descriptionText.text = description;
    }

    private void RestartAutoHide()
    {
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(AutoHideAfter(autoHideDelay));
    }

    private IEnumerator AutoHideAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (root != null) root.SetActive(false);
        hideRoutine = null;
    }

    // Wire this to a full-panel invisible button, or a "tap to continue"
    // prompt, so players who read fast aren't stuck waiting out the timer.
    public void Dismiss()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }
        if (root != null) root.SetActive(false);
    }

    // ---------- Flavor text ----------

    private static (string name, string description) MalignoInfo(MalignoType type)
    {
        switch (type)
        {
            case MalignoType.Duwende:
                return ("Duwende", "Plant cursed traps that slow and blind.");
            case MalignoType.Tikbalang:
                return ("Tikbalang", "Bewilder — invert a Villager's controls.");
            case MalignoType.BalBal:
                return ("Bal-Bal", "Eat a body — leaves no trace.");
            case MalignoType.Aswang:
                return ("Aswang", "Shapeshift into a nearby player.");
            case MalignoType.Kapre:
                return ("Kapre", "Smoke screen blinds nearby Villagers.");
            default:
                return ("Maligno", "Eliminate the Villagers before they find you.");
        }
    }

    private static (string name, string description) SurvivorClassInfo(SurvivorClass survivorClass)
    {
        switch (survivorClass)
        {
            case SurvivorClass.ThirdEyeUser:
                return ("Third Eye", "Reveal nearby Maligno footprints.");
            case SurvivorClass.ReikiPractitioner:
                return ("Aura", "Sense a rough Maligno location.");
            case SurvivorClass.Albularyo:
                return ("Pag-aalaga", "Cure a nearby cursed Villager.");
            case SurvivorClass.Babaylan:
                return ("Judge", "Condemn a player — guess wrong, you're exiled.");
            case SurvivorClass.TarotUser:
                return ("Sapalaran", "Draw a card for a random buff or curse.");
            default:
                return ("", "");
        }
    }
}