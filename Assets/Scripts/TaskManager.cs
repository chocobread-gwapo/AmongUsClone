using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Task Tracking")]
    [SerializeField] private List<TaskZone> allTasks = new List<TaskZone>();

    // NetworkVariable: the server owns the real value, and it auto-syncs
    // to every connected client whenever it changes.
    private NetworkVariable<int> completedTasks = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> totalTasks = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int CompletedTasks => completedTasks.Value;
    public int TotalTasks => totalTasks.Value;
    public int TasksRemaining => TotalTasks - CompletedTasks;

    // Local events UI still subscribes to, same as before
    public static event Action<int, int> OnTaskProgressChanged;
    public static event Action OnAllTasksCompleted;

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
        // Listen for changes on ALL clients (including host) so UI updates everywhere
        completedTasks.OnValueChanged += HandleProgressChanged;

        if (IsServer)
        {
            if (allTasks.Count == 0)
            {
                allTasks.AddRange(FindObjectsByType<TaskZone>(FindObjectsSortMode.None));
            }
            totalTasks.Value = allTasks.Count;
            completedTasks.Value = 0;
        }

        // Fire once on spawn so UI has correct initial values immediately
        OnTaskProgressChanged?.Invoke(CompletedTasks, TotalTasks);
    }

    public override void OnNetworkDespawn()
    {
        completedTasks.OnValueChanged -= HandleProgressChanged;
    }

    private void HandleProgressChanged(int previousValue, int newValue)
    {
        OnTaskProgressChanged?.Invoke(newValue, TotalTasks);

        if (newValue >= TotalTasks && TotalTasks > 0)
        {
            OnAllTasksCompleted?.Invoke();
        }
    }

    // Called via ServerRpc from TaskZone when a player completes a task.
    // Only the server is allowed to actually increment the NetworkVariable.
    public void ServerReportTaskCompleted(TaskZone task)
    {
        if (!IsServer) return;

        if (!allTasks.Contains(task))
        {
            allTasks.Add(task);
            totalTasks.Value = allTasks.Count;
        }

        completedTasks.Value++;
        Debug.Log($"[Server] Tasks complete: {completedTasks.Value}/{totalTasks.Value}");
    }
}