using UnityEngine;

// Bridge for the Lobby scene's "Start Game" button. GameStartManager is now a
// runtime-spawned, DontDestroyOnLoad object (not placed in any scene), so the
// button's OnClick can't be dragged to it in the Inspector — wire OnClick to
// this component's OnStartGameClicked() instead. Only the host's click does
// anything (HostStartGame() is server-gated internally).
public class StartGameButton : MonoBehaviour
{
    public void OnStartGameClicked()
    {
        GameStartManager.Instance?.HostStartGame();
    }
}
