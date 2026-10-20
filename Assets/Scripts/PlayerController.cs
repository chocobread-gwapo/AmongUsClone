using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.5f;

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

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
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

        CameraFollow cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cam != null)
        {
            cam.SetTarget(transform);
        }
        else
        {
            Debug.LogWarning("Could not find a Main Camera with CameraFollow attached.");
        }
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

            if (targetRole != null && !targetRole.IsAlive && targetNetObj != null)
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
    private void OnAbilityPressed(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsBlocked()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        GetComponent<TikbalangAbility>()?.TryActivate();
        GetComponent<DuwendeAbility>()?.TryActivate();
        GetComponent<AswangAbility>()?.TryActivate();
        GetComponent<KapreAbility>()?.TryActivate();
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
        // TODO: AbilityCooldownUI.Instance?.StartKillCooldown(duration);
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