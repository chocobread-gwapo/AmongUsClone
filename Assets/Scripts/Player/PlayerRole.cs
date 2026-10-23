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

    [Header("Dead Body")]
    [Tooltip("Prefab with a NetworkObject + a trigger Collider2D + the dead-body sprite + a DeadBody component. Spawned at this player's exact position on a normal kill so living players can see and report it (ghosts keep moving after death, so the body can't just be this player's own object). Leave empty to skip spawning a body (that's already the case for Bal-Bal victims and meeting ejections, which never call Kill() with spawnBody: true). Just needs to exist anywhere in the project as a prefab — the NetworkManager's prefab list auto-picks it up since Is Default is checked.")]
    [SerializeField] private GameObject deadBodyPrefab;

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

        // true here (a real assignment) vs. false from ServerResetForNewGame()
        // below (a reset back to Crewmate) — see that method for why this
        // flag exists.
        ReceiveRoleClientRpc(role, malignoType, true, targetOnlyOwner);
    }

    [ClientRpc]
    private void ReceiveRoleClientRpc(Role role, MalignoType malignoType, bool showReveal, ClientRpcParams rpcParams = default)
    {
        localCachedRole = role;
        localCachedMalignoType = malignoType;
        Debug.Log($"[Client] You are: {role}" + (role == Role.Impostor ? $" ({malignoType})" : ""));

        // Owner-only RPC, so this only ever runs on the client this exact
        // player object belongs to — safe to directly show/hide this
        // client's own KILL button based on the role they were just told.
        // Also runs on ServerResetForNewGame() (which calls this with
        // Role.Crewmate), so the button correctly hides again on return to
        // lobby without any extra code there.
        KillButton.Instance?.SetVisible(role == Role.Impostor);
        UpdateAbilityButtonVisibility();

        // showReveal is false on a lobby reset — only a REAL assignment at
        // match start should pop the "you are X" screen.
        if (showReveal)
        {
            Sprite portrait = GetComponent<CharacterSpriteAnimator>()?.GetPortraitSprite();
            RoleRevealUI.Instance?.ShowRole(role == Role.Impostor, malignoType, portrait);
        }
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

        ReceiveSurvivorClassClientRpc(survivorClass, true, targetOnlyOwner);
    }

    [ClientRpc]
    private void ReceiveSurvivorClassClientRpc(SurvivorClass survivorClass, bool showReveal, ClientRpcParams rpcParams = default)
    {
        localCachedSurvivorClass = survivorClass;
        if (survivorClass != SurvivorClass.None)
        {
            Debug.Log($"[Client] Your Survivor Class: {survivorClass}");
        }

        UpdateAbilityButtonVisibility();

        // Same reasoning as ReceiveRoleClientRpc's showReveal — only a real
        // assignment should extend the reveal panel, not a lobby reset.
        if (showReveal)
        {
            RoleRevealUI.Instance?.AppendSurvivorClass(survivorClass);
        }
    }

    // Shown for anything actually wired through PlayerController.
    // TryUseAbility() — every Maligno type (Impostor), and every Survivor
    // Class EXCEPT Babaylan, whose "Judge Skill" is meeting-only and goes
    // through MeetingUI's own Judge Mode toggle instead of this button.
    // Role and Survivor Class arrive via two SEPARATE RPCs that can land in
    // either order, so this re-evaluates from both cached fields every time
    // either one updates, rather than assuming which one fired most recently.
    private void UpdateAbilityButtonVisibility()
    {
        bool show = isAlive.Value && (localCachedRole == Role.Impostor
            || (localCachedSurvivorClass != SurvivorClass.None && localCachedSurvivorClass != SurvivorClass.Babaylan));
        AbilityButton.Instance?.SetVisible(show);
    }

    // spawnBody defaults to false so every EXISTING call site (meeting
    // ejection, Babaylan's judgment, BalBalAbility's own Kill() call) keeps
    // behaving exactly as before — no trace left behind. Only Killable.Try
    // Kill() (the normal kill-button path) passes true.
    public void Kill(bool spawnBody = false)
    {
        if (!IsServer) return;
        if (!isAlive.Value) return;

        isAlive.Value = false;
        Debug.Log($"{gameObject.name} was eliminated.");

        if (spawnBody && deadBodyPrefab != null)
        {
            ServerSpawnDeadBody();
        }

        // NEW — a death (from a direct kill OR a meeting ejection, since both
        // call this same method) can end the match, so check right after.
        GameResultManager.Instance?.CheckWinConditions();
    }

    private void ServerSpawnDeadBody()
    {
        GameObject bodyInstance = Instantiate(deadBodyPrefab, transform.position, Quaternion.identity);
        NetworkObject bodyNetObj = bodyInstance.GetComponent<NetworkObject>();
        if (bodyNetObj == null)
        {
            Debug.LogWarning("[PlayerRole] deadBodyPrefab has no NetworkObject component — destroying the unspawned instance.");
            Destroy(bodyInstance);
            return;
        }

        bodyNetObj.Spawn();

        DeadBody deadBody = bodyInstance.GetComponent<DeadBody>();
        if (deadBody != null)
        {
            deadBody.ServerInitialize(NetworkObjectId);
        }
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
        GetComponent<CurseStatus>()?.ServerReset();

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        ReceiveRoleClientRpc(Role.Crewmate, MalignoType.None, false, targetOnlyOwner);
        ReceiveSurvivorClassClientRpc(SurvivorClass.None, false, targetOnlyOwner);
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

        // The local player's own HUD: a ghost has no KILL or ABILITY button.
        if (IsOwner)
        {
            KillButton.Instance?.SetVisible(current && CurrentRole == Role.Impostor);
            UpdateAbilityButtonVisibility();
        }

        if (!current)
        {
            if (previous && IsOwner) Sfx.Play2D("ghost");
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