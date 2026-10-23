using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Killable : NetworkBehaviour
{
    private PlayerRole targetRole;

    private void Awake()
    {
        targetRole = GetComponent<PlayerRole>();
        if (targetRole == null)
        {
            Debug.LogWarning($"{gameObject.name} has Killable but no PlayerRole component.");
        }
    }

    // Only ever called server-side (from PlayerController's RequestKillServerRpc)
    public bool TryKill()
    {
        if (!IsServer) return false;
        if (targetRole == null || !targetRole.IsAlive) return false;

        // Only the normal kill-button path leaves a reportable body behind —
        // meeting ejections and Bal-Bal consumption call PlayerRole.Kill()
        // directly elsewhere and intentionally leave spawnBody at its
        // default (false).
        targetRole.Kill(spawnBody: true);
        KillSfxClientRpc(transform.position);
        return true;
    }

    [ClientRpc]
    private void KillSfxClientRpc(Vector3 pos)
    {
        Sfx.PlayAt("kill", pos);
    }
}