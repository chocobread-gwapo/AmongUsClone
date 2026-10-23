using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// One player's cosmetic choices. Packed into a single int so it can ride in one NetworkVariable.
// Bits: color 4 | shirt 3 | pants 3 | pantsColor 4 | hat 3 | hatColor 4.
public struct LookData
{
    public int color, shirt, pants, pantsColor, hat, hatColor;

    public int Pack()
    {
        return (color & 15) | ((shirt & 7) << 4) | ((pants & 7) << 7)
             | ((pantsColor & 15) << 10) | ((hat & 7) << 14) | ((hatColor & 15) << 17);
    }

    public static LookData Unpack(int p)
    {
        LookData d;
        d.color = p & 15;
        d.shirt = (p >> 4) & 7;
        d.pants = (p >> 7) & 7;
        d.pantsColor = (p >> 10) & 15;
        d.hat = (p >> 14) & 7;
        d.hatColor = (p >> 17) & 15;
        return d.Clamped();
    }

    public static LookData Default(int color)
    {
        LookData d;
        d.color = color; d.shirt = 0; d.pants = 1; d.pantsColor = 1; d.hat = 0; d.hatColor = 0;
        return d.Clamped();
    }

    public LookData Clamped()
    {
        LookData d = this;
        d.color = Mathf.Clamp(d.color, 0, LookCatalog.PaletteCount - 1);
        d.pantsColor = Mathf.Clamp(d.pantsColor, 0, LookCatalog.PaletteCount - 1);
        d.hatColor = Mathf.Clamp(d.hatColor, 0, LookCatalog.PaletteCount - 1);
        d.shirt = Mathf.Clamp(d.shirt, 0, LookCatalog.ShirtNames.Length - 1);
        d.pants = Mathf.Clamp(d.pants, 0, LookCatalog.PantsNames.Length - 1);
        d.hat = Mathf.Clamp(d.hat, 0, LookCatalog.HatNames.Length - 1);
        return d;
    }
}

// Fixed palette + style names + overlay sprite atlases (Resources/Look/*.bytes are PNGs, one cell per
// style x walk frame, 19x46 native pixels each; they are tinted at runtime).
public static class LookCatalog
{
    public static readonly (string Name, Color Color)[] Palette =
    {
        ("Red", new Color(0.78f, 0.12f, 0.12f)),
        ("Blue", new Color(0.13f, 0.35f, 0.85f)),
        ("Green", new Color(0.16f, 0.6f, 0.2f)),
        ("Pink", new Color(0.93f, 0.4f, 0.75f)),
        ("Orange", new Color(0.95f, 0.55f, 0.1f)),
        ("Yellow", new Color(0.95f, 0.85f, 0.15f)),
        ("Purple", new Color(0.5f, 0.2f, 0.75f)),
        ("Cyan", new Color(0.2f, 0.8f, 0.85f)),
        ("Brown", new Color(0.55f, 0.33f, 0.15f)),
        ("Lime", new Color(0.55f, 0.9f, 0.2f)),
        ("White", new Color(0.95f, 0.95f, 0.95f)),
        ("Gray", new Color(0.42f, 0.45f, 0.5f)),
    };

    public static int PaletteCount => Palette.Length;

    public static readonly string[] ShirtNames = { "T-Shirt", "Long Sleeve", "Tank Top", "Striped Tee", "Hoodie", "V-Neck" };
    public static readonly string[] PantsNames = { "None", "Long Pants", "Shorts", "Capris", "Jeans" };
    public static readonly string[] HatNames = { "None", "Cap", "Beanie", "Salakot", "Headband", "Top Hat", "Crown" };

    public const int LayerShirt = 0, LayerPants = 1, LayerHat = 2;
    private static readonly string[] AtlasNames = { "shirts", "pants", "hats" };
    private static readonly int[] StyleCounts = { 6, 5, 7 };
    private const int CellW = 19, CellH = 46, Frames = 24;
    // Where the base sprite's pivot sits inside a cell (the cell has 4 rows of head-room on top).
    public static readonly Vector2 Pivot = new Vector2(0.5f, 21f / 46f);

    private static readonly Texture2D[] atlases = new Texture2D[3];
    private static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();

    private static Texture2D Atlas(int layer)
    {
        if (atlases[layer] != null) return atlases[layer];
        TextAsset asset = Resources.Load<TextAsset>("Look/" + AtlasNames[layer]);
        if (asset == null) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.LoadImage(asset.bytes);
        atlases[layer] = tex;
        return tex;
    }

    public static Sprite GetSprite(int layer, int style, int frame, float pixelsPerUnit)
    {
        if (style < 0 || style >= StyleCounts[layer] || frame < 0 || frame >= Frames) return null;
        int key = layer * 100000 + style * 100 + frame;
        if (sprites.TryGetValue(key, out Sprite s) && s != null) return s;
        Texture2D tex = Atlas(layer);
        if (tex == null) return null;
        var rect = new Rect(frame * CellW, tex.height - (style + 1) * CellH, CellW, CellH);
        s = Sprite.Create(tex, rect, Pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        s.name = $"look_{AtlasNames[layer]}_{style}_{frame}";
        sprites[key] = s;
        return s;
    }

    // "walk_downleft_2" -> frame index 0..23 (direction * 3 + step), or -1 if it isn't a walk frame.
    private static readonly Dictionary<string, int> frameCache = new Dictionary<string, int>();
    public static int FrameIndex(Sprite sprite)
    {
        if (sprite == null) return -1;
        string n = sprite.name;
        if (frameCache.TryGetValue(n, out int cached)) return cached;
        int result = -1;
        if (n.StartsWith("walk_"))
        {
            string[] parts = n.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[2], out int step) && step >= 0 && step < 3)
            {
                int dir = -1;
                switch (parts[1])
                {
                    case "down": dir = 0; break;
                    case "up": dir = 1; break;
                    case "left": dir = 2; break;
                    case "right": dir = 3; break;
                    case "downleft": dir = 4; break;
                    case "downright": dir = 5; break;
                    case "upleft": dir = 6; break;
                    case "upright": dir = 7; break;
                }
                if (dir >= 0) result = dir * 3 + step;
            }
        }
        frameCache[n] = result;
        return result;
    }

    private const string PrefKey = "maligno.look";
    public static int LoadSaved() { return PlayerPrefs.GetInt(PrefKey, -1); }
    public static void Save(int packed) { PlayerPrefs.SetInt(PrefKey, packed); PlayerPrefs.Save(); }
}


// Puts a player's clothes on a UI portrait (an Image showing the idle front-facing walk frame).
public static class LookPortrait
{
    public static void Attach(Image baseImage, LookData look, Color tint)
    {
        if (baseImage == null) return;
        Transform old = baseImage.transform.Find("LookOverlay");
        if (old != null) Object.Destroy(old.gameObject);

        var box = new GameObject("LookOverlay", typeof(RectTransform), typeof(AspectRatioFitter));
        var brt = (RectTransform)box.transform;
        brt.SetParent(baseImage.transform, false);
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        var fit = box.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 19f / 42f; // same box the preserve-aspect character occupies

        int[] styles = { look.shirt, look.pants, look.hat };
        int[] colors = { look.color, look.pantsColor, look.hatColor };
        for (int layer = 0; layer < 3; layer++)
        {
            if (layer != LookCatalog.LayerShirt && styles[layer] == 0) continue;
            Sprite sprite = LookCatalog.GetSprite(layer, styles[layer], 0, 100f);
            if (sprite == null) continue;
            var go = new GameObject("Layer" + layer, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(brt, false);
            // The art cell has 4 extra rows of head-room above the 42-row body.
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 46f / 42f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            Color c = LookCatalog.Palette[colors[layer]].Color;
            img.color = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, tint.a);
            img.raycastTarget = false;
        }
    }
}
