using Unity.Netcode;
using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : NetworkBehaviour
{
    private Role serverRole = Role.Crewmate;
    private MalignoType serverMalignoType = MalignoType.None;

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

        // NEW — a death (from a direct kill OR a meeting ejection, since both
        // call this same method) can end the match, so check right after.
        GameResultManager.Instance?.CheckWinConditions();
    }

    // Called by GameStartManager.HostReturnToLobby() for every player, server-side
    // only, so a fresh match starts with everyone alive, unrevealed, and back
    // under Crewmate colors until the next AssignRoles() picks new Impostors.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;

        serverRole = Role.Crewmate;
        serverMalignoType = MalignoType.None;

        // Only actually triggers HandleAliveChanged if this player was dead —
        // a player who survived the round was already true, so nothing to undo.
        isAlive.Value = true;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveRoleClientRpc(Role.Crewmate, MalignoType.None, targetOnlyOwner);
    }

    private void HandleAliveChanged(bool previous, bool current)
    {
        var rb = GetComponent<Rigidbody2D>();
        var sr = GetComponent<SpriteRenderer>();

        if (!current)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (sr != null) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0.3f);

            if (IsOwner)
            {
                var controller = GetComponent<PlayerController>();
                if (controller != null) controller.enabled = false;
            }
        }
        else
        {
            // Revived for a fresh match (ServerResetForNewGame) — undo the fade
            // and re-enable the owner's controls.
            if (sr != null) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f);

            if (IsOwner)
            {
                var controller = GetComponent<PlayerController>();
                if (controller != null) controller.enabled = true;
            }
        }
    }
}