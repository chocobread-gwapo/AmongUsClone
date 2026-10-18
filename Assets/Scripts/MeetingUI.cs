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

    private List<GameObject> spawnedButtons = new List<GameObject>();
    private bool hasVotedThisMeeting = false;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        // Poll isn't ideal, but MeetingManager is a NetworkBehaviour that may
        // not exist yet when this UI first loads — check periodically instead
        // of a hard reference dependency at scene load time.
        InvokeRepeating(nameof(CheckMeetingState), 0.2f, 0.2f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(CheckMeetingState));
    }

    private bool wasActiveLastCheck = false;

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
        hasVotedThisMeeting = false;
        if (resultPanel != null) resultPanel.SetActive(false);
        if (votingPanel != null) votingPanel.SetActive(true);

        PopulatePlayerList();
    }

    private void CloseVotingPanel()
    {
        if (votingPanel != null) votingPanel.SetActive(false);
        ClearPlayerList();
    }

    private void PopulatePlayerList()
    {
        ClearPlayerList();

        PlayerRole[] allPlayers = FindObjectsByType<PlayerRole>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (!player.IsAlive) continue; // dead players aren't votable, and can't vote (enforced server-side too)

            GameObject btnObj = Instantiate(voteButtonPrefab, playerListContainer);
            spawnedButtons.Add(btnObj);

            TextMeshProUGUI label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = player.gameObject.name;

            ulong targetClientId = player.OwnerClientId;
            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => CastVote(targetClientId));
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
            skipBtn.onClick.AddListener(() => CastVote(MeetingManager.SkipVoteValue));
        }
    }

    private void ClearPlayerList()
    {
        foreach (var btn in spawnedButtons)
        {
            if (btn != null) Destroy(btn);
        }
        spawnedButtons.Clear();
    }

    private void CastVote(ulong targetClientId)
    {
        if (hasVotedThisMeeting) return; // prevent double-voting locally (server also rejects duplicates implicitly via dictionary overwrite)

        hasVotedThisMeeting = true;
        MeetingManager.Instance.CastVoteServerRpc(targetClientId);

        // Disable all buttons after voting so it's clear you've committed
        foreach (var btn in spawnedButtons)
        {
            Button b = btn.GetComponent<Button>();
            if (b != null) b.interactable = false;
        }
    }

    public void ShowResult(string message)
    {
        if (votingPanel != null) votingPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText != null) resultText.text = message;

        Invoke(nameof(HideResult), resultDisplayDuration);
    }

    private void HideResult()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
    }
}