using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Two small code-built pieces, no scene edits:
//  * MainMenu: an ACCOUNT button under PLAY (username) and a FIND GAME button above HOST GAME (list of public rooms).
//  * Lobby (host only): a ROOM: PRIVATE / PUBLIC toggle. Rooms are private by default.
public class FindGameUI : MonoBehaviour
{
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
        if (scene.name != "MainMenu" && scene.name != "Lobby") return;
        if (FindFirstObjectByType<FindGameUI>() != null) return;
        var go = new GameObject("FindGameUI");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<FindGameUI>();
    }

    private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.1f, 1f);
    private static readonly Color RowColor = new Color(0.12f, 0.14f, 0.2f, 1f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.22f, 0.3f, 1f);
    private static readonly Color AccentColor = new Color(0.16f, 0.83f, 0.74f, 1f);
    private static readonly Color GreenColor = new Color(0.2f, 0.62f, 0.3f, 1f);

    private bool isMenu;
    private Transform canvasT;
    private GameObject overlay;
    private GameObject accountOverlay;
    private RectTransform listRoot;
    private TMP_Text statusText;
    private Button refreshButton;
    private Button toggleButton;
    private TMP_Text toggleLabel;
    private bool busy;
    private bool arranged;

    private void Awake()
    {
        isMenu = SceneManager.GetActiveScene().name == "MainMenu";

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("FindGameCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = isMenu ? 45 : 55;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasT = canvasGo.transform;

        if (isMenu)
        {
            BuildOverlay();
            overlay.SetActive(false);
            BuildAccountOverlay();
            accountOverlay.SetActive(false);
            AddMenuButtons();

            var mgr = MultiplayerConnectionManager.Instance;
            if (mgr != null)
            {
                mgr.OnStatusChanged += HandleStatus;
                mgr.OnConnectionFailed += HandleStatus;
            }
        }
        else
        {
            toggleButton = MakeButton(canvasT, "ROOM: PRIVATE", new Vector2(340, 64), ButtonColor, 26, Color.white);
            toggleLabel = toggleButton.GetComponentInChildren<TMP_Text>();
            var rt = (RectTransform)toggleButton.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(30, 240);
            toggleButton.onClick.AddListener(() =>
            {
                var m = MultiplayerConnectionManager.Instance;
                if (m != null) m.SetPublic(!m.IsPublic);
            });
            toggleButton.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        var mgr = MultiplayerConnectionManager.Instance;
        if (mgr != null)
        {
            mgr.OnStatusChanged -= HandleStatus;
            mgr.OnConnectionFailed -= HandleStatus;
        }
    }

    private void HandleStatus(string msg)
    {
        if (statusText != null && overlay != null && overlay.activeSelf) statusText.text = msg;
    }

    // ---------- Lobby toggle ----------

    private void Update()
    {
        if (isMenu)
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (overlay != null && overlay.activeSelf) CloseList();
                else if (accountOverlay != null && accountOverlay.activeSelf) accountOverlay.SetActive(false);
            }
            return;
        }

        var nm = NetworkManager.Singleton;
        var mgr = MultiplayerConnectionManager.Instance;
        bool show = nm != null && nm.IsHost && mgr != null && !LookCustomizerUI.PanelOpen;
        if (toggleButton == null) return;
        if (!arranged) TryArrange();
        if (toggleButton.gameObject.activeSelf != show) toggleButton.gameObject.SetActive(show);
        if (show)
        {
            toggleLabel.text = mgr.IsPublic ? "ROOM: PUBLIC" : "ROOM: PRIVATE";
            toggleButton.GetComponent<Image>().color = mgr.IsPublic ? GreenColor : ButtonColor;
        }
    }


    // Puts ROOM: PRIVATE/PUBLIC next to the EDIT button and CUSTOMIZE underneath, inside the room panel.
    private void TryArrange()
    {
        var custom = FindFirstObjectByType<LookCustomizerUI>();
        var panelGo = GameObject.Find("InfoPanel");
        if (custom == null || custom.OpenButton == null || panelGo == null) return;
        var panel = (RectTransform)panelGo.transform;

        RectTransform edit = null;
        foreach (var t in panel.GetComponentsInChildren<RectTransform>(false))
            if (t.name == "EditButton" || t.name == "ViewButton") { edit = t; break; }
        if (edit == null || edit.rect.width < 10f) return;

        var c = new Vector3[4];
        edit.GetWorldCorners(c);
        Vector3 bl = panel.InverseTransformPoint(c[0]), tr = panel.InverseTransformPoint(c[2]);
        if (tr.x <= bl.x || tr.y <= bl.y) return;

        Rect pr = panel.rect;
        float pad = bl.x - pr.xMin;                 // same side padding the EDIT button has
        float editW = tr.x - bl.x, editH = tr.y - bl.y, gap = 20f;
        float left = bl.x - pr.xMin;
        float top = pr.yMax - tr.y;

        Place(toggleButton.transform, panel, left + editW + gap, top, pr.width - pad - (left + editW + gap), editH);
        Place(custom.OpenButton.transform, panel, left, top + editH + 24f, pr.width - pad - left, editH);
        panel.sizeDelta += new Vector2(0f, editH + 24f);
        arranged = true;
    }

    private static void Place(Transform t, RectTransform parent, float x, float yFromTop, float w, float h)
    {
        t.SetParent(parent, false);
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -yFromTop);
        rt.sizeDelta = new Vector2(w, h);
    }

    // ---------- Find Game overlay ----------

    private void BuildOverlay()
    {
        overlay = new GameObject("FindGamePanel", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasT, false);
        var rt = (RectTransform)overlay.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = PanelColor;

        var title = MakeText(overlay.transform, "FIND GAME", 64, TextAlignmentOptions.MidlineRight, Color.white);
        var trt = (RectTransform)title.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(1, 1);
        trt.anchoredPosition = new Vector2(-60, -40); trt.sizeDelta = new Vector2(800, 100);
        title.fontStyle = FontStyles.Bold;

        Button back = MakeButton(overlay.transform, "BACK", new Vector2(260, 80), ButtonColor, 34, Color.white);
        var brt = (RectTransform)back.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
        brt.anchoredPosition = new Vector2(60, -40);
        back.onClick.AddListener(CloseList);

        var listGo = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup));
        listGo.transform.SetParent(overlay.transform, false);
        listRoot = (RectTransform)listGo.transform;
        listRoot.anchorMin = new Vector2(0, 0); listRoot.anchorMax = new Vector2(1, 1);
        listRoot.offsetMin = new Vector2(80, 180); listRoot.offsetMax = new Vector2(-80, -170);
        var v = listGo.GetComponent<VerticalLayoutGroup>();
        v.spacing = 14; v.childControlWidth = true; v.childControlHeight = false;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;

        statusText = MakeText(overlay.transform, "", 30, TextAlignmentOptions.Center, new Color(0.8f, 0.83f, 0.9f));
        var srt = (RectTransform)statusText.transform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(0, 0); srt.sizeDelta = new Vector2(1200, 100);

        refreshButton = MakeButton(overlay.transform, "REFRESH", new Vector2(520, 84), AccentColor, 36, new Color(0.05f, 0.1f, 0.1f));
        var rrt = (RectTransform)refreshButton.transform;
        rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0);
        rrt.anchoredPosition = new Vector2(0, 50);
        refreshButton.onClick.AddListener(Refresh);
    }

    private void OpenList()
    {
        overlay.SetActive(true);
        Refresh();
    }

    private void CloseList() { overlay.SetActive(false); }

    private async void Refresh()
    {
        if (busy) return;
        busy = true;
        statusText.text = "Looking for rooms...";
        Clear();
        var mgr = MultiplayerConnectionManager.Instance;
        if (mgr == null) { statusText.text = "Not connected to the matchmaking service."; busy = false; return; }

        List<MultiplayerConnectionManager.RoomInfo> rooms = await mgr.QueryRoomsAsync();
        if (this == null || overlay == null) return;
        Clear();
        int shown = 0;
        foreach (var r in rooms)
        {
            if (shown >= 8) break;
            AddRow(r);
            shown++;
        }
        statusText.text = shown == 0 ? "No public rooms right now.\nHost one and set it to PUBLIC in the lobby!" : "";
        busy = false;
    }

    private void Clear()
    {
        for (int i = listRoot.childCount - 1; i >= 0; i--) Destroy(listRoot.GetChild(i).gameObject);
    }

    private void AddRow(MultiplayerConnectionManager.RoomInfo r)
    {
        var row = new GameObject("Room", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(listRoot, false);
        row.GetComponent<Image>().color = RowColor;
        row.GetComponent<LayoutElement>().preferredHeight = 96;

        string host = string.IsNullOrEmpty(r.Host) ? r.Name : r.Host + "'s room";
        var name = MakeText(row.transform, host, 36, TextAlignmentOptions.MidlineLeft, Color.white);
        Place((RectTransform)name.transform, new Vector2(30, 0), new Vector2(760, 96), new Vector2(0, 0.5f));
        name.fontStyle = FontStyles.Bold;

        bool full = r.Players >= r.Capacity || r.Locked;
        string state = r.Started ? "IN GAME" : (full ? "FULL" : "OPEN");
        Color stateColor = r.Started ? new Color(0.9f, 0.7f, 0.2f) : (full ? new Color(0.9f, 0.35f, 0.35f) : new Color(0.3f, 0.9f, 0.5f));

        var count = MakeText(row.transform, $"{r.Players}/{r.Capacity}", 38, TextAlignmentOptions.MidlineRight, Color.white);
        Place((RectTransform)count.transform, new Vector2(840, 0), new Vector2(200, 96), new Vector2(0, 0.5f));

        var st = MakeText(row.transform, state, 30, TextAlignmentOptions.Center, stateColor);
        Place((RectTransform)st.transform, new Vector2(1060, 0), new Vector2(220, 96), new Vector2(0, 0.5f));
        st.fontStyle = FontStyles.Bold;

        bool canJoin = !r.Started && !full;
        Button join = MakeButton(row.transform, "JOIN", new Vector2(220, 68), canJoin ? GreenColor : ButtonColor, 32, Color.white);
        var jrt = (RectTransform)join.transform;
        jrt.anchorMin = jrt.anchorMax = jrt.pivot = new Vector2(1, 0.5f);
        jrt.anchoredPosition = new Vector2(-24, 0);
        join.interactable = canJoin;
        string id = r.Id;
        join.onClick.AddListener(() => JoinRoom(id));
    }

    private async void JoinRoom(string id)
    {
        if (busy) return;
        busy = true;
        statusText.text = "Joining...";
        await MultiplayerConnectionManager.Instance.JoinGameByIdAsync(id);
        busy = false;
    }


    // ---------- Main-menu integration (clones of the scene's own buttons, so styling matches) ----------

    private void AddMenuButtons()
    {
        // ACCOUNT: a copy of the SETTINGS button, placed right under PLAY.
        GameObject playBtn = FindDeep("PlayButton");
        GameObject settingsBtn = FindDeep("SettingsButton");
        if (playBtn != null && settingsBtn != null)
        {
            var acc = Instantiate(settingsBtn, playBtn.transform.parent);
            acc.name = "AccountButton";
            SetLabel(acc, "ACCOUNT");
            acc.transform.SetSiblingIndex(playBtn.transform.GetSiblingIndex() + 1);
            var b = acc.GetComponent<Button>();
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => accountOverlay.SetActive(true));
        }

        // FIND GAME: a copy of HOST GAME, placed right above it inside the PLAY panel.
        var lobbyUi = FindFirstObjectByType<LobbyConnectionUI>(FindObjectsInactive.Include);
        GameObject hostBtn = null;
        if (lobbyUi != null)
            foreach (var t in lobbyUi.GetComponentsInChildren<Transform>(true))
                if (t.name == "HostButton") { hostBtn = t.gameObject; break; }
        if (hostBtn != null)
        {
            var find = Instantiate(hostBtn, hostBtn.transform.parent);
            find.name = "FindGameButton";
            SetLabel(find, "FIND GAME");
            find.transform.SetSiblingIndex(hostBtn.transform.GetSiblingIndex());
            var b = find.GetComponent<Button>();
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(OpenList);
        }
    }

    private static GameObject FindDeep(string name)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.GetComponent<Button>() != null) return t.gameObject;
        return null;
    }

    private static void SetLabel(GameObject button, string text)
    {
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = text; return; }
        var legacy = button.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = text;
    }

    // ---------- Account overlay (username) ----------

    private void BuildAccountOverlay()
    {
        accountOverlay = new GameObject("AccountPanel", typeof(RectTransform), typeof(Image));
        accountOverlay.transform.SetParent(canvasT, false);
        var rt = (RectTransform)accountOverlay.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        accountOverlay.GetComponent<Image>().color = PanelColor;

        var title = MakeText(accountOverlay.transform, "ACCOUNT", 64, TextAlignmentOptions.MidlineRight, Color.white);
        var trt = (RectTransform)title.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(1, 1);
        trt.anchoredPosition = new Vector2(-60, -40); trt.sizeDelta = new Vector2(800, 100);
        title.fontStyle = FontStyles.Bold;

        Button back = MakeButton(accountOverlay.transform, "BACK", new Vector2(260, 80), ButtonColor, 34, Color.white);
        var brt = (RectTransform)back.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
        brt.anchoredPosition = new Vector2(60, -40);
        back.onClick.AddListener(() => accountOverlay.SetActive(false));

        var cap = MakeText(accountOverlay.transform, "USERNAME", 30, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.78f, 0.85f));
        Place((RectTransform)cap.transform, new Vector2(0, 70), new Vector2(760, 50), new Vector2(0.5f, 0.5f));
        cap.fontStyle = FontStyles.Bold;

        var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        fieldGo.transform.SetParent(accountOverlay.transform, false);
        Place((RectTransform)fieldGo.transform, new Vector2(0, 0), new Vector2(760, 90), new Vector2(0.5f, 0.5f));
        var bg = fieldGo.GetComponent<Image>();
        bg.color = ButtonColor;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(fieldGo.transform, false);
        var vrt = (RectTransform)viewport.transform;
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(20, 6); vrt.offsetMax = new Vector2(-20, -6);

        var text = MakeText(viewport.transform, "", 40, TextAlignmentOptions.MidlineLeft, Color.white);
        Stretch((RectTransform)text.transform);
        var ph = MakeText(viewport.transform, "Enter a name", 40, TextAlignmentOptions.MidlineLeft, new Color(1, 1, 1, 0.4f));
        ph.fontStyle = FontStyles.Italic;
        Stretch((RectTransform)ph.transform);

        var input = fieldGo.GetComponent<TMP_InputField>();
        input.textViewport = vrt;
        input.textComponent = text;
        input.placeholder = ph;
        input.characterLimit = 14;
        input.targetGraphic = bg;
        input.text = PlayerName.Load();
        input.onValueChanged.AddListener(v => PlayerName.Save(v));
        input.onEndEdit.AddListener(v => { PlayerName.Save(v); input.text = PlayerName.Load(); });

        var hint = MakeText(accountOverlay.transform, "Shown above your character, in meetings and in Find Game.\nUp to 14 characters. Saved on this computer.", 26,
            TextAlignmentOptions.TopLeft, new Color(0.6f, 0.64f, 0.72f));
        Place((RectTransform)hint.transform, new Vector2(-380, -70), new Vector2(760, 100), new Vector2(0.5f, 0.5f));
        hint.rectTransform.pivot = new Vector2(0, 1);
        hint.rectTransform.anchoredPosition = new Vector2(-380, -60);
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    // ---------- UI helpers ----------

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
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

    private static Button MakeButton(Transform parent, string text, Vector2 dims, Color color, float fontSize, Color textColor)
    {
        var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = dims;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        var label = MakeText(go.transform, text, fontSize, TextAlignmentOptions.Center, textColor);
        label.fontStyle = FontStyles.Bold;
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return btn;
    }
}
