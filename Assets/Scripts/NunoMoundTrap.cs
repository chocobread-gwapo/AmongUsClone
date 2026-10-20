using System;
using Unity.Netcode;
using UnityEngine;

// The actual trap object a Duwende places. Networked (server-spawned) so every
// client sees the same trap exist and trigger at the same moment.
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class NunoMoundTrap : NetworkBehaviour
{
    [Header("Trap Effect")]
    [SerializeField] private float slowMultiplier = 0.5f;
    [SerializeField] private float visionReduction = 0.6f; // TODO: wire into vision system once built
    [SerializeField] private float effectDuration = 4f;

    // Raised server-side so DuwendeAbility can drop this trap from its active-count tracking.
    public event Action<NunoMoundTrap> OnConsumed;

    private bool triggered = false;

    // Server-only — set right after spawn by DuwendeAbility so the Duwende
    // can't immediately trip their own trap (it spawns on top of them).
    private ulong placerClientId;
    private bool placerSet = false;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
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

        targetController.ServerApplySlow(slowMultiplier, effectDuration);

        // TODO: once a vision-cone/fog-of-war system exists, also call something like
        // targetController.ServerApplyVisionReduction(visionReduction, effectDuration);

        Debug.Log($"[Server] {other.gameObject.name} triggered a Nuno mound trap.");

        OnConsumed?.Invoke(this);

        if (NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}