#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Moves the traced-image map assets (no longer used) to the OS recycle bin.
public static class MalignoRemoveOldMap
{
    static readonly string[] Paths =
    {
        "Assets/Sprites/Map/MapTemplate.png",
        "Assets/Sprites/Map/Pieces",
        "Assets/Sprites/Furniture",
        "Assets/Prefabs/Furniture",
        "Assets/Editor/MalignoFurniture.cs",
        "Assets/Editor/MalignoMapPieces.cs",
        "Assets/Editor/MapTemplateFaces.cs",
        "Assets/Editor/MalignoMapData.cs",
        "Assets/Editor/MalignoMapBuilder.cs",
        "Assets/Sprites/UI/Circle.png",
    };

    [MenuItem("Maligno/12. Remove Old Map Assets")]
    public static void Run()
    {
        int n = 0;
        foreach (var p in Paths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(p) == null && !AssetDatabase.IsValidFolder(p)) continue;
            if (AssetDatabase.MoveAssetToTrash(p)) { n++; Debug.Log("[Cleanup] Trashed " + p); }
            else Debug.LogWarning("[Cleanup] Could not trash " + p);
        }
        AssetDatabase.Refresh();
        Debug.Log("[Cleanup] Removed " + n + " old map assets.");
    }
}
#endif
