using UnityEngine;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI gameOverText;

    private void Awake()
    {
        Instance = this;
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void Show(string message)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText != null) gameOverText.text = message;

        // The match is over the moment this shows — the in-match HUD
        // (task panel, USE/REPORT/ABILITY/Call Meeting) has nothing left to
        // do and shouldn't be visible behind/alongside the result panel.
        GameHudUI.Instance?.Hide();
    }

    // Called by GameStartManager's NotifyReturnedToLobbyClientRpc once the
    // host has reset match state, so the panel clears for everyone.
    public void Hide()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    // Wired to the "Return to Lobby" button's OnClick inside GameOverPanel —
    // same bridge pattern as CallMeetingButton/UseButton: only the host's
    // click actually does anything (GameStartManager.HostReturnToLobby() is
    // server-gated internally), everyone else's click is a harmless no-op.
    public void OnReturnToLobbyClicked()
    {
        GameStartManager.Instance?.HostReturnToLobby();
    }
}