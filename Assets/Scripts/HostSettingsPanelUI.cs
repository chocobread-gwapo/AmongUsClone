using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lets the host tune match settings at runtime (in the lobby, before Start
// Game) instead of editing GameStartManager's Inspector values by hand.
// Every setting lives on GameStartManager as a NetworkVariable/NetworkList,
// so clients see the current settings too — their controls just aren't
// interactable, since only the host is allowed to change them.
//
// Each role (Maligno type or Survivor Class) gets a "# OF ROLE" stepper (its
// cap this match) and a "% CHANCE" stepper (its relative pick weight) — same
// layout as the reference role-chance screenshots. Build ONE row design in
// the Unity Editor (two StepperRows side by side) and duplicate it per role,
// rather than hand-building 20 unique widgets.
public class HostSettingsPanelUI : MonoBehaviour
{
    [Serializable]
    public class StepperRow
    {
        public Button minusButton;
        public Button plusButton;
        public TMP_Text valueText;
    }

    [Serializable]
    public class RoleRow
    {
        [Tooltip("The '# OF ROLE' stepper — this role's cap for the match.")]
        public StepperRow countStepper;
        [Tooltip("The '% CHANCE' stepper — this role's relative pick weight.")]
        public StepperRow chanceStepper;
    }

    [Serializable]
    public class MalignoRoleRow
    {
        public MalignoType type;
        public RoleRow row;
    }

    [Serializable]
    public class SurvivorRoleRow
    {
        public SurvivorClass survivorClass;
        public RoleRow row;
    }

    [Header("Impostor Count (overall, not per-type)")]
    [SerializeField] private StepperRow impostorCountStepper;

    [Header("Kill Cooldown")]
    [Tooltip("Preset values in seconds, shown in the dropdown in this order. PRD suggests 15 / 22.5 / 30 / 45.")]
    [SerializeField] private float[] killCooldownPresets = { 15f, 22.5f, 30f, 45f };
    [SerializeField] private TMP_Dropdown killCooldownDropdown;

    [Header("Meeting Duration")]
    [SerializeField] private float[] meetingDurationPresets = { 30f, 45f, 60f, 90f };
    [SerializeField] private TMP_Dropdown meetingDurationDropdown;

    [Header("Maligno Roles — populate all 5 (Duwende, Tikbalang, BalBal, Aswang, Kapre)")]
    [SerializeField] private List<MalignoRoleRow> malignoRoleRows = new List<MalignoRoleRow>();

    [Header("Survivor Class Roles — populate all 5 (ThirdEyeUser, ReikiPractitioner, Albularyo, Babaylan, TarotUser)")]
    [SerializeField] private List<SurvivorRoleRow> survivorRoleRows = new List<SurvivorRoleRow>();

    [Header("Stepper Step Sizes")]
    [SerializeField] private int countStep = 1;
    [SerializeField] private int chanceStep = 5;

    private bool suppressCallbacks; // guards against feedback loops while refreshing UI from network values

    // ---------- Open / Close (wire these to your Settings and X buttons) ----------

    // Deferred by one frame: toggling a button's own container inactive
    // from inside that button's own OnClick leaves Unity's input module
    // mid-event, which commonly eats or stalls the very next click (often
    // on whatever is now sitting where that button used to be — Start
    // Game, in our case). Letting the click fully finish first fixes it.
    // The coroutine runs on EventSystem.current rather than this object,
    // since this object is the one about to be deactivated.
    public void OpenPanel()
    {
        RunNextFrame(true);
    }

    public void ClosePanel()
    {
        RunNextFrame(false);
    }

    private void RunNextFrame(bool active)
    {
        if (EventSystem.current != null)
            EventSystem.current.StartCoroutine(SetActiveNextFrame(gameObject, active));
        else
            gameObject.SetActive(active); // no EventSystem in scene (e.g. a test) — just do it immediately
    }

    private static IEnumerator SetActiveNextFrame(GameObject target, bool active)
    {
        yield return null;
        if (target != null) target.SetActive(active);
    }

    private void Start()
    {
        if (GameStartManager.Instance == null)
        {
            Debug.LogError("[HostSettingsPanelUI] GameStartManager.Instance is null in Start() — make sure GameStartManager exists in this scene.");
            enabled = false;
            return;
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        SetInteractable(isHost);

        BuildDropdownOptions(killCooldownDropdown, killCooldownPresets, "s");
        BuildDropdownOptions(meetingDurationDropdown, meetingDurationPresets, "s");

        WireUpCallbacks(isHost);
        RefreshAllFromNetwork();

        InvokeRepeating(nameof(RefreshAllFromNetwork), 0.5f, 0.5f);
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(RefreshAllFromNetwork));
    }

    // ---------- Setup ----------

    private void SetInteractable(bool interactable)
    {
        SetStepperInteractable(impostorCountStepper, interactable);
        if (killCooldownDropdown != null) killCooldownDropdown.interactable = interactable;
        if (meetingDurationDropdown != null) meetingDurationDropdown.interactable = interactable;

        foreach (var entry in malignoRoleRows)
        {
            SetStepperInteractable(entry.row.countStepper, interactable);
            SetStepperInteractable(entry.row.chanceStepper, interactable);
        }

        foreach (var entry in survivorRoleRows)
        {
            SetStepperInteractable(entry.row.countStepper, interactable);
            SetStepperInteractable(entry.row.chanceStepper, interactable);
        }
    }

    private void SetStepperInteractable(StepperRow stepper, bool interactable)
    {
        if (stepper == null) return;
        if (stepper.minusButton != null) stepper.minusButton.interactable = interactable;
        if (stepper.plusButton != null) stepper.plusButton.interactable = interactable;
    }

    private void BuildDropdownOptions(TMP_Dropdown dropdown, float[] presets, string suffix)
    {
        if (dropdown == null) return;

        dropdown.ClearOptions();
        List<string> labels = new List<string>();
        foreach (float value in presets)
        {
            labels.Add(value % 1f == 0f ? $"{value:0}{suffix}" : $"{value}{suffix}");
        }
        dropdown.AddOptions(labels);
    }

    private void WireUpCallbacks(bool isHost)
    {
        if (!isHost) return; // clients never trigger changes, only observe

        if (impostorCountStepper.minusButton != null)
            impostorCountStepper.minusButton.onClick.AddListener(() =>
                GameStartManager.Instance.ServerSetImpostorCount(GameStartManager.Instance.ImpostorCount - countStep));
        if (impostorCountStepper.plusButton != null)
            impostorCountStepper.plusButton.onClick.AddListener(() =>
                GameStartManager.Instance.ServerSetImpostorCount(GameStartManager.Instance.ImpostorCount + countStep));

        if (killCooldownDropdown != null)
            killCooldownDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressCallbacks) return;
                if (index >= 0 && index < killCooldownPresets.Length)
                    GameStartManager.Instance.ServerSetKillCooldownDuration(killCooldownPresets[index]);
            });

        if (meetingDurationDropdown != null)
            meetingDurationDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressCallbacks) return;
                if (index >= 0 && index < meetingDurationPresets.Length)
                    GameStartManager.Instance.ServerSetMeetingDuration(meetingDurationPresets[index]);
            });

        foreach (var entry in malignoRoleRows)
        {
            MalignoType type = entry.type; // capture for closure
            var row = entry.row;

            if (row.countStepper.minusButton != null)
                row.countStepper.minusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetMalignoMaxCount(type, GameStartManager.Instance.GetMalignoMaxCount(type) - countStep));
            if (row.countStepper.plusButton != null)
                row.countStepper.plusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetMalignoMaxCount(type, GameStartManager.Instance.GetMalignoMaxCount(type) + countStep));

            if (row.chanceStepper.minusButton != null)
                row.chanceStepper.minusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetMalignoChance(type, GameStartManager.Instance.GetMalignoChance(type) - chanceStep));
            if (row.chanceStepper.plusButton != null)
                row.chanceStepper.plusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetMalignoChance(type, GameStartManager.Instance.GetMalignoChance(type) + chanceStep));
        }

        foreach (var entry in survivorRoleRows)
        {
            SurvivorClass sc = entry.survivorClass; // capture for closure
            var row = entry.row;

            if (row.countStepper.minusButton != null)
                row.countStepper.minusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetSurvivorMaxCount(sc, GameStartManager.Instance.GetSurvivorMaxCount(sc) - countStep));
            if (row.countStepper.plusButton != null)
                row.countStepper.plusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetSurvivorMaxCount(sc, GameStartManager.Instance.GetSurvivorMaxCount(sc) + countStep));

            if (row.chanceStepper.minusButton != null)
                row.chanceStepper.minusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetSurvivorChance(sc, GameStartManager.Instance.GetSurvivorChance(sc) - chanceStep));
            if (row.chanceStepper.plusButton != null)
                row.chanceStepper.plusButton.onClick.AddListener(() =>
                    GameStartManager.Instance.ServerSetSurvivorChance(sc, GameStartManager.Instance.GetSurvivorChance(sc) + chanceStep));
        }
    }

    // ---------- Keeping the UI in sync with the network values ----------

    private void RefreshAllFromNetwork()
    {
        if (GameStartManager.Instance == null) return;

        suppressCallbacks = true;

        if (impostorCountStepper.valueText != null)
            impostorCountStepper.valueText.text = GameStartManager.Instance.ImpostorCount.ToString();

        SetDropdownToValue(killCooldownDropdown, killCooldownPresets, GameStartManager.Instance.KillCooldownDuration);
        SetDropdownToValue(meetingDurationDropdown, meetingDurationPresets, GameStartManager.Instance.MeetingDuration);

        foreach (var entry in malignoRoleRows)
        {
            if (entry.row.countStepper.valueText != null)
                entry.row.countStepper.valueText.text = GameStartManager.Instance.GetMalignoMaxCount(entry.type).ToString();
            if (entry.row.chanceStepper.valueText != null)
                entry.row.chanceStepper.valueText.text = GameStartManager.Instance.GetMalignoChance(entry.type).ToString();
        }

        foreach (var entry in survivorRoleRows)
        {
            if (entry.row.countStepper.valueText != null)
                entry.row.countStepper.valueText.text = GameStartManager.Instance.GetSurvivorMaxCount(entry.survivorClass).ToString();
            if (entry.row.chanceStepper.valueText != null)
                entry.row.chanceStepper.valueText.text = GameStartManager.Instance.GetSurvivorChance(entry.survivorClass).ToString();
        }

        suppressCallbacks = false;
    }

    private void SetDropdownToValue(TMP_Dropdown dropdown, float[] presets, float currentValue)
    {
        if (dropdown == null) return;

        int closestIndex = 0;
        float closestDiff = float.MaxValue;
        for (int i = 0; i < presets.Length; i++)
        {
            float diff = Mathf.Abs(presets[i] - currentValue);
            if (diff < closestDiff)
            {
                closestDiff = diff;
                closestIndex = i;
            }
        }

        if (dropdown.value != closestIndex)
        {
            dropdown.SetValueWithoutNotify(closestIndex);
        }
    }
}