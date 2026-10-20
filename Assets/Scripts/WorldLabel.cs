using TMPro;
using UnityEngine;

// Generic label component — attach to any object you want a name/debug tag
// floating above it. Works for Players, Tasks, Dummy, or anything else later.
//
// Also doubles as the fog-of-war fix for labels: TextMeshPro world-space text
// ignores Light2D entirely, so without this a task's name or another player's
// nametag would stay fully readable through the darkness. Every frame, each
// label checks its distance to the LOCAL player's current vision radius
// (PlayerVision.LocalInstance) and hides itself if it's outside that circle.
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private TextMeshPro labelText; // child TMP (3D) object, positioned above the sprite

    [Tooltip("Extra distance beyond the vision radius before the label actually hides, so it doesn't flicker right at the edge.")]
    [SerializeField] private float visibilityBuffer = 0.2f;

    public void SetLabel(string text)
    {
        if (labelText != null)
        {
            labelText.text = text;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name} has a WorldLabel but no TextMeshPro assigned.");
        }
    }

    private void Update()
    {
        if (labelText == null) return;

        // Fail-open: before the local player exists yet (e.g. still in lobby),
        // just show labels normally rather than hiding everything.
        if (PlayerVision.LocalInstance == null)
        {
            labelText.enabled = true;
            return;
        }

        float distance = Vector2.Distance(transform.position, PlayerVision.LocalInstance.transform.position);
        labelText.enabled = distance <= PlayerVision.LocalInstance.CurrentVisionRadius + visibilityBuffer;
    }
}