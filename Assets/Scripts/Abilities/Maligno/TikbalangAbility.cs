using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Tikbalang — Liligawin (Bewilder)
// Inverts a nearby target's movement axes for 5s. Server-RPC triggered, 25s cooldown.
public class TikbalangAbility : NetworkBehaviour
{
    [Header("Liligawin (Bewilder)")]
    [SerializeField] private float targetRange = 1.5f;
    [SerializeField] private float inversionDuration = 5f;
    [SerializeField] private float cooldownDuration = 25f;

    private PlayerRole myRole;

    // Server-only — never exposed to clients, matches the privacy pattern
    // used for role/MalignoType (plain field, not a NetworkVariable).
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.IsAlive && myRole.CurrentMalignoType == MalignoType.Tikbalang; // dead players cannot use abilities
    }

    // Time.time keeps counting up for the whole play session, not just one
    // match — without this, a cooldown started near the end of a match
    // would still be "in the future" at the start of the next one, silently
    // blocking this ability on a fresh game. Called from GameStartManager's
    // HostReturnToLobby().
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverCooldownEndTime = -999f;
    }

    // See PlayerController.ServerExtendKillCooldownIfActive() — same idea,
    // called by MeetingManager once a meeting resolves.
    public void ServerExtendCooldownIfActive(float seconds)
    {
        if (!IsServer) return;
        if (Time.time < serverCooldownEndTime)
        {
            serverCooldownEndTime += seconds;
        }
    }

    // Called locally by the owner (from PlayerController's shared Ability input).
    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, targetRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();
            if (targetRole == null || targetNetObj == null || !targetRole.IsAlive) continue;

            // Maligno don't bewilder each other — keep looking for a Crewmate.
            if (targetRole.IsImpostor) continue;

            BewilderServerRpc(targetNetObj.NetworkObjectId);
            break;
        }
    }

    [ServerRpc]
    private void BewilderServerRpc(ulong targetNetworkObjectId)
    {
        if (!IsMyAbility()) return;
        if (Time.time < serverCooldownEndTime) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
            return;

        PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
        PlayerController targetController = targetObj.GetComponent<PlayerController>();
        if (targetRole == null || targetController == null || !targetRole.IsAlive) return;

        // Server-authoritative check — never bewilder a fellow Maligno.
        if (targetRole.IsImpostor) return;

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        StartCoroutine(InvertControlsFor(targetController, inversionDuration));
    }

    private IEnumerator InvertControlsFor(PlayerController targetController, float duration)
    {
        targetController.ServerSetControlsInverted(true);
        yield return new WaitForSeconds(duration);
        targetController.ServerSetControlsInverted(false);
    }

    // Targeted at the Tikbalang's own owner only, so the ability-cooldown UI
    // (once built) can start its countdown. Doesn't leak anything secret.
    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Liligawin used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }
}
