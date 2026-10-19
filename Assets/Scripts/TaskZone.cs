using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class TaskZone : NetworkBehaviour
{
    [Header("Task Settings")]
    [SerializeField] private string taskName = "Task";

    // Synced so every client knows if this specific task is already done
    // (prevents two players both "completing" the same task)
    private NetworkVariable<bool> isComplete = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Visual Feedback (optional)")]
    [SerializeField] private GameObject promptIndicator;

    public bool IsComplete => isComplete.Value;
    public string TaskName => taskName;

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (promptIndicator != null)
            promptIndicator.SetActive(false);

        // NEW — set the floating label to this task's name, e.g. "Wiring"
        GetComponent<WorldLabel>()?.SetLabel(taskName);
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
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null && player.IsOwner && !IsComplete)
        {
            player.CurrentTaskZone = this;
            if (promptIndicator != null)
                promptIndicator.SetActive(true);
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
        }
    }

    // Called locally by the owning player; requests completion from the server
    public void TryComplete()
    {
        if (IsComplete) return;
        RequestCompleteServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCompleteServerRpc()
    {
        if (isComplete.Value) return;

        isComplete.Value = true;
        Debug.Log($"[Server] Task completed: {taskName}");

        TaskManager.Instance?.ServerReportTaskCompleted(this);
    }
}