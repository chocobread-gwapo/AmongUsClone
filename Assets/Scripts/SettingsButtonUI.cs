using UnityEngine;

// Show/hide hook for the lobby's "Settings" button (opens HostSettingsPanel).
// Exact same pattern as StartGameButtonUI — both are lobby-only controls
// driven by GameStartManager's NotifyGameStartedClientRpc/
// NotifyReturnedToLobbyClientRpc. Without this, the Settings button (and
// whatever it opens) stayed clickable mid-match since nothing ever told it
// to hide — only the Start Game button itself had this wiring.
public class SettingsButtonUI : MonoBehaviour
{
    public static SettingsButtonUI Instance { get; private set; }

    [Tooltip("Optional — if the host had the settings panel open the moment Start Game was pressed, this force-closes it too, not just the button that opens it.")]
    [SerializeField] private HostSettingsPanelUI settingsPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        if (settingsPanel != null) settingsPanel.ClosePanel();
    }
}