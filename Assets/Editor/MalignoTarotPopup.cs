using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the Tarot card popup UI under the Game canvas. Called from
// MalignoSceneBuilder.BuildGameHud so rebuilding the scene keeps it.
public static class MalignoTarotPopup
{
    const string Dir = "Assets/Sprites/Tarot/";

    public static void ImportArt()
    {
        foreach (var f in new[] { "TarotCardBackDesign.jpg", "TheFool.png", "TheStar.png" })
        {
            var path = Dir + f;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }
    }

    public static void AddTo(RectTransform canvas, Transform controllers, TMP_FontAsset font)
    {
        ImportArt();

        var root = new GameObject("TarotPopup", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas, false);
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
        rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        var group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;

        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(root.transform, false);
        var drt = dim.GetComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
        var dimImg = dim.GetComponent<Image>(); dimImg.color = new Color(0, 0, 0, 0.55f); dimImg.raycastTarget = false;

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0, 70);
        crt.sizeDelta = new Vector2(330, 550);
        var cardImg = card.GetComponent<Image>();
        cardImg.preserveAspect = true; cardImg.raycastTarget = false;
        var back = AssetDatabase.LoadAssetAtPath<Sprite>(Dir + "TarotCardBackDesign.jpg");
        cardImg.sprite = back;

        var name = Txt("CardName", root.transform, font, 54, FontStyles.Bold, Color.white, new Vector2(0, -250), new Vector2(900, 70));
        var orient = Txt("Orientation", root.transform, font, 36, FontStyles.Bold, Color.white, new Vector2(0, -312), new Vector2(900, 50));
        var effect = Txt("Effect", root.transform, font, 30, FontStyles.Normal, new Color(0.9f, 0.9f, 0.9f), new Vector2(0, -366), new Vector2(1000, 50));

        var host = controllers.gameObject.AddComponent<TarotCardPopupUI>();
        var so = new SerializedObject(host);
        so.FindProperty("popupGroup").objectReferenceValue = group;
        so.FindProperty("cardImage").objectReferenceValue = cardImg;
        so.FindProperty("cardNameText").objectReferenceValue = name;
        so.FindProperty("orientationText").objectReferenceValue = orient;
        so.FindProperty("effectText").objectReferenceValue = effect;
        so.FindProperty("backSprite").objectReferenceValue = back;
        var arr = so.FindProperty("cardArt");
        var names = new[] { "The Fool", "The Star" };
        var files = new[] { "TheFool.png", "TheStar.png" };
        arr.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("cardName").stringValue = names[i];
            e.FindPropertyRelative("sprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Dir + files[i]);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(false);
    }

    static TextMeshProUGUI Txt(string n, Transform parent, TMP_FontAsset font, float size, FontStyles style, Color c, Vector2 pos, Vector2 sz)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sz;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size; t.fontStyle = style; t.color = c;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }

    [MenuItem("Maligno/15. Preview Tarot Popup (Play mode)")]
    static void Preview()
    {
        if (!Application.isPlaying || TarotCardPopupUI.Instance == null) { Debug.LogWarning("[Tarot] Enter Play mode in the Game scene first."); return; }
        TarotCardPopupUI.Instance.ShowCard("The Fool", "UPRIGHT", "+50% move speed", true);
    }

    [MenuItem("Maligno/16. Preview Tarot Popup Slow-Mo (Play mode)")]
    static void PreviewSlow()
    {
        if (!Application.isPlaying || TarotCardPopupUI.Instance == null) { Debug.LogWarning("[Tarot] Enter Play mode in the Game scene first."); return; }
        Time.timeScale = 0.1f;
        TarotCardPopupUI.Instance.ShowCard("The Star", "REVERSED", "-50% vision radius", false);
    }

    [MenuItem("Maligno/17. Reset Time Scale")]
    static void ResetTime() { Time.timeScale = 1f; }

    [MenuItem("Maligno/14. Import Tarot Art")]
    static void Menu() { ImportArt(); }
}
