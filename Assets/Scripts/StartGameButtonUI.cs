using UnityEngine;

// Show/hide hook for the lobby's "Start Game" button. Lives on the button's
// own GameObject (or a parent wrapping it) and is driven by GameStartManager:
// hidden the instant a match starts, shown again once HostReturnToLobby()
// resets everything. Keeps the button from staying clickable (or visible)
// mid-match, and reappearing is how players know a new match can begin.
public class StartGameButtonUI : MonoBehaviour
{
    public static StartGameButtonUI Instance { get; private set; }

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
    }
}