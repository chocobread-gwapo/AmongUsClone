using UnityEngine;

// Draws a furniture cutout in front of the player while the player's feet are
// north of the cutout's "foot line" (so the player appears to walk behind the
// piece), and behind the player once the feet are south of it. Uses the same
// sort-order formula as YSortedSprite so the two compare directly.
[RequireComponent(typeof(SpriteRenderer))]
public class FootLineSorter : MonoBehaviour
{
    [Tooltip("Foot line height in the PARENT's local space (map-local units).")]
    [SerializeField] private float footLocalY;
    [Tooltip("World distance from the player's pivot (YSortedSprite uses the pivot) down to the feet.")]
    [SerializeField] private float playerFootOffset = 0.55f;
    [SerializeField] private int precision = 100;
    [SerializeField] private int baseOrder = 10000;

    private void Start()
    {
        var parent = transform.parent != null ? transform.parent : transform;
        float footWorldY = parent.TransformPoint(new Vector3(0f, footLocalY, 0f)).y;
        GetComponent<SpriteRenderer>().sortingOrder = baseOrder - (int)((footWorldY + playerFootOffset) * precision);
    }
}
