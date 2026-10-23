using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Press F9 in a match (or run Maligno > 24. Profile Performance) to sample 10 seconds of frame time,
// per-frame GC allocation, physics query cost, collider size and network state. The report is logged
// and written to perf_report.txt next to the project (or the exe), appended per run, so reports from
// the host and each client can be compared.
public class PerfProbe : MonoBehaviour
{
    private static PerfProbe instance;
    private bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("PerfProbe");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PerfProbe>();
    }

    public static void Run(float seconds = 10f)
    {
        if (instance == null) Boot();
        if (!instance.running) instance.StartCoroutine(instance.Sample(seconds));
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) Run();
    }

    private System.Collections.IEnumerator Sample(float seconds)
    {
        running = true;
        Debug.Log("[Perf] Sampling for " + seconds + "s ...");

        var frames = new List<float>(1024);
        long allocStart = GC.GetAllocatedBytesForCurrentThread();
        int gcStart = GC.CollectionCount(0);
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            frames.Add(Time.unscaledDeltaTime * 1000f);
            yield return null;
        }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        int gcs = GC.CollectionCount(0) - gcStart;

        frames.Sort();
        float sum = 0f; foreach (var f in frames) sum += f;
        float avg = sum / Mathf.Max(1, frames.Count);
        float p95 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))] : 0f;
        float max = frames.Count > 0 ? frames[frames.Count - 1] : 0f;

        var sb = new StringBuilder();
        sb.AppendLine("==== PERF " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " scene=" + SceneManager.GetActiveScene().name + " ====");
        sb.AppendLine($"frames={frames.Count} avg={avg:F2}ms ({1000f / Mathf.Max(0.01f, avg):F0} fps) p95={p95:F2}ms max={max:F2}ms");
        sb.AppendLine($"GC alloc={alloc / 1024f:F1} KB total, {alloc / 1024f / Mathf.Max(1, frames.Count):F2} KB/frame, gen0 collections={gcs}");

        // Colliders
        int composites = 0, compPoints = 0, compPaths = 0, polys = 0, allColliders = 0;
        foreach (var c in FindObjectsByType<CompositeCollider2D>(FindObjectsInactive.Exclude)) { composites++; compPoints += c.pointCount; compPaths += c.pathCount; }
        polys = FindObjectsByType<PolygonCollider2D>(FindObjectsInactive.Exclude).Length;
        allColliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude).Length;
        sb.AppendLine($"colliders: total={allColliders} polygon={polys} composite={composites} (points={compPoints}, paths={compPaths})");

        // Physics query micro-benchmark over the walkable area
        var bounds = new Bounds();
        bool any = false;
        foreach (var c in FindObjectsByType<CompositeCollider2D>(FindObjectsInactive.Exclude)) { if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds); }
        if (any)
        {
            var rng = new System.Random(1);
            const int N = 4000;
            var buf = new Collider2D[16];
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
            {
                Vector2 p = new Vector2((float)(bounds.min.x + rng.NextDouble() * bounds.size.x), (float)(bounds.min.y + rng.NextDouble() * bounds.size.y));
                Physics2D.Raycast(p, Vector2.right, 0.5f);
            }
            sw.Stop();
            double rayUs = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
            sw.Restart();
            for (int i = 0; i < N; i++)
            {
                Vector2 p = new Vector2((float)(bounds.min.x + rng.NextDouble() * bounds.size.x), (float)(bounds.min.y + rng.NextDouble() * bounds.size.y));
                Physics2D.OverlapCircle(p, 1.5f, new ContactFilter2D().NoFilter(), buf);
            }
            sw.Stop();
            double ovUs = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
            sb.AppendLine($"physics queries (random map points): raycast={rayUs:F1} us, overlapCircle={ovUs:F1} us");
        }

        // Network
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            int objs = nm.SpawnManager.SpawnedObjects.Count;
            sb.AppendLine($"network: host={nm.IsHost} server={nm.IsServer} clients={(nm.IsServer ? nm.ConnectedClientsList.Count : 1)} spawnedObjects={objs} tickRate={nm.NetworkTickSystem?.TickRate}");
            var ut = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (ut != null && !nm.IsServer)
            {
                try { sb.AppendLine($"rtt to host={ut.GetCurrentRtt(NetworkManager.ServerClientId)} ms"); } catch (Exception) { }
            }
            else if (ut != null && nm.IsServer)
            {
                foreach (var cl in nm.ConnectedClientsList)
                {
                    if (cl.ClientId == nm.LocalClientId) continue;
                    try { sb.AppendLine($"rtt to client {cl.ClientId}={ut.GetCurrentRtt(cl.ClientId)} ms"); } catch (Exception) { }
                }
            }
        }
        else sb.AppendLine("network: not running");

        string report = sb.ToString();
        Debug.Log(report);
        try
        {
            string path = Path.Combine(Application.isEditor ? Path.GetDirectoryName(Application.dataPath) : Path.GetDirectoryName(Application.dataPath), "perf_report.txt");
            File.AppendAllText(path, report + "\n");
            Debug.Log("[Perf] Saved to " + path);
        }
        catch (Exception e) { Debug.LogWarning("[Perf] Could not save report: " + e.Message); }
        running = false;
    }
}
