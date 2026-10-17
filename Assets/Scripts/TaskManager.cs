using System;
using System.Collections.Generic;
using UnityEngine;

public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Task Tracking")]
    [SerializeField] private List<TaskZone> allTasks = new List<TaskZone>();

    public int TotalTasks => allTasks.Count;
    public int CompletedTasks { get; private set; } = 0;
    public int TasksRemaining => TotalTasks - CompletedTasks;

    // Subscribe to these from UI or game-state scripts
    public static event Action<int, int> OnTaskProgressChanged; // (completed, total)
    public static event Action OnAllTasksCompleted;

    private void Awake()
    {
        // Simple singleton — if one already exists, destroy this duplicate
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Auto-find all TaskZones in the scene if the list wasn't filled in manually
        if (allTasks.Count == 0)
        {
            allTasks.AddRange(FindObjectsByType<TaskZone>(FindObjectsSortMode.None));
        }

        CompletedTasks = 0;
        OnTaskProgressChanged?.Invoke(CompletedTasks, TotalTasks);
    }

    public void OnTaskCompleted(TaskZone task)
    {
        if (!allTasks.Contains(task))
        {
            allTasks.Add(task);
        }

        CompletedTasks++;
        Debug.Log($"Tasks complete: {CompletedTasks}/{TotalTasks}");

        OnTaskProgressChanged?.Invoke(CompletedTasks, TotalTasks);

        if (CompletedTasks >= TotalTasks)
        {
            Debug.Log("ALL TASKS COMPLETE");
            OnAllTasksCompleted?.Invoke();
        }
    }
}