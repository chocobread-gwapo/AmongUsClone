using UnityEngine;

// Local-only visualization for Aura Sense. Spawns temporary, purely cosmetic
// translucent "aura" markers at each given (already-jittered) world point —
// deliberately distinct in look from ThirdEyeRevealUI's gray footprints, so
// the two Survivor Classes read as different kinds of information at a glance.
public class ReikiRevealUI : MonoBehaviour
{
    public static ReikiRevealUI Instance { get; private set; }

    [Tooltip("A soft colored glow/aura sprite prefab, purely local (no NetworkObject needed).")]
    [SerializeField] private GameObject auraMarkerPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowAuras(Vector3[] points, float duration)
    {
        if (auraMarkerPrefab == null)
        {
            Debug.LogWarning("ReikiRevealUI has no auraMarkerPrefab assigned.");
            return;
        }

        foreach (var point in points)
        {
            GameObject marker = Instantiate(auraMarkerPrefab, point, Quaternion.identity);
            Destroy(marker, duration);
        }
    }
}