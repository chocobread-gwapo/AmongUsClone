#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// One-time: moves the controls asset into Assets/Resources so the Settings menu
// can load it (and rebind keys) from any scene, including the main menu.
// Scene/prefab references keep working because Unity moves the .meta with it.
public static class MalignoMoveControls
{
    [MenuItem("Maligno/23. Move Controls To Resources")]
    static void Move()
    {
        const string from = "Assets/Input/PlayerControls.inputactions";
        const string to = "Assets/Resources/PlayerControls.inputactions";
        if (AssetDatabase.LoadMainAssetAtPath(to) != null) { Debug.Log("[Controls] already in Resources"); return; }
        string err = AssetDatabase.MoveAsset(from, to);
        Debug.Log(string.IsNullOrEmpty(err) ? "[Controls] moved to Resources" : "[Controls] move failed: " + err);
        AssetDatabase.SaveAssets();
    }
}
#endif
