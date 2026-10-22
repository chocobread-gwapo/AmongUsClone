using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TaskManager : NetworkBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Task Tracking")]
    [SerializeField] private List<TaskZone> allTasks = new List<TaskZone>();

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

        OnTaskProgressChanged?.Invoke(CompletedTasks, TotalTasks);
    }

    public override void OnNetworkDespawn()
    {
        completedTasks.OnValueChanged -= HandleProgressChanged;
    }

    private void HandleProgressChanged(int previousValue, int newValue)
    {
        OnTaskProgressChanged?.Invoke(newValue, TotalTasks);
        if (newValue > previousValue) Sfx.Play2D(TotalTasks > 0 && newValue >= TotalTasks ? "alltasks" : "task");

        if (newValue >= TotalTasks && TotalTasks > 0)
        {
            OnAllTasksCompleted?.Invoke();
        }
    }

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

        // NEW — completing a task might be the one that finishes the whole
        // task bar, which is a Crewmate win condition.
        GameResultManager.Instance?.CheckWinConditions();
    }

    // Called by GameStartManager.HostReturnToLobby() — marks every tracked
    // task incomplete again and resets the counter, so the same task set can
    // be played again next match without re-discovering tasks via triggers.
    public void ServerResetAllTasks()
    {
        if (!IsServer) return;

        foreach (var task in allTasks)
        {
            task?.ServerResetForNewGame();
        }

        completedTasks.Value = 0;
    }
}