using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Among Us-style task breakdown panel: a header bar + a per-task list like
// "Storage: Fuel Engines (0/2)". Groups scene TaskZones by (room, task name)
// so multiple zones that share a name (e.g. two "Fuel Engines" spots) show
// as one combined count, matching the reference HUD.
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

        TaskZone[] allZones = FindObjectsByType<TaskZone>(FindObjectsSortMode.None);

        // Group by (room, task name) so duplicate task spots combine into one line.
        var groups = allZones
            .GroupBy(z => (z.RoomName, z.TaskName))
            .OrderBy(g => g.Key.RoomName)
            .ThenBy(g => g.Key.TaskName);

        foreach (var group in groups)
        {
            int total = group.Count();
            int completed = group.Count(z => z.IsComplete);
            bool allDone = completed >= total;

            string room = group.Key.RoomName;
            string label = string.IsNullOrEmpty(room) ? group.Key.TaskName : $"{room}: {group.Key.TaskName}";
            if (total > 1)
            {
                label += $" ({completed}/{total})";
            }

            GameObject entry = Instantiate(entryPrefab, listContainer);
            TextMeshProUGUI tmp = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = label;
                tmp.color = allDone ? completeColor : incompleteColor;
            }
            spawnedEntries.Add(entry);
        }

        if (containerRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
        }
    }
}