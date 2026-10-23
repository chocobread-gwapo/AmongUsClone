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
    [Tooltip("Task panel, USE/REPORT/ABILITY buttons, Call Meeting button: anything from the normal HUD that would otherwise render on top of (or behind, but still visible/clickable alongside) the voting panel. Assign every HUD element that shouldn't be visible/usable during a meeting.")]
    [SerializeField] private GameObject[] hudToHideDuringMeeting;

    [Header("Babaylan: Judge Skill")]
    [Tooltip("Toggle button shown ONLY to a Babaylan who hasn't used their one judgment yet. While active, clicking a player condemns them instantly instead of casting a vote.")]
    [SerializeField] private GameObject judgeModeButton;
    [Tooltip("Optional label on the toggle button: swapped between normal/active text so it's obvious Judge Mode is on.")]
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

    private bool discussionOver = true;

    private void Update()
    {
        if (votingPanel == null || !votingPanel.activeSelf) return;
        foreach (var id in new List<ulong>(voteBadges.Keys)) RefreshBadge(id);
        var g = GameStartManager.Instance;
        float discussion = g != null ? g.DiscussionTime : 0f;
        float voting = g != null ? g.MeetingDuration : 30f;
        float elapsed = Time.time - meetingOpenedAt;

        if (elapsed < discussion)
        {
            if (timerText != null) timerText.text = $"DISCUSSION ENDS IN {Mathf.CeilToInt(discussion - elapsed)}s";
            return;
        }

        if (!discussionOver)
        {
            discussionOver = true;
            if (localPlayerIsAlive && !hasVotedThisMeeting && !hasPendingTarget) SetPlayerButtonsInteractable(true);
        }
        if (timerText != null)
            timerText.text = $"VOTING ENDS IN {Mathf.CeilToInt(Mathf.Max(0f, discussion + voting - elapsed))}s";
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

        discussionOver = !(GameStartManager.Instance != null && GameStartManager.Instance.DiscussionTime > 0f);
        if (!localPlayerIsAlive || !discussionOver)
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
            ? "BABAYLAN MODE: ON (pick a player to condemn)"
            : "Use Babaylan Skill";
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
            if (player == null || !player.IsAlive) continue; // dead players are listed after the living ones (below), greyed out

            GameObject btnObj = Instantiate(voteButtonPrefab, playerListContainer);
            spawnedButtons.Add(btnObj);

            TextMeshProUGUI label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = PlayerLabel.DisplayName(player.OwnerClientId);

            ulong targetClientId = player.OwnerClientId;
            string targetName = PlayerLabel.DisplayName(player.OwnerClientId);
            AddPortrait(btnObj, player, false);
            AddVoteBadge(btnObj, targetClientId);

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

        // Dead players: shown greyed out with a red X, not votable (the server rejects votes for / from the dead anyway).
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            PlayerRole deadPlayer = client.PlayerObject.GetComponent<PlayerRole>();
            if (deadPlayer == null || deadPlayer.IsAlive) continue;

            GameObject deadObj = Instantiate(voteButtonPrefab, playerListContainer);
            spawnedButtons.Add(deadObj);
            deadButtons.Add(deadObj);
            MarkAsDead(deadObj, PlayerLabel.DisplayName(deadPlayer.OwnerClientId)
                + (deadPlayer.OwnerClientId == NetworkManager.Singleton.LocalClientId ? " (You)" : ""));
            AddPortrait(deadObj, deadPlayer, true);
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

    private readonly List<GameObject> deadButtons = new List<GameObject>();

    // ---------- portraits + "voted" badges ----------

    private const float PortraitBox = 56f;

    // The player's character (head and shoulders), on the left of their entry, like Among Us.
    private void AddPortrait(GameObject entry, PlayerRole player, bool dead)
    {
        Sprite sprite = null;
        var anim = player.GetComponent<CharacterSpriteAnimator>();
        if (anim != null) sprite = anim.GetPortraitSprite();
        if (sprite == null) { var sr = player.GetComponent<SpriteRenderer>(); if (sr != null) sprite = sr.sprite; }

        var box = new GameObject("Portrait", typeof(RectTransform), typeof(RectMask2D));
        var brt = (RectTransform)box.transform;
        brt.SetParent(entry.transform, false);
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(10f, 0f);
        brt.sizeDelta = new Vector2(PortraitBox, PortraitBox);

        if (sprite != null)
        {
            var imgGo = new GameObject("Character", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)imgGo.transform;
            irt.SetParent(brt, false);
            float h = PortraitBox * 2.1f; // oversized and clipped from the bottom, so only head and shoulders show
            float w = h * sprite.rect.width / sprite.rect.height;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0f, -3f);
            irt.sizeDelta = new Vector2(w, h);
            var img = imgGo.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (dead) img.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            var lookLabel = player.GetComponent<PlayerLabel>();
            if (lookLabel != null) LookPortrait.Attach(img, lookLabel.NetworkLook, dead ? new Color(0.55f, 0.55f, 0.6f, 1f) : Color.white);
        }

        // Make room for the portrait: push the name to the right.
        TextMeshProUGUI label = entry.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) { var mg = label.margin; mg.x = PortraitBox + 24f; label.margin = mg; }

        // The red X (dead entries) must draw on top of the portrait.
        var x = entry.transform.Find("DeadX");
        if (x != null) x.SetAsLastSibling();
    }

    private readonly Dictionary<ulong, (Image pill, TextMeshProUGUI text)> voteBadges =
        new Dictionary<ulong, (Image, TextMeshProUGUI)>();

    // Small pill on the right of a living player's entry: "VOTED" (green) or "..." (grey).
    private void AddVoteBadge(GameObject entry, ulong clientId)
    {
        var pillGo = new GameObject("VoteBadge", typeof(RectTransform), typeof(Image));
        var prt = (RectTransform)pillGo.transform;
        prt.SetParent(entry.transform, false);
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 0.5f);
        prt.pivot = new Vector2(1f, 0.5f);
        prt.anchoredPosition = new Vector2(-14f, 0f);
        prt.sizeDelta = new Vector2(150f, 34f);
        var pill = pillGo.GetComponent<Image>();
        pill.raycastTarget = false;

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var trt = (RectTransform)txtGo.transform;
        trt.SetParent(prt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        var tmp = txtGo.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI src = entry.GetComponentInChildren<TextMeshProUGUI>();
        if (src != null) { tmp.font = src.font; tmp.fontSharedMaterial = src.fontSharedMaterial; }
        tmp.fontSize = 22; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;

        voteBadges[clientId] = (pill, tmp);
        RefreshBadge(clientId);
    }

    private void RefreshBadge(ulong clientId)
    {
        if (!voteBadges.TryGetValue(clientId, out var b) || b.pill == null) return;
        bool voted = MeetingManager.Instance != null && MeetingManager.Instance.HasVoted(clientId);
        b.pill.color = voted ? new Color(0.16f, 0.62f, 0.3f, 1f) : new Color(0.12f, 0.16f, 0.19f, 0.9f);
        b.text.text = voted ? "VOTED" : "WAITING";
        b.text.color = voted ? Color.white : new Color(0.65f, 0.7f, 0.74f, 1f);
    }

    // Greys an entry out, strikes its name through and stamps a red X over it.
    private void MarkAsDead(GameObject entry, string name)
    {
        Button b = entry.GetComponent<Button>();
        if (b != null) b.interactable = false;

        foreach (var img in entry.GetComponentsInChildren<Image>(true))
            img.color = new Color(img.color.r * 0.45f, img.color.g * 0.45f, img.color.b * 0.45f, img.color.a);

        TextMeshProUGUI label = entry.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = name;
            label.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            label.fontStyle |= FontStyles.Strikethrough;
        }

        // Red X: two thin bars rotated +-45 degrees, on the left side of the entry.
        var holder = new GameObject("DeadX", typeof(RectTransform));
        var hrt = (RectTransform)holder.transform;
        hrt.SetParent(entry.transform, false);
        hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 0.5f);
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = new Vector2(38f, 0f);
        hrt.sizeDelta = new Vector2(44f, 44f);
        for (int i = 0; i < 2; i++)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)bar.transform;
            brt.SetParent(hrt, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(58f, 8f);
            brt.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45f : -45f);
            var bi = bar.GetComponent<Image>();
            bi.color = new Color(0.9f, 0.12f, 0.12f, 1f);
            bi.raycastTarget = false;
        }
    }

    private void ClearPlayerList()
    {
        deadButtons.Clear();
        voteBadges.Clear();
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
        if (hasVotedThisMeeting || hasPendingTarget || !discussionOver) return;

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
        if (hasVotedThisMeeting || hasPendingTarget || !discussionOver) return;

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
                ? $"Use Babaylan Skill on {pendingTargetName}?\nIf wrong, YOU will be banished instead."
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
            if (btn == null || deadButtons.Contains(btn)) continue; // the dead stay greyed out
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