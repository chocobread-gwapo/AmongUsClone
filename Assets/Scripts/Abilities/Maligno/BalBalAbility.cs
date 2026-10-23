using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BalBalAbility : NetworkBehaviour
{
    [Header("Banana Trunk Prop")]
    [SerializeField] private GameObject bananaTrunkPropPrefab;

    // The prop is spawned with a plain local Instantiate() (see
    // SpawnBananaTrunkClientRpc below) — it's purely cosmetic, so it was
    // never worth making it its own NetworkObject. That also means nothing
    // ever destroyed it on its own, so every decoy from every past match
    // just kept piling up. This tracks every instance THIS client has ever
    // spawned so ClearAllTrunks() can wipe them on return to lobby. Static
    // (not per-player) since any client might render decoys spawned by any
    // Bal-bal's ClientRpc, and it needs to survive this component being
    // destroyed/recreated between scenes.
    private static readonly List<GameObject> spawnedTrunks = new List<GameObject>();

    [Tooltip("How close a target needs to be for Bal-bal to consume them via the ABILITY button. Match this to the KILL button's own range so the two feel consistent.")]
    [SerializeField] private float targetRange = 1.2f;

    private PlayerRole myRole;

    // The ABILITY (consuming a body) has its own cooldown, twice as long as the kill cooldown.
    // The consume is still a kill, so the normal kill cooldown starts too.
    private const float AbilityCooldownFactor = 2f;
    private float serverAbilityCdEnd = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.IsAlive && myRole.CurrentMalignoType == MalignoType.BalBal; // dead players cannot use abilities
    }

    // ABILITY-button entry point — same owner-side target-finding pattern as
    // every other ability (see AswangAbility.TryActivate()). The KILL button
    // no longer special-cases Bal-bal at all; this is now the only way to
    // "eat" a victim and leave a banana-trunk decoy instead of a body.
    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, targetRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            // Maligno never consume a fellow Maligno's body.
            if (targetRole != null && targetRole.IsImpostor) continue;

            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();
            if (targetRole == null || targetNetObj == null || !targetRole.IsAlive) continue;

            ConsumeBodyServerRpc(targetNetObj.NetworkObjectId);
            break;
        }
    }

    // Renamed from ServerConsumeBodyRpc — Netcode's code generator requires
    // the method name to END with "ServerRpc" exactly, not just contain "Rpc".
    [ServerRpc]
    public void ConsumeBodyServerRpc(ulong targetNetworkObjectId)
    {
        if (!IsMyAbility()) return;

        // Bal-bal's "eat the body" is still a kill — shares the same
        // kill-cooldown clock as the normal kill button, not a separate one.
        PlayerController myController = GetComponent<PlayerController>();
        if (myController != null && myController.ServerIsKillOnCooldown()) return;
        if (Time.time < serverAbilityCdEnd) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
            return;

        PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
        if (targetRole == null || !targetRole.IsAlive) return;

        // Maligno never consume a fellow Maligno's body.
        if (targetRole.IsImpostor) return;

        Vector3 deathPosition = targetObj.transform.position;

        // SERVER-AUTHORITATIVE RANGE CHECK — same gap as RequestKillServerRpc
        // had: TryActivate()'s OverlapCircleAll above is client-side only, so
        // without this a modified client could send ConsumeBodyServerRpc with
        // any target's id from anywhere on the map.
        float distanceToTarget = Vector2.Distance(transform.position, deathPosition);
        if (distanceToTarget > targetRange * 1.5f)
        {
            Debug.LogWarning($"[Server] Rejected consume-body request from {gameObject.name}: " +
                $"{distanceToTarget:F2} units from target (max {targetRange * 1.5f:F2}).");
            return;
        }

        targetRole.Kill();
        targetRole.ServerMarkBalBalConsumed();
        myController?.ServerStartKillCooldown();

        float killCd = GameStartManager.Instance != null ? GameStartManager.Instance.KillCooldownDuration : 15f;
        float abilityCd = killCd * AbilityCooldownFactor;
        serverAbilityCdEnd = Time.time + abilityCd;
        NotifyAbilityCooldownClientRpc(abilityCd);

        // Same Among-Us-style snap as a normal kill — Bal-bal ends up
        // standing where the victim was too.
        myController?.ServerTeleportTo(deathPosition);

        SpawnBananaTrunkClientRpc(deathPosition);
    }

    // Owner-only: shows the countdown on the ABILITY button.
    [ClientRpc]
    private void NotifyAbilityCooldownClientRpc(float duration)
    {
        if (!IsOwner) return;
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    // Called by GameStartManager.HostReturnToLobby() so a new match starts with a ready ability.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverAbilityCdEnd = -999f;
    }

    [ClientRpc]
    private void SpawnBananaTrunkClientRpc(Vector3 position)
    {
        if (bananaTrunkPropPrefab != null)
        {
            GameObject trunk = Instantiate(bananaTrunkPropPrefab, position, Quaternion.identity);
            spawnedTrunks.Add(trunk);
        }
    }

    // Called on every client from GameStartManager.NotifyReturnedToLobbyClientRpc()
    // so decoys from the match that just ended don't carry over into the next one.
    public static void ClearAllTrunks()
    {
        foreach (var trunk in spawnedTrunks)
        {
            if (trunk != null) Destroy(trunk);
        }
        spawnedTrunks.Clear();
    }
}