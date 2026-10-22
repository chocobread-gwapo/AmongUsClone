#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Swaps the player's box "feet" collider for a rounded capsule. A box corner
// catches on polygon facets (round tables) and makes the character shake while
// pushing into them; a rounded shape slides off cleanly.
public static class MalignoPlayerCapsule
{
    [MenuItem("Maligno/13. Round Player Feet Collider")]
    public static void Run()
    {
        const string path = "Assets/Prefabs/Player/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        BoxCollider2D box = null;
        foreach (var b in root.GetComponents<BoxCollider2D>()) if (!b.isTrigger) { box = b; break; }
        if (box == null) { Debug.LogWarning("[Capsule] No solid box collider found (already rounded?)."); PrefabUtility.UnloadPrefabContents(root); return; }
        var off = box.offset; var size = box.size; var mat = box.sharedMaterial;
        Object.DestroyImmediate(box);
        var cap = root.AddComponent<CapsuleCollider2D>();
        cap.direction = CapsuleDirection2D.Horizontal;
        cap.offset = off;
        var csize = new Vector2(size.x, Mathf.Min(size.y, size.x * 0.7f));
        cap.size = csize;
        cap.sharedMaterial = mat;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[Capsule] Player feet collider is now a capsule: offset " + off + ", size " + csize);
    }
}
#endif
