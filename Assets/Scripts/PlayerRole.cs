using Unity.Netcode;
using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : NetworkBehaviour
{
    [Tooltip("Physics2D layer a dead player's GameObject is moved to. This layer's collision matrix (Project Settings > Physics 2D) must be set to NOT collide with the normal Player layer, so ghosts pass through living players, but MUST still collide with whatever layer your walls are on, so ghosts can't walk through walls.")]
    [SerializeField] private string ghostLayerName = "Ghost";

    private Role serverRole = Role.Crewmate;
    private MalignoType serverMalignoType = MalignoType.None;
    private SurvivorClass serverSurvivorClass = SurvivorClass.None;

    private int originalLayer;
    private bool originalLayerCaptured = false;

    private Role localCachedRole = Role.Crewmate;
    private MalignoType localCachedMalignoType = MalignoType.None;
    private SurvivorClass localCachedSurvivorClass = SurvivorClass.None;

    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Bal-bal's gimmick: the victim is replaced by a convincing banana-trunk
    // decoy, so the real body should leave no trace at all — never
    // reportable, not even by someone standing right on top of its (now
    // invisible, thanks to GhostVisibility) corpse. Everyone-read/Server-
    // write like isAlive, since MeetingManager's server-side report check
    // needs to read this for any player.
    private NetworkVariable<bool> isBalBalConsumed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsBalBalConsumed => isBalBalConsumed.Value;

    public void ServerMarkBalBalConsumed()
    {
        if (!IsServer) return;
        isBalBalConsumed.Value = true;
    }

    public Role CurrentRole => IsServer ? serverRole : localCachedRole;
    public MalignoType CurrentMalignoType => IsServer ? serverMalignoType : localCachedMalignoType;
    public SurvivorClass CurrentSurvivorClass => IsServer ? serverSurvivorClass : localCachedSurvivorClass;
    public bool IsImpostor => CurrentRole == Role.Impostor;
    public bool IsAlive => isAlive.Value;

    private void Awake()
    {
        originalLayer = gameObject.layer;
        originalLayerCaptured = true;
    }

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

    // Survivor Classes are Crewmate-only and just as private as MalignoType —
    // same targeted-ClientRpc pattern, separate call since it's set
    // independently of the base role (assigned after Impostors are picked).
    public void ServerSetSurvivorClass(SurvivorClass survivorClass)
    {
        if (!IsServer) return;

        serverSurvivorClass = survivorClass;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };

        ReceiveSurvivorClassClientRpc(survivorClass, targetOnlyOwner);
    }

    [ClientRpc]
    private void ReceiveSurvivorClassClientRpc(SurvivorClass survivorClass, ClientRpcParams rpcParams = default)
    {
        localCachedSurvivorClass = survivorClass;
        if (survivorClass != SurvivorClass.None)
        {
            Debug.Log($"[Client] Your Survivor Class: {survivorClass}");
        }
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
        serverSurvivorClass = SurvivorClass.None;

        // Only actually triggers HandleAliveChanged if this player was dead —
        // a player who survived the round was already true, so nothing to undo.
        isAlive.Value = true;
        isBalBalConsumed.Value = false;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveRoleClientRpc(Role.Crewmate, MalignoType.None, targetOnlyOwner);
        ReceiveSurvivorClassClientRpc(SurvivorClass.None, targetOnlyOwner);
    }

    // Ghost/Spirit state: a dead player keeps moving and can still finish
    // tasks (to help the living win) — they're blocked from meetings,
    // reporting, and abilities individually (see PlayerController, Meeting-
    // Manager, and each ability's own IsAlive check), not by disabling
    // movement entirely. Sprite visibility (hidden from the living, visible
    // to other ghosts) is handled continuously by GhostVisibility, not here,
    // since that depends on the LOCAL VIEWER's alive state too, which can
    // change independently of this player's own state.
    //
    // Physical blocking is handled via Physics2D LAYERS, not isTrigger.
    // isTrigger is wrong here: it disables physical collision against
    // EVERYTHING (walls included), not just other players, because Unity
    // 2D physics only produces a blocking response when BOTH colliders are
    // non-trigger. A dedicated "Ghost" layer, with its collision matrix set
    // to skip Ghost<->Player but keep Ghost<->Wall (Project Settings >
    // Physics 2D), gives exactly the selective behavior we want: ghosts
    // walk through living players but still can't walk through walls.
    private void HandleAliveChanged(bool previous, bool current)
    {
        var rb = GetComponent<Rigidbody2D>();

        if (!current)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;

            if (!originalLayerCaptured)
            {
                originalLayer = gameObject.layer;
                originalLayerCaptured = true;
            }

            int ghostLayer = LayerMask.NameToLayer(ghostLayerName);
            if (ghostLayer == -1)
            {
                Debug.LogWarning($"[PlayerRole] No layer named \"{ghostLayerName}\" exists. " +
                    "Create it under Edit > Project Settings > Tags and Layers, then uncheck " +
                    "Ghost x Player (keep Ghost x Wall/Default checked) in Edit > Project Settings > Physics 2D. " +
                    "Staying on the original layer for now, so ghosts will still block living players.");
            }
            else
            {
                gameObject.layer = ghostLayer;
            }
        }
        else
        {
            // Revived for a fresh match (ServerResetForNewGame) — restore original layer.
            if (originalLayerCaptured) gameObject.layer = originalLayer;
        }
    }
}