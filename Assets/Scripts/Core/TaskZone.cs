using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class TaskZone : NetworkBehaviour
{
    [Header("Task Settings")]
    [SerializeField] private string taskName = "Task";

    [Tooltip("Optional room/location label shown before the task name in the breakdown panel, e.g. \"Storage\" for \"Storage: Fuel Engines\". Leave blank to show just the task name.")]
    [SerializeField] private string roomName = "";

    // Synced so every client knows if this specific task is already done
    // (prevents two players both "completing" the same task)
    private NetworkVariable<bool> isComplete = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Visual Feedback (optional)")]
    [SerializeField] private GameObject promptIndicator;
    [Tooltip("The TargetOutline component on whatever GameObject actually has the visible task-machine sprite (may be this same object, or a child — TargetOutline needs a SpriteRenderer to work from). Leave empty to skip the glow and keep using only promptIndicator.")]
    [SerializeField] private TargetOutline targetOutline;

    public bool IsComplete => isComplete.Value;
    public string TaskName => taskName;
    public string RoomName => roomName;

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (promptIndicator != null)
            promptIndicator.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        isComplete.OnValueChanged += HandleCompletionChanged;
    }

    public override void OnNetworkDespawn()
    {
        isComplete.OnValueChanged -= HandleCompletionChanged;
    }

    private void HandleCompletionChanged(bool previous, bool current)
    {
        if (current && promptIndicator != null)
            promptIndicator.SetActive(false);

        // A task can complete while the player is still standing inside its
        // trigger (no OnTriggerExit2D fires in that case) — clear the glow
        // here too, or it'd stay lit on a task that's already done.
        if (current && targetOutline != null)
            targetOutline.SetHighlighted(false);
    }

    // Only the local player who's actually standing in range should see this
    // zone glow — same local-only reasoning as kill/heal highlighting
    // (TargetOutline itself is never networked), it's just driven by this
    // trigger instead of a distance scan since the trigger already exists.
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && player.IsOwner && !IsComplete)
        {
            player.CurrentTaskZone = this;
            if (promptIndicator != null)
                promptIndicator.SetActive(true);
            if (targetOutline != null)
                targetOutline.SetHighlighted(true);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && player.IsOwner)
        {
            if (player.CurrentTaskZone == this)
                player.CurrentTaskZone = null;

            if (promptIndicator != null)
                promptIndicator.SetActive(false);
            if (targetOutline != null)
                targetOutline.SetHighlighted(false);
        }
    }

    // Called locally by the owning player; requests completion from the server
    public void TryComplete()
    {
        if (IsComplete || TaskMiniGame.IsOpen) return;
        // Player finishes a short mini-game first; only then does the server
        // get asked to mark the task done.
        TaskMiniGame.Open(roomName, taskName, () => { if (this != null && !IsComplete) RequestCompleteServerRpc(); });
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCompleteServerRpc()
    {
        if (isComplete.Value) return;

        isComplete.Value = true;
        Debug.Log($"[Server] Task completed: {taskName}");

        TaskManager.Instance?.ServerReportTaskCompleted(this);
    }

    // Called by TaskManager.ServerResetAllTasks() when returning to the lobby,
    // so the same task set can be completed again next match.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        isComplete.Value = false;
    }
}