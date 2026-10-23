using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Settings screen, built in code: master / music / effects sliders, fullscreen,
// and rebindable keys. Opens from the main menu's SETTINGS button, from the
// small SETTINGS button shown in Lobby/Game, or with Esc.
public class SettingsMenu : MonoBehaviour
{
    private static SettingsMenu instance;

    private GameObject canvasGo, panel, gearButton;
    private Button quitButton;
    private TextMeshProUGUI quitLabel;
    private bool quitArmed, leaving;
    private Action onClosed;
    private bool built;
    private TextMeshProUGUI fullscreenLabel, hintsLabel;
    private readonly List<Slider> sliders = new List<Slider>();
    private readonly List<KeyRow> keyRows = new List<KeyRow>();
    private InputActionRebindingExtensions.RebindingOperation rebinding;
    private int rebindEndFrame = -10;
    private bool miniGameOpenLastFrame;
    private InputActionAsset asset;

    private class KeyRow
    {
        public InputAction action;
        public int index;
        public TextMeshProUGUI label;
    }

    private static readonly Color Ink = new Color(0.05f, 0.08f, 0.1f, 0.98f);
    private static readonly Color Panel2 = new Color(0.1f, 0.15f, 0.18f, 1f);
    private static readonly Color Teal = new Color(0.15f, 0.83f, 0.72f, 1f);
    private static readonly Color Grey = new Color(0.65f, 0.72f, 0.74f, 1f);

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("SettingsMenu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SettingsMenu>();
        SceneManager.sceneLoaded += (s, m) => instance.OnSceneLoaded();
        instance.Build();
        instance.OnSceneLoaded();
    }

    public static void Open(Action onClosed = null)
    {
        if (instance == null) Boot();
        instance.OpenInternal(onClosed);
    }

    private void OnSceneLoaded()
    {
        EnsureEventSystem();
        if (gearButton != null) gearButton.SetActive(!IsMainMenu());
    }

    private static bool IsMainMenu() => SceneManager.GetActiveScene().name == "MainMenu";

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem (Settings)", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        bool miniOpen = TaskMiniGame.IsOpen;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && rebinding == null && Time.frameCount - rebindEndFrame > 2)
        {
            if (IsOpen) Close();
            else if (!miniOpen && !miniGameOpenLastFrame && !IsMainMenu()) OpenInternal(null);
        }
        miniGameOpenLastFrame = miniOpen;
    }

    // ------------------------------------------------------------ open/close

    private void OpenInternal(Action closedCallback)
    {
        EnsureEventSystem();
        if (!built) Build();
        onClosed = closedCallback;
        asset = GameSettings.FindAsset();
        RefreshAll();
        canvasGo.SetActive(true);
        panel.SetActive(true);
        quitArmed = false;
        if (quitButton != null)
        {
            quitButton.gameObject.SetActive(!IsMainMenu());
            quitLabel.text = "QUIT TO MENU";
        }
    }

    // Leaves the match/lobby (the host leaving hands the group to a new host) and returns to the main menu.
    private async void OnQuitToMenu()
    {
        if (leaving) return;
        if (!quitArmed)
        {
            quitArmed = true;
            quitLabel.text = "CLICK AGAIN TO CONFIRM";
            return;
        }
        leaving = true;
        Close();
        if (MultiplayerConnectionManager.Instance != null)
            await MultiplayerConnectionManager.Instance.LeaveSessionAsync();
        SceneManager.LoadScene("MainMenu");
        leaving = false;
    }

    public void Close()
    {
        CancelRebind();
        panel.SetActive(false);
        PlayerPrefs.Save();
        var cb = onClosed; onClosed = null;
        cb?.Invoke();
    }

    // --------------------------------------------------------------- refresh

    private void RefreshAll()
    {
        sliders[0].SetValueWithoutNotify(GameSettings.Master);
        sliders[1].SetValueWithoutNotify(GameSettings.Music);
        sliders[2].SetValueWithoutNotify(GameSettings.Sfx_);
        foreach (var s in sliders) UpdatePct(s);
        fullscreenLabel.text = "FULLSCREEN: " + (Screen.fullScreen ? "ON" : "OFF");
        hintsLabel.text = "HINTS: " + (GameSettings.HintsOn ? "ON" : "OFF");
        RebuildKeyRows();
    }

    private static void UpdatePct(Slider s)
    {
        var link = s.GetComponent<PctLink>();
        if (link != null && link.label != null) link.label.text = Mathf.RoundToInt(s.value * 100f) + "%";
    }

    private Transform keyGrid;

    private void RebuildKeyRows()
    {
        foreach (Transform c in keyGrid) Destroy(c.gameObject);
        keyRows.Clear();
        if (asset == null)
        {
            Label(keyGrid, "Controls are not available in this scene.", 24, Grey);
            return;
        }
        var map = asset.FindActionMap("Player", false);
        if (map == null) return;
        AddAction(map, "Interact", "Use / Interact");
        AddAction(map, "Kill", "Kill");
        AddAction(map, "Report", "Report");
        AddAction(map, "Ability", "Ability");
        var move = map.FindAction("Move", false);
        if (move != null)
        {
            for (int i = 0; i < move.bindings.Count; i++)
            {
                var b = move.bindings[i];
                if (!b.isPartOfComposite) continue;
                string n = char.ToUpper(b.name[0]) + b.name.Substring(1);
                AddRow(move, i, "Move " + n);
            }
        }
    }

    private void AddAction(InputActionMap map, string name, string display)
    {
        var a = map.FindAction(name, false);
        if (a == null) return;
        for (int i = 0; i < a.bindings.Count; i++)
        {
            if (a.bindings[i].path.StartsWith("<Keyboard>") || a.bindings[i].effectivePath.StartsWith("<Keyboard>"))
            { AddRow(a, i, display); return; }
        }
    }

    private void AddRow(InputAction action, int index, string display)
    {
        var row = new GameObject("KeyRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(keyGrid, false);
        row.GetComponent<Image>().color = Panel2;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 10, 4, 4); h.spacing = 10;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;

        var name = Label(row.transform, display, 26, Color.white);
        name.GetComponent<LayoutElement>().preferredWidth = 230;

        var kr = new KeyRow { action = action, index = index };
        var btn = MakeButton(row.transform, "", 24, new Color(0.15f, 0.3f, 0.35f), 170, 44);
        kr.label = btn.GetComponentInChildren<TextMeshProUGUI>();
        kr.label.text = KeyName(action, index);
        btn.onClick.AddListener(() => StartRebind(kr));
        keyRows.Add(kr);
    }

    private static string KeyName(InputAction a, int index)
    {
        string s = a.GetBindingDisplayString(index);
        return string.IsNullOrEmpty(s) ? "-" : s.ToUpperInvariant();
    }

    // --------------------------------------------------------------- rebinding

    private void StartRebind(KeyRow row)
    {
        if (rebinding != null) CancelRebind();
        var action = row.action;
        bool wasEnabled = action.enabled;
        action.Disable();
        row.label.text = "PRESS A KEY...";
        string oldPath = action.bindings[row.index].effectivePath;

        rebinding = action.PerformInteractiveRebinding(row.index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnComplete(op =>
            {
                string newPath = action.bindings[row.index].effectivePath;
                // Another key row already uses this key? Swap, so no two actions share a key.
                foreach (var other in keyRows)
                {
                    if (other == row) continue;
                    if (other.action.bindings[other.index].effectivePath == newPath)
                    {
                        bool oe = other.action.enabled; other.action.Disable();
                        other.action.ApplyBindingOverride(other.index, oldPath);
                        if (oe) other.action.Enable();
                    }
                }
                FinishRebind(action, wasEnabled);
            })
            .OnCancel(op => FinishRebind(action, wasEnabled))
            .Start();
    }

    private void FinishRebind(InputAction action, bool wasEnabled)
    {
        rebinding?.Dispose(); rebinding = null;
        rebindEndFrame = Time.frameCount;
        if (wasEnabled) action.Enable();
        GameSettings.SaveBindings(asset);
        foreach (var k in keyRows) k.label.text = KeyName(k.action, k.index);
    }

    private void CancelRebind()
    {
        if (rebinding == null) return;
        rebinding.Cancel();
    }

    // ----------------------------------------------------------------- build

    private void Build()
    {
        built = true;
        canvasGo = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;

        // Small always-available button (not on the main menu, which has its own).
        gearButton = MakeButton(canvasGo.transform, "SETTINGS", 22, new Color(0.1f, 0.15f, 0.18f, 0.85f), 170, 44).gameObject;
        var grt = (RectTransform)gearButton.transform;
        grt.anchorMin = grt.anchorMax = Vector2.zero; grt.pivot = Vector2.zero; grt.anchoredPosition = new Vector2(16, 16);
        gearButton.GetComponent<Button>().onClick.AddListener(() => OpenInternal(null));
        gearButton.SetActive(!IsMainMenu());

        // Full-screen dim + centered panel.
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        Stretch((RectTransform)panel.transform);
        panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.82f);

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        box.transform.SetParent(panel.transform, false);
        var brt = (RectTransform)box.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(1060, 820);
        box.GetComponent<Image>().color = Ink;
        var v = box.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 28, 28); v.spacing = 12;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var title = Label(box.transform, "SETTINGS", 54, Teal);
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<LayoutElement>().preferredHeight = 70;

        Heading(box.transform, "AUDIO");
        SliderRow(box.transform, "Master volume", GameSettings.SetMaster);
        SliderRow(box.transform, "Music", GameSettings.SetMusic);
        SliderRow(box.transform, "Effects", GameSettings.SetSfx);

        Heading(box.transform, "DISPLAY");
        var disp = new GameObject("DisplayRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        disp.transform.SetParent(box.transform, false);
        disp.GetComponent<LayoutElement>().preferredHeight = 50;
        var dh = disp.GetComponent<HorizontalLayoutGroup>();
        dh.spacing = 20; dh.childAlignment = TextAnchor.MiddleLeft;
        dh.childControlWidth = true; dh.childControlHeight = true; dh.childForceExpandWidth = false;
        var fs = MakeButton(disp.transform, "FULLSCREEN", 26, new Color(0.15f, 0.3f, 0.35f), 470, 50);
        fullscreenLabel = fs.GetComponentInChildren<TextMeshProUGUI>();
        fs.onClick.AddListener(() =>
        {
            // Screen.fullScreen updates a frame late, so flip from what it said and label from that.
            bool now = !Screen.fullScreen;
            GameSettings.SetFullscreen(now);
            fullscreenLabel.text = "FULLSCREEN: " + (now ? "ON" : "OFF");
        });
        var hb = MakeButton(disp.transform, "HINTS", 26, new Color(0.15f, 0.3f, 0.35f), 470, 50);
        hintsLabel = hb.GetComponentInChildren<TextMeshProUGUI>();
        hb.onClick.AddListener(() =>
        {
            GameSettings.HintsOn = !GameSettings.HintsOn;
            hintsLabel.text = "HINTS: " + (GameSettings.HintsOn ? "ON" : "OFF");
        });

        Heading(box.transform, "CONTROLS  (click a key, then press the new one)");
        var grid = new GameObject("Keys", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        grid.transform.SetParent(box.transform, false);
        var g = grid.GetComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(460, 52); g.spacing = new Vector2(20, 10);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = 2;
        grid.GetComponent<LayoutElement>().preferredHeight = 4 * 52 + 3 * 10;
        keyGrid = grid.transform;

        var bottom = new GameObject("Bottom", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        bottom.transform.SetParent(box.transform, false);
        var bh = bottom.GetComponent<HorizontalLayoutGroup>();
        bh.spacing = 24; bh.childAlignment = TextAnchor.MiddleCenter;
        bh.childControlWidth = true; bh.childControlHeight = true; bh.childForceExpandWidth = false;
        bottom.GetComponent<LayoutElement>().preferredHeight = 64;
        var reset = MakeButton(bottom.transform, "RESET DEFAULTS", 26, new Color(0.45f, 0.18f, 0.18f), 360, 56);
        reset.onClick.AddListener(() =>
        {
            CancelRebind();
            GameSettings.ResetAudio(); GameSettings.ResetBindings(asset);
            RefreshAll();
        });
        quitButton = MakeButton(bottom.transform, "QUIT TO MENU", 26, new Color(0.6f, 0.2f, 0.2f), 400, 56);
        quitLabel = quitButton.GetComponentInChildren<TextMeshProUGUI>();
        quitButton.onClick.AddListener(OnQuitToMenu);
        quitButton.gameObject.SetActive(!IsMainMenu());
        var close = MakeButton(bottom.transform, "CLOSE", 26, new Color(0.15f, 0.55f, 0.5f), 260, 56);
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        canvasGo.SetActive(true);
    }

    private void Heading(Transform parent, string text)
    {
        var t = Label(parent, text, 28, Teal);
        t.GetComponent<LayoutElement>().preferredHeight = 38;
    }

    private void SliderRow(Transform parent, string label, Action<float> setter)
    {
        var row = new GameObject("SliderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 44;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 20; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
        var l = Label(row.transform, label, 26, Color.white);
        l.GetComponent<LayoutElement>().preferredWidth = 280;

        var s = MakeSlider(row.transform);
        var pct = Label(row.transform, "", 26, Grey);
        pct.GetComponent<LayoutElement>().preferredWidth = 90;
        pct.alignment = TextAlignmentOptions.MidlineRight;
        s.onValueChanged.AddListener(val => { setter(val); pct.text = Mathf.RoundToInt(val * 100f) + "%"; });
        s.gameObject.AddComponent<PctLink>().label = pct;
        sliders.Add(s);
    }

    private class PctLink : MonoBehaviour { public TextMeshProUGUI label; }

    private static Slider MakeSlider(Transform parent)
    {
        var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        var le = root.GetComponent<LayoutElement>(); le.preferredWidth = 520; le.preferredHeight = 36; le.flexibleWidth = 1;

        var bg = Child(root.transform, "Background", typeof(Image));
        bg.GetComponent<Image>().color = new Color(0.2f, 0.26f, 0.29f);
        var bgr = (RectTransform)bg.transform; bgr.anchorMin = new Vector2(0, 0.3f); bgr.anchorMax = new Vector2(1, 0.7f); bgr.offsetMin = bgr.offsetMax = Vector2.zero;

        var fa = Child(root.transform, "Fill Area", null);
        var far = (RectTransform)fa.transform; far.anchorMin = new Vector2(0, 0.3f); far.anchorMax = new Vector2(1, 0.7f); far.offsetMin = new Vector2(6, 0); far.offsetMax = new Vector2(-14, 0);
        var fill = Child(fa.transform, "Fill", typeof(Image));
        fill.GetComponent<Image>().color = Teal;
        ((RectTransform)fill.transform).sizeDelta = new Vector2(10, 0);

        var ha = Child(root.transform, "Handle Slide Area", null);
        var har = (RectTransform)ha.transform; har.anchorMin = Vector2.zero; har.anchorMax = Vector2.one; har.offsetMin = new Vector2(10, 0); har.offsetMax = new Vector2(-10, 0);
        var handle = Child(ha.transform, "Handle", typeof(Image));
        handle.GetComponent<Image>().color = Color.white;
        ((RectTransform)handle.transform).sizeDelta = new Vector2(22, 0);

        var s = root.GetComponent<Slider>();
        s.fillRect = (RectTransform)fill.transform; s.handleRect = (RectTransform)handle.transform;
        s.targetGraphic = handle.GetComponent<Image>(); s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0; s.maxValue = 1; s.value = 1;
        return s;
    }

    private static GameObject Child(Transform parent, string name, Type graphic)
    {
        var go = graphic != null ? new GameObject(name, typeof(RectTransform), graphic) : new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.MidlineLeft; t.raycastTarget = false;
        return t;
    }

    private static Button MakeButton(Transform parent, string label, float size, Color fill, float w, float h)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = fill;
        var le = go.GetComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = h;
        var b = go.GetComponent<Button>();
        var cols = b.colors; cols.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cols.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cols;
        var t = Label(go.transform, label, size, Color.white);
        t.alignment = TextAlignmentOptions.Center;
        Stretch((RectTransform)t.transform);
        return b;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
