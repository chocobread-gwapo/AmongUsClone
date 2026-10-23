#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Player size / name tag / vision glow pass.
public static class MalignoPlayerLook
{
    const float Scale = 0.65f;      // was 0.5 (1.3x)
    const float Vision = 5f;        // was 3.5 (local light radius; world = x scale)
    const float LabelY = 1.45f, LabelFont = 3f;

    [MenuItem("Maligno/18. Player Size, Name Tag, Vision")]
    public static void Run()
    {
        const string path = "Assets/Prefabs/Player/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        root.transform.localScale = Vector3.one * Scale;

        var label = root.transform.Find("Label");
        if (label != null)
        {
            var rt = (RectTransform)label;
            rt.anchoredPosition = new Vector2(0, LabelY);
            var tmp = label.GetComponent<TMP_Text>();
            if (tmp != null) tmp.fontSize = LabelFont;
        }
        var tarot = root.transform.Find("TarotBuffText");
        if (tarot != null) ((RectTransform)tarot).anchoredPosition = new Vector2(0, 2.1f);

        var vl = root.transform.Find("VisionLight");
        if (vl != null)
        {
            var l = vl.GetComponent<Light2D>();
            if (l != null) l.pointLightOuterRadius = Vision;
        }
        var pv = root.GetComponent<PlayerVision>();
        if (pv != null)
        {
            var so = new SerializedObject(pv);
            var p = so.FindProperty("baseVisionRadius");
            if (p != null) { p.floatValue = Vision; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[PlayerLook] scale " + Scale + ", label y " + LabelY + " font " + LabelFont + ", vision " + Vision);
    }
}
#endif
