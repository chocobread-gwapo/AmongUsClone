using Unity.Netcode;
using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : NetworkBehaviour
{
    // Server-authoritative, never directly synced — see ServerSetRole/ServerSetMalignoType
    private Role serverRole = Role.Crewmate;
    private MalignoType serverMalignoType = MalignoType.None;

    // Each client's own cached copy of ITS OWN role/type, set only via targeted ClientRpc
    private Role localCachedRole = Role.Crewmate;
    private MalignoType localCachedMalignoType = MalignoType.None;

    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Role CurrentRole => IsServer ? serverRole : localCachedRole;
    public MalignoType CurrentMalignoType => IsServer ? serverMalignoType : localCachedMalignoType;
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

    // Called ONLY by the server during role assignment (GameStartManager)
    public void ServerSetRole(Role role, MalignoType malignoType = MalignoType.None)
    {
        if (!IsServer) return;

        serverRole = role;
        serverMalignoType = malignoType;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };

        ReceiveRoleClientRpc(role, malignoType, targetOnlyOwner);

        // Enable the matching ability component, server-side, if one exists.
        // Each ability script checks CurrentMalignoType itself and no-ops if it doesn't match,
        // so it's safe to just leave all of them present on the prefab and let them self-gate.
    }

    [ClientRpc]
    private void ReceiveRoleClientRpc(Role role, MalignoType malignoType, ClientRpcParams rpcParams = default)
    {
        localCachedRole = role;
        localCachedMalignoType = malignoType;
        Debug.Log($"[Client] You are: {role}" + (role == Role.Impostor ? $" ({malignoType})" : ""));
    }

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