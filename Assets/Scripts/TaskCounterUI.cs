using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TaskCounterUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI taskText;
    [SerializeField] private GameObject victoryPanel;

    private void OnEnable()
    {
        TaskManager.OnTaskProgressChanged += UpdateTaskText;
        TaskManager.OnAllTasksCompleted += ShowVictory;
    }

    private void OnDisable()
    {
        TaskManager.OnTaskProgressChanged -= UpdateTaskText;
        TaskManager.OnAllTasksCompleted -= ShowVictory;
    }

    private void Start()
    {
        if (victoryPanel != null)
            victoryPanel.SetActive(false);

        if (TaskManager.Instance != null)
            UpdateTaskText(TaskManager.Instance.CompletedTasks, TaskManager.Instance.TotalTasks);
    }

    private void UpdateTaskText(int completed, int total)
    {
        if (taskText != null)
            taskText.text = $"Tasks: {completed}/{total}";
    }

    private void ShowVictory()
    {
        if (victoryPanel != null)
            victoryPanel.SetActive(true);
    }
}