using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameStartManager : NetworkBehaviour
{
    public static GameStartManager Instance { get; private set; }

    [Header("Match Settings")]
    [SerializeField] private int impostorCount = 1;

    [Header("Maligno Types")]
    [Tooltip("Which folklore roles can be randomly assigned. Uncheck any not built/tested yet.")]
    [SerializeField]
    private List<MalignoType> enabledMalignoTypes = new List<MalignoType>
    {
        MalignoType.Duwende,
        MalignoType.Tikbalang,
        MalignoType.BalBal,
        MalignoType.Aswang,
        MalignoType.Kapre,
    };

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

        List<ulong> impostorClientIds = new List<ulong>();

        for (int i = 0; i < allPlayers.Count; i++)
        {
            if (i < actualImpostorCount)
            {
                MalignoType assignedType = PickRandomMalignoType();
                allPlayers[i].ServerSetRole(Role.Impostor, assignedType);
                impostorClientIds.Add(allPlayers[i].OwnerClientId);

                Debug.Log($"[Server] {allPlayers[i].gameObject.name} assigned Impostor ({assignedType})");
            }
            else
            {
                allPlayers[i].ServerSetRole(Role.Crewmate, MalignoType.None);
            }
        }

        // Let each impostor know who their fellow impostor(s) are
        if (impostorClientIds.Count > 1)
        {
            ClientRpcParams targetImpostorsOnly = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = impostorClientIds.ToArray() }
            };
            NotifyFellowImpostorsClientRpc(impostorClientIds.ToArray(), targetImpostorsOnly);
        }

        Debug.Log($"[Server] Assigned roles: {actualImpostorCount} Impostor(s) out of {allPlayers.Count} players.");
    }

    private MalignoType PickRandomMalignoType()
    {
        if (enabledMalignoTypes == null || enabledMalignoTypes.Count == 0)
        {
            Debug.LogWarning("No Maligno types enabled — defaulting to BalBal.");
            return MalignoType.BalBal;
        }

        int index = Random.Range(0, enabledMalignoTypes.Count);
        return enabledMalignoTypes[index];
    }

    [ClientRpc]
    private void NotifyFellowImpostorsClientRpc(ulong[] impostorClientIds, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[Client] Your fellow impostor client IDs: {string.Join(", ", impostorClientIds)}");
        // TODO: use this to highlight teammates in UI, e.g. colored name tags
    }
}