using UnityEngine;
using UnityEngine.UI;

// MainMenu scene's entry screen — styled after Among Us's own main menu but
// trimmed to what this project actually has right now: no inventory, shop,
// friends list, or account system, just the core flow of PLAY / SETTINGS /
// QUIT. Add those other buttons back later once there's something real for
// them to do.
//
// PLAY swaps to the Host/Join sub-panel — LobbyConnectionUI (already built)
// lives on that panel and owns all the actual hosting/joining logic; this
// script only shows/hides panels. Once the host presses Host (or a client's
// Join succeeds), MultiplayerConnectionManager takes over and loads the
// Lobby scene — MainMenu's job ends there.
public class MainMenuUI : MonoBehaviour
{
    [Header("Main Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Panels")]
    [Tooltip("Root panel holding the PLAY / SETTINGS / QUIT buttons. Shown by default, hidden while playPanel or settingsPanel is open.")]
    [SerializeField] private GameObject mainButtonsPanel;
    [Tooltip("Root panel holding LobbyConnectionUI's Host/Join controls. Hidden until PLAY is pressed.")]
    [SerializeField] private GameObject playPanel;
    [Tooltip("Simple options panel (e.g. a volume slider wired to AudioManager.SetVolume). Optional — leave empty if you don't have one yet; the SETTINGS button just won't do anything.")]
    [SerializeField] private GameObject settingsPanel;

    private void Awake()
    {
        if (playPanel != null) playPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);

        if (playButton != null) playButton.onClick.AddListener(OnPlayClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void OnPlayClicked()
    {
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(false);
        if (playPanel != null) playPanel.SetActive(true);
    }

    private void OnSettingsClicked()
    {
        if (settingsPanel == null) return; // no options panel wired up yet — no-op
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    // Wire this to a "Back"/"X" button on playPanel and settingsPanel so the
    // player can return to the main buttons without restarting the app.
    public void OnBackClicked()
    {
        if (playPanel != null) playPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
