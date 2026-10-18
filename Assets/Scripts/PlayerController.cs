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

    [Header("Kill Settings")]
    [SerializeField] private float killRange = 1.2f;

    [Header("Report Settings")]
    [SerializeField] private float reportRange = 1.5f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;
    private Vector2 facingDirection = Vector2.down;

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

        interactAction.action.performed += OnInteract;
        killAction.action.performed += OnKillPressed;
        reportAction.action.performed += OnReportPressed;

        CameraFollow cam = Camera.main.GetComponent<CameraFollow>();
        if (cam != null)
        {
            cam.SetTarget(transform);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        moveAction.action.Disable();
        interactAction.action.Disable();
        killAction.action.Disable();
        reportAction.action.Disable();

        interactAction.action.performed -= OnInteract;
        killAction.action.performed -= OnKillPressed;
        reportAction.action.performed -= OnReportPressed;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = moveAction.action.ReadValue<Vector2>();

        if (moveInput.sqrMagnitude > 0.01f)
        {
            facingDirection = moveInput.normalized;
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 velocity = moveInput.normalized * moveSpeed;
        rb.linearVelocity = velocity;
    }

    private bool IsMeetingBlocking()
    {
        return MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsMeetingBlocking()) return;

        if (CurrentTaskZone != null)
        {
            CurrentTaskZone.TryComplete();
        }
    }

    private void OnKillPressed(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsMeetingBlocking()) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, killRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Killable target = hit.GetComponent<Killable>();
            if (target != null)
            {
                RequestKillServerRpc(hit.GetComponent<NetworkObject>().NetworkObjectId);
                break;
            }
        }
    }

    private void OnReportPressed(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsMeetingBlocking()) return;

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

    [ServerRpc]
    private void RequestKillServerRpc(ulong targetNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
        {
            Killable target = targetObj.GetComponent<Killable>();
            target?.TryKill();
        }
    }

    public Vector2 FacingDirection => facingDirection;
}