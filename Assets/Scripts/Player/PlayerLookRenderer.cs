using UnityEngine;

// Draws a player's shirt / pants / hat as three child SpriteRenderers that mirror the base character
// sprite (same walk frame, flip, sorting layer and alpha). Runs after PlayerFogHide so the fade and
// ghost visibility rules apply to the clothes too.
[DefaultExecutionOrder(1100)]
public class PlayerLookRenderer : MonoBehaviour
{
    public const string LayerPrefix = "LookLayer";

    private SpriteRenderer baseSr;
    private PlayerLabel label;
    private readonly SpriteRenderer[] layers = new SpriteRenderer[3];
    private static readonly string[] LayerNames = { "Shirt", "Pants", "Hat" };

    private void Awake()
    {
        baseSr = GetComponent<SpriteRenderer>();
        label = GetComponent<PlayerLabel>();
        if (baseSr == null) { enabled = false; return; }

        for (int i = 0; i < layers.Length; i++)
        {
            var go = new GameObject(LayerPrefix + "_" + LayerNames[i]);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = baseSr.sharedMaterial;
            sr.enabled = false;
            layers[i] = sr;
        }
    }

    private void LateUpdate()
    {
        if (baseSr == null || label == null) return;

        Sprite baseSprite = baseSr.sprite;
        int frame = LookCatalog.FrameIndex(baseSprite);
        Color baseColor = baseSr.color;
        bool show = baseSr.enabled && baseColor.a > 0.01f && frame >= 0 && label.IsSpawned;
        if (!show)
        {
            for (int i = 0; i < layers.Length; i++) if (layers[i].enabled) layers[i].enabled = false;
            return;
        }

        LookData look = label.CurrentLook;
        float ppu = baseSprite.pixelsPerUnit / 33f; // the art is 33x upscaled native pixels

        Apply(0, LookCatalog.LayerShirt, look.shirt, look.color, frame, ppu, baseColor, 1);
        Apply(1, LookCatalog.LayerPants, look.pants, look.pantsColor, frame, ppu, baseColor, 2);
        Apply(2, LookCatalog.LayerHat, look.hat, look.hatColor, frame, ppu, baseColor, 3);
    }

    private void Apply(int index, int layerId, int style, int colorIndex, int frame, float ppu, Color baseColor, int orderOffset)
    {
        SpriteRenderer sr = layers[index];
        bool hasStyle = !(layerId != LookCatalog.LayerShirt && style == 0);
        Sprite sprite = hasStyle ? LookCatalog.GetSprite(layerId, style, frame, ppu) : null;
        if (sprite == null) { if (sr.enabled) sr.enabled = false; return; }

        Color tint = LookCatalog.Palette[colorIndex].Color;
        sr.sprite = sprite;
        sr.flipX = baseSr.flipX;
        sr.flipY = baseSr.flipY;
        sr.sortingLayerID = baseSr.sortingLayerID;
        sr.sortingOrder = baseSr.sortingOrder + orderOffset;
        sr.color = new Color(tint.r * baseColor.r, tint.g * baseColor.g, tint.b * baseColor.b, baseColor.a);
        sr.enabled = true;
    }
}
