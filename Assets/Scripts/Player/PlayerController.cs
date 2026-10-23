using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;

    [Header("Server-Side Movement Validation")]
    [Tooltip("The PRD's hybrid model: the owning client still moves itself locally for responsiveness, but the server periodically checks the resulting position against what should be physically possible, and snaps the client back if it isn't — a speed-hacked or wall-clipped client gets corrected rather than trusted.")]
    [SerializeField] private float validationInterval = 0.2f;
    [Tooltip("Multiplier of leeway on top of the theoretical max distance per check, to absorb normal network jitter/lag without false-flagging legitimate movement.")]
    [SerializeField] private float speedToleranceMultiplier = 1.5f;
    [Tooltip("Layer(s) a straight-line path between two validated positions is raycast against to catch wall-clipping. Assign whatever layer your Wall_Top/Bottom/Left/Right objects are on.")]
    [SerializeField] private LayerMask wallLayerMask;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private InputActionReference killAction;
    [SerializeField] private InputActionReference reportAction;
    [SerializeField] private InputActionReference abilityAction;

    [Header("Kill Settings")]
    [SerializeField] private float killRange = 1.2f;
    // Host-set game settings (Player Speed / Kill Distance) scale the Inspector values.
    private float MoveSpeed => moveSpeed * (GameStartManager.Instance != null ? GameStartManager.Instance.PlayerSpeed : 1f);
    private float KillRange => killRange * (GameStartManager.Instance != null ? GameStartManager.Instance.KillRangeScale : 1f);
    [Tooltip("Color of the kill-target outline glow (see TargetOutline.cs).")]
    [SerializeField] private Color killHighlightColor = Color.red;

    // Server-only — never exposed to clients directly. Falls back to 15s if
    // GameStartManager isn't found, so a kill is never silently unlimited.
    private float serverKillCooldownEndTime = -999f;

    // Client-side mirror (owner only) — purely a UX nicety so the Impostor's
    // own client stops spamming kill requests during cooldown. The server
    // check above is still the real authority either way.
    private float localKillCooldownEndTime = -999f;

    [Header("Report Settings")]
    [SerializeField] private float reportRange = 1.5f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;
    private Vector2 facingDirection = Vector2.down;

    // Networked so CharacterSpriteAnimator can play the right walk-cycle
    // frames for THIS player on every OTHER client, not just locally.
    // Owner-write (not server-write) because this is pure cosmetic input
    // state, not anti-cheat-relevant — same "owner drives it locally"
    // reasoning as moveInput/facingDirection above, just made visible to
    // everyone instead of staying owner-only.
    private NetworkVariable<Direction4> networkFacing = new NetworkVariable<Direction4>(
        Direction4.Down,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<bool> networkIsMoving = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public Direction4 NetworkFacing => networkFacing.Value;
    public bool NetworkIsMoving => networkIsMoving.Value;

    // ---- Ability-driven status effects ----
    // Synced to everyone on purpose: these are visible effects (the victim's own
    // movement looks/feels different to everyone watching), not secret information,
    // so a plain NetworkVariable is fine here — unlike role/MalignoType, which use
    // the targeted-ClientRpc privacy pattern instead.
    private NetworkVariable<bool> controlsInverted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> slowMultiplier = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public TaskZone CurrentTaskZone { get; set; }

    // ---- Server-side movement validation state (server-only, unused on clients) ----
    private Vector3 serverLastValidatedPosition;
    private float serverValidationTimer;

    // ---- Kill-target highlighting (local-only cosmetic, see TargetOutline.cs) ----
    // Nothing here is networked — each client decides for itself, every
    // frame, who ITS OWN local player can currently kill, and toggles the
    // glow directly on those targets' own TargetOutline components. Other
    // clients never run this for anyone but their own local player, so a
    // Villager never sees any glow at all, same as Among Us.
    private readonly HashSet<TargetOutline> highlightedKillTargets = new HashSet<TargetOutline>();
    private readonly List<TargetOutline> killTargetScratch = new List<TargetOutline>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Seed with wherever the player actually spawned, so the very
            // first check doesn't compare against (0,0,0) and false-flag a
            // "teleport" from the world origin.
            serverLastValidatedPosition = transform.position;
            serverValidationTimer = 0f;
        }

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        moveAction.action.Enable();
        interactAction.action.Enable();
        killAction.action.Enable();
        reportAction.action.Enable();
        abilityAction.action.Enable();

        interactAction.action.performed += OnInteract;
        killAction.action.performed += OnKillPressed;
        reportAction.action.performed += OnReportPressed;
        abilityAction.action.performed += OnAbilityPressed;

        StartCoroutine(HookUpCameraWhenReady());
    }

    // For the host specifically, OnNetworkSpawn fires immediately when
    // StartHost() runs — which, with Relay/Session matchmaking, is BEFORE
    // the host's own LoadScene("Game") call finishes swapping out of the
    // menu scene. A one-shot Camera.main lookup here would find the menu's
    // camera (no CameraFollow on it) and give up. Instead, keep checking
    // each frame until the real gameplay camera is loaded in and ready, so
    // this works regardless of exactly when the scene transition lands
    // relative to spawn (joining clients are usually already in the right
    // scene by spawn time, but this covers them too, harmlessly).
    private IEnumerator HookUpCameraWhenReady()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            CameraFollow cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cam != null)
            {
                cam.SetTarget(transform);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("Could not find a Main Camera with CameraFollow attached within 10 seconds of spawning.");
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        moveAction.action.Disable();
        interactAction.action.Disable();
        killAction.action.Disable();
        reportAction.action.Disable();
        abilityAction.action.Disable();

        interactAction.action.performed -= OnInteract;
        killAction.action.performed -= OnKillPressed;
        reportAction.action.performed -= OnReportPressed;
        abilityAction.action.performed -= OnAbilityPressed;
    }

    private void Update()
    {
        // Server-side validation runs independently of ownership — the host
        // validates every player's object, not just its own — so this check
        // comes BEFORE the owner-only early-out below, and has its own.
        if (IsServer)
        {
            ServerValidateMovement();
        }

        if (!IsOwner) return;

        // Runs every frame regardless of IsBlocked() below — it needs to be
        // able to CLEAR highlights the instant a meeting starts or this
        // player dies, not just stop adding new ones.
        UpdateKillTargetHighlighting();

        if (IsBlocked())
        {
            moveInput = Vector2.zero;
            // Still sync "not moving" so other clients don't see this
            // player frozen mid-walk-cycle for the rest of the meeting.
            if (networkIsMoving.Value) networkIsMoving.Value = false;
            return;
        }

        moveInput = moveAction.action.ReadValue<Vector2>();

        // Tikbalang's Liligawin: flip both axes while the victim is bewildered.
        if (controlsInverted.Value)
        {
            moveInput = -moveInput;
        }

        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        if (isMoving)
        {
            facingDirection = moveInput.normalized;
        }

        // Owner writes the networked facing/moving state every frame it
        // changes, so CharacterSpriteAnimator on OTHER clients can animate
        // this player correctly. NetworkVariable already no-ops/dedupes
        // when the value hasn't actually changed, so writing every frame
        // here is fine — it just compares, not re-broadcasts needlessly.
        Direction4 newFacing = DirectionFromVector(facingDirection);
        if (networkFacing.Value != newFacing) networkFacing.Value = newFacing;
        // Like Among Us: pushing into a wall/furniture cancels the walk
        // animation instead of running in place.
        bool animMoving = isMoving && !pushingAgainstObstacle;
        if (networkIsMoving.Value != animMoving) networkIsMoving.Value = animMoving;
    }

    // Set from FixedUpdate: true when the player is holding a direction but
    // isn't actually getting anywhere (blocked head-on by a collider).
    private bool pushingAgainstObstacle;
    private float progressRatio = 1f;
    private Vector2 lastFixedPos;
    private Vector2 lastAppliedVelocity;

    // Picks whichever axis the facing vector leans more strongly toward —
    // standard 4-direction sprite convention (no diagonal frames to pick between).
    private static Direction4 DirectionFromVector(Vector2 direction)
    {
        float ax = Mathf.Abs(direction.x), ay = Mathf.Abs(direction.y);
        // Within ~22.5 degrees of an axis -> that cardinal direction;
        // anywhere in between -> the matching diagonal.
        const float diagLow = 0.4142f, diagHigh = 2.4142f; // tan(22.5), tan(67.5)
        bool diagonal = ax > 0.0001f && ay > 0.0001f && ay / ax > diagLow && ay / ax < diagHigh;
        if (diagonal)
        {
            if (direction.y > 0f) return direction.x > 0f ? Direction4.UpRight : Direction4.UpLeft;
            return direction.x > 0f ? Direction4.DownRight : Direction4.DownLeft;
        }
        if (ax > ay)
        {
            return direction.x > 0f ? Direction4.Right : Direction4.Left;
        }
        return direction.y > 0f ? Direction4.Up : Direction4.Down;
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        if (IsBlocked())
        {
            rb.linearVelocity = Vector2.zero;
            pushingAgainstObstacle = false; progressRatio = 1f;
            lastFixedPos = rb.position; lastAppliedVelocity = Vector2.zero;
            return;
        }

        // Compare how far we really moved during the last step with how far
        // the velocity we applied should have taken us. Sliding along a wall
        // still counts as moving; only a head-on block counts as stuck.
        Vector2 moved = rb.position - lastFixedPos;
        lastFixedPos = rb.position;
        float expected = lastAppliedVelocity.magnitude * Time.fixedDeltaTime;
        if (expected > 0.0001f)
        {
            // Smoothed progress ratio (1 = moving freely, 0 = fully blocked).
            // Smoothing stops contact jitter from flickering the animation.
            float along = Mathf.Clamp(Vector2.Dot(moved, lastAppliedVelocity.normalized) / expected, 0f, 1f);
            progressRatio = Mathf.Lerp(progressRatio, along, 0.35f);
        }
        else progressRatio = 1f;
        pushingAgainstObstacle = progressRatio < 0.45f;

        // Duwende's Nuno mound trap: slowMultiplier drops below 1 while snared.
        Vector2 velocity = moveInput.normalized * MoveSpeed * slowMultiplier.Value;
        rb.linearVelocity = velocity;
        lastAppliedVelocity = velocity;
    }

    // Movement/actions are blocked during an active meeting OR once the match has ended.
    private bool IsBlocked()
    {
        bool meetingActive = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        bool gameOver = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        return meetingActive || gameOver || TaskMiniGame.IsOpen;
    }

    private void OnInteract(InputAction.CallbackContext context) => TryInteract();
    private void OnKillPressed(InputAction.CallbackContext context) => TryKill();
    private void OnReportPressed(InputAction.CallbackContext context) => TryReport();

    // ---- UI-button entry points ----
    // Wired to the on-screen REPORT/USE buttons' OnClick. Share the exact same
    // logic as the keyboard bindings above, so both input paths stay in sync
    // and nothing has to be duplicated.

    // Mirrors Among Us's single contextual "USE" button: kill takes priority
    // if a valid target is in range, otherwise it falls through to the
    // current task (if standing in one). Nothing happens if neither applies.
    public void OnUseButtonPressed()
    {
        var myRole = GetComponent<PlayerRole>();
        bool canKill = myRole != null && myRole.IsImpostor && myRole.IsAlive;

        if (canKill && HasValidKillTargetInRange())
        {
            TryKill();
        }
        else
        {
            TryInteract();
        }
    }

    public void OnReportButtonPressed() => TryReport();

    // Dedicated KILL button — unlike OnUseButtonPressed() (which falls back
    // to interacting with a task if no kill target is in range), this always
    // attempts a kill and does nothing otherwise. TryKill() already handles
    // every safety check (IsImpostor, IsAlive, cooldown, range), so this is
    // safe to wire up even though a Survivor pressing it will just no-op.
    public void OnKillButtonPressed() => TryKill();

    // Among-Us-style white glow on anyone this player can currently kill.
    // Re-evaluated every frame (cheap: one OverlapCircleAll + a HashSet
    // diff, same OverlapCircleAll already used for the real kill check) so
    // the glow appears/disappears the instant a target steps in/out of
    // range, dies, becomes a fellow Maligno via Aswang's disguise, etc.
    private PlayerRole cachedRole;
    private readonly Collider2D[] killHitBuffer = new Collider2D[16];
    private readonly List<TargetOutline> staleHighlights = new List<TargetOutline>();

    private void UpdateKillTargetHighlighting()
    {
        if (cachedRole == null) cachedRole = GetComponent<PlayerRole>();
        var myRole = cachedRole;
        bool canKillNow = myRole != null && myRole.IsImpostor && myRole.IsAlive
            && Time.time >= localKillCooldownEndTime && !IsBlocked();

        killTargetScratch.Clear();
        if (canKillNow)
        {
            // Non-allocating overlap into a reused buffer (the old OverlapCircleAll allocated every frame).
            int count = Physics2D.OverlapCircle(transform.position, KillRange, new ContactFilter2D().NoFilter(), killHitBuffer);
            for (int i = 0; i < count; i++)
            {
                var hit = killHitBuffer[i];
                if (hit == null || hit.gameObject == gameObject) continue;
                if (hit.GetComponent<Killable>() == null) continue;

                PlayerRole hitRole = hit.GetComponent<PlayerRole>();
                if (hitRole != null && hitRole.IsImpostor) continue;

                TargetOutline outline = hit.GetComponent<TargetOutline>();
                if (outline != null) killTargetScratch.Add(outline);
            }
        }

        // Turn off anything highlighted last frame that isn't valid anymore.
        staleHighlights.Clear();
        foreach (var outline in highlightedKillTargets)
        {
            if (outline == null || !killTargetScratch.Contains(outline)) staleHighlights.Add(outline);
        }
        for (int i = 0; i < staleHighlights.Count; i++)
        {
            var outline = staleHighlights[i];
            highlightedKillTargets.Remove(outline);
            if (outline != null) outline.SetHighlighted(false);
        }

        // Turn on anything newly valid.
        foreach (var outline in killTargetScratch)
        {
            if (highlightedKillTargets.Add(outline))
            {
                outline.SetHighlighted(true, killHighlightColor);
            }
        }
    }

    private bool HasValidKillTargetInRange()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, KillRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (hit.GetComponent<Killable>() == null) continue;

            PlayerRole hitRole = hit.GetComponent<PlayerRole>();
            if (hitRole != null && hitRole.IsImpostor) continue;

            return true;
        }
        return false;
    }

    private void TryInteract()
    {
        if (!IsOwner || IsBlocked()) return;

        if (CurrentTaskZone != null)
        {
            CurrentTaskZone.TryComplete();
            return;
        }

        // Standing at the emergency table: USE / the interact key calls an emergency meeting
        // (the server enforces alive, cooldown and the per-player limit).
        if (EmergencyUI.NearTable)
        {
            var role = GetComponent<PlayerRole>();
            if (role != null && role.IsAlive) MeetingManager.Instance?.RequestCallMeetingServerRpc();
        }
    }

    private void TryKill()
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        if (Time.time < localKillCooldownEndTime) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, KillRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Killable target = hit.GetComponent<Killable>();
            if (target == null) continue;

            // Maligno don't kill each other — skip fellow Impostors and keep
            // looking for a valid Crewmate target instead.
            PlayerRole hitRole = hit.GetComponent<PlayerRole>();
            if (hitRole != null && hitRole.IsImpostor) continue;

            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();
            if (targetNetObj == null) continue;

            // KILL is now a normal kill for every Maligno type, Bal-bal
            // included — Bal-bal's "eat the body" (banana-trunk decoy, no
            // reportable corpse) only happens via the ABILITY button now
            // (see BalBalAbility.TryActivate()).
            RequestKillServerRpc(targetNetObj.NetworkObjectId);

            break;
        }
    }

    private void TryReport()
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsAlive) return;

        // Stationary dead-body markers have no collider (purely a visual +
        // networked state), so they're found by distance against every
        // spawned DeadBody, not via Physics2D overlap.
        foreach (var spawned in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
        {
            if (spawned == null) continue;

            DeadBody deadBody = spawned.GetComponent<DeadBody>();
            if (deadBody == null) continue;

            if (Vector2.Distance(transform.position, spawned.transform.position) <= reportRange)
            {
                MeetingManager.Instance.RequestReportBodyServerRpc(spawned.NetworkObjectId);
                return;
            }
        }

        // Fallback: a dead player's own collider directly. Shouldn't
        // normally trigger (Bal-Bal victims and ejections never spawn a
        // DeadBody anyway and already leave no trace), kept as a safety
        // net — same client-side UX nicety pattern as before;
        // MeetingManager's server-side check is the real authority either way.
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, reportRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();
            if (targetNetObj == null) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            if (targetRole != null && !targetRole.IsAlive && !targetRole.IsBalBalConsumed)
            {
                MeetingManager.Instance.RequestReportBodyServerRpc(targetNetObj.NetworkObjectId);
                break;
            }
        }
    }

    // Shared "Ability" button for the four non-kill Maligno abilities.
    // Only the ability component matching this player's assigned MalignoType
    // will actually do anything — each one gates itself via IsMyAbility(),
    // the same pattern BalBalAbility already uses. Bal-bal is excluded here
    // since "eating the body" is wired through the kill button instead.
    private void OnAbilityPressed(InputAction.CallbackContext context) => TryUseAbility();

    // On-screen ABILITY button entry point — same bridge pattern as
    // OnUseButtonPressed/OnReportButtonPressed, so the keyboard binding and
    // the UI button both funnel through the exact same logic.
    public void OnAbilityButtonPressed() => TryUseAbility();

    private void TryUseAbility()
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsAlive) return;

        // Shared by both Maligno abilities AND Survivor Classes now — each
        // ability component still gates itself (IsMyAbility()), so calling
        // all of them is safe; only the one matching this player's actual
        // role/class does anything.
        bool hasAnyAbility = myRole.IsImpostor || myRole.CurrentSurvivorClass != SurvivorClass.None;
        if (!hasAnyAbility) return;

        GetComponent<TikbalangAbility>()?.TryActivate();
        GetComponent<DuwendeAbility>()?.TryActivate();
        GetComponent<AswangAbility>()?.TryActivate();
        GetComponent<KapreAbility>()?.TryActivate();
        GetComponent<ThirdEyeAbility>()?.TryActivate();
        GetComponent<ReikiAbility>()?.TryActivate();
        GetComponent<AlbularyoAbility>()?.TryActivate();
        GetComponent<TarotUserAbility>()?.TryActivate();

        // Bal-bal's "eat the body" now lives here instead of the KILL
        // button — the KILL button is a normal kill for every Maligno,
        // Bal-bal included.
        GetComponent<BalBalAbility>()?.TryActivate();
    }

    [ServerRpc]
    private void RequestKillServerRpc(ulong targetNetworkObjectId)
    {
        if (ServerIsKillOnCooldown()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
        {
            // Server-authoritative check — a modified client could otherwise
            // request a kill on anyone. Maligno never kill fellow Maligno.
            PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
            if (targetRole != null && targetRole.IsImpostor) return;

            // Captured before TryKill() in case anything about the victim's
            // transform ever changes as a side effect of dying — right now
            // it doesn't (only isAlive flips, DeadBody spawns separately),
            // but reading it first costs nothing and stays correct either way.
            Vector3 victimPosition = targetObj.transform.position;

            // SERVER-AUTHORITATIVE RANGE CHECK — this was missing entirely.
            // PlayerController's client-side OverlapCircleAll in TryKill() is
            // only a UX nicety (keeps a legit client from even sending a
            // request when clearly out of range); it does nothing to stop a
            // modified client from sending RequestKillServerRpc with any
            // target's NetworkObjectId regardless of actual distance. This is
            // the real gate. A small tolerance is added on top of killRange
            // to absorb the same kind of network/position staleness the
            // movement validator already tolerates (speedToleranceMultiplier),
            // not to be generous about cheating.
            float distanceToVictim = Vector2.Distance(transform.position, victimPosition);
            float maxKillDistance = KillRange * speedToleranceMultiplier;
            if (distanceToVictim > maxKillDistance)
            {
                Debug.LogWarning($"[Server] Rejected kill request from {gameObject.name}: " +
                    $"{distanceToVictim:F2} units from target (max {maxKillDistance:F2}).");
                return;
            }

            Killable target = targetObj.GetComponent<Killable>();
            if (target != null && target.TryKill())
            {
                ServerStartKillCooldown();
                ServerTeleportTo(victimPosition);
            }
        }
    }

    // Among Us-style kill: the killer ends up standing exactly where the
    // victim was. Same two-part pattern as the movement-correction code
    // below (CorrectPositionClientRpc) — the server sets the authoritative
    // position directly, which syncs out to every other client on its own,
    // but the owner's own client needs the explicit targeted RPC to apply
    // it locally too.
    public void ServerTeleportTo(Vector3 position)
    {
        transform.position = position;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // Without this, the next movement-validation tick would see a huge
        // jump from the pre-kill position to here and flag it as a speed
        // violation, snapping the killer straight back — silently undoing
        // the teleport a frame later.
        serverLastValidatedPosition = position;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        TeleportClientRpc(position, targetOnlyOwner);
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 position, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        transform.position = position;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        moveInput = Vector2.zero;
    }

    // ---- Kill cooldown — shared by the normal kill path above AND
    // BalBalAbility's "consume the body" path, since both are still "a kill". ----

    public bool ServerIsKillOnCooldown()
    {
        return Time.time < serverKillCooldownEndTime;
    }

    // Time.time keeps counting up for the whole play session, not just one
    // match — without this, a kill cooldown started near the end of a match
    // would still be "in the future" at the start of the next one, silently
    // blocking the first kill/ability of a fresh game. Resetting the server
    // field alone isn't enough: localKillCooldownEndTime (the client-side
    // pre-check in TryKill()) only ever lives on each owning client, so it
    // needs its own targeted RPC the same way starting a cooldown does.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverKillCooldownEndTime = -999f;

        ClientRpcParams targetOnlyOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        NotifyKillCooldownResetClientRpc(targetOnlyOwner);
    }

    [ClientRpc]
    private void NotifyKillCooldownResetClientRpc(ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        localKillCooldownEndTime = -999f;
    }

    // Called by MeetingManager once a meeting resolves, so a kill cooldown
    // that was already ticking down when the meeting started doesn't lose
    // that time. Only pushes it forward if it's actually still active right
    // now — a player with no cooldown running is left untouched.
    public void ServerExtendKillCooldownIfActive(float seconds)
    {
        if (!IsServer) return;
        if (Time.time < serverKillCooldownEndTime)
        {
            serverKillCooldownEndTime += seconds;
        }
    }

    public void ServerStartKillCooldown()
    {
        if (!IsServer) return;

        float duration = GameStartManager.Instance != null ? GameStartManager.Instance.KillCooldownDuration : 15f;
        serverKillCooldownEndTime = Time.time + duration;
        NotifyKillCooldownStartedClientRpc(duration);
    }

    // Targeted at this Impostor's own owner only — the cooldown UI (once built)
    // hooks in here. Doesn't leak anything to other clients.
    [ClientRpc]
    private void NotifyKillCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        localKillCooldownEndTime = Time.time + duration;
        Debug.Log($"[Client] Kill used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartKillCooldown(duration);

        // Bal-bal's ABILITY button IS the kill (eat the body) — it shares
        // this exact clock, so dim it too. Every other Maligno type's
        // ability is unrelated to killing and runs its own separate
        // cooldown (started from that ability's own TryActivate/ServerRpc),
        // so this must stay scoped to Bal-bal only.
        var myRole = GetComponent<PlayerRole>();
        if (myRole != null && myRole.CurrentMalignoType == MalignoType.BalBal)
        {
            AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
        }
    }

    // ---- Server-side movement validation (the PRD's hybrid anti-cheat model) ----
    // The owning client still moves itself every frame for responsiveness —
    // nothing changes about movement FEEL. Periodically (every
    // validationInterval seconds), the server checks the distance actually
    // covered against the theoretical max for this player's CURRENT status
    // (moveSpeed * slowMultiplier — this already accounts for Duwende/Kapre
    // slows AND Tarot's Fool speed boost, since both write through the same
    // NetworkVariable), with a tolerance multiplier to absorb normal network
    // jitter. It also raycasts the straight-line path against wallLayerMask
    // to catch wall-clipping. Either violation snaps the offending client
    // back to its last validated position via a targeted ClientRpc.
    private Collider2D feetCollider;

    private void ServerValidateMovement()
    {
        serverValidationTimer += Time.deltaTime;
        if (serverValidationTimer < validationInterval) return;

        float elapsed = serverValidationTimer;
        serverValidationTimer = 0f;

        Vector3 currentPosition = transform.position;
        float distanceMoved = Vector2.Distance(serverLastValidatedPosition, currentPosition);

        float maxAllowedSpeed = MoveSpeed * slowMultiplier.Value * speedToleranceMultiplier;
        float maxAllowedDistance = maxAllowedSpeed * elapsed;

        bool speedViolation = distanceMoved > maxAllowedDistance;

        bool wallViolation = false;
        // Ghosts may pass through inner walls and furniture (only the map's outer boundary,
        // GhostBounds, stops them), so the wall-clip raycast does not apply to them.
        var validatedRole = GetComponent<PlayerRole>();
        bool isGhost = validatedRole != null && !validatedRole.IsAlive;

        if (!speedViolation && !isGhost && distanceMoved > 0.001f)
        {
            Vector2 direction = (currentPosition - serverLastValidatedPosition).normalized;

            // Cast from where the player's FEET are, not the sprite pivot: the pivot sits
            // ~0.55 units above the feet collider, so a ray from it ran through tables and
            // walls the player was legitimately sliding along and caused false "wall-clip"
            // snap-backs (the jitter near furniture).
            Vector2 feetOffset = Vector2.zero;
            if (feetCollider == null) feetCollider = GetComponent<Collider2D>();
            if (feetCollider != null) feetOffset = (Vector2)feetCollider.bounds.center - (Vector2)transform.position;

            RaycastHit2D hit = Physics2D.Raycast((Vector2)serverLastValidatedPosition + feetOffset, direction, distanceMoved, wallLayerMask);
            wallViolation = hit.collider != null;
        }

        if (speedViolation || wallViolation)
        {
            Debug.LogWarning($"[Server] Movement violation for {gameObject.name}: " +
                $"{(speedViolation ? $"moved {distanceMoved:F2} units in {elapsed:F2}s (max {maxAllowedDistance:F2})" : "wall-clip detected")}. Correcting.");

            transform.position = serverLastValidatedPosition;
            if (rb != null) rb.linearVelocity = Vector2.zero;

            CorrectPositionClientRpc(serverLastValidatedPosition);
            // lastValidatedPosition stays where it was — don't advance it to
            // the rejected position.
        }
        else
        {
            serverLastValidatedPosition = currentPosition;
        }
    }

    // Targeted at the offending client only — everyone else already sees the
    // server's authoritative (reverted) position via the owner's own
    // NetworkTransform re-syncing once its transform.position is forced back.
    [ClientRpc]
    private void CorrectPositionClientRpc(Vector3 correctedPosition, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        transform.position = correctedPosition;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        moveInput = Vector2.zero;

        Debug.LogWarning("[Client] Movement corrected by server (speed or wall-clip check failed).");
    }

    // ---- Called by ability scripts (server-side only) to apply status effects ----

    public void ServerSetControlsInverted(bool value)
    {
        if (!IsServer) return;
        controlsInverted.Value = value;
    }

    // Slows stack multiplicatively; each expires on its own timer.
    private readonly System.Collections.Generic.List<(float mult, float end, string tag)> slowEffects =
        new System.Collections.Generic.List<(float, float, string)>();

    // tag lets one source (e.g. the curse) later remove only its own slow.
    public void ServerApplySlow(float multiplier, float duration, string tag = null)
    {
        if (!IsServer) return;
        slowEffects.Add((multiplier, Time.time + duration, tag));
        RecomputeSlow();
        StartCoroutine(ExpireSlowAfter(duration));
    }

    // Called by CurseStatus.ServerCure() (Albularyo's heal) to end all slows early.
    public void ServerClearSlow(string tag = null)
    {
        if (!IsServer) return;
        if (tag == null) slowEffects.Clear(); else slowEffects.RemoveAll(e => e.tag == tag);
        RecomputeSlow();
    }

    // Coroutine (not Update) because this component is disabled on non-owned instances.
    private IEnumerator ExpireSlowAfter(float duration)
    {
        yield return new WaitForSeconds(duration + 0.02f);
        TickSlowEffects();
    }

    private void TickSlowEffects()
    {
        if (!IsServer || slowEffects.Count == 0) return;
        if (slowEffects.RemoveAll(e => Time.time >= e.end) > 0) RecomputeSlow();
    }

    private void RecomputeSlow()
    {
        float m = 1f;
        foreach (var e in slowEffects) m *= e.mult;
        if (!Mathf.Approximately(slowMultiplier.Value, m)) slowMultiplier.Value = m;
    }

    public Vector2 FacingDirection => facingDirection;
}