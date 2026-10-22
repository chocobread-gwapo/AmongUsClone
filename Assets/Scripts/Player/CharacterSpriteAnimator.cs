using UnityEngine;

// Script-driven sprite swap for the 4-directional walk cycle — no Unity
// Animator Controller involved, just a timer cycling through 3 frames per
// direction. Runs on every client (not owner-gated) so you see every other
// player animate too, reading PlayerController's synced facing/moving state.
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterSpriteAnimator : MonoBehaviour
{
    [Header("Walk Cycle Frames (3 each)")]
    [SerializeField] private Sprite[] downFrames = new Sprite[3];
    [SerializeField] private Sprite[] upFrames = new Sprite[3];
    [SerializeField] private Sprite[] leftFrames = new Sprite[3];
    [SerializeField] private Sprite[] rightFrames = new Sprite[3];

    [Header("Diagonal Walk Cycle Frames (3 each, optional)")]
    [SerializeField] private Sprite[] downLeftFrames = new Sprite[3];
    [SerializeField] private Sprite[] downRightFrames = new Sprite[3];
    [SerializeField] private Sprite[] upLeftFrames = new Sprite[3];
    [SerializeField] private Sprite[] upRightFrames = new Sprite[3];

    [Header("Playback")]
    [SerializeField] private float frameDuration = 0.15f; // ~6-7 fps, tweak to taste

    private PlayerController playerController;
    private SpriteRenderer spriteRenderer;

    private int frameIndex = 0;
    private float frameTimer = 0f;
    private Direction4 lastDirection = Direction4.Down;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        Direction4 direction = playerController.NetworkFacing;
        bool isMoving = playerController.NetworkIsMoving;

        if (direction != lastDirection)
        {
            // Snap to frame 0 on a direction change so turning looks crisp
            // instead of continuing mid-stride on the new direction's cycle.
            frameIndex = 0;
            frameTimer = 0f;
            lastDirection = direction;
        }

        if (isMoving)
        {
            frameTimer += Time.deltaTime;
            if (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                frameIndex = (frameIndex + 1) % 3;
            }
        }
        else
        {
            // Idle: just rest on frame 0 of whichever direction we're facing.
            frameIndex = 0;
            frameTimer = 0f;
        }

        Sprite[] frames = GetFramesFor(direction);
        if (frames != null && frames.Length == 3 && frames[frameIndex] != null)
        {
            spriteRenderer.sprite = frames[frameIndex];
        }
    }

    private Sprite[] GetFramesFor(Direction4 direction)
    {
        switch (direction)
        {
            case Direction4.Down: return downFrames;
            case Direction4.Up: return upFrames;
            case Direction4.Left: return leftFrames;
            case Direction4.Right: return rightFrames;
            // Diagonals fall back to the matching up/down frames if not assigned.
            case Direction4.DownLeft: return Usable(downLeftFrames) ? downLeftFrames : downFrames;
            case Direction4.DownRight: return Usable(downRightFrames) ? downRightFrames : downFrames;
            case Direction4.UpLeft: return Usable(upLeftFrames) ? upLeftFrames : upFrames;
            case Direction4.UpRight: return Usable(upRightFrames) ? upRightFrames : upFrames;
            default: return downFrames;
        }
    }

    private static bool Usable(Sprite[] frames)
    {
        return frames != null && frames.Length == 3 && frames[0] != null && frames[1] != null && frames[2] != null;
    }

    // Used for UI portraits (e.g. RoleRevealUI) — the idle down-facing
    // frame reads best as a static "face the camera" pose.
    public Sprite GetPortraitSprite()
    {
        return downFrames != null && downFrames.Length > 0 ? downFrames[0] : null;
    }
}