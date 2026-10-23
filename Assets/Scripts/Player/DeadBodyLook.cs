using UnityEngine;

// Dresses a DeadBody with the dead player's shirt / pants / hat.
// Added at runtime by DeadBody. Art: Resources/Look/b{shirts,pants,hats}.bytes
// (PNG, cell 665x350: 280-row corpse + 70 rows head-room, styles stacked).
public class DeadBodyLook : MonoBehaviour
{
    const int CellW = 665, CellH = 350;
    static readonly string[] Names = { "bshirts", "bpants", "bhats" };
    static readonly Texture2D[] atlases = new Texture2D[3];

    SpriteRenderer baseRenderer;
    SpriteRenderer[] layers = new SpriteRenderer[3];
    int applied = -2;

    static Texture2D Atlas(int i)
    {
        if (atlases[i] != null) return atlases[i];
        var ta = Resources.Load<TextAsset>("Look/" + Names[i]);
        if (ta == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        t.LoadImage(ta.bytes);
        return atlases[i] = t;
    }

    public void Apply(int packed)
    {
        if (packed == applied || packed < 0) return;
        applied = packed;
        if (baseRenderer == null) baseRenderer = GetComponent<SpriteRenderer>();
        if (baseRenderer == null || baseRenderer.sprite == null) return;
        var look = LookData.Unpack(packed).Clamped();
        int[] style = { look.shirt, look.pants, look.hat };
        Color[] tint =
        {
            LookCatalog.Palette[look.color].Color,
            LookCatalog.Palette[look.pantsColor].Color,
            LookCatalog.Palette[look.color].Color,
        };
        tint[2] = LookCatalog.Palette[look.hatColor].Color;
        float ppu = baseRenderer.sprite.pixelsPerUnit;
        for (int i = 0; i < 3; i++)
        {
            if (layers[i] == null)
            {
                var go = new GameObject("LookLayer_Body" + i);
                go.transform.SetParent(transform, false);
                layers[i] = go.AddComponent<SpriteRenderer>();
                layers[i].sharedMaterial = baseRenderer.sharedMaterial;
            }
            var tex = Atlas(i);
            bool show = tex != null && !(i == 1 && style[i] == 0) && !(i == 2 && style[i] == 0);
            layers[i].enabled = show;
            if (!show) continue;
            var rect = new Rect(0, tex.height - (style[i] + 1) * CellH, CellW, CellH);
            layers[i].sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.6f), ppu);
            var c = tint[i]; c.a = 1f; layers[i].color = c;
        }
    }

    void LateUpdate()
    {
        if (baseRenderer == null) return;
        for (int i = 0; i < 3; i++)
        {
            if (layers[i] == null) continue;
            layers[i].sortingLayerID = baseRenderer.sortingLayerID;
            layers[i].sortingOrder = baseRenderer.sortingOrder + 1 + i;
            layers[i].flipX = baseRenderer.flipX;
            var c = layers[i].color; c.a = baseRenderer.color.a; layers[i].color = c;
        }
    }
}
