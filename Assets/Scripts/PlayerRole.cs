using Unity.Netcode;
using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : NetworkBehaviour
{
    // NOT a NetworkVariable anymore — this field only ever holds the
    // correct value on the SERVER. Other clients never receive it directly.
    private Role serverRole = Role.Crewmate;

    // Each client's own local cache of ITS OWN role, set only via the
    // targeted ClientRpc below. On any client that isn't the owner,
    // this stays at the default (Crewmate) and is never used for anything
    // meaningful, since IsOwner gates all the real gameplay checks anyway.
    private Role localCachedRole = Role.Crewmate;

    // isAlive stays a normal synced NetworkVariable — whether someone is
    // alive/dead is NOT secret info, everyone needs to see dead bodies etc.
    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Returns the correct value depending on who's asking:
    // - On the server, the authoritative serverRole is always correct.
    // - On a client, only the owner's cached copy is meaningful.
    public Role CurrentRole => IsServer ? serverRole : localCachedRole;
    public bool IsImpostor => CurrentRole == Role.Impostor;
    public bool IsAlive => isAlive.Value;

    public override void OnNetworkSpawn()
    {
        isAlive.OnValueChanged += HandleAliveChanged;
    }

    public override void OnNetworkDespawn()
    {
        isAlive.OnValueChanged -= HandleAliveChanged;
    }

    // Called ONLY by the server (e.g. from GameStartManager during role assignment).
    public void ServerSetRole(Role role)
    {
        if (!IsServer) return;

        serverRole = role;

        // Send this role ONLY to the client that owns this player object.
        // No one else — including the server's own broadcast — reaches other clients.
        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };

        ReceiveRoleClientRpc(role, targetOnlyOwner);
    }

    [ClientRpc]
    private void ReceiveRoleClientRpc(Role role, ClientRpcParams rpcParams = default)
    {
        // This method only actually executes on the targeted client (the owner).
        // Every other client never receives this call at all.
        localCachedRole = role;
        Debug.Log($"[Client] You are: {role}");
    }

    // Called server-side only (e.g. from Killable.TryKill())
    public void Kill()
    {
        if (!IsServer) return;
        if (!isAlive.Value) return;

        isAlive.Value = false;
        Debug.Log($"{gameObject.name} was eliminated.");
    }

    private void HandleAliveChanged(bool previous, bool current)
    {
        if (!current)
        {
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