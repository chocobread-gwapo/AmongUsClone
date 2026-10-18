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

        targetRole.Kill();
        return true;
    }
}