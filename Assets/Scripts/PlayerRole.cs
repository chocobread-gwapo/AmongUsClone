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