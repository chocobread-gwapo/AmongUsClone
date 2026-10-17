using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TaskZone : MonoBehaviour
{
    [Header("Task Settings")]
    [SerializeField] private string taskName = "Task";
    [SerializeField] private bool isComplete = false;

    [Header("Visual Feedback (optional)")]
    [SerializeField] private GameObject promptIndicator; // e.g. a "Press E" icon above the task

    public bool IsComplete => isComplete;
    public string TaskName => taskName;

    private void Awake()
    {
        // Make sure the collider is a trigger so the player can walk into it without being blocked
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (promptIndicator != null)
            promptIndicator.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && !isComplete)
        {
            player.CurrentTaskZone = this;
            if (promptIndicator != null)
                promptIndicator.SetActive(true);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            if (player.CurrentTaskZone == this)
                player.CurrentTaskZone = null;

            if (promptIndicator != null)
                promptIndicator.SetActive(false);
        }
    }

    public void TryComplete()
    {
        if (isComplete) return;

        isComplete = true;
        Debug.Log($"Task completed: {taskName}");

        if (promptIndicator != null)
            promptIndicator.SetActive(false);

        TaskManager.Instance?.OnTaskCompleted(this);
    }
}