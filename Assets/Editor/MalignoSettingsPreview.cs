#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Editor helper: shows the Game Settings window in any scene while in Play mode,
// so the layout can be checked without starting a lobby.
public static class MalignoSettingsPreview
{
    [MenuItem("Maligno/26. Preview Settings Window (Play mode)")]
    private static void Preview()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Preview] Press Play first, then run this."); return; }
        if (Object.FindFirstObjectByType<HostSettingsPanelUI>(FindObjectsInactive.Include) != null)
        {
            Object.FindFirstObjectByType<HostSettingsPanelUI>(FindObjectsInactive.Include).gameObject.SetActive(true);
            return;
        }
        new GameObject("SettingsWindowPreview").AddComponent<HostSettingsPanelUI>();
    }
}
#endif
