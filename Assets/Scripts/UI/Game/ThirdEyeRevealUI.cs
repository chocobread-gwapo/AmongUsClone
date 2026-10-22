using UnityEngine;

// Local-only visualization for Third Eye Sight's reveal. Spawns temporary,
// purely cosmetic markers at each given world point — gray, no names or
// colors, matching the "neutral gray residue, no player identity" spec. Not
// networked: only the activating client ever sees these (ThirdEyeAbility
// only RPCs the result back to its own owner).
public class ThirdEyeRevealUI : MonoBehaviour
{
    public static ThirdEyeRevealUI Instance { get; private set; }

    [Tooltip("A small gray footprint/residue sprite prefab, purely local (no NetworkObject needed).")]
    [SerializeField] private GameObject residueMarkerPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowResidue(Vector3[] points, float duration)
    {
        if (residueMarkerPrefab == null)
        {
            Debug.LogWarning("ThirdEyeRevealUI has no residueMarkerPrefab assigned.");
            return;
        }

        foreach (var point in points)
        {
            GameObject marker = Instantiate(residueMarkerPrefab, point, Quaternion.identity);
            marker.GetComponent<ResidueFade>()?.Initialize(duration);
            Destroy(marker, duration);
        }
    }
}