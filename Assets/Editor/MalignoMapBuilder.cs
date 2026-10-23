#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Places the high-resolution map image, its wall colliders (merged into one
// CompositeCollider2D so there are no seams) and furniture colliders.
public static class MalignoMapBuilder
{
    const string ImagePath = "Assets/Sprites/Map/Skeld.jpg";
    // The image is 8000x4631 and is 0.99x the scale of the old traced map, so
    // PPU = 4 * 0.99 * 32 keeps task/spawn coordinates valid.
    const float PPU = 126.72f;
    const int ImgW = 8000, ImgH = 4631;

    static void PrepareImage()
    {
        var imp = AssetImporter.GetAtPath(ImagePath) as TextureImporter;
        if (imp == null) { Debug.LogError("[Map] Missing " + ImagePath); return; }
        bool dirty = imp.textureType != TextureImporterType.Sprite || imp.spritePixelsPerUnit != PPU
            || imp.maxTextureSize != 8192 || imp.mipmapEnabled
            || imp.textureCompression != TextureImporterCompression.CompressedHQ;
        if (!dirty) return;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = PPU;
        imp.maxTextureSize = 8192;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear;
        imp.wrapMode = TextureWrapMode.Clamp;
        var ts = new TextureImporterSettings();
        imp.ReadTextureSettings(ts);
        ts.spriteMeshType = SpriteMeshType.FullRect;
        ts.spriteAlignment = (int)SpriteAlignment.Center;
        imp.SetTextureSettings(ts);
        imp.SaveAndReimport();
    }

    static Vector2[] ToPts(float[] f)
    {
        var p = new Vector2[f.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = new Vector2(f[i * 2], f[i * 2 + 1]);
        return p;
    }

    public static bool Fill(Transform root, int wallLayer)
    {
        PrepareImage();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath);
        if (sprite == null) return false;

        var art = new GameObject("MapArt");
        art.transform.SetParent(root, false);
        // Centre of the image in map-local units (alignment measured against the old map).
        float cxPx = ImgW / 2f, cyPx = ImgH / 2f;
        float oldX2k = (cxPx / 4f - 6f) / 0.99f, oldY2k = (cyPx / 4f - 38f) / 0.99f;
        art.transform.localPosition = new Vector3((oldX2k - 1000f) / 32f, (559.5f - oldY2k) / 32f, 0f);
        var sr = art.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -100;

        // Walls: boxes merged into a single composite outline.
        var walls = new GameObject("WallColliders") { layer = wallLayer };
        walls.transform.SetParent(root, false);
        walls.isStatic = true;
        var rb = walls.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var comp = walls.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
        // Smooth outlines (with holes) traced from the hull mask: diagonals are real diagonals now,
        // not stair-steps of little boxes.
        foreach (var o in MalignoMapWalls.Outlines)
        {
            var poly = walls.AddComponent<PolygonCollider2D>();
            poly.pathCount = 1 + o.holes.Length;
            poly.SetPath(0, ToPts(o.outer));
            for (int h = 0; h < o.holes.Length; h++) poly.SetPath(h + 1, ToPts(o.holes[h]));
            poly.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        // Furniture colliders: polygons traced from the actual furniture shapes.
        // Parented under the wall object so every furniture polygon merges into the SAME composite
        // collider as the walls: no internal seams for the player's capsule to catch on.
        var fur = new GameObject("FurnitureColliders") { layer = wallLayer };
        fur.transform.SetParent(walls.transform, false);
        fur.isStatic = true;
        foreach (var f in MalignoMapFurniture.Polys)
        {
            var go = new GameObject(f.name) { layer = wallLayer };
            go.transform.SetParent(fur.transform, false);
            go.isStatic = true;
            var pts = new Vector2[f.pts.Length / 2];
            for (int k = 0; k < pts.Length; k++) pts[k] = new Vector2(f.pts[k * 2], f.pts[k * 2 + 1]);
            var poly = go.AddComponent<PolygonCollider2D>();
            poly.pathCount = 1;
            poly.SetPath(0, pts);
            poly.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        // Furniture cutouts: the same pixels as the map, drawn above the player when he stands behind the piece.
        var cutRoot = new GameObject("FurnitureCutouts");
        cutRoot.transform.SetParent(root, false);
        foreach (var c in MalignoMapFurniture.Cutouts)
        {
            string path = "Assets/Sprites/Map/Cutouts/" + c.name + ".png";
            var cimp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (cimp == null) { Debug.LogWarning("[Map] Missing cutout " + path); continue; }
            if (cimp.textureType != TextureImporterType.Sprite || cimp.spritePixelsPerUnit != PPU || cimp.textureCompression != TextureImporterCompression.Uncompressed)
            {
                cimp.textureType = TextureImporterType.Sprite;
                cimp.spriteImportMode = SpriteImportMode.Single;
                cimp.spritePixelsPerUnit = PPU;
                cimp.mipmapEnabled = false;
                cimp.alphaIsTransparency = true;
                cimp.textureCompression = TextureImporterCompression.Uncompressed;
                cimp.filterMode = FilterMode.Bilinear;
                var cs = new TextureImporterSettings();
                cimp.ReadTextureSettings(cs);
                cs.spriteAlignment = (int)SpriteAlignment.Center;
                cs.spriteMeshType = SpriteMeshType.FullRect;
                cimp.SetTextureSettings(cs);
                cimp.SaveAndReimport();
            }
            var csprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var cgo = new GameObject("Cut_" + c.name);
            cgo.transform.SetParent(cutRoot.transform, false);
            cgo.transform.localPosition = new Vector3(c.x, c.y, 0f);
            var csr = cgo.AddComponent<SpriteRenderer>();
            csr.sprite = csprite;
            var sorter = cgo.AddComponent<FootLineSorter>();
            var so = new SerializedObject(sorter);
            so.FindProperty("footLocalY").floatValue = c.footY;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return true;
    }

    [MenuItem("Maligno/11. Build Map From Image")]
    public static void Build() { MalignoSceneBuilder.BuildMapPrefab(); }
}
#endif
