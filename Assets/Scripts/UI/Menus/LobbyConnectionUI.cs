using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Simple host/join screen wiring for MultiplayerConnectionManager. Drop this
// on your main-menu/lobby panel and wire up the fields in the Inspector.
// Doesn't dictate layout — just the minimum: a Host button, a Join button +
// code input field, a status text, and a "your join code is X" display that
// appears once hosting succeeds so the host can read it out/share it.
public class LobbyConnectionUI : MonoBehaviour
{
    [Header("Host")]
    [SerializeField] private Button hostButton;
    [SerializeField] private GameObject joinCodeDisplayRoot; // panel shown once hosting, containing joinCodeText
    [SerializeField] private TextMeshProUGUI joinCodeText;

    [Header("Join")]
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField joinCodeInputField;

    [Header("Status")]
    [SerializeField] private TextMeshProUGUI statusText;

    private void Awake()
    {
        if (joinCodeDisplayRoot != null) joinCodeDisplayRoot.SetActive(false);

        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
    }

    // Subscribing in Start() rather than OnEnable() matters here: Unity
    // doesn't guarantee Awake() order between different GameObjects, so if
    // this ran in OnEnable(), it could fire before
    // MultiplayerConnectionManager.Awake() has set Instance — silently
    // skipping the subscription (the button clicks would still work, since
    // those call Instance directly, but status text / join code display
    // would never update). Start() is guaranteed to run only after every
    // object's Awake() has already completed, so Instance is reliably set
    // by the time we get here.
    private void Start()
    {
        if (MultiplayerConnectionManager.Instance != null)
        {
            MultiplayerConnectionManager.Instance.OnStatusChanged += HandleStatus;
            MultiplayerConnectionManager.Instance.OnJoinCodeReady += HandleJoinCodeReady;
            MultiplayerConnectionManager.Instance.OnConnectionFailed += HandleFailed;
        }
        else
        {
            Debug.LogError("[LobbyConnectionUI] MultiplayerConnectionManager.Instance is null in Start() — make sure MultiplayerConnectionManager exists in this scene.");
        }
    }

    private void OnDestroy()
    {
        if (MultiplayerConnectionManager.Instance != null)
        {
            MultiplayerConnectionManager.Instance.OnStatusChanged -= HandleStatus;
            MultiplayerConnectionManager.Instance.OnJoinCodeReady -= HandleJoinCodeReady;
            MultiplayerConnectionManager.Instance.OnConnectionFailed -= HandleFailed;
        }
    }

    private async void OnHostClicked()
    {
        SetButtonsInteractable(false);
        await MultiplayerConnectionManager.Instance.HostGameAsync();
        SetButtonsInteractable(true);
    }

    private async void OnJoinClicked()
    {
        if (joinCodeInputField == null) return;

        SetButtonsInteractable(false);
        await MultiplayerConnectionManager.Instance.JoinGameAsync(joinCodeInputField.text);
        SetButtonsInteractable(true);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
    }

    private void HandleStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void HandleJoinCodeReady(string code)
    {
        if (joinCodeDisplayRoot != null) joinCodeDisplayRoot.SetActive(true);
        if (joinCodeText != null) joinCodeText.text = code;
    }

    private void HandleFailed(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.LogWarning($"[LobbyConnectionUI] {message}");
    }
}