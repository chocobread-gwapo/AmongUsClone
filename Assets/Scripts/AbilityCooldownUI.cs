using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Central HUD hook for cooldown countdowns.
// Kill cooldown: shown on its own standalone badge (separate GameObject).
// Ability cooldown: shown directly ON the ABILITY button itself — the button
// dims (Interactable = false) and a number overlay counts down on top of it,
// matching the reference HUD's "greyed icon with a countdown number" style.
// Every ability script (and PlayerController's kill cooldown) calls into this
// through a targeted ClientRpc to its own owner, so only the acting player's
// screen shows their own cooldowns. Singleton, same pattern as MeetingUI/GameOverUI.
public class AbilityCooldownUI : MonoBehaviour
{
    public static AbilityCooldownUI Instance { get; private set; }

    [Header("Kill Cooldown Display (standalone badge)")]
    [Tooltip("Parent object to show/hide while the kill cooldown is active (a small badge near USE, for example).")]
    [SerializeField] private GameObject killCooldownRoot;
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

        if (killCooldownRoot != null) killCooldownRoot.SetActive(false);
        if (abilityCooldownText != null) abilityCooldownText.gameObject.SetActive(false);
    }

    public void StartKillCooldown(float duration)
    {
        if (killRoutine != null) StopCoroutine(killRoutine);
        killRoutine = StartCoroutine(RunBadgeCooldown(duration, killCooldownRoot, killCooldownText));
    }

    public void StartAbilityCooldown(float duration)
    {
        if (abilityRoutine != null) StopCoroutine(abilityRoutine);
        abilityRoutine = StartCoroutine(RunButtonCooldown(duration));
    }

    private IEnumerator RunBadgeCooldown(float duration, GameObject root, TextMeshProUGUI text)
    {
        if (root != null) root.SetActive(true);

        float remaining = duration;
        while (remaining > 0f)
        {
            if (text != null) text.text = Mathf.CeilToInt(remaining).ToString();
            yield return null;
            remaining -= Time.deltaTime;
        }

        if (root != null) root.SetActive(false);
    }

    private IEnumerator RunButtonCooldown(float duration)
    {
        if (abilityButton != null) abilityButton.interactable = false;
        if (abilityCooldownText != null) abilityCooldownText.gameObject.SetActive(true);

        float remaining = duration;
        while (remaining > 0f)
        {
            if (abilityCooldownText != null) abilityCooldownText.text = Mathf.CeilToInt(remaining).ToString();
            yield return null;
            remaining -= Time.deltaTime;
        }

        if (abilityButton != null) abilityButton.interactable = true;
        if (abilityCooldownText != null) abilityCooldownText.gameObject.SetActive(false);
    }
}