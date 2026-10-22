using UnityEditor;
using UnityEngine;

// Applies the hand-made Banana Trunk and Nuno Mound sprites to their prefabs.
public static class MalignoAbilitySprites
{
    const string Dir = "Assets/Sprites/Abilities/";
    const string TrunkPrefab = "Assets/Prefabs/Abilities/Banana Trunk.prefab";
    const string MoundPrefab = "Assets/Prefabs/Abilities/Nuno Mound Trap.prefab";
    const float TrunkWidthUnits = 1.8f;
    const float MoundWidthUnits = 1.1f;

    [MenuItem("Maligno/9. Apply Trunk And Mound Sprites")]
    public static void Run()
    {
        Apply(Dir + "BananaTrunk.png", TrunkPrefab, TrunkWidthUnits);
        Apply(Dir + "NunoMound.png", MoundPrefab, MoundWidthUnits);
        AssetDatabase.SaveAssets();
    }

    static void Apply(string png, string prefab, float widthUnits)
    {
        var imp = AssetImporter.GetAtPath(png) as TextureImporter;
        if (imp == null) { Debug.LogError("[AbilitySprites] Missing " + png); return; }
        var tex = new Texture2D(2, 2);
        tex.LoadImage(System.IO.File.ReadAllBytes(png));
        float w = tex.width;
        Object.DestroyImmediate(tex);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsToUnits = w / widthUnits;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);
        var root = PrefabUtility.LoadPrefabContents(prefab);
        try
        {
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) { Debug.LogError("[AbilitySprites] No SpriteRenderer on " + prefab); return; }
            sr.sprite = sprite;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Simple;
            root.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(root, prefab);
            Debug.Log("[AbilitySprites] " + prefab + " now uses " + png);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
