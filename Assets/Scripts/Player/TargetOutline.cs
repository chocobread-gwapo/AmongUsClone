using UnityEngine;

// The Among-Us-style white glow around a player (or body/prop) you can
// currently act on — kill target, heal target, etc. Purely a LOCAL visual
// effect: nothing here is networked. Each client independently decides who
// to highlight on ITS OWN screen by calling SetHighlighted() on whichever
// targets are currently valid for the LOCAL player's current role/ability,
// exactly like Among Us (only the killer sees the glow on their victim —
// nobody else's client ever runs this check against that same target).
//
// Technique: 4 child copies of this object's sprite, each using the flat
// SpriteSilhouette shader (see SpriteSilhouette.shader) and nudged a few
// pixels in the 4 cardinal directions, sitting just BEHIND the real sprite.
// Anywhere a ghost copy isn't covered by the real sprite (i.e. right at its
// edge) shows through as a solid-color outline — classic cheap pixel-outline
// trick, no custom render feature or Shader Graph needed.
[RequireComponent(typeof(SpriteRenderer))]
public class TargetOutline : MonoBehaviour
{
    [Tooltip("How far (in world units) each ghost copy is nudged from center. With this project's player scale (0.5), ~0.04-0.06 reads as a clean 1-2px outline — tune to taste.")]
    [SerializeField] private float offsetDistance = 0.05f;

    [SerializeField] private Color outlineColor = Color.white;

    [Tooltip("Assign Assets/Shaders? Leave empty to auto-find 'Custom/SpriteSilhouette' by name at runtime.")]
    [SerializeField] private Shader silhouetteShader;

    private SpriteRenderer sourceRenderer;
    private SpriteRenderer[] ghosts;
    private Material sharedGhostMaterial;
    private bool isHighlighted;

    private static readonly Vector2[] Offsets =
    {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right
    };

    private void Awake()
    {
        sourceRenderer = GetComponent<SpriteRenderer>();

        if (silhouetteShader == null)
        {
            silhouetteShader = Shader.Find("Custom/SpriteSilhouette");
        }

        if (silhouetteShader == null)
        {
            Debug.LogWarning("[TargetOutline] Could not find 'Custom/SpriteSilhouette' shader. " +
                "Make sure SpriteSilhouette.shader is in the project (any Assets folder is fine). Outline disabled.");
            enabled = false;
            return;
        }

        sharedGhostMaterial = new Material(silhouetteShader);
        sharedGhostMaterial.SetColor("_Color", outlineColor);

        ghosts = new SpriteRenderer[Offsets.Length];
        for (int i = 0; i < Offsets.Length; i++)
        {
            GameObject ghostObj = new GameObject($"OutlineGhost_{i}");
            ghostObj.transform.SetParent(transform, worldPositionStays: false);
            ghostObj.transform.localPosition = Offsets[i] * offsetDistance;
            ghostObj.transform.localRotation = Quaternion.identity;
            ghostObj.transform.localScale = Vector3.one;

            SpriteRenderer ghostRenderer = ghostObj.AddComponent<SpriteRenderer>();
            ghostRenderer.material = sharedGhostMaterial;
            ghostRenderer.enabled = false;

            ghosts[i] = ghostRenderer;
        }
    }

    // Sprite/flip/sorting sync only runs while actually highlighted — this
    // component sits on every targetable object (every player, maybe
    // bodies), so skipping work while hidden matters for perf.
    private void LateUpdate()
    {
        if (!isHighlighted || sourceRenderer == null) return;

        // If the real sprite isn't visible to this viewer (a ghost seen by the living, or a player
        // hidden by fog-of-war), the outline copies must not show either, or they'd draw a solid silhouette.
        bool sourceVisible = sourceRenderer.enabled && sourceRenderer.color.a > 0.05f;

        foreach (var ghost in ghosts)
        {
            if (ghost == null) continue;
            if (ghost.enabled != sourceVisible) ghost.enabled = sourceVisible;
            ghost.sprite = sourceRenderer.sprite;
            ghost.flipX = sourceRenderer.flipX;
            ghost.flipY = sourceRenderer.flipY;
            // Track the source's sorting every frame too — PlayerController's
            // Y-sort (YSortedSprite) changes sortingOrder continuously as the
            // player moves, and the outline must stay just behind that.
            ghost.sortingLayerID = sourceRenderer.sortingLayerID;
            ghost.sortingOrder = sourceRenderer.sortingOrder - 1;
        }
    }

    // color is optional — pass it to tint this specific highlight (red for a
    // kill target, green for a heal target, etc.). Omit it to use whatever
    // color was already set (defaults to outlineColor, white, the first time).
    // Callers that highlight for more than one reason (unlikely, but e.g. an
    // Impostor who's ALSO somehow a heal target) will just show whichever
    // call happened most recently — there's only one outline per object.
    public void SetHighlighted(bool highlighted, Color? color = null)
    {
        if (color.HasValue && sharedGhostMaterial != null)
        {
            sharedGhostMaterial.SetColor("_Color", color.Value);
        }

        if (isHighlighted == highlighted) return;
        isHighlighted = highlighted;

        if (ghosts == null) return;
        foreach (var ghost in ghosts)
        {
            if (ghost != null) ghost.enabled = highlighted;
        }

        // Force an immediate sync on turning on, so the very first visible
        // frame already shows the right pose instead of a one-frame stale copy.
        if (highlighted) LateUpdate();
    }

    public bool IsHighlighted => isHighlighted;

    private void OnDestroy()
    {
        if (sharedGhostMaterial != null) Destroy(sharedGhostMaterial);
    }
}