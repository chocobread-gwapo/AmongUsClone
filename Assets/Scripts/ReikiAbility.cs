using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Reiki Practitioner — Survivor Class "Aura Sense"
// Deliberately weaker/vaguer than Third-Eye Sight: reveals a JITTERED sense
// of where living Maligno currently are (not their exact position, not their
// recent path), for a short duration, with no names/colors attached. Shorter
// cooldown than Third-Eye since the information is worth less.
//
// Same shape as every other ability script: sits on every Player prefab,
// gates itself via IsMyAbility(), shares PlayerController's Ability input/button.
public class ReikiAbility : NetworkBehaviour
{
    [Header("Aura Sense")]
    [SerializeField] private float revealDuration = 5f;
    [SerializeField] private float cooldownDuration = 22f;

    [Tooltip("Each revealed aura is offset from the real position by up to this many units, in a random direction — the whole point is that it's NOT precise.")]
    [SerializeField] private float jitterRadius = 2.5f;

    private PlayerRole myRole;

    // Server-only.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentSurvivorClass == SurvivorClass.ReikiPractitioner;
    }

    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;
        UseReikiServerRpc();
    }

    [ServerRpc]
    private void UseReikiServerRpc()
    {
        if (!IsMyAbility()) return;
        if (myRole == null || !myRole.IsAlive) return;
        if (Time.time < serverCooldownEndTime) return;

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        Vector3[] auraPoints = GatherJitteredAuras();

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        RevealAurasClientRpc(auraPoints, revealDuration, targetOnlyOwner);

        Debug.Log($"[Server] Aura Sense used — revealing {auraPoints.Length} fuzzy aura(s).");
    }

    // Server-only: every living Maligno's real position, each independently
    // jittered so the result is a rough sense, not a pinpoint.
    private Vector3[] GatherJitteredAuras()
    {
        List<Vector3> auras = new List<Vector3>();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            PlayerRole targetRole = client.PlayerObject.GetComponent<PlayerRole>();
            if (targetRole == null || !targetRole.IsImpostor || !targetRole.IsAlive) continue;

            Vector2 jitter = Random.insideUnitCircle * jitterRadius;
            Vector3 realPos = client.PlayerObject.transform.position;
            auras.Add(realPos + new Vector3(jitter.x, jitter.y, 0f));
        }

        return auras.ToArray();
    }

    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Aura Sense used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    [ClientRpc]
    private void RevealAurasClientRpc(Vector3[] points, float duration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        ReikiRevealUI.Instance?.ShowAuras(points, duration);
    }
}