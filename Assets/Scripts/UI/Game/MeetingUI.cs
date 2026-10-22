using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MeetingUI : MonoBehaviour
{
    public static MeetingUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject votingPanel;
    [SerializeField] private Transform playerListContainer; // parent with a Vertical Layout Group
    [SerializeField] private GameObject voteButtonPrefab;   // a simple Button + TMP text prefab
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private float resultDisplayDuration = 4f;

    [Header("Timer")]
    [Tooltip("Optional countdown label shown while voting is open.")]
    [SerializeField] private TextMeshProUGUI timerText;
    private float meetingOpenedAt;

    [Header("HUD to hide while a meeting is open")]
    [Tooltip("Task panel, USE/REPORT/ABILITY buttons, Call Meeting button — anything from the normal HUD that would otherwise render on top of (or behind, but still visible/clickable alongside) the voting panel. Assign every HUD element that shouldn't be visible/usable during a meeting.")]
    [SerializeField] private GameObject[] hudToHideDuringMeeting;

    [Header("Babaylan — Judge Skill")]
    [Tooltip("Toggle button shown ONLY to a Babaylan who hasn't used their one judgment yet. While active, clicking a player condemns them instantly instead of casting a vote.")]
    [SerializeField] private GameObject judgeModeButton;
    [Tooltip("Optional label on the toggle button — swapped between normal/active text so it's obvious Judge Mode is on.")]
    [SerializeField] private TextMeshProUGUI judgeModeButtonText;

    [Header("Confirmation")]
    [Tooltip("Small panel shown after picking a player, before the vote/judgment actually goes through. Needs a Confirm button (wired to ConfirmPendingAction()) and a Cancel button (wired to CancelPendingAction()) in its own hierarchy.")]
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private TextMeshProUGUI confirmationText;

    private List<GameObject> spawnedButtons = new List<GameObject>();
    private GameObject localPlayerButton; // this client's own entry in the list, if any
    private bool hasVotedThisMeeting = false;
    private bool judgeModeActive = false;
    private bool localPlayerIsAlive = true;

    // The player picked but not yet confirmed.
    private bool hasPendingTarget = false;
    private ulong pendingTargetClientId;
    private string pendingTargetName;
    private bool pendingIsJudge;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        InvokeRepeating(nameof(CheckMeetingState), 0.2f, 0.2f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(CheckMeetingState));
    }

    private bool wasActiveLastCheck = false;

    private void Update()
    {
        if (timerText == null || votingPanel == null || !votingPanel.activeSelf) return;
        float duration = GameStartManager.Instance != null ? GameStartManager.Instance.MeetingDuration : 30f;
        float left = Mathf.Max(0f, duration - (Time.time - meetingOpenedAt));
        timerText.text = $"VOTING ENDS IN {Mathf.CeilToInt(left)}s";
    }

    private void CheckMeetingState()
    {
        if (MeetingManager.Instance == null) return;

        bool isActive = MeetingManager.Instance.IsMeetingActive;

        if (isActive && !wasActiveLastCheck)
        {
            OpenVotingPanel();
        }
        else if (!isActive && wasActiveLastCheck)
        {
            CloseVotingPanel();
        }

        wasActiveLastCheck = isActive;
    }

    private void OpenVotingPanel()
    {
        Sfx.Play2D("meeting");
        // A dead player (ghost) can watch the meeting but shouldn't be able
        // to vote or judge — the server already rejects a dead player's vote
        // (CastVoteServerRpc/JudgeServerRpc both check IsAlive), but the UI
        // previously let them click through the whole flow anyway with no
        // feedback that it was silently being ignored.
        PlayerRole localRole = NetworkManager.Singleton.LocalClient?.PlayerObject != null
            ? NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerRole>()
            : null;
        localPlayerIsAlive = localRole == null || localRole.IsAlive;

        meetingOpenedAt = Time.time;
        hasVotedThisMeeting = !localPlayerIsAlive; // pre-lock the vote UI for ghosts
        judgeModeActive = false;
        hasPendingTarget = false;
        if (resultPanel != null) resultPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (votingPanel != null) votingPanel.SetActive(true);

        SetHudVisible(false);

        UpdateJudgeModeButtonVisibility();
        UpdateJudgeModeButtonText();
        PopulatePlayerList();

        if (!localPlayerIsAlive)
        {
            SetPlayerButtonsInteractable(false);
        }
    }

    // Only shown if the local player IS a Babaylan, is still alive, AND
    // hasn't used their one judgment yet this match. BabaylanAbility.LocalInstance
    // is null for everyone else (or if the owner's object hasn't spawned yet),
    // so this safely hides the button for non-Babaylan players.
    private void UpdateJudgeModeButtonVisibility()
    {
        if (judgeModeButton == null) return;

        bool canJudge = localPlayerIsAlive
            && BabaylanAbility.LocalInstance != null
            && BabaylanAbility.LocalInstance.IsMyAbility()
            && !BabaylanAbility.LocalInstance.LocalHasJudged;

        judgeModeButton.SetActive(canJudge);
    }

    // Wired to the Judge Mode button's OnClick.
    public void ToggleJudgeMode()
    {
        judgeModeActive = !judgeModeActive;
        UpdateJudgeModeButtonText();

        // Judging yourself is never valid (always a wrong guess, just
        // banishes you for nothing) — disable your own entry while Judge
        // Mode is on instead of silently ignoring the click, so it's obvious
        // why it doesn't respond rather than looking broken.
        if (localPlayerButton != null)
        {
            Button b = localPlayerButton.GetComponent<Button>();
            if (b != null) b.interactable = !judgeModeActive;
        }
    }

    private void UpdateJudgeModeButtonText()
    {
        if (judgeModeButtonText == null) return;
        judgeModeButtonText.text = judgeModeActive
            ? "JUDGE MODE: ON (pick a player to condemn)"
            : "Use Judge Skill";
    }

    private void CloseVotingPanel()
    {
        if (votingPanel != null) votingPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        hasPendingTarget = false;
        ClearPlayerList();

        // The result panel is still about to show (ShowResult fires right
        // around when this does), so the HUD only comes back once THAT
        // finishes too — see HideResult() below. If for some reason no
        // result panel is assigned, there's nothing left to wait for, so
        // restore the HUD immediately instead of leaving it hidden forever.
        if (resultPanel == null)
        {
            SetHudVisible(true);
        }
    }

    private void SetHudVisible(bool visible)
    {
        if (hudToHideDuringMeeting == null) return;
        foreach (var obj in hudToHideDuringMeeting)
        {
            if (obj != null) obj.SetActive(visible);
        }
    }

    private void PopulatePlayerList()
    {
        ClearPlayerList();
        localPlayerButton = null;

        // Only actual connected clients' own PlayerObjects — NOT
        // FindObjectsByType<PlayerRole>(), which would also pick up any
        // non-networked scene object that happens to carry a PlayerRole
        // component (a leftover test/debug dummy, for instance), letting it
        // show up as a "votable/judgeable player" when it isn't one.
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            PlayerRole player = client.PlayerObject.GetComponent<PlayerRole>();
            if (player == null || !player.IsAlive) continue; // dead players aren't votable, and can't vote (enforced server-side too)

            GameObject btnObj = Instantiate(voteButtonPrefab, playerListContainer);
            spawnedButtons.Add(btnObj);

            TextMeshProUGUI label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = PlayerLabel.DisplayName(player.OwnerClientId);

            ulong targetClientId = player.OwnerClientId;
            string targetName = PlayerLabel.DisplayName(player.OwnerClientId);

            bool isLocalPlayer = targetClientId == NetworkManager.Singleton.LocalClientId;
            if (isLocalPlayer)
            {
                localPlayerButton = btnObj;
                if (label != null) label.text += " (You)";
            }

            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnPlayerButtonClicked(targetClientId, targetName, isLocalPlayer));
            }
        }

        // Add a Skip button
        GameObject skipBtnObj = Instantiate(voteButtonPrefab, playerListContainer);
        spawnedButtons.Add(skipBtnObj);
        TextMeshProUGUI skipLabel = skipBtnObj.GetComponentInChildren<TextMeshProUGUI>();
        if (skipLabel != null) skipLabel.text = "SKIP";
        Button skipBtn = skipBtnObj.GetComponent<Button>();
        if (skipBtn != null)
        {
            skipBtn.onClick.AddListener(OnSkipClicked);
        }

        // Force the layout group to recalculate immediately instead of waiting
        // for Unity's next internal layout pass — without this, newly
        // instantiated children can visually stack at (0,0) for a frame or more.
        RectTransform containerRect = playerListContainer.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
        }
    }

    private void ClearPlayerList()
    {
        foreach (var btn in spawnedButtons)
        {
            if (btn != null) Destroy(btn);
        }
        spawnedButtons.Clear();
        localPlayerButton = null;
    }

    // Picking a player no longer votes/judges immediately — it just stages
    // the choice and shows a confirmation panel. The actual vote/judgment
    // only fires from ConfirmPendingAction().
    private void OnPlayerButtonClicked(ulong targetClientId, string targetName, bool isLocalPlayer)
    {
        Sfx.Play2D("click");
        if (hasVotedThisMeeting || hasPendingTarget) return;

        // Defense-in-depth: the local player's own button is already
        // disabled while Judge Mode is active (see ToggleJudgeMode), but
        // guard here too in case it's clicked through some other path.
        if (judgeModeActive && isLocalPlayer) return;

        hasPendingTarget = true;
        pendingTargetClientId = targetClientId;
        pendingTargetName = targetName;
        pendingIsJudge = judgeModeActive;

        ShowConfirmation();
    }

    // Wired to the Skip button directly — skipping is low-stakes/reversible
    // in spirit (it's the "do nothing" option), so it still confirms through
    // the same panel for consistency, just with a fixed label.
    private void OnSkipClicked()
    {
        Sfx.Play2D("click");
        if (hasVotedThisMeeting || hasPendingTarget) return;

        hasPendingTarget = true;
        pendingTargetClientId = MeetingManager.SkipVoteValue;
        pendingTargetName = "SKIP";
        pendingIsJudge = false;

        ShowConfirmation();
    }

    private void ShowConfirmation()
    {
        SetPlayerButtonsInteractable(false);

        if (confirmationPanel != null) confirmationPanel.SetActive(true);
        if (confirmationText != null)
        {
            confirmationText.text = pendingIsJudge
                ? $"Use Judge Skill on {pendingTargetName}?\nIf wrong, YOU will be banished instead."
                : pendingTargetClientId == MeetingManager.SkipVoteValue
                    ? "Skip voting this meeting?"
                    : $"Vote to eject {pendingTargetName}?";
        }
    }

    // Wired to the confirmation panel's Confirm button.
    public void ConfirmPendingAction()
    {
        Sfx.Play2D("vote");
        if (!hasPendingTarget) return;

        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        if (pendingIsJudge)
        {
            BabaylanAbility.LocalInstance?.TryJudge(pendingTargetClientId);

            // Single-use and ends the meeting immediately either way.
            hasVotedThisMeeting = true;
            if (judgeModeButton != null) judgeModeButton.SetActive(false);
            SetPlayerButtonsInteractable(false);
        }
        else
        {
            hasVotedThisMeeting = true;
            MeetingManager.Instance.CastVoteServerRpc(pendingTargetClientId);
            SetPlayerButtonsInteractable(false);
        }

        hasPendingTarget = false;
    }

    // Wired to the confirmation panel's Cancel button.
    public void CancelPendingAction()
    {
        hasPendingTarget = false;
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        SetPlayerButtonsInteractable(true);
    }

    private void SetPlayerButtonsInteractable(bool interactable)
    {
        foreach (var btn in spawnedButtons)
        {
            Button b = btn.GetComponent<Button>();
            if (b != null) b.interactable = interactable;
        }
    }

    public void ShowResult(string message)
    {
        Sfx.Play2D("eject");
        if (votingPanel != null) votingPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText != null) resultText.text = message;

        Invoke(nameof(HideResult), resultDisplayDuration);
    }

    private void HideResult()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
        SetHudVisible(true);
    }
}