using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Play-mode test helper: turns the host's own player into a ghost WITHOUT running the win check,
// so ghost movement can be tested in a two-player session without the match ending.
public static class MalignoGhostTest
{
    [MenuItem("Maligno/25. Test: Make Host Player A Ghost (Play mode)")]
    private static void MakeGhost()
    {
        var nm = NetworkManager.Singleton;
        if (!Application.isPlaying || nm == null || !nm.IsServer || nm.LocalClient?.PlayerObject == null)
        {
            Debug.LogWarning("[GhostTest] Needs Play mode as host with a spawned player.");
            return;
        }
        var role = nm.LocalClient.PlayerObject.GetComponent<PlayerRole>();
        var f = typeof(PlayerRole).GetField("isAlive", BindingFlags.NonPublic | BindingFlags.Instance);
        var nv = (NetworkVariable<bool>)f.GetValue(role);
        nv.Value = false;
        Debug.Log("[GhostTest] Host player is now a ghost (layer " + LayerMask.LayerToName(role.gameObject.layer) + ").");
    }
}
