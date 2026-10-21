using Unity.Netcode;
using UnityEngine;

public class GameResultManager : NetworkBehaviour
{
    public static GameResultManager Instance { get; private set; }

    private NetworkVariable<bool> gameEnded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool GameEnded => gameEnded.Value;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Call this server-side after anything that could end the match:
    // a kill, an ejection, or a task completion.
    public void CheckWinConditions()
    {
        if (!IsServer || gameEnded.Value) return;

        // Don't evaluate before a match has actually started (no roles assigned yet)
        if (GameStartManager.Instance == null || !GameStartManager.Instance.GameStarted) return;

        int aliveCrewmates = 0;
        int aliveImpostors = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            PlayerRole role = client.PlayerObject.GetComponent<PlayerRole>();
            if (role == null || !role.IsAlive) continue;

            if (role.IsImpostor) aliveImpostors++;
            else aliveCrewmates++;
        }

        bool tasksComplete = TaskManager.Instance != null
            && TaskManager.Instance.TotalTasks > 0
            && TaskManager.Instance.CompletedTasks >= TaskManager.Instance.TotalTasks;

        // Check in priority order — tasks-complete and all-impostors-eliminated
        // both favor Crewmates; only check Maligno parity after ruling those out.
        if (tasksComplete)
        {
            EndGame("TAGUMPAY ANG BARYO — the villagers finished every chore. Crewmates win!");
            return;
        }

        if (aliveImpostors <= 0)
        {
            EndGame("TAGUMPAY ANG BARYO — every Maligno has been cast out. Crewmates win!");
            return;
        }

        if (aliveImpostors >= aliveCrewmates)
        {
            EndGame("NAGHARI ANG DILIM — the Maligno now equal or outnumber the living. They win.");
            return;
        }
    }

    private void EndGame(string message)
    {
        gameEnded.Value = true;
        Debug.Log($"[Server] GAME OVER: {message}");

        // Clear the board immediately — a body left over from the kill that
        // just ended the match (no meeting ever ran to clean it up) would
        // otherwise sit there visually straight through the Game Over screen.
        DeadBody.DespawnAll();

        AnnounceGameOverClientRpc(message);
    }

    [ClientRpc]
    private void AnnounceGameOverClientRpc(string message, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[Client] GAME OVER: {message}");
        GameOverUI.Instance?.Show(message);
    }

    // Called by GameStartManager.HostReturnToLobby() — clears the ended flag
    // so GameResultManager.CheckWinConditions() can evaluate again next match.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        gameEnded.Value = false;
    }
}