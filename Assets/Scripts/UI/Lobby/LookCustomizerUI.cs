using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Lobby-only "CUSTOMIZE" panel: pick your color, shirt, pants and hat. Changes are previewed live on
// your own character; DONE sends them to the host (which keeps main colors unique) and saves them on
// this computer so they come back next session. Built entirely in code, so it needs no scene edits.
public class LookCustomizerUI : MonoBehaviour
{
    public static bool PanelOpen { get; private set; }

    private static bool booted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!booted)
        {
            booted = true;
            SceneManager.sceneLoaded += (scene, mode) => { if (scene.name == "Lobby") Create(scene); };
        }
        Scene active = SceneManager.GetActiveScene();
        if (active.name == "Lobby") Create(active);
    }

    private static void Create(Scene scene)
    {
        if (FindFirstObjectByType<LookCustomizerUI>() != null) return;
        var go = new GameObject("LookCustomizer");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<LookCustomizerUI>();
    }

    private static readonly string[] RowLabels = { "COLOR", "SHIRT", "PANTS", "PANTS COLOR", "HAT", "HAT COLOR" };

    private GameObject openButton;
    private GameObject panel;
    private readonly TMP_Text[] valueTexts = new TMP_Text[6];
    private PlayerLabel label;
    private LookData cur;

    private void Awake()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("LookCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        BuildOpenButton(canvasGo.transform);
        BuildPanel(canvasGo.transform);
        panel.SetActive(false);
    }

    private void OnDestroy()
    {
        PanelOpen = false;
    }

    // ---------- UI building ----------

    private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.1f, 0.94f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.22f, 0.3f, 1f);
    private static readonly Color AccentColor = new Color(0.2f, 0.62f, 0.3f, 1f);

    private void BuildOpenButton(Transform parent)
    {
        Button b = MakeButton(parent, "CUSTOMIZE", new Vector2(240, 64), AccentColor, 28);
        openButton = b.gameObject;
        var rt = (RectTransform)openButton.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(30, 160);
        b.onClick.AddListener(Open);
        openButton.SetActive(false);
    }

    private void BuildPanel(Transform parent)
    {
        panel = new GameObject("LookPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(40, 0);
        rt.sizeDelta = new Vector2(560, 700);
        panel.GetComponent<Image>().color = PanelColor;

        MakeText(panel.transform, "CUSTOMIZE", 40, new Vector2(0, -22), new Vector2(560, 60), TextAlignmentOptions.Center, Color.white, true);

        for (int i = 0; i < RowLabels.Length; i++)
        {
            int row = i;
            float y = -110 - i * 78;
            MakeText(panel.transform, RowLabels[i], 24, new Vector2(30, y), new Vector2(190, 56), TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.78f, 0.85f), false);

            Button left = MakeButton(panel.transform, "<", new Vector2(56, 56), ButtonColor, 30);
            Place(left.transform, new Vector2(230, y));
            left.onClick.AddListener(() => Step(row, -1));

            valueTexts[i] = MakeText(panel.transform, "", 24, new Vector2(290, y), new Vector2(190, 56), TextAlignmentOptions.Center, Color.white, true);

            Button right = MakeButton(panel.transform, ">", new Vector2(56, 56), ButtonColor, 30);
            Place(right.transform, new Vector2(486, y));
            right.onClick.AddListener(() => Step(row, +1));
        }

        Button random = MakeButton(panel.transform, "RANDOM", new Vector2(240, 60), ButtonColor, 26);
        Place(random.transform, new Vector2(30, -610));
        random.onClick.AddListener(Randomize);

        Button done = MakeButton(panel.transform, "DONE", new Vector2(240, 60), AccentColor, 28);
        Place(done.transform, new Vector2(290, -610));
        done.onClick.AddListener(Close);
    }

    private static void Place(Transform t, Vector2 topLeftPos)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = topLeftPos;
    }

    private static TMP_Text MakeText(Transform parent, string text, float size, Vector2 topLeftPos, Vector2 dims,
        TextAlignmentOptions align, Color color, bool bold)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = color;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        Place(rt, topLeftPos);
        rt.sizeDelta = dims;
        return tmp;
    }

    private static Button MakeButton(Transform parent, string text, Vector2 dims, Color color, float fontSize)
    {
        var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = dims;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return btn;
    }

    // ---------- behaviour ----------

    private static PlayerLabel FindLocalLabel()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<PlayerLabel>();
    }

    private void Update()
    {
        label = FindLocalLabel();
        bool canShow = label != null && !PanelOpen;
        if (openButton != null && openButton.activeSelf != canShow) openButton.SetActive(canShow);

        if (PanelOpen)
        {
            if (label == null) { ClosePanelOnly(); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }
    }

    private void Open()
    {
        label = FindLocalLabel();
        if (label == null) return;
        cur = label.NetworkLook;
        PanelOpen = true;
        panel.SetActive(true);
        Apply();
    }

    private void Close()
    {
        if (label != null)
        {
            label.CommitLook(cur);
            LookCatalog.Save(cur.Pack());
        }
        ClosePanelOnly();
    }

    private void ClosePanelOnly()
    {
        PanelOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    private static int Wrap(int v, int count) { return ((v % count) + count) % count; }

    private int NextFreeColor(int from, int dir)
    {
        int n = LookCatalog.PaletteCount;
        int c = from;
        for (int i = 0; i < n; i++)
        {
            c = Wrap(c + dir, n);
            if (label == null || !label.IsColorTakenByOther(c)) return c;
        }
        return from;
    }

    private void Step(int row, int dir)
    {
        switch (row)
        {
            case 0: cur.color = NextFreeColor(cur.color, dir); break;
            case 1: cur.shirt = Wrap(cur.shirt + dir, LookCatalog.ShirtNames.Length); break;
            case 2: cur.pants = Wrap(cur.pants + dir, LookCatalog.PantsNames.Length); break;
            case 3: cur.pantsColor = Wrap(cur.pantsColor + dir, LookCatalog.PaletteCount); break;
            case 4: cur.hat = Wrap(cur.hat + dir, LookCatalog.HatNames.Length); break;
            case 5: cur.hatColor = Wrap(cur.hatColor + dir, LookCatalog.PaletteCount); break;
        }
        Apply();
    }

    private void Randomize()
    {
        cur.color = NextFreeColor(Random.Range(0, LookCatalog.PaletteCount), 1);
        cur.shirt = Random.Range(0, LookCatalog.ShirtNames.Length);
        cur.pants = Random.Range(1, LookCatalog.PantsNames.Length);
        cur.pantsColor = Random.Range(0, LookCatalog.PaletteCount);
        cur.hat = Random.Range(0, LookCatalog.HatNames.Length);
        cur.hatColor = Random.Range(0, LookCatalog.PaletteCount);
        Apply();
    }

    private void Apply()
    {
        if (label != null) label.SetPreview(cur);
        SetValue(0, LookCatalog.Palette[cur.color].Name, LookCatalog.Palette[cur.color].Color);
        SetValue(1, LookCatalog.ShirtNames[cur.shirt], Color.white);
        SetValue(2, LookCatalog.PantsNames[cur.pants], Color.white);
        SetValue(3, LookCatalog.Palette[cur.pantsColor].Name, LookCatalog.Palette[cur.pantsColor].Color);
        SetValue(4, LookCatalog.HatNames[cur.hat], Color.white);
        SetValue(5, LookCatalog.Palette[cur.hatColor].Name, LookCatalog.Palette[cur.hatColor].Color);
    }

    private void SetValue(int i, string text, Color color)
    {
        if (valueTexts[i] == null) return;
        valueTexts[i].text = text;
        valueTexts[i].color = color;
    }
}
