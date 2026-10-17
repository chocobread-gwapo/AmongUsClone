using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
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

    // Hook for TaskZone to register itself when player enters range
    public TaskZone CurrentTaskZone { get; set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Top-down game: no gravity should pull the player
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        interactAction.action.Enable();
        killAction.action.Enable();

        interactAction.action.performed += OnInteract;
        killAction.action.performed += OnKillPressed;
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        interactAction.action.Disable();
        killAction.action.Disable();

        interactAction.action.performed -= OnInteract;
        killAction.action.performed -= OnKillPressed;
    }

    private void Update()
    {
        moveInput = moveAction.action.ReadValue<Vector2>();

        if (moveInput.sqrMagnitude > 0.01f)
        {
            facingDirection = moveInput.normalized;
        }
    }

    private void FixedUpdate()
    {
        // Normalize so diagonal movement isn't faster than straight movement
        Vector2 velocity = moveInput.normalized * moveSpeed;
        rb.linearVelocity = velocity;
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (CurrentTaskZone != null)
        {
            CurrentTaskZone.TryComplete();
        }
    }

    private void OnKillPressed(InputAction.CallbackContext context)
    {
        var myRole = GetComponent<PlayerRole>();
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, killRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Killable target = hit.GetComponent<Killable>();
            if (target != null && target.TryKill())
            {
                break; // one kill at a time
            }
        }
    }

    public Vector2 FacingDirection => facingDirection;
}