using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Among Us-style task list: shows only the tasks given to the local player,
// e.g. "Storage: Fuel Engines", greyed out once done.
public class TaskListUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform listContainer;   // the Vertical Layout Group content area
    [SerializeField] private GameObject entryPrefab;     // a single TextMeshProUGUI prefab for one list line
    [SerializeField] private RectTransform containerRect; // same object as listContainer, for layout rebuild

    [Header("Style")]
    [SerializeField] private Color incompleteColor = Color.white;
    [SerializeField] private Color completeColor = new Color(1f, 1f, 1f, 0.4f);

    private readonly List<GameObject> spawnedEntries = new List<GameObject>();

    private void OnEnable()
    {
        TaskManager.OnTaskProgressChanged += HandleProgressChanged;
        TaskManager.OnMyTasksChanged += Refresh;
        Refresh();
    }

    // Tab collapses/expands the task list so it doesn't cover the map.
    private bool hiddenForMaligno;

    private void Update()
    {
        var local = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        var role = local != null ? local.GetComponent<PlayerRole>() : null;
        if (role != null && listContainer != null)
        {
            if (role.IsImpostor)
            {
                if (listContainer.gameObject.activeSelf) listContainer.gameObject.SetActive(false);
                hiddenForMaligno = true;
                return; // no Tab toggle for Maligno
            }
            if (hiddenForMaligno) { listContainer.gameObject.SetActive(true); hiddenForMaligno = false; }
        }

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && listContainer != null)
            listContainer.gameObject.SetActive(!listContainer.gameObject.activeSelf);
    }

    private void OnDisable()
    {
        TaskManager.OnTaskProgressChanged -= HandleProgressChanged;
        TaskManager.OnMyTasksChanged -= Refresh;
    }

    private void HandleProgressChanged(int completed, int total)
    {
        Refresh();
    }

    private void Refresh()
    {
        foreach (var entry in spawnedEntries)
        {
            if (entry != null) Destroy(entry);
        }
        spawnedEntries.Clear();

        // Only the local player's own tasks.
        var mine = TaskManager.Instance != null ? TaskManager.Instance.MyTasks() : new List<(TaskZone zone, bool done)>();
        mine.Sort((x, y) =>
        {
            int c = string.CompareOrdinal(x.zone.RoomName, y.zone.RoomName);
            return c != 0 ? c : string.CompareOrdinal(x.zone.TaskName, y.zone.TaskName);
        });

        foreach (var (zone, done) in mine)
        {
            string room = zone.RoomName;
            string label = string.IsNullOrEmpty(room) ? zone.TaskName : $"{room}: {zone.TaskName}";

            GameObject entry = Instantiate(entryPrefab, listContainer);
            TextMeshProUGUI tmp = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = label;
                tmp.color = done ? completeColor : incompleteColor;
            }
            spawnedEntries.Add(entry);
        }

        if (containerRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
        }
    }
}