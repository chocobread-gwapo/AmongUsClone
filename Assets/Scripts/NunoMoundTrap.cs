using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The actual trap object a Duwende places. Networked (server-spawned) so every
// client sees the same trap exist and trigger at the same moment.
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class NunoMoundTrap : NetworkBehaviour
{
    [Header("Trap Effect")]
    [Tooltip("How much the curse slows/dims the victim. The curse itself is indefinite now — CurseStatus owns how long it lasts and what happens if it isn't cured (see its Death Timer Seconds field).")]
    [SerializeField] private float slowMultiplier = 0.5f;
    [SerializeField] private float visionReduction = 0.6f;

    // Raised server-side so DuwendeAbility can drop this trap from its active-count tracking.
    public event Action<NunoMoundTrap> OnConsumed;

    // Server-only registry of every currently-active trap across all Duwendes —
    // DuwendeAbility only tracks its own placer's traps, but Third Eye Sight
    // needs to reveal ALL active traps regardless of who placed them.
    public static readonly List<NunoMoundTrap> ActiveTraps = new List<NunoMoundTrap>();

    private bool triggered = false;

    // Server-only — set right after spawn by DuwendeAbility so the Duwende
    // can't immediately trip their own trap (it spawns on top of them).
    private ulong placerClientId;
    private bool placerSet = false;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) ActiveTraps.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ActiveTraps.Remove(this);
    }

    public void ServerSetPlacer(ulong clientId)
    {
        if (!IsServer) return;
        placerClientId = clientId;
        placerSet = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || triggered) return;

        PlayerController targetController = other.GetComponent<PlayerController>();
        PlayerRole targetRole = other.GetComponent<PlayerRole>();
        NetworkObject targetNetObj = other.GetComponent<NetworkObject>();
        if (targetController == null || targetRole == null || !targetRole.IsAlive) return;

        // Never trigger on the Duwende who placed this trap.
        if (placerSet && targetNetObj != null && targetNetObj.OwnerClientId == placerClientId) return;

        // Maligno don't trigger each other's traps either.
        if (targetRole.IsImpostor) return;

        triggered = true;

        // CurseStatus now owns the whole effect: it applies the persistent
        // slow + vision reduction itself AND starts the death timer. An
        // un-healed curse no longer wears off — only Albularyo can end it.
        CurseStatus curse = other.GetComponent<CurseStatus>();
        if (curse != null)
        {
            curse.ServerApplyCurse(slowMultiplier, visionReduction);
        }
        else
        {
            // Defensive fallback in case a player is somehow missing
            // CurseStatus — falls back to the old timed-effect behavior so a
            // trigger never does nothing at all.
            Debug.LogWarning($"{other.gameObject.name} has no CurseStatus — applying a one-off timed slow/vision effect instead of a real curse.");
            targetController.ServerApplySlow(slowMultiplier, 4f);
            other.GetComponent<PlayerVision>()?.ServerApplyVisionReduction(visionReduction, 4f);
        }

        Debug.Log($"[Server] {other.gameObject.name} triggered a Nuno mound trap.");

        OnConsumed?.Invoke(this);

        if (NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}