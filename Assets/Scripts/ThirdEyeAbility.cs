using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Third-Eye User — Survivor Class "Third Eye Sight"
// Reveals supernatural footprint residue left by Maligno over the last 8s,
// plus any currently-active traps, visible for 4s in neutral gray (no player
// names or colors attached). Costs impaired vision for the duration of the
// skill's cooldown, as the price for seeing what shouldn't be seen.
//
// Sits on every Player prefab alongside the Maligno ability scripts, same
// IsMyAbility() gating pattern — only activates for the one player actually
// holding this Survivor Class. Shares PlayerController's Ability input/button.
public class ThirdEyeAbility : NetworkBehaviour
{
    [Header("Third Eye Sight")]
    [SerializeField] private float lookbackSeconds = 8f;
    [SerializeField] private float revealDuration = 4f;
    [SerializeField] private float cooldownDuration = 40f;

    [Tooltip("Only footprints/traps within this many units of the Third-Eye User, at the moment the skill is used, are revealed — not the Maligno's entire recent path across the map.")]
    [SerializeField] private float revealRadius = 6f;

    [Header("Cost")]
    [Tooltip("Vision-radius multiplier applied to the user themselves for the duration of the cooldown.")]
    [SerializeField] private float impairedVisionMultiplier = 0.6f;

    private PlayerRole myRole;

    // Server-only — never exposed to clients, same privacy pattern as the
    // other abilities' cooldowns.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentSurvivorClass == SurvivorClass.ThirdEyeUser;
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

    // Called locally by the owner (from PlayerController's shared Ability input/button).
    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;
        UseThirdEyeServerRpc();
    }

    [ServerRpc]
    private void UseThirdEyeServerRpc()
    {
        if (!IsMyAbility()) return;
        if (myRole == null || !myRole.IsAlive) return;
        if (Time.time < serverCooldownEndTime) return;

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        // The cost: impaired vision for as long as the skill is cooling down.
        GetComponent<PlayerVision>()?.ServerApplyVisionReduction(impairedVisionMultiplier, cooldownDuration);

        Vector3[] points = GatherResiduePoints().ToArray();

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        RevealResidueClientRpc(points, revealDuration, targetOnlyOwner);

        Debug.Log($"[Server] Third Eye Sight used — revealing {points.Length} point(s).");
    }

    // Server-only: gathers every living Impostor's recent footprint samples,
    // plus every currently-active trap's position, regardless of who placed it —
    // but only those within revealRadius of the Third-Eye User's own position
    // right now. The skill shows what's nearby, not the Maligno's whole route
    // across the map.
    private List<Vector3> GatherResiduePoints()
    {
        List<Vector3> points = new List<Vector3>();
        Vector3 origin = transform.position;
        float radiusSqr = revealRadius * revealRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            FootprintTracker tracker = client.PlayerObject.GetComponent<FootprintTracker>();
            if (tracker == null) continue;

            foreach (var point in tracker.GetRecentFootprints(lookbackSeconds))
            {
                if ((point - origin).sqrMagnitude <= radiusSqr)
                {
                    points.Add(point);
                }
            }
        }

        foreach (var trap in NunoMoundTrap.ActiveTraps)
        {
            if (trap == null) continue;

            Vector3 trapPos = trap.transform.position;
            if ((trapPos - origin).sqrMagnitude <= radiusSqr)
            {
                points.Add(trapPos);
            }
        }

        return points;
    }

    // Targeted at this Third-Eye User's own owner only — the ability-cooldown
    // UI hooks in here, same as every other ability.
    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Third Eye Sight used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    // Also targeted only at the owner — rpcParams restricts delivery, but the
    // IsOwner check stays as a defensive double-check like the rest of the codebase.
    [ClientRpc]
    private void RevealResidueClientRpc(Vector3[] points, float duration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        ThirdEyeRevealUI.Instance?.ShowResidue(points, duration);
    }
}
