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

    [Header("Kill Settings")]
    [SerializeField] private float killRange = 1.2f;

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
        // Only the player who owns this object should read local input
        // and enable their own Input Actions. Remote players' movement
        // is driven entirely by NetworkTransform syncing, not local input.
        if (!IsOwner)
        {
            // Disable input reading for anything that isn't "my" player
            enabled = false;
            return;
        }

        moveAction.action.Enable();
        interactAction.action.Enable();
        killAction.action.Enable();

        interactAction.action.performed += OnInteract;
        killAction.action.performed += OnKillPressed;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        moveAction.action.Disable();
        interactAction.action.Disable();
        killAction.action.Disable();

        interactAction.action.performed -= OnInteract;
        killAction.action.performed -= OnKillPressed;
    }

    private void Update()
    {
        if (!IsOwner) return;

        moveInput = moveAction.action.ReadValue<Vector2>();

        if (moveInput.sqrMagnitude > 0.01f)
        {
            facingDirection = moveInput.normalized;
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        Vector2 velocity = moveInput.normalized * moveSpeed;
        rb.linearVelocity = velocity;
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        if (CurrentTaskZone != null)
        {
            CurrentTaskZone.TryComplete();
        }
    }

    private void OnKillPressed(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, killRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Killable target = hit.GetComponent<Killable>();
            if (target != null)
            {
                // Request the kill via RPC so the server (source of truth)
                // actually applies it and syncs the result to everyone.
                RequestKillServerRpc(hit.GetComponent<NetworkObject>().NetworkObjectId);
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