using TMPro;
using UnityEngine;

// "TOTAL TASKS COMPLETED" bar for the Game HUD. The fill is a RectTransform
// whose right anchor is moved, so it works with any sprite (or none).
// The host's "Task Bar Updates" setting decides how live it is:
//   Always   : follows every completed task
//   Meetings : only catches up when a meeting ends
//   Never    : stays hidden ("?") the whole match
public class TaskProgressBarUI : MonoBehaviour
{
    [SerializeField] private RectTransform fill;
    [SerializeField] private TMP_Text label;

    private float nextPoll;
    private int shownCompleted;
    private bool wasMeeting;

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
        bool meeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        if (wasMeeting && !meeting && TaskManager.Instance != null)
            shownCompleted = TaskManager.Instance.CompletedTasks; // "Meetings" mode catches up now
        wasMeeting = meeting;

        if (TaskManager.Instance != null)
            Apply(TaskManager.Instance.CompletedTasks, TaskManager.Instance.TotalTasks);
        else
            Apply(0, 0);
    }

    private void Apply(int completed, int total)
    {
        int mode = GameStartManager.Instance != null ? GameStartManager.Instance.TaskBarMode : 0;
        if (mode == 0) shownCompleted = completed;
        if (completed < shownCompleted) shownCompleted = completed; // new match / reset

        if (mode == 2)
        {
            if (fill != null) fill.anchorMax = new Vector2(0f, 1f);
            if (label != null) label.text = total > 0 ? "?" : "";
            return;
        }

        float ratio = total > 0 ? Mathf.Clamp01(shownCompleted / (float)total) : 0f;
        if (fill != null) fill.anchorMax = new Vector2(ratio, 1f);
        if (label != null) label.text = total > 0 ? $"{Mathf.RoundToInt(ratio * 100f)}%" : "";
    }
}
