using UnityEngine;
using UnityEngine.SceneManagement;

// Ghosts (dead players) pass through inner walls, doors and furniture, but must stay inside the map.
//  * Ghost <-> Wall layer collision is switched off at startup.
//  * In the Game scene, a "GhostBounds" collider is built from the map's outermost wall outline: a solid
//    ring around the outside of the walkable area, on its own layer that only ghosts collide with.
public static class GhostBounds
{
    public const int BoundsLayer = 7; // unused slot in the project's layer list

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        int ghost = LayerMask.NameToLayer("Ghost");
        int wall = LayerMask.NameToLayer("Wall");
        if (ghost >= 0 && wall >= 0) Physics2D.IgnoreLayerCollision(ghost, wall, true);

        // The bounds ring is for ghosts only.
        for (int i = 0; i < 32; i++)
        {
            if (i == ghost) continue;
            Physics2D.IgnoreLayerCollision(BoundsLayer, i, true);
        }
        if (ghost >= 0) Physics2D.IgnoreLayerCollision(BoundsLayer, ghost, false);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Game") return;
        foreach (var comp in Object.FindObjectsByType<CompositeCollider2D>(FindObjectsInactive.Include))
        {
            if (comp.name != "WallColliders") continue;
            Build(comp.transform);
            return;
        }
        Debug.LogWarning("[GhostBounds] WallColliders not found; ghosts will not be kept inside the map.");
    }

    private static void Build(Transform walls)
    {
        // The outline polygon with the biggest outer ring is the solid "outside of the map";
        // its biggest hole is the walkable area's outer edge: the main walls.
        PolygonCollider2D best = null; float bestArea = 0f;
        foreach (var p in walls.GetComponents<PolygonCollider2D>())
        {
            if (p.pathCount == 0) continue;
            float a = Area(p.GetPath(0));
            if (a > bestArea) { bestArea = a; best = p; }
        }
        if (best == null || best.pathCount < 2) { Debug.LogWarning("[GhostBounds] No outer outline found."); return; }

        int holeIndex = -1; float holeArea = 0f;
        for (int i = 1; i < best.pathCount; i++)
        {
            float a = Area(best.GetPath(i));
            if (a > holeArea) { holeArea = a; holeIndex = i; }
        }
        if (holeIndex < 0) return;

        var go = new GameObject("GhostBounds") { layer = BoundsLayer };
        go.transform.SetParent(walls.parent, false);
        go.transform.SetPositionAndRotation(walls.position, walls.rotation);
        go.transform.localScale = walls.localScale;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var poly = go.AddComponent<PolygonCollider2D>();
        poly.pathCount = 2;
        poly.SetPath(0, best.GetPath(0));
        poly.SetPath(1, best.GetPath(holeIndex));
        Debug.Log("[GhostBounds] Built ghost boundary from the main wall outline.");
    }

    private static float Area(Vector2[] pts)
    {
        float a = 0f;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++) a += (pts[j].x + pts[i].x) * (pts[j].y - pts[i].y);
        return Mathf.Abs(a * 0.5f);
    }
}
