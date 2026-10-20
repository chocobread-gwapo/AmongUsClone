using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Server-only movement history for a single player, used by the Third-Eye
// User Survivor Class to reveal "supernatural footprint residue." Only
// Maligno leave this trace — ordinary Villagers don't, since the whole point
// is spotting where a Maligno has recently been, not everyone.
public class FootprintTracker : NetworkBehaviour
{
    [Tooltip("How often a position sample is recorded, in seconds.")]
    [SerializeField] private float sampleInterval = 0.4f;

    [Tooltip("Oldest a sample is kept before being discarded, regardless of what any ability asks for.")]
    [SerializeField] private float maxHistorySeconds = 30f;

    private PlayerRole myRole;
    private readonly List<(float time, Vector3 pos)> history = new List<(float, Vector3)>();
    private float nextSampleTime;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (myRole == null || !myRole.IsImpostor || !myRole.IsAlive) return;

        if (Time.time >= nextSampleTime)
        {
            history.Add((Time.time, transform.position));
            nextSampleTime = Time.time + sampleInterval;
        }

        history.RemoveAll(entry => Time.time - entry.time > maxHistorySeconds);
    }

    // Server-only query — called by ThirdEyeAbility when its skill is used.
    public List<Vector3> GetRecentFootprints(float lookbackSeconds)
    {
        List<Vector3> result = new List<Vector3>();
        float cutoff = Time.time - lookbackSeconds;

        foreach (var entry in history)
        {
            if (entry.time >= cutoff)
            {
                result.Add(entry.pos);
            }
        }

        return result;
    }
}