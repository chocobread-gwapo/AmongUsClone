using TMPro;
using UnityEngine;

// "TOTAL TASKS COMPLETED" bar for the Game HUD. The fill is a RectTransform
// whose right anchor is moved, so it works with any sprite (or none).
public class TaskProgressBarUI : MonoBehaviour
{
    [SerializeField] private RectTransform fill;
    [SerializeField] private TMP_Text label;

    private float nextPoll;

    private void OnEnable()
    {
        TaskManager.OnTaskProgressChanged += Apply;
        Poll();
    }

    private void OnDisable()
    {
        TaskManager.OnTaskProgressChanged -= Apply;
    }

    private void Update()
    {
        // Fallback: TaskManager may spawn after this UI enables.
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.5f;
        Poll();
    }

    private void Poll()
    {
        if (TaskManager.Instance != null)
            Apply(TaskManager.Instance.CompletedTasks, TaskManager.Instance.TotalTasks);
        else
            Apply(0, 0);
    }

    private void Apply(int completed, int total)
    {
        float ratio = total > 0 ? Mathf.Clamp01(completed / (float)total) : 0f;
        if (fill != null) fill.anchorMax = new Vector2(ratio, 1f);
        if (label != null) label.text = total > 0 ? $"{Mathf.RoundToInt(ratio * 100f)}%" : "";
    }
}
