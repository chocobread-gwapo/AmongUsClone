using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameStartManager : NetworkBehaviour
{
    public static GameStartManager Instance { get; private set; }

    [Header("Match Settings")]
    [SerializeField] private int impostorCount = 1;

    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool GameStarted => gameStarted.Value;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Call this from a "Start Game" button, host-only.
    // Only works if called on the server/host instance.
    public void HostStartGame()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only the host/server can start the game.");
            return;
        }

        if (gameStarted.Value)
        {
            Debug.LogWarning("Game already started.");
            return;
        }

        AssignRoles();
        gameStarted.Value = true;
    }

    private void AssignRoles()
    {
        // Gather every connected player's NetworkObject
        List<PlayerRole> allPlayers = new List<PlayerRole>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                PlayerRole role = client.PlayerObject.GetComponent<PlayerRole>();
                if (role != null)
                {
                    allPlayers.Add(role);
                }
            }
        }

        if (allPlayers.Count == 0)
        {
            Debug.LogWarning("No players found to assign roles to.");
            return;
        }

        // Shuffle (Fisher-Yates) so impostor selection is unbiased
        for (int i = allPlayers.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (allPlayers[i], allPlayers[j]) = (allPlayers[j], allPlayers[i]);
        }

        int actualImpostorCount = Mathf.Min(impostorCount, allPlayers.Count - 1);
        actualImpostorCount = Mathf.Max(actualImpostorCount, allPlayers.Count > 1 ? 1 : 0);

        for (int i = 0; i < allPlayers.Count; i++)
        {
            Role assignedRole = (i < actualImpostorCount) ? Role.Impostor : Role.Crewmate;
            allPlayers[i].ServerSetRole(assignedRole);
        }

        Debug.Log($"[Server] Assigned roles: {actualImpostorCount} Impostor(s) out of {allPlayers.Count} players.");
    }
}