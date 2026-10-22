using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Per-player tasks (Among Us style). At match start the server gives every Villager
// their own set of tasks from the map's task spots: the same "common" tasks for
// everyone, plus a random pick of long and short ones for each player. The counts come
// from the host's Game Settings. Each player only sees and can only complete their own
// tasks, and the shared task bar counts everyone's tasks together.
//
// Tasks are identified by their index in a list of all TaskZones sorted the same way on
// every machine, so the server can tell a client "your tasks are #3, #7, #9".
public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    private NetworkVariable<int> completedTasks = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> totalTasks = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int CompletedTasks => completedTasks.Value;
    public int TotalTasks => totalTasks.Value;
    public int TasksRemaining => TotalTasks - CompletedTasks;

    public static event Action<int, int> OnTaskProgressChanged;
    public static event Action OnAllTasksCompleted;
    // Raised on the local client whenever its own task list changes.
    public static event Action OnMyTasksChanged;

    // ------------------------------------------------------------- zone index

    private List<TaskZone> zones;

    public IReadOnlyList<TaskZone> Zones
    {
        get
        {
            if (zones == null || zones.Count == 0 || zones[0] == null)
            {
                zones = new List<TaskZone>(FindObjectsByType<TaskZone>(FindObjectsInactive.Include));
                zones.Sort((a, b) =>
                {
                    int c = string.CompareOrdinal(a.RoomName, b.RoomName);
                    if (c == 0) c = string.CompareOrdinal(a.TaskName, b.TaskName);
                    if (c == 0) c = a.transform.position.x.CompareTo(b.transform.position.x);
                    if (c == 0) c = a.transform.position.y.CompareTo(b.transform.position.y);
                    return c;
                });
            }
            return zones;
        }
    }

    private int IndexOf(TaskZone z)
    {
        var list = Zones;
        for (int i = 0; i < list.Count; i++) if (list[i] == z) return i;
        return -1;
    }

    // ------------------------------------------------------- local (my) tasks

    private int[] myZoneIds = new int[0];
    private bool[] myDone = new bool[0];

    public bool IsPendingForMe(TaskZone z)
    {
        int idx = IndexOf(z);
        for (int i = 0; i < myZoneIds.Length; i++) if (myZoneIds[i] == idx) return !myDone[i];
        return false;
    }

    public bool IsDoneForMe(TaskZone z)
    {
        int idx = IndexOf(z);
        for (int i = 0; i < myZoneIds.Length; i++) if (myZoneIds[i] == idx) return myDone[i];
        return false;
    }

    // My assigned tasks, for the HUD list.
    public List<(TaskZone zone, bool done)> MyTasks()
    {
        var result = new List<(TaskZone, bool)>();
        var list = Zones;
        for (int i = 0; i < myZoneIds.Length; i++)
            if (myZoneIds[i] >= 0 && myZoneIds[i] < list.Count && list[myZoneIds[i]] != null)
                result.Add((list[myZoneIds[i]], myDone[i]));
        return result;
    }

    [ClientRpc]
    private void SyncMyTasksClientRpc(int[] zoneIds, bool[] done, ClientRpcParams rpcParams = default)
    {
        int before = 0, after = 0;
        foreach (bool d in myDone) if (d) before++;
        foreach (bool d in done) if (d) after++;

        myZoneIds = zoneIds;
        myDone = done;
        if (after > before && before >= 0 && myZoneIds.Length > 0 && IsSpawned) Sfx.Play2D("task");

        foreach (var z in Zones) if (z != null) z.RefreshForMe();
        OnMyTasksChanged?.Invoke();
    }

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        completedTasks.OnValueChanged += HandleProgressChanged;
        totalTasks.OnValueChanged += HandleTotalChanged;

        if (IsServer)
        {
            totalTasks.Value = 0;
            completedTasks.Value = 0;
        }

        foreach (var z in Zones) if (z != null) z.RefreshForMe();
        OnTaskProgressChanged?.Invoke(CompletedTasks, TotalTasks);
    }

    public override void OnNetworkDespawn()
    {
        completedTasks.OnValueChanged -= HandleProgressChanged;
        totalTasks.OnValueChanged -= HandleTotalChanged;
    }

    private void HandleProgressChanged(int previousValue, int newValue)
    {
        OnTaskProgressChanged?.Invoke(newValue, TotalTasks);
        if (newValue > previousValue && TotalTasks > 0 && newValue >= TotalTasks) Sfx.Play2D("alltasks");

        if (newValue >= TotalTasks && TotalTasks > 0)
            OnAllTasksCompleted?.Invoke();
    }

    private void HandleTotalChanged(int previousValue, int newValue)
    {
        OnTaskProgressChanged?.Invoke(CompletedTasks, newValue);
    }

    // ------------------------------------------------------------------ server

    private readonly Dictionary<ulong, List<int>> assigned = new Dictionary<ulong, List<int>>();
    private readonly Dictionary<ulong, HashSet<int>> doneBy = new Dictionary<ulong, HashSet<int>>();

    // Called by GameStartManager right after roles are assigned.
    public void ServerAssignTasks()
    {
        if (!IsServer) return;
        assigned.Clear();
        doneBy.Clear();

        var gsm = GameStartManager.Instance;
        int nCommon = gsm != null ? Mathf.RoundToInt(gsm.Get(GameStartManager.Setting.CommonTasks)) : 1;
        int nLong = gsm != null ? Mathf.RoundToInt(gsm.Get(GameStartManager.Setting.LongTasks)) : 1;
        int nShort = gsm != null ? Mathf.RoundToInt(gsm.Get(GameStartManager.Setting.ShortTasks)) : 2;

        var common = ZoneIdsOfKind(TaskZone.TaskKind.Common);
        var longs = ZoneIdsOfKind(TaskZone.TaskKind.Long);
        var shorts = ZoneIdsOfKind(TaskZone.TaskKind.Short);

        // Everyone gets the same common tasks.
        List<int> commonPick = Pick(common, nCommon);

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var role = client.PlayerObject.GetComponent<PlayerRole>();
            if (role == null || role.IsImpostor) continue; // Maligno do not do chores

            var mine = new List<int>(commonPick);
            mine.AddRange(Pick(longs, nLong));
            mine.AddRange(Pick(shorts, nShort));
            assigned[client.ClientId] = mine;
            doneBy[client.ClientId] = new HashSet<int>();
            Push(client.ClientId);
        }

        Debug.Log($"[Server] Tasks assigned: {assigned.Count} villager(s), {nCommon} common / {nLong} long / {nShort} short each.");
        RecomputeTotals();
    }

    private List<int> ZoneIdsOfKind(TaskZone.TaskKind kind)
    {
        var ids = new List<int>();
        var list = Zones;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Kind == kind) ids.Add(i);
        return ids;
    }

    private static List<int> Pick(List<int> pool, int count)
    {
        var copy = new List<int>(pool);
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        count = Mathf.Clamp(count, 0, copy.Count);
        return copy.GetRange(0, count);
    }

    private void Push(ulong clientId)
    {
        if (!assigned.TryGetValue(clientId, out var list)) return;
        var done = doneBy[clientId];
        var ids = list.ToArray();
        var flags = new bool[ids.Length];
        for (int i = 0; i < ids.Length; i++) flags[i] = done.Contains(ids[i]);
        SyncMyTasksClientRpc(ids, flags, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    private void RecomputeTotals()
    {
        int total = 0, completed = 0;
        foreach (var kvp in assigned) total += kvp.Value.Count;
        foreach (var kvp in doneBy) completed += kvp.Value.Count;
        if (totalTasks.Value != total) totalTasks.Value = total;
        if (completedTasks.Value != completed) completedTasks.Value = completed;
    }

    // Called by TaskZone's server RPC when a player finishes a task's mini-game.
    public void ServerCompleteTask(ulong clientId, TaskZone zone)
    {
        if (!IsServer) return;
        int idx = IndexOf(zone);
        if (idx < 0 || !assigned.TryGetValue(clientId, out var list) || !list.Contains(idx)) return; // not their task
        if (!doneBy[clientId].Add(idx)) return; // already done

        Debug.Log($"[Server] Task completed by client {clientId}: {zone.RoomName} {zone.TaskName}");
        Push(clientId);
        RecomputeTotals();
        GameResultManager.Instance?.CheckWinConditions();
    }

    // A player left mid-match: their tasks no longer count either way.
    public void ServerOnPlayerLeft(ulong clientId)
    {
        if (!IsServer) return;
        assigned.Remove(clientId);
        doneBy.Remove(clientId);
        RecomputeTotals();
    }

    // Called by GameStartManager.HostReturnToLobby(). The Game scene (and this object)
    // is reloaded for the next match, so only the server's bookkeeping needs clearing.
    public void ServerResetAllTasks()
    {
        if (!IsServer) return;
        assigned.Clear();
        doneBy.Clear();
        totalTasks.Value = 0;
        completedTasks.Value = 0;
    }
}
