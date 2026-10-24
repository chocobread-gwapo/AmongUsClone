using System;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main menu only: shows the game version (bottom-left) and, if a newer GitHub release exists,
// an "Update available" banner with a DOWNLOAD button that opens the release page.
// Silent if offline or if GitHub can't be reached. The version comes from
// Project Settings > Player > Version, so keep it equal to the release tag (without the "v").
public class UpdateChecker : MonoBehaviour
{
    private const string Repo = "chocobread-gwapo/AmongUsClone";
    private static bool checkedThisRun;
    private static bool booted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!booted)
        {
            booted = true;
            SceneManager.sceneLoaded += (scene, mode) => Create(scene);
        }
        Create(SceneManager.GetActiveScene());
    }

    private static void Create(Scene scene)
    {
        if (scene.name != "MainMenu") return;
        if (FindFirstObjectByType<UpdateChecker>() != null) return;
        var go = new GameObject("UpdateChecker");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<UpdateChecker>();
    }

    [Serializable] private class Release { public string tag_name; public string html_url; public bool draft; }
    [Serializable] private class ReleaseList { public Release[] items; }

    private Transform canvasT;
    private GameObject banner;
    private TMP_Text bannerText;
    private string url;

    private void Awake()
    {
        var canvasGo = new GameObject("UpdateCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasT = canvasGo.transform;

        var ver = MakeText(canvasT, "v" + Application.version, 26, TextAlignmentOptions.BottomLeft, new Color(1, 1, 1, 0.55f));
        var vrt = (RectTransform)ver.transform;
        vrt.anchorMin = vrt.anchorMax = vrt.pivot = new Vector2(0, 0);
        vrt.anchoredPosition = new Vector2(24, 16); vrt.sizeDelta = new Vector2(400, 50);

        BuildBanner();
        banner.SetActive(false);
    }

    private void Start()
    {
        if (!checkedThisRun) StartCoroutine(Check());
    }

    private System.Collections.IEnumerator Check()
    {
        checkedThisRun = true;
        using (var req = UnityWebRequest.Get($"https://api.github.com/repos/{Repo}/releases?per_page=5"))
        {
            req.SetRequestHeader("User-Agent", "Maligno-UpdateCheck");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            ReleaseList list;
            try { list = JsonUtility.FromJson<ReleaseList>("{\"items\":" + req.downloadHandler.text + "}"); }
            catch { yield break; }
            if (list == null || list.items == null) yield break;

            Release best = null; Version bestV = null;
            foreach (var r in list.items)
            {
                if (r == null || r.draft || !TryParse(r.tag_name, out Version v)) continue;
                if (bestV == null || v > bestV) { best = r; bestV = v; }
            }
            if (best == null || !TryParse(Application.version, out Version mine)) yield break;
            if (bestV > mine)
            {
                url = best.html_url;
                bannerText.text = $"Update available: {best.tag_name}   (you have v{Application.version})";
                banner.SetActive(true);
            }
        }
    }

    private static bool TryParse(string s, out Version v)
    {
        v = null;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim().TrimStart('v', 'V');
        int dash = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash >= 0) s = s.Substring(0, dash);
        if (!Version.TryParse(s, out v)) return false;
        return true;
    }

    private void BuildBanner()
    {
        banner = new GameObject("UpdateBanner", typeof(RectTransform), typeof(Image));
        banner.transform.SetParent(canvasT, false);
        var rt = (RectTransform)banner.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -20);
        rt.sizeDelta = new Vector2(1100, 80);
        banner.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.1f, 0.95f);

        bannerText = MakeText(banner.transform, "", 30, TextAlignmentOptions.MidlineLeft, Color.white);
        bannerText.fontStyle = FontStyles.Bold;
        var trt = (RectTransform)bannerText.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28, 0); trt.offsetMax = new Vector2(-440, 0);

        var dl = MakeButton(banner.transform, "DOWNLOAD", new Vector2(250, 56), new Color(0.16f, 0.83f, 0.74f, 1f), new Color(0.05f, 0.1f, 0.1f));
        var drt = (RectTransform)dl.transform;
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1, 0.5f);
        drt.anchoredPosition = new Vector2(-140, 0);
        dl.onClick.AddListener(() => { if (!string.IsNullOrEmpty(url)) Application.OpenURL(url); });

        var close = MakeButton(banner.transform, "X", new Vector2(80, 56), new Color(0.2f, 0.22f, 0.3f, 1f), Color.white);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 0.5f);
        crt.anchoredPosition = new Vector2(-24, 0);
        close.onClick.AddListener(() => banner.SetActive(false));
    }

    private static TMP_Text MakeText(Transform parent, string text, float size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = align; t.color = color;
        t.raycastTarget = false;
        return t;
    }

    private static Button MakeButton(Transform parent, string text, Vector2 dims, Color color, Color textColor)
    {
        var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = dims;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var label = MakeText(go.transform, text, 28, TextAlignmentOptions.Center, textColor);
        label.fontStyle = FontStyles.Bold;
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return btn;
    }
}
