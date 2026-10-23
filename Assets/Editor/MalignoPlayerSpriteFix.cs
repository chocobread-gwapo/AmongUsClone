using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

// Re-fits the player sprite after the art was swapped, then sizes the solid
// "feet" collider to the shadow baked into the new sprite.
public static class MalignoPlayerSpriteFix
{
    const string SpritePath = "Assets/Sprites/Characters/CharacterTemplate.png";
    const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";

    // Keeps the on-screen height the old sprite had (2.09 units at scale 1).
    const float TargetHeightUnits = 2.09f;

    // Where the shadow sits inside the new sprite (fractions of the image).
    const float ShadowCentreFromBottom = 0.097f;
    const float ColliderWidthFraction = 0.70f;
    const float ColliderHeightFraction = 0.12f;

    [MenuItem("Maligno/7. Fit Player Sprite And Collider")]
    public static void Run()
    {
        var importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null) { Debug.LogError("[SpriteFix] Not found: " + SpritePath); return; }

        // The texture is read from disk so we know its real pixel size.
        var tex = new Texture2D(2, 2);
        tex.LoadImage(System.IO.File.ReadAllBytes(SpritePath));
        int w = tex.width, h = tex.height;
        Object.DestroyImmediate(tex);

        importer.spritePixelsToUnits = h / TargetHeightUnits;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        // Re-slice the existing sprite to cover the whole new image. Staying in
        // Multiple mode keeps the sprite's internal ID, so every prefab that
        // already points at it keeps working.
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var rects = provider.GetSpriteRects();
        if (rects.Length == 0) { Debug.LogError("[SpriteFix] Texture has no sprite slice."); return; }
        rects[0].rect = new Rect(0, 0, w, h);
        rects[0].alignment = SpriteAlignment.Center;
        rects[0].pivot = new Vector2(0.5f, 0.5f);
        provider.SetSpriteRects(rects);
        provider.Apply();
        importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAllAssetsAtPath(SpritePath).Length > 0
            ? System.Array.Find(AssetDatabase.LoadAllAssetsAtPath(SpritePath), o => o is Sprite) as Sprite
            : null;
        if (sprite == null) { Debug.LogError("[SpriteFix] Sprite missing after reimport."); return; }

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = sprite;

            Bounds b = sprite.bounds; // local space of the player root
            BoxCollider2D feet = null;
            foreach (var c in root.GetComponents<BoxCollider2D>()) { feet = c; break; }
            if (feet == null) feet = root.AddComponent<BoxCollider2D>();
            feet.isTrigger = false;
            feet.offset = new Vector2(b.center.x, b.min.y + ShadowCentreFromBottom * b.size.y);
            feet.size = new Vector2(b.size.x * ColliderWidthFraction, b.size.y * ColliderHeightFraction);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log($"[SpriteFix] Sprite {w}x{h} fitted; feet collider offset {feet.offset} size {feet.size}.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
