using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Central HUD hook for cooldown countdowns.
// Both Kill and Ability cooldowns use the same style: the button itself
// dims (Interactable = false) and a number overlay counts down on top of
// it — matching the reference HUD's "greyed icon with a countdown number"
// style. No separate standalone badge; the button IS the display.
// Every ability script (and PlayerController's kill cooldown) calls into this
// through a targeted ClientRpc to its own owner, so only the acting player's
// screen shows their own cooldowns. Singleton, same pattern as MeetingUI/GameOverUI.
public class AbilityCooldownUI : MonoBehaviour
{
    public static AbilityCooldownUI Instance { get; private set; }

    [Header("Kill Cooldown Display (overlay on the KILL button)")]
    [Tooltip("The on-screen KILL button itself — gets disabled while the kill is on cooldown.")]
    [SerializeField] private GameObject killCooldownRoot;
    [Tooltip("A TextMeshPro child sitting on top of the KILL button, showing the countdown number.")]
    [SerializeField] private TextMeshProUGUI killCooldownText;

    [Header("Ability Cooldown Display (overlay on the ABILITY button)")]
    [Tooltip("The on-screen ABILITY button — gets disabled while any ability is on cooldown.")]
    [SerializeField] private Button abilityButton;
    [Tooltip("A TextMeshPro child sitting on top of the ABILITY button, showing the countdown number.")]
    [SerializeField] private TextMeshProUGUI abilityCooldownText;

    private Coroutine killRoutine;
    private Coroutine abilityRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (killCooldownText != null) killCooldownText.gameObject.SetActive(false);
        if (abilityCooldownText != null) abilityCooldownText.gameObject.SetActive(false);
    }

    public void StartKillCooldown(float duration)
    {
        if (killRoutine != null) StopCoroutine(killRoutine);
        Button killButton = killCooldownRoot != null ? killCooldownRoot.GetComponent<Button>() : null;
        killRoutine = StartCoroutine(RunButtonCooldown(killButton, killCooldownText, duration));
    }

    public void StartAbilityCooldown(float duration)
    {
        if (abilityRoutine != null) StopCoroutine(abilityRoutine);
        abilityRoutine = StartCoroutine(RunButtonCooldown(abilityButton, abilityCooldownText, duration));
    }

    // Called on every client via GameStartManager's return-to-lobby RPC —
    // without this, a button that was mid-cooldown when the match ended
    // just stays dimmed/counting down from whatever coroutine was already
    // running, instead of being ready for the new match.
    public void ResetAll()
    {
        if (killRoutine != null) { StopCoroutine(killRoutine); killRoutine = null; }
        if (abilityRoutine != null) { StopCoroutine(abilityRoutine); abilityRoutine = null; }

        Button killButton = killCooldownRoot != null ? killCooldownRoot.GetComponent<Button>() : null;
        if (killButton != null) killButton.interactable = true;
        if (killCooldownText != null) killCooldownText.gameObject.SetActive(false);

        if (abilityButton != null) abilityButton.interactable = true;
        if (abilityCooldownText != null) abilityCooldownText.gameObject.SetActive(false);
    }

    private IEnumerator RunButtonCooldown(Button button, TextMeshProUGUI text, float duration)
    {
        if (button != null) button.interactable = false;
        if (text != null) text.gameObject.SetActive(true);

        float remaining = duration;
        while (remaining > 0f)
        {
            if (text != null) text.text = Mathf.CeilToInt(remaining).ToString();
            yield return null;

            // Freeze the countdown while a meeting is active — meetings
            // already block kills/abilities entirely (PlayerController.
            // IsBlocked()), so ticking this down during one would silently
            // eat cooldown time the player never got a chance to spend.
            // The server mirrors this on its own by extending the real
            // cooldown end time by the meeting's duration once it resolves
            // (see MeetingManager.ExtendAllCooldownsByMeetingDuration()),
            // so this stays in sync without needing an extra RPC.
            bool meetingActive = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
            if (!meetingActive)
            {
                remaining -= Time.deltaTime;
            }
        }

        if (button != null) button.interactable = true;
        if (text != null) text.gameObject.SetActive(false);
    }
}
