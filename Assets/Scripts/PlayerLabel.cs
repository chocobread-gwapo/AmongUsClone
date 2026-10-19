using Unity.Netcode;
using UnityEngine;

// Separate from PlayerController deliberately — PlayerController disables
// itself for non-owned player instances, but every client needs to see
// EVERY player's label correctly, not just their own. OnNetworkSpawn runs
// for all players on all clients regardless of ownership, so this works
// correctly without any ownership checks.
public class PlayerLabel : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        WorldLabel label = GetComponent<WorldLabel>();
        if (label != null)
        {
            // OwnerClientId is visible to everyone for a spawned object (not secret),
            // so this produces the same label on every client's screen.
            // Host is typically client 0, so "Player 1" = host, matching your request.
            label.SetLabel($"Player {OwnerClientId + 1}");
        }
    }
}