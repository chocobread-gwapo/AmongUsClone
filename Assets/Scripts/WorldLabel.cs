using Unity.Netcode;
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
//
// If this object is a Player (has a PlayerRole), it ALSO follows the same
// ghost-visibility rule as GhostVisibility.cs's sprite hiding: a dead
// player's label is fully hidden from living viewers, and only shown to
// other ghosts (still subject to the normal vision-radius fog check above).
// Non-player labels (tasks, etc.) are unaffected since they have no PlayerRole.
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private TextMeshPro labelText; // child TMP (3D) object, positioned above the sprite

    [Tooltip("Extra distance beyond the vision radius before the label actually hides, so it doesn't flicker right at the edge.")]
    [SerializeField] private float visibilityBuffer = 0.2f;

    private PlayerRole ownerRole;

    private void Awake()
    {
        ownerRole = GetComponentInParent<PlayerRole>();
    }

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

        // Ghost rule first: a dead player's label is invisible to living
        // viewers no matter the distance/fog check below.
        if (ownerRole != null && !ownerRole.IsAlive && !IsLocalViewerDead())
        {
            labelText.enabled = false;
            return;
        }

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

    private bool IsLocalViewerDead()
    {
        NetworkObject localPlayerObj = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject
            : null;
        if (localPlayerObj == null) return false;

        PlayerRole localRole = localPlayerObj.GetComponent<PlayerRole>();
        return localRole != null && !localRole.IsAlive;
    }
}