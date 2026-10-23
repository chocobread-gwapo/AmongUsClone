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

    // Which pool this task belongs to when the host sets "# Common / Long / Short Tasks".
    // Auto picks by task name (see DefaultKind); set it here to override.
    public enum TaskKind { Auto, Common, Short, Long }

    [Tooltip("Common = everyone gets it. Short / Long = handed out at random. Auto = decide by task name.")]
    [SerializeField] private TaskKind kind = TaskKind.Auto;

    [Header("Visual Feedback (optional)")]
    [SerializeField] private GameObject promptIndicator;
    [Tooltip("The TargetOutline component on whatever GameObject actually has the visible task-machine sprite (may be this same object, or a child — TargetOutline needs a SpriteRenderer to work from). Leave empty to skip the glow and keep using only promptIndicator.")]
    [SerializeField] private TargetOutline targetOutline;

    // Tasks are per player now: "complete" means complete for the LOCAL player.
    public bool IsComplete => TaskManager.Instance != null && TaskManager.Instance.IsDoneForMe(this);
    public string TaskName => taskName;
    public string RoomName => roomName;
    public TaskKind Kind => kind != TaskKind.Auto ? kind : DefaultKind(taskName);

    private static TaskKind DefaultKind(string name)
    {
        switch (name)
        {
            case "Swipe Card":
            case "Fix Wiring":
                return TaskKind.Common;
            case "Align Engine":
            case "Fuel Engines":
            case "Start Reactor":
            case "Submit Scan":
                return TaskKind.Long;
            default:
                return TaskKind.Short;
        }
    }

    private GameObject marker;

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (promptIndicator != null)
            promptIndicator.SetActive(false);

        // The yellow marker on the map is only shown for tasks that are yours.
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name == "Marker") { marker = t.gameObject; break; }
        if (marker != null) marker.SetActive(false);
    }

    // Called by TaskManager whenever the local player's task list changes: show the marker
    // only while this task is still to do, and clear the glow/prompt once it's done
    // (no OnTriggerExit2D fires if you finish it while standing inside the trigger).
    public void RefreshForMe()
    {
        bool pending = TaskManager.Instance != null && TaskManager.Instance.IsPendingForMe(this);
        if (marker != null) marker.SetActive(pending);
        if (pending) return;

        if (promptIndicator != null) promptIndicator.SetActive(false);
        if (targetOutline != null) targetOutline.SetHighlighted(false);

        var local = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        var pc = local != null ? local.GetComponent<PlayerController>() : null;
        if (pc != null && pc.CurrentTaskZone == this) pc.CurrentTaskZone = null;
    }

    // Only the local player who's actually standing in range should see this
    // zone glow — same local-only reasoning as kill/heal highlighting
    // (TargetOutline itself is never networked), it's just driven by this
    // trigger instead of a distance scan since the trigger already exists.
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && player.IsOwner && !IsMaligno(player) && TaskManager.Instance != null && TaskManager.Instance.IsPendingForMe(this))
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
    private static bool IsMaligno(Component c)
    {
        var role = c != null ? c.GetComponent<PlayerRole>() : null;
        return role != null && role.IsImpostor;
    }

    public void TryComplete()
    {
        if (TaskMiniGame.IsOpen || TaskManager.Instance == null || !TaskManager.Instance.IsPendingForMe(this)) return; // not one of my tasks, or already done
        var local = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (local != null && IsMaligno(local)) return; // Maligno do not do chores
        // Player finishes a short mini-game first; only then does the server
        // get asked to mark the task done.
        TaskMiniGame.Open(roomName, taskName, () => { if (this != null && !IsComplete) RequestCompleteServerRpc(); });
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCompleteServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Maligno never do tasks; the server is the real authority.
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var sender)
            && sender.PlayerObject != null && IsMaligno(sender.PlayerObject)) return;

        // TaskManager checks the task really is one of this player's and not done yet.
        TaskManager.Instance?.ServerCompleteTask(senderId, this);
    }
}