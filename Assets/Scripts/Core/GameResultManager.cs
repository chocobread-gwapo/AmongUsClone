using System.Collections.Generic;
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
            EndGame(true, "TAGUMPAY ANG BARYO", "The villagers finished every chore before the Maligno could stop them.");
            return;
        }

        if (aliveImpostors <= 0)
        {
            EndGame(true, "TAGUMPAY ANG BARYO", "Every Maligno has been cast out of the village.");
            return;
        }

        if (aliveImpostors >= aliveCrewmates)
        {
            EndGame(false, "NAGHARI ANG DILIM", "The Maligno now equal or outnumber the living villagers.");
            return;
        }
    }

    private void EndGame(bool crewWon, string headline, string reason)
    {
        gameEnded.Value = true;
        Debug.Log($"[Server] GAME OVER: {headline}: {reason}");

        // Clear the board immediately — a body left over from the kill that
        // just ended the match (no meeting ever ran to clean it up) would
        // otherwise sit there visually straight through the Game Over screen.
        DeadBody.DespawnAll();

        // Roles are private during a match (each client only knows its own),
        // so the full reveal has to come from the server, at the very end.
        var ids = new List<ulong>();
        var roles = new List<int>();
        var types = new List<int>();
        var classes = new List<int>();
        var alive = new List<bool>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            PlayerRole r = client.PlayerObject.GetComponent<PlayerRole>();
            if (r == null) continue;
            ids.Add(client.ClientId);
            roles.Add(r.IsImpostor ? 1 : 0);
            types.Add((int)r.CurrentMalignoType);
            classes.Add((int)r.CurrentSurvivorClass);
            alive.Add(r.IsAlive);
        }

        int done = TaskManager.Instance != null ? TaskManager.Instance.CompletedTasks : 0;
        int total = TaskManager.Instance != null ? TaskManager.Instance.TotalTasks : 0;

        AnnounceGameOverClientRpc(crewWon, headline, reason,
            ids.ToArray(), roles.ToArray(), types.ToArray(), classes.ToArray(), alive.ToArray(),
            done, total);
    }

    [ClientRpc]
    private void AnnounceGameOverClientRpc(bool crewWon, string headline, string reason,
        ulong[] ids, int[] roles, int[] types, int[] classes, bool[] alive,
        int tasksDone, int tasksTotal, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[Client] GAME OVER: {headline}: {reason}");
        GameOverUI.Instance?.ShowResult(crewWon, headline, reason, ids, roles, types, classes, alive, tasksDone, tasksTotal);
    }

    // Server-only: show a short notice (toast) to every player, e.g. "Player 3 left the game".
    public void ServerBroadcastNotice(string message)
    {
        if (!IsServer) return;
        NoticeClientRpc(message);
    }

    [ClientRpc]
    private void NoticeClientRpc(string message, ClientRpcParams rpcParams = default)
    {
        Notice.Show(message);
    }

    // Called by GameStartManager.HostReturnToLobby() — clears the ended flag
    // so GameResultManager.CheckWinConditions() can evaluate again next match.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        gameEnded.Value = false;
    }
}
