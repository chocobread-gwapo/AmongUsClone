using UnityEditor;
using UnityEngine;

// Imports the 12 generated walk-frame PNGs and wires them into the Player
// prefab's CharacterSpriteAnimator (3 frames per direction).
public static class MalignoWalkFramesSetup
{
    const string Folder = "Assets/Sprites/Characters/Walk";
    const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    const float TargetHeightUnits = 2.09f;   // same on-screen height as before
    static readonly string[] Dirs = { "down", "up", "left", "right", "downleft", "downright", "upleft", "upright" };
    static readonly string[] Fields = { "downFrames", "upFrames", "leftFrames", "rightFrames", "downLeftFrames", "downRightFrames", "upLeftFrames", "upRightFrames" };

    [MenuItem("Maligno/8. Setup Walk Frames")]
    public static void Run()
    {
        // 1) import settings
        for (int d = 0; d < Dirs.Length; d++)
            for (int i = 0; i < 3; i++)
            {
                string path = $"{Folder}/walk_{Dirs[d]}_{i}.png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) { Debug.LogError("[WalkFrames] Missing " + path); return; }
                var tex = new Texture2D(2, 2);
                tex.LoadImage(System.IO.File.ReadAllBytes(path));
                float h = tex.height;
                Object.DestroyImmediate(tex);

                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsToUnits = h / TargetHeightUnits;
                var ts = new TextureImporterSettings();
                imp.ReadTextureSettings(ts);
                ts.spriteAlignment = (int)SpriteAlignment.Center;
                ts.spritePivot = new Vector2(0.5f, 0.5f);
                imp.SetTextureSettings(ts);
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }

        // 2) assign to the animator
        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var anim = root.GetComponent<CharacterSpriteAnimator>();
            if (anim == null) { Debug.LogError("[WalkFrames] Player has no CharacterSpriteAnimator."); return; }
            var so = new SerializedObject(anim);
            for (int d = 0; d < Dirs.Length; d++)
            {
                var arr = so.FindProperty(Fields[d]);
                arr.arraySize = 3;
                for (int i = 0; i < 3; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/walk_{Dirs[d]}_{i}.png");
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/walk_down_0.png");

            // Same feet collider placement as the shadow in the new frames.
            var feet = root.GetComponent<BoxCollider2D>();
            if (feet != null && sr != null && sr.sprite != null)
            {
                Bounds b = sr.sprite.bounds;
                feet.offset = new Vector2(b.center.x, b.min.y + 0.097f * b.size.y);
                feet.size = new Vector2(b.size.x * 0.70f, b.size.y * 0.12f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("[WalkFrames] 24 walk frames (8 directions) assigned to the Player prefab.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
