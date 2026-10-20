using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Duwende — Nuno Mound Trap
// Places a camouflaged trap (max 3 active at once). On trigger: slows the
// victim 50% and reduces their vision radius 60% for 4s.
public class DuwendeAbility : NetworkBehaviour
{
    [Header("Nuno Mound Trap")]
    [Tooltip("Must have a NetworkObject + NunoMoundTrap component, and must be " +
             "registered in NetworkManager's Network Prefabs list — otherwise you'll " +
             "get the same 'NetworkPrefab hash not found' error the Dummy corruption caused.")]
    [SerializeField] private GameObject trapPrefab;
    [SerializeField] private int maxActiveTraps = 3;

    private PlayerRole myRole;

    // Server-only bookkeeping — never synced.
    private readonly List<NunoMoundTrap> activeTraps = new List<NunoMoundTrap>();

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentMalignoType == MalignoType.Duwende;
    }

    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;
        PlaceTrapServerRpc(transform.position);
    }

    [ServerRpc]
    private void PlaceTrapServerRpc(Vector3 position)
    {
        if (!IsMyAbility()) return;

        activeTraps.RemoveAll(t => t == null);
        if (activeTraps.Count >= maxActiveTraps)
        {
            Debug.Log($"[Server] Duwende already has {maxActiveTraps} traps active.");
            return;
        }

        if (trapPrefab == null)
        {
            Debug.LogWarning("DuwendeAbility has no trapPrefab assigned.");
            return;
        }

        GameObject trapObj = Instantiate(trapPrefab, position, Quaternion.identity);
        NetworkObject netObj = trapObj.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning("trapPrefab is missing a NetworkObject component.");
            Destroy(trapObj);
            return;
        }
        netObj.Spawn();

        NunoMoundTrap trap = trapObj.GetComponent<NunoMoundTrap>();
        if (trap != null)
        {
            trap.ServerSetPlacer(OwnerClientId);
            activeTraps.Add(trap);
            trap.OnConsumed += HandleTrapConsumed;
        }

        Debug.Log($"[Server] Duwende placed a Nuno mound trap ({activeTraps.Count}/{maxActiveTraps}).");
    }

    private void HandleTrapConsumed(NunoMoundTrap trap)
    {
        activeTraps.Remove(trap);
        trap.OnConsumed -= HandleTrapConsumed;
    }
}