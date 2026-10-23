using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Per-scene spawn layout. Player objects persist across the Lobby <-> Game
// scene switches, so after each switch the host moves everyone to this
// scene's spawn area (a ring around `center`). Place one in every scene
// players can end up in.
public class SceneSpawnPoints : MonoBehaviour
{
    public static SceneSpawnPoints Instance { get; private set; }

    [SerializeField] private Transform center;
    public Vector3 CenterPosition => center != null ? center.position : transform.position;

    [Header("Emergency Table")]
    [Tooltip("How close (in world units) a player must stand to the emergency table to call a meeting. The yellow circle in the Scene view (and in the Game view with Gizmos on) shows this range.")]
    [SerializeField] private float emergencyReach = 3.8f;
    public float EmergencyReach => emergencyReach;

    private void OnDrawGizmos()
    {
        Vector3 c = CenterPosition;
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 1f);
        Gizmos.DrawWireSphere(c, emergencyReach);
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(c, emergencyReach * 0.98f);
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(c, Mathf.Max(ringRadius, ClearRingRadius)); // where players actually spawn
    }
    [SerializeField] private float ringRadius = 2.5f;

    // Furniture (e.g. the cafeteria's central table) can sit on the centre
    // point, so the ring must clear it. Used whenever something solid is there.
    private const float ClearRingRadius = 3.4f;

    // The Lobby scatters players at random so nobody stacks; the Game scene
    // keeps the fair, even ring. Players already placed stay where they are
    // when someone new joins.
    private const string RandomScene = "Lobby";
    private const float MinSpacing = 1.1f;
    private readonly HashSet<ulong> placed = new HashSet<ulong>();

    private void Awake() { Instance = this; }
    // Players can appear (or finish spawning) a moment after the player count
    // changes, so the Lobby keeps checking for anyone not yet scattered.
    private float nextCheck;
    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || SceneManager.GetActiveScene().name != RandomScene) return;
        Vector3 c = center != null ? center.position : transform.position;
        PlaceRandomly(nm.ConnectedClientsList, c);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Server only.
    public static void PlaceAllPlayers()
    {
        if (Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        Vector3 c = Instance.center != null ? Instance.center.position : Instance.transform.position;

        // Something solid at the centre (a table)? Then even a lone player must
        // stand on the ring, and the ring must be wide enough to clear it.
        if (SceneManager.GetActiveScene().name == RandomScene)
        {
            Instance.PlaceRandomly(clients, c);
            return;
        }

        bool centreBlocked = Physics2D.OverlapPoint(c) != null;
        float radius = centreBlocked ? Mathf.Max(Instance.ringRadius, ClearRingRadius) : Instance.ringRadius;

        int n = clients.Count, i = 0;
        foreach (var client in clients)
        {
            var obj = client.PlayerObject;
            if (obj == null) continue;
            bool useRing = n > 1 || centreBlocked;
            // Single player on a blocked centre: stand below the table.
            float angle = n > 1 ? i / (float)n * Mathf.PI * 2f : -Mathf.PI * 0.5f;
            Vector3 pos = c + (useRing ? new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius : Vector3.zero);
            var pc = obj.GetComponent<PlayerController>();
            if (pc != null) pc.ServerTeleportTo(pos); else obj.transform.position = pos;
            i++;
        }
    }

    private void PlaceRandomly(IReadOnlyList<NetworkClient> clients, Vector3 c)
    {
        var taken = new List<Vector3>();
        foreach (var client in clients)
            if (placed.Contains(client.ClientId) && client.PlayerObject != null)
                taken.Add(client.PlayerObject.transform.position);

        foreach (var client in clients)
        {
            var obj = client.PlayerObject;
            if (obj == null || placed.Contains(client.ClientId)) continue;

            Vector3 pos = c;
            bool found = false;
            for (int attempt = 0; attempt < 40 && !found; attempt++)
            {
                Vector2 r = Random.insideUnitCircle * (ringRadius * 1.5f);
                Vector3 p = c + new Vector3(r.x * 1.4f, r.y, 0f);
                if (Physics2D.OverlapCircle(p, 0.4f) != null) continue;
                bool clear = true;
                foreach (var t in taken)
                    if (Vector3.Distance(t, p) < MinSpacing) { clear = false; break; }
                if (!clear) continue;
                pos = p; found = true;
            }
            if (!found)
            {
                // Crowded: fall back to a random angle on the ring.
                float a = Random.value * Mathf.PI * 2f;
                pos = c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ringRadius;
            }

            var pc = obj.GetComponent<PlayerController>();
            if (pc != null) pc.ServerTeleportTo(pos); else obj.transform.position = pos;
            taken.Add(pos);
            placed.Add(client.ClientId);
        }
    }
}
