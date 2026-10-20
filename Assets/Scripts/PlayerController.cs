using System.Collections;
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

        if (IsBlocked())
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = moveAction.action.ReadValue<Vector2>();

        // Tikbalang's Liligawin: flip both axes while the victim is bewildered.
        if (controlsInverted.Value)
        {
            moveInput = -moveInput;
        }

        if (moveInput.sqrMagnitude > 0.01f)
        {
            facingDirection = moveInput.normalized;
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        if (IsBlocked())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Duwende's Nuno mound trap: slowMultiplier drops below 1 while snared.
        Vector2 velocity = moveInput.normalized * moveSpeed * slowMultiplier.Value;
        rb.linearVelocity = velocity;
    }

    // Movement/actions are blocked during an active meeting OR once the match has ended.
    private bool IsBlocked()
    {
        bool meetingActive = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        bool gameOver = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        return meetingActive || gameOver;
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

    private bool HasValidKillTargetInRange()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, killRange);
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
        }
    }

    private void TryKill()
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        if (Time.time < localKillCooldownEndTime) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, killRange);
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

            // Branch: Bal-bal consumes the body (no reportable corpse).
            // Every other Maligno type uses the normal kill flow.
            var balBal = GetComponent<BalBalAbility>();
            if (balBal != null && balBal.IsMyAbility())
            {
                balBal.ConsumeBodyServerRpc(targetNetObj.NetworkObjectId);
            }
            else
            {
                RequestKillServerRpc(targetNetObj.NetworkObjectId);
            }

            break;
        }
    }

    private void TryReport()
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsAlive) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, reportRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();

            // Bal-bal's consumed victims leave no trace — same client-side UX
            // nicety pattern as the ghost checks elsewhere; MeetingManager's
            // server-side check is the real authority either way.
            if (targetRole != null && !targetRole.IsAlive && !targetRole.IsBalBalConsumed && targetNetObj != null)
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
    }

    [ServerRpc]
    private void RequestKillServerRpc(ulong targetNetworkObjectId)
    {
        if (ServerIsKillOnCooldown()) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
        {
            // Server-authoritative check — a modified client could otherwise
            // request a kill on anyone. Maligno never kill fellow Maligno.
            PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
            if (targetRole != null && targetRole.IsImpostor) return;

            Killable target = targetObj.GetComponent<Killable>();
            if (target != null && target.TryKill())
            {
                ServerStartKillCooldown();
            }
        }
    }

    // ---- Kill cooldown — shared by the normal kill path above AND
    // BalBalAbility's "consume the body" path, since both are still "a kill". ----

    public bool ServerIsKillOnCooldown()
    {
        return Time.time < serverKillCooldownEndTime;
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
    private void ServerValidateMovement()
    {
        serverValidationTimer += Time.deltaTime;
        if (serverValidationTimer < validationInterval) return;

        float elapsed = serverValidationTimer;
        serverValidationTimer = 0f;

        Vector3 currentPosition = transform.position;
        float distanceMoved = Vector2.Distance(serverLastValidatedPosition, currentPosition);

        float maxAllowedSpeed = moveSpeed * slowMultiplier.Value * speedToleranceMultiplier;
        float maxAllowedDistance = maxAllowedSpeed * elapsed;

        bool speedViolation = distanceMoved > maxAllowedDistance;

        bool wallViolation = false;
        if (!speedViolation && distanceMoved > 0.001f)
        {
            Vector2 direction = (currentPosition - serverLastValidatedPosition).normalized;
            RaycastHit2D hit = Physics2D.Raycast(serverLastValidatedPosition, direction, distanceMoved, wallLayerMask);
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

    public void ServerApplySlow(float multiplier, float duration)
    {
        if (!IsServer) return;
        StartCoroutine(ApplySlowRoutine(multiplier, duration));
    }

    // Called by CurseStatus.ServerCure() (Albularyo's heal) to end a slow
    // early instead of waiting out its duration. Safe to call even if no
    // slow is active. The original ApplySlowRoutine's own revert check
    // (slowMultiplier.Value == multiplier) simply no-ops once this has
    // already reset it, so there's no conflict between the two.
    public void ServerClearSlow()
    {
        if (!IsServer) return;
        slowMultiplier.Value = 1f;
    }

    private IEnumerator ApplySlowRoutine(float multiplier, float duration)
    {
        slowMultiplier.Value = multiplier;
        yield return new WaitForSeconds(duration);
        // Only clear if nothing else re-slowed us in the meantime.
        if (slowMultiplier.Value == multiplier)
        {
            slowMultiplier.Value = 1f;
        }
    }

    public Vector2 FacingDirection => facingDirection;
}