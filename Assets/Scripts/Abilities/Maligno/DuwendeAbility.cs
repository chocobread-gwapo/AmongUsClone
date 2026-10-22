using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Duwende — Nuno Mound Trap
// Places a camouflaged trap (max 3 active at once, AND capped at 3 total for
// the whole match — a triggered/consumed trap frees up an "active" slot but
// does NOT refund a use, so Duwende can't just place infinite traps over time
// by waiting for old ones to get triggered).
public class DuwendeAbility : NetworkBehaviour
{
    [Header("Nuno Mound Trap")]
    [Tooltip("Must have a NetworkObject + NunoMoundTrap component, and must be " +
             "registered in NetworkManager's Network Prefabs list — otherwise you'll " +
             "get the same 'NetworkPrefab hash not found' error the Dummy corruption caused.")]
    [SerializeField] private GameObject trapPrefab;
    [SerializeField] private int maxActiveTraps = 3;

    [Tooltip("Hard cap on how many traps this Duwende can EVER place in one match, regardless of how many have already been triggered/consumed.")]
    [SerializeField] private int maxTrapsPerMatch = 3;

    [SerializeField] private float placeCooldown = 1f;
    [SerializeField] private float minTrapSpacing = 1f;
    private float nextPlaceTime;

    private PlayerRole myRole;

    // Server-only bookkeeping — never synced.
    private readonly List<NunoMoundTrap> activeTraps = new List<NunoMoundTrap>();

    // Server-only — counts UP only, never decrements when a trap is consumed.
    // This is what actually enforces the per-match cap; activeTraps.Count
    // only limits how many can be out at the same time.
    private int totalTrapsPlaced = 0;

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

        // Server decides where the trap goes (at the feet) and rate-limits placement.
        if (Time.time < nextPlaceTime) return;
        position = transform.position + Vector3.down * 0.55f;
        foreach (var t in activeTraps)
            if (t != null && Vector2.Distance(t.transform.position, position) < minTrapSpacing) return;

        if (totalTrapsPlaced >= maxTrapsPerMatch)
        {
            Debug.Log($"[Server] Duwende has used all {maxTrapsPerMatch} traps for this match.");
            return;
        }

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

        totalTrapsPlaced++;
        nextPlaceTime = Time.time + placeCooldown;

        Debug.Log($"[Server] Duwende placed a Nuno mound trap ({activeTraps.Count}/{maxActiveTraps} active, {totalTrapsPlaced}/{maxTrapsPerMatch} used this match).");
    }

    private void HandleTrapConsumed(NunoMoundTrap trap)
    {
        activeTraps.Remove(trap);
        trap.OnConsumed -= HandleTrapConsumed;
    }

    // Called by GameStartManager.HostReturnToLobby() so a fresh match gives
    // Duwende a fresh set of 3 uses, not whatever was left over from last time.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;

        activeTraps.Clear();
        totalTrapsPlaced = 0;
        nextPlaceTime = 0f;
    }
}