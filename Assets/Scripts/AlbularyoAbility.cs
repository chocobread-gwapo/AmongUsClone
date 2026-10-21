using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Albularyo — Survivor Class "Pag-aalaga" (Heal)
// Cures a nearby Villager currently cursed by Duwende's Nuno mound trap:
// ends their slow + vision reduction immediately instead of making them
// wait it out. Doesn't help Maligno (CurseStatus is never applied to them
// in the first place — NunoMoundTrap already skips Impostors).
//
// Same shape as every other ability script: sits on every Player prefab,
// gates itself via IsMyAbility(), shares PlayerController's Ability input/button.
public class AlbularyoAbility : NetworkBehaviour
{
    [Header("Pag-aalaga (Heal)")]
    [SerializeField] private float healRange = 1.5f;
    [SerializeField] private float cooldownDuration = 30f;

    [Header("Heal Glow (local, nearby-only)")]
    [Tooltip("Only players within this many units of the heal see the green glow — everyone else never even receives the event.")]
    [SerializeField] private float healGlowVisibilityRadius = 5f;
    [SerializeField] private float healGlowDuration = 1.5f;

    private PlayerRole myRole;

    // Server-only.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentSurvivorClass == SurvivorClass.Albularyo;
    }

    // See TikbalangAbility.ServerResetForNewGame() for why this is needed.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverCooldownEndTime = -999f;
    }

    public void ServerExtendCooldownIfActive(float seconds)
    {
        if (!IsServer) return;
        if (Time.time < serverCooldownEndTime)
        {
            serverCooldownEndTime += seconds;
        }
    }

    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;

        // Find a nearby cursed player client-side first — CurseStatus's
        // isCursed is an Everyone-readable NetworkVariable, so this is just
        // a UX nicety (don't bother sending an RPC with no target in range).
        // The server re-validates everything below regardless.
        NetworkObject target = FindNearbyCursedPlayer();
        if (target == null)
        {
            Debug.Log("[Client] No cursed ally in range to heal.");
            return;
        }

        HealServerRpc(target.NetworkObjectId);
    }

    private NetworkObject FindNearbyCursedPlayer()
    {
        // Unlike kill/report, Albularyo IS allowed to target themselves —
        // no self-exclusion here, so a cursed Albularyo can heal their own curse.
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, healRange);
        foreach (var hit in hits)
        {
            CurseStatus curse = hit.GetComponent<CurseStatus>();
            if (curse == null || !curse.IsCursed) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            if (targetRole == null || !targetRole.IsAlive) continue;

            NetworkObject netObj = hit.GetComponent<NetworkObject>();
            if (netObj != null) return netObj;
        }
        return null;
    }

    [ServerRpc]
    private void HealServerRpc(ulong targetNetworkObjectId)
    {
        if (!IsMyAbility()) return;
        if (myRole == null || !myRole.IsAlive) return;
        if (Time.time < serverCooldownEndTime) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
        {
            return;
        }

        // Server-authoritative re-check: still in range, still actually cursed.
        float distance = Vector2.Distance(transform.position, targetObj.transform.position);
        if (distance > healRange + 0.5f) return;

        CurseStatus targetCurse = targetObj.GetComponent<CurseStatus>();
        if (targetCurse == null || !targetCurse.IsCursed) return;

        targetCurse.ServerCure();

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        NotifyNearbyPlayersOfHeal(targetObj);

        Debug.Log($"[Server] Albularyo healed {targetObj.gameObject.name} — curse cleared.");
    }

    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Pag-aalaga used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    // Server-only: figures out which clients are currently close enough to
    // the heal to see it, and only sends the glow event to those — anyone
    // elsewhere on the map never receives the RPC at all, not just "chooses"
    // not to render it.
    private void NotifyNearbyPlayersOfHeal(NetworkObject healedPlayer)
    {
        Vector3 healPosition = healedPlayer.transform.position;
        List<ulong> nearbyClientIds = new List<ulong>();
        float radiusSqr = healGlowVisibilityRadius * healGlowVisibilityRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            float distSqr = (client.PlayerObject.transform.position - healPosition).sqrMagnitude;
            if (distSqr <= radiusSqr)
            {
                nearbyClientIds.Add(client.ClientId);
            }
        }

        if (nearbyClientIds.Count == 0) return;

        ClientRpcParams nearbyOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = nearbyClientIds.ToArray() }
        };
        // Sends the healed player's NetworkObjectId rather than a fixed point,
        // so each receiving client can resolve the live Transform and have
        // the glow follow them — not just mark where they stood at heal-time.
        ShowHealGlowClientRpc(healedPlayer.NetworkObjectId, healGlowDuration, nearbyOnly);
    }

    [ClientRpc]
    private void ShowHealGlowClientRpc(ulong targetNetworkObjectId, float duration, ClientRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
        {
            targetObj.GetComponent<HealGlowDisplay>()?.ShowGlow(duration);
        }
    }
}
