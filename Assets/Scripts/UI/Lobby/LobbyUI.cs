using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// HUD for the Lobby scene — modeled on the Among Us lobby: room code + room
// info panel on the right, START (host) / WAITING FOR HOST (everyone else)
// along the bottom, EDIT (host) / VIEW (everyone else) for game settings.
public class LobbyUI : MonoBehaviour
{
    [Tooltip("Shown only to the host (START button, EDIT settings button).")]
    [SerializeField] private GameObject[] hostOnlyObjects;
    [Tooltip("Shown only to non-hosts (WAITING FOR HOST box, VIEW settings button).")]
    [SerializeField] private GameObject[] clientOnlyObjects;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private Button leaveButton;
    [SerializeField] private int maxPlayers = 15;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private float nextRefresh;
    private int lastPlayerCount = -1;
    [SerializeField] private int recommendedPlayers = 4;

    private void Start()
    {
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        SetAll(hostOnlyObjects, isHost);
        SetAll(clientOnlyObjects, !isHost);

        string code = MultiplayerConnectionManager.Instance != null ? MultiplayerConnectionManager.Instance.CurrentJoinCode : "";
        if (joinCodeText != null) joinCodeText.text = string.IsNullOrEmpty(code) ? "-" : code;

        int count = FindObjectsByType<PlayerRole>(FindObjectsSortMode.None).Length;
        if (capacityText != null) capacityText.text = $"{count}/{(GameStartManager.Instance != null ? GameStartManager.Instance.Capacity : maxPlayers)}";
        if (playerCountText != null)
            playerCountText.text = count < recommendedPlayers
                ? $"PLAYERS: {count}  ({recommendedPlayers}+ RECOMMENDED)"
                : $"PLAYERS: {count}";

        // Spread everyone out whenever someone joins or leaves, so players
        // (and their name tags) don't stack on the same spot.
        if (isHost && count != lastPlayerCount)
        {
            SceneSpawnPoints.PlaceAllPlayers();
        }
        lastPlayerCount = count;
    }

    private static void SetAll(GameObject[] objs, bool active)
    {
        if (objs == null) return;
        foreach (var o in objs)
            if (o != null && o.activeSelf != active) o.SetActive(active);
    }

    private async void OnLeaveClicked()
    {
        if (leaveButton != null) leaveButton.interactable = false;
        if (MultiplayerConnectionManager.Instance != null)
            await MultiplayerConnectionManager.Instance.LeaveSessionAsync();
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
