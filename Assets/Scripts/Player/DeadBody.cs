using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Stationary, reportable marker left behind at the exact spot a player was
// killed. Needed because dead players ("ghosts") keep moving and can still
// finish tasks (see the Ghost/Spirit comment on PlayerRole.Kill()) — if
// reporting still targeted the dead player's own, now-moving, now-invisible
// collider (see GhostVisibility), a living player standing right on top of
// the visible body sprite could be nowhere near the real report range.
//
// This object is what villagers actually SEE and WALK UP TO report. The
// real "who died / were they Bal-Bal'd" answer still lives on the original
// PlayerRole — this just remembers which one, so MeetingManager's existing
// report rules (IsAlive / IsBalBalConsumed) don't need duplicating here.
//
// No Collider2D needed — PlayerController finds nearby bodies by checking
// distance directly against every spawned DeadBody, not via physics
// overlap, so this is purely a visual + a piece of networked state.
public class DeadBody : NetworkBehaviour
{
    // Everyone-read/Server-write, same pattern as every other synced value
    // in this project. Clients never write this — only MeetingManager's
    // server-side report resolution needs to read it.
    private readonly NetworkVariable<ulong> originalPlayerId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public ulong OriginalPlayerId => originalPlayerId.Value;

    // Called by PlayerRole.Kill() right after this object's own Spawn()
    // call returns (Spawn() is synchronous on the server, so the object is
    // already fully spawned by the time this runs — writing here is safe;
    // writing BEFORE Spawn() is what caused the NetworkVariable warnings
    // fixed earlier in GameStartManager).
    public void ServerInitialize(ulong deadPlayerNetworkObjectId)
    {
        if (!IsServer) return;
        originalPlayerId.Value = deadPlayerNetworkObjectId;
    }

    // Server-only: removes every DeadBody currently on the map. Lives here
    // (rather than being duplicated in each caller) since every place that
    // needs to clear bodies — a meeting starting, the match ending, or a
    // fresh match beginning — wants the exact same logic. Collected into a
    // list first since Despawn() removes entries from SpawnedObjects as it
    // goes, which would otherwise break a foreach mid-iteration.
    public static void DespawnAll()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        List<NetworkObject> bodies = new List<NetworkObject>();
        foreach (var spawned in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
        {
            if (spawned != null && spawned.GetComponent<DeadBody>() != null)
            {
                bodies.Add(spawned);
            }
        }
        foreach (var body in bodies)
        {
            body.Despawn();
        }
    }
}