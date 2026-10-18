using Unity.Netcode;
using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : NetworkBehaviour
{
    // Server-owned, synced to everyone. Note: in a real game, you'd want
    // each player to ONLY see their own role (and maybe fellow impostors),
    // not everyone else's — that requires a more targeted sync approach
    // (e.g. ClientRpc targeted at one client). This version is simplified
    // for getting the core flow working first.
    private NetworkVariable<Role> currentRole = new NetworkVariable<Role>(
        Role.Crewmate,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Role CurrentRole => currentRole.Value;
    public bool IsImpostor => currentRole.Value == Role.Impostor;
    public bool IsAlive => isAlive.Value;

    public override void OnNetworkSpawn()
    {
        isAlive.OnValueChanged += HandleAliveChanged;
    }

    public override void OnNetworkDespawn()
    {
        isAlive.OnValueChanged -= HandleAliveChanged;
    }

    // Only the server should ever call this directly (e.g. from GameStartManager)
    public void ServerSetRole(Role role)
    {
        if (!IsServer) return;
        currentRole.Value = role;
    }

    public void Kill()
    {
        if (!IsServer) return; // only server applies kills
        if (!isAlive.Value) return;

        isAlive.Value = false;
        Debug.Log($"{gameObject.name} was eliminated.");

        // Movement should stop everywhere once dead
        var controller = GetComponent<PlayerController>();
        if (controller != null)
        {
            // Disabling directly here affects the server's copy; the owning
            // client's copy reacts via HandleAliveChanged below instead.
        }
    }

    private void HandleAliveChanged(bool previous, bool current)
    {
        if (!current)
        {
            // Runs on every client when a player dies, including the server
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0.3f);

            if (IsOwner)
            {
                var controller = GetComponent<PlayerController>();
                if (controller != null) controller.enabled = false;
            }
        }
    }
}