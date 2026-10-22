using UnityEngine;

// Simple top-down Y-sort. Nothing in this project currently controls sprite
// draw order, so overlapping sprites (a player standing on a dead body or a
// banana trunk, two players crossing paths) just draw in whatever order
// Unity happens to spawn/instantiate them — sometimes the player ends up
// behind something it should be in front of, and vice versa.
//
// The fix: the lower something sits on screen (smaller Y), the more it
// should draw in front, since it's visually "closer" to the camera in a
// top-down game. This sets each sprite's sortingOrder from its own Y
// position every frame, so whichever object is actually standing further
// "down" always wins, no matter instantiation order.
//
// Attach to the Player prefab (on whichever GameObject holds the
// SpriteRenderer), the DeadBody prefab, and the banana trunk prop prefab —
// anything that can visually overlap something else at runtime.
[RequireComponent(typeof(SpriteRenderer))]
public class YSortedSprite : MonoBehaviour
{
    [Tooltip("Multiplier converting Y position into sortingOrder. Higher gives finer precision between objects close together in Y. 100 is plenty for a typical small top-down scene.")]
    [SerializeField] private int precision = 100;

    [Tooltip("Added to every computed sortingOrder so the result stays comfortably positive no matter this scene's Y range (including negative Y). Without this, a large enough Y pushes sortingOrder below 0 — and anything else in the scene still using the default order (0), like a background sprite or a darkness/vignette overlay, would then draw ON TOP of the player instead of behind them, making the player appear to vanish. 10000 is far more headroom than any reasonable map needs.")]
    [SerializeField] private int baseOrder = 10000;

    [Tooltip("Static props (DeadBody, banana trunk) only need their sort order set once, on spawn — uncheck to skip the per-frame cost. Leave checked for anything that moves (the Player).")]
    [SerializeField] private bool updateEveryFrame = true;

    [Tooltip("Added to this object's Y before sorting. Furniture uses about +0.42 so it is compared against a player's FEET (the player's pivot sits mid-body) rather than their centre.")]
    [SerializeField] private float sortYOffset = 0f;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        ApplySortOrder();
    }

    // LateUpdate so this applies after this frame's movement has settled
    // (movement happens in FixedUpdate via Rigidbody2D) — same timing
    // convention as CameraFollow.
    private void LateUpdate()
    {
        if (updateEveryFrame) ApplySortOrder();
    }

    private void ApplySortOrder()
    {
        spriteRenderer.sortingOrder = baseOrder - (int)((transform.position.y + sortYOffset) * precision);
    }
}
