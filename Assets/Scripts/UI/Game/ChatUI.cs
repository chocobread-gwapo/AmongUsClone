using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// In-game text chat, built entirely in code (no scene or prefab changes).
//  - Lobby: everyone can talk freely.
//  - In a match: chat only works while a meeting is running. Dead players talk to the other
//    dead players only (the server decides who receives what, so living players never see it).
// Messages travel as NGO named messages: client -> server (validated, rate limited, sanitized)
// -> every allowed client. Press ENTER (or click CHAT) to open, ENTER to send, ENTER on an empty box
// or the X to close.
public class ChatUI : MonoBehaviour
{
    private const string ToServer = "maligno.chat.c2s";
    private const string ToClients = "maligno.chat.s2c";
    private const int MaxLength = 100;
    private const int MaxRows = 60;
    private const float MinSecondsBetweenMessages = 0.6f;

    // True while the chat panel is open: movement and action keys are ignored (see PlayerController).
    public static bool Typing { get; private set; }

    private static ChatUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("ChatUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ChatUI>();
    }

    // ---- UI ----
    private GameObject canvasGo;
    private GameObject button;
    private TMP_Text buttonText;
    private GameObject panel;
    private RectTransform content;
    private ScrollRect scroll;
    private TMP_InputField input;
    private GameObject toast;
    private TMP_Text toastText;
    private float toastUntil;
    private bool scrollNextFrame;
    private bool focusNextFrame;
    private int unread;
    private readonly List<GameObject> rows = new List<GameObject>();

    // ---- network ----
    private CustomMessagingManager registeredOn;
    private readonly Dictionary<ulong, float> lastSent = new Dictionary<ulong, float>();

    private void Awake()
    {
        BuildUI();
        SetPanel(false);
        canvasGo.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        Typing = false;
    }

    // =====================================================================
    // Availability + networking hookup
    // =====================================================================

    private static bool InSession()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening && SceneManager.GetActiveScene().name != "MainMenu";
    }

    private static bool CanChatNow()
    {
        if (!InSession()) return false;
        bool started = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted;
        bool meeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        return !started || meeting;
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening;
        EnsureHandlers(nm, listening);

        bool available = CanChatNow() && !TaskMiniGame.IsOpen;
        if (canvasGo.activeSelf != available) canvasGo.SetActive(available);
        if (!available)
        {
            if (panel.activeSelf) SetPanel(false);
            Typing = false;
            return;
        }

        var kb = Keyboard.current;
        if (kb != null && !panel.activeSelf && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            SetPanel(true);

        if (toast.activeSelf && Time.unscaledTime > toastUntil) toast.SetActive(false);
    }

    private void LateUpdate()
    {
        if (scrollNextFrame && scroll != null)
        {
            scrollNextFrame = false;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }
        if (focusNextFrame && input != null && panel.activeSelf)
        {
            focusNextFrame = false;
            if (EventSystem_Current() != null) EventSystem_Current().SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
        }
    }

    private static UnityEngine.EventSystems.EventSystem EventSystem_Current() => UnityEngine.EventSystems.EventSystem.current;

    private void EnsureHandlers(NetworkManager nm, bool listening)
    {
        if (!listening || nm.CustomMessagingManager == null)
        {
            if (registeredOn != null)
            {
                registeredOn = null;
                ClearHistory();
                lastSent.Clear();
            }
            return;
        }
        if (registeredOn == nm.CustomMessagingManager) return;
        registeredOn = nm.CustomMessagingManager;
        registeredOn.RegisterNamedMessageHandler(ToServer, OnServerMessage);
        registeredOn.RegisterNamedMessageHandler(ToClients, OnClientMessage);
    }

    // ---- sending (any client) ----

    private static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in raw)
        {
            if (c == '<' || c == '>' || char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length >= MaxLength) break;
        }
        return sb.ToString().Trim();
    }

    private void Submit(string raw)
    {
        string text = Clean(raw);
        var nm = NetworkManager.Singleton;
        if (text.Length == 0 || nm == null || !nm.IsListening) return;

        if (nm.IsServer)
        {
            ServerProcess(nm.LocalClientId, text);
            return;
        }
        using (var w = new FastBufferWriter(512, Allocator.Temp))
        {
            w.WriteValueSafe(text);
            nm.CustomMessagingManager.SendNamedMessage(ToServer, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }
    }

    // ---- server side ----

    private void OnServerMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string text);
        ServerProcess(senderClientId, text);
    }

    private static PlayerRole RoleOf(ulong clientId)
    {
        foreach (var r in FindObjectsByType<PlayerRole>(FindObjectsSortMode.None))
            if (r != null && r.OwnerClientId == clientId) return r;
        return null;
    }

    private void ServerProcess(ulong sender, string raw)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        string text = Clean(raw);
        if (text.Length == 0) return;
        if (!CanChatNow()) return; // match running and no meeting: nobody may talk

        if (lastSent.TryGetValue(sender, out float t) && Time.unscaledTime - t < MinSecondsBetweenMessages) return;
        lastSent[sender] = Time.unscaledTime;

        bool started = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted;
        PlayerRole senderRole = RoleOf(sender);
        bool dead = started && senderRole != null && !senderRole.IsAlive;

        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (dead)
            {
                PlayerRole rr = RoleOf(id);
                if (rr == null || rr.IsAlive) continue; // ghosts only talk to ghosts
            }
            if (id == nm.LocalClientId)
            {
                Receive(sender, dead, text);
                continue;
            }
            using (var w = new FastBufferWriter(512, Allocator.Temp))
            {
                w.WriteValueSafe(sender);
                w.WriteValueSafe(dead);
                w.WriteValueSafe(text);
                nm.CustomMessagingManager.SendNamedMessage(ToClients, id, w, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    // ---- client side ----

    private void OnClientMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong who);
        reader.ReadValueSafe(out bool dead);
        reader.ReadValueSafe(out string text);
        Receive(who, dead, text);
    }

    private void Receive(ulong who, bool dead, string text)
    {
        text = Clean(text);
        if (text.Length == 0) return;

        Color c = PlayerLabel.ColorFor(who);
        string hex = ColorUtility.ToHtmlStringRGB(c);
        string name = PlayerLabel.NameFor(who);
        string prefix = dead ? "<color=#9aa3b2><i>(ghost)</i></color> " : "";
        string line = $"{prefix}<b><color=#{hex}>{name}</color></b>\n{text}";
        AddRow(line, dead);

        if (!panel.activeSelf)
        {
            unread++;
            RefreshButton();
            toastText.text = $"<b><color=#{hex}>{name}</color></b>: {text}";
            toast.SetActive(true);
            toastUntil = Time.unscaledTime + 5f;
        }
    }

    // =====================================================================
    // UI
    // =====================================================================

    private void SetPanel(bool open)
    {
        panel.SetActive(open);
        Typing = open;
        if (open)
        {
            unread = 0;
            RefreshButton();
            toast.SetActive(false);
            focusNextFrame = true;
            scrollNextFrame = true;
        }
        else if (input != null)
        {
            input.DeactivateInputField();
            input.text = "";
        }
    }

    private void RefreshButton()
    {
        buttonText.text = unread > 0 ? $"CHAT ({unread})" : "CHAT";
    }

    private void ClearHistory()
    {
        foreach (var r in rows) if (r != null) Destroy(r);
        rows.Clear();
        unread = 0;
        if (buttonText != null) RefreshButton();
    }

    private void AddRow(string richText, bool dead)
    {
        var row = new GameObject("Msg", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        row.transform.SetParent(content, false);
        row.GetComponent<Image>().color = dead ? new Color(0.2f, 0.22f, 0.3f, 0.95f) : new Color(0.13f, 0.15f, 0.22f, 0.95f);
        var v = row.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(16, 16, 8, 10);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var t = MakeText(row.transform, richText, 30, TextAlignmentOptions.TopLeft, Color.white);
        t.richText = true;

        rows.Add(row);
        while (rows.Count > MaxRows)
        {
            Destroy(rows[0]);
            rows.RemoveAt(0);
        }
        scrollNextFrame = true;
    }

    private void BuildUI()
    {
        canvasGo = new GameObject("ChatCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400; // above the meeting screen
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // --- CHAT button (bottom-left) ---
        button = new GameObject("ChatButton", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(root, false);
        var brt = (RectTransform)button.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 1);
        brt.anchoredPosition = new Vector2(-206, -16); // top-right, just left of SETTINGS
        brt.sizeDelta = new Vector2(170, 52);
        var bimg = button.GetComponent<Image>();
        bimg.color = new Color(0.16f, 0.83f, 0.74f, 1f);
        var bbtn = button.GetComponent<Button>();
        bbtn.targetGraphic = bimg;
        bbtn.onClick.AddListener(() => SetPanel(!panel.activeSelf));
        buttonText = MakeText(button.transform, "CHAT", 26, TextAlignmentOptions.Center, new Color(0.05f, 0.1f, 0.1f));
        buttonText.fontStyle = FontStyles.Bold;
        Stretch((RectTransform)buttonText.transform);

        // --- toast: last message preview while the panel is closed ---
        toast = new GameObject("ChatToast", typeof(RectTransform), typeof(Image));
        toast.transform.SetParent(root, false);
        var trt = (RectTransform)toast.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = Vector2.zero;
        trt.anchoredPosition = new Vector2(24, 280); // above the bottom bar so it never covers START
        trt.sizeDelta = new Vector2(620, 64);
        toast.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.1f, 0.92f);
        toast.GetComponent<Image>().raycastTarget = false;
        toastText = MakeText(toast.transform, "", 28, TextAlignmentOptions.MidlineLeft, Color.white);
        toastText.richText = true;
        toastText.overflowMode = TextOverflowModes.Ellipsis;
        var ttr = (RectTransform)toastText.transform;
        ttr.anchorMin = Vector2.zero; ttr.anchorMax = Vector2.one;
        ttr.offsetMin = new Vector2(16, 0); ttr.offsetMax = new Vector2(-16, 0);
        toast.SetActive(false);

        // --- panel ---
        panel = new GameObject("ChatPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = Vector2.zero;
        prt.anchoredPosition = new Vector2(24, 280); // above the bottom bar so it never covers START
        prt.sizeDelta = new Vector2(640, 640);
        panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.96f);

        var title = MakeText(panel.transform, "CHAT", 34, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.78f, 0.85f));
        title.fontStyle = FontStyles.Bold;
        var titleRt = (RectTransform)title.transform;
        titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1); titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.anchoredPosition = new Vector2(0, 0); titleRt.sizeDelta = new Vector2(-100, 70);
        titleRt.offsetMin = new Vector2(24, titleRt.offsetMin.y);

        var close = MakeButton(panel.transform, "X", new Vector2(64, 50), new Color(0.2f, 0.22f, 0.3f, 1f), Color.white);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 1);
        crt.anchoredPosition = new Vector2(-14, -10);
        close.onClick.AddListener(() => SetPanel(false));

        // scroll area
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(panel.transform, false);
        var srt = (RectTransform)scrollGo.transform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(12, 96); srt.offsetMax = new Vector2(-12, -76);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch((RectTransform)viewport.transform);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0); // catches mouse wheel/drag

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8; vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        // input row
        var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        fieldGo.transform.SetParent(panel.transform, false);
        var frt = (RectTransform)fieldGo.transform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 0); frt.pivot = new Vector2(0, 0);
        frt.offsetMin = new Vector2(12, 14); frt.offsetMax = new Vector2(-188, 84);
        var fbg = fieldGo.GetComponent<Image>();
        fbg.color = new Color(0.16f, 0.18f, 0.26f, 1f);

        var fv = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        fv.transform.SetParent(fieldGo.transform, false);
        var fvrt = (RectTransform)fv.transform;
        fvrt.anchorMin = Vector2.zero; fvrt.anchorMax = Vector2.one;
        fvrt.offsetMin = new Vector2(16, 4); fvrt.offsetMax = new Vector2(-16, -4);
        var ftext = MakeText(fv.transform, "", 32, TextAlignmentOptions.MidlineLeft, Color.white);
        Stretch((RectTransform)ftext.transform);
        var fph = MakeText(fv.transform, "Type a message...", 32, TextAlignmentOptions.MidlineLeft, new Color(1, 1, 1, 0.4f));
        fph.fontStyle = FontStyles.Italic;
        Stretch((RectTransform)fph.transform);

        input = fieldGo.GetComponent<TMP_InputField>();
        input.textViewport = fvrt;
        input.textComponent = ftext;
        input.placeholder = fph;
        input.characterLimit = MaxLength;
        input.targetGraphic = fbg;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSubmit.AddListener(OnFieldSubmit);

        var send = MakeButton(panel.transform, "SEND", new Vector2(164, 70), new Color(0.16f, 0.83f, 0.74f, 1f), new Color(0.05f, 0.1f, 0.1f));
        var sendRt = (RectTransform)send.transform;
        sendRt.anchorMin = sendRt.anchorMax = sendRt.pivot = new Vector2(1, 0);
        sendRt.anchoredPosition = new Vector2(-12, 14);
        send.onClick.AddListener(() => OnFieldSubmit(input.text));
    }

    private void OnFieldSubmit(string value)
    {
        if (Clean(value).Length == 0)
        {
            // ENTER on an empty box closes the chat.
            SetPanel(false);
            return;
        }
        Submit(value);
        input.text = "";
        focusNextFrame = true;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
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
        var label = MakeText(go.transform, text, 30, TextAlignmentOptions.Center, textColor);
        label.fontStyle = FontStyles.Bold;
        Stretch((RectTransform)label.transform);
        return btn;
    }
}
