using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Client-side feedback for the meeting rules (the server in MeetingManager enforces them):
//  * a toast when an emergency call is refused (cooldown / none left),
//  * the Call Meeting button shows its countdown or remaining calls and dims while unavailable,
//  * a "BODY FOUND" prompt when you stand next to a reportable body (click it or press the Report key).
public class EmergencyUI : MonoBehaviour
{
    private static EmergencyUI instance;

    private GameObject toast, prompt;
    private TextMeshProUGUI toastText, promptText;
    private CanvasGroup promptGroup;
    private float toastUntil;
    private Button meetingButton;
    private TextMeshProUGUI meetingLabel;
    private CanvasGroup meetingGroup;
    private string meetingBase;
    private float nextScan, nextBody;
    private bool bodyNear;

    // True while the local, living player stands close enough to the emergency table to use it.
    public static bool NearTable { get; private set; }

    // True while the big-red-button pop-up is open (movement and other actions are paused).
    public static bool PopupOpen => instance != null && instance.popup != null && instance.popup.activeSelf;

    public static void OpenPopup()
    {
        if (instance == null || !NearTable || PopupOpen) return;
        instance.popup.SetActive(true);
        instance.RefreshPopup();
        Sfx.Play2D("click");
    }

    private GameObject popup;
    private TextMeshProUGUI popupInfo;
    private Image redTop, redBase;

    private GameObject meetPrompt;
    private TextMeshProUGUI meetText;
    private CanvasGroup meetGroup;
    private Button useButton;
    private Image useImage;
    private Color useBaseColor = Color.white;
    private float nextTableScan;

    private const float ReportRange = 1.5f; // matches PlayerController.reportRange

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("EmergencyUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<EmergencyUI>();
        instance.Build();
        MeetingManager.EmergencyDenied += instance.ShowToast;
        Notice.Posted += instance.ShowToast;
        SceneManager.sceneLoaded += (s, m) => instance.OnScene();
    }

    private void OnScene()
    {
        meetingButton = null; meetingLabel = null; meetingGroup = null; meetingBase = null;
        toast.SetActive(false); prompt.SetActive(false); meetPrompt.SetActive(false); popup.SetActive(false);
        NearTable = false; useButton = null; useImage = null;
        nextScan = 0f;
    }

    private void ShowToast(string msg)
    {
        toastText.text = msg;
        toast.SetActive(true);
        toastUntil = Time.unscaledTime + 3f;
    }

    private void Update()
    {
        if (toast.activeSelf && Time.unscaledTime > toastUntil) toast.SetActive(false);

        bool inGame = SceneManager.GetActiveScene().name == "Game";
        var nm = NetworkManager.Singleton;
        var mm = MeetingManager.Instance;
        if (!inGame || nm == null || mm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null)
        {
            prompt.SetActive(false);
            NearTable = false;
            if (meetPrompt != null) meetPrompt.SetActive(false);
            return;
        }

        UpdateMeetingButton(mm);
        UpdateTable(mm, nm.LocalClient.PlayerObject);
        UpdateBodyPrompt(nm.LocalClient.PlayerObject);
    }

    // ------------------------------------------------------- meeting button

    private void UpdateMeetingButton(MeetingManager mm)
    {
        if (meetingButton == null && Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 1f;
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                int n = b.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    if (b.onClick.GetPersistentTarget(i) is CallMeetingButton)
                    {
                        meetingButton = b;
                        meetingLabel = b.GetComponentInChildren<TextMeshProUGUI>(true);
                        meetingBase = meetingLabel != null ? meetingLabel.text : null;
                        meetingGroup = b.GetComponent<CanvasGroup>();
                        if (meetingGroup == null) meetingGroup = b.gameObject.AddComponent<CanvasGroup>();
                        break;
                    }
                }
                if (meetingButton != null) break;
            }
        }
        if (meetingButton == null) return;

        // The on-screen Call Meeting button is replaced by the emergency table: keep it out of sight.
        meetingGroup.alpha = 0f;
        meetingGroup.interactable = false;
        meetingGroup.blocksRaycasts = false;
    }

    // ------------------------------------------------------- emergency table

    private void UpdateTable(MeetingManager mm, NetworkObject localPlayer)
    {
        var role = localPlayer.GetComponent<PlayerRole>();
        bool blocked = role == null || !role.IsAlive
            || mm.IsMeetingActive || TaskMiniGame.IsOpen
            || (RoleRevealUI.Instance != null && RoleRevealUI.Instance.IsShowing)
            || (GameResultManager.Instance != null && GameResultManager.Instance.GameEnded);

        var spawns = SceneSpawnPoints.Instance;
        NearTable = !blocked && spawns != null
            && Vector2.Distance(localPlayer.transform.position, spawns.CenterPosition) <= spawns.EmergencyReach;

        if (useButton == null && Time.unscaledTime >= nextTableScan)
        {
            nextTableScan = Time.unscaledTime + 1f;
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                int n = b.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                    if (b.onClick.GetPersistentTarget(i) is UseButton)
                    {
                        useButton = b;
                        useImage = b.targetGraphic as Image;
                        if (useImage != null) useBaseColor = useImage.color;
                        break;
                    }
                if (useButton != null) break;
            }
        }

        if (popup.activeSelf)
        {
            if (!NearTable || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) popup.SetActive(false);
            else RefreshPopup();
        }

        if (!NearTable)
        {
            if (meetPrompt.activeSelf) meetPrompt.SetActive(false);
            if (useImage != null) useImage.color = useBaseColor;
            return;
        }

        int limit = mm.EmergencyCallsPerPlayer;
        int left = MeetingManager.LocalCallsLeft < 0 ? limit : MeetingManager.LocalCallsLeft;
        float cd = mm.EmergencyCooldownRemaining;
        bool usable = left > 0 && cd <= 0f;

        string line = left <= 0 ? "No emergency meetings left"
            : cd > 0f ? "Available in " + Mathf.CeilToInt(cd) + "s"
            : "Press <color=#FFD54A>" + GameSettings.KeyFor("Interact") + "</color> or USE to open it (" + left + " left)";
        meetText.text = "EMERGENCY BUTTON\n" + line;
        meetPrompt.SetActive(!popup.activeSelf);
        meetGroup.alpha = usable ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f) : 0.6f;

        // Light up USE while the table can be used.
        if (useImage != null)
            useImage.color = usable ? Color.Lerp(useBaseColor, new Color(1f, 0.93f, 0.4f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : useBaseColor;
    }

    private void OnMeetPromptClicked()
    {
        var p = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (p == null || !NearTable) return;
        OpenPopup();
    }

    // The big red button inside the pop-up: actually calls the meeting.
    private void OnRedButtonClicked()
    {
        var mm = MeetingManager.Instance;
        if (mm == null || !NearTable) return;
        int left = MeetingManager.LocalCallsLeft < 0 ? mm.EmergencyCallsPerPlayer : MeetingManager.LocalCallsLeft;
        if (left <= 0 || mm.EmergencyCooldownRemaining > 0f) { Sfx.Play2D("click"); return; } // the info box already says why
        mm.RequestCallMeetingServerRpc();
        popup.SetActive(false);
    }

    private void RefreshPopup()
    {
        var mm = MeetingManager.Instance;
        var nm = NetworkManager.Singleton;
        if (mm == null || nm == null) return;
        int left = MeetingManager.LocalCallsLeft < 0 ? mm.EmergencyCallsPerPlayer : MeetingManager.LocalCallsLeft;
        float cd = mm.EmergencyCooldownRemaining;
        bool usable = left > 0 && cd <= 0f;

        string who = PlayerLabel.DisplayName(nm.LocalClientId).ToUpperInvariant();
        string status = left <= 0 ? "NO MORE EMERGENCY MEETINGS"
            : cd > 0f ? "AVAILABLE IN " + Mathf.CeilToInt(cd) + "S"
            : "EMERGENCY MEETING" + (left == 1 ? "" : "S") + " LEFT";
        popupInfo.text = left <= 0 || cd > 0f
            ? who + "\n<size=70%>" + status + "</size>"
            : who + " HAS\n<size=170%><color=#D32F2F>" + left + "</color></size>\n" + status;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        redTop.color = usable ? Color.Lerp(new Color(0.78f, 0.1f, 0.1f), new Color(0.95f, 0.2f, 0.2f), pulse * 0.6f) : new Color(0.38f, 0.16f, 0.16f);
        redBase.color = usable ? new Color(0.5f, 0.05f, 0.05f) : new Color(0.22f, 0.1f, 0.1f);
    }

    // ---------------------------------------------------------- body prompt

    private void UpdateBodyPrompt(NetworkObject localPlayer)
    {
        var role = localPlayer.GetComponent<PlayerRole>();
        bool blocked = role == null || !role.IsAlive
            || MeetingManager.Instance.IsMeetingActive || TaskMiniGame.IsOpen
            || (RoleRevealUI.Instance != null && RoleRevealUI.Instance.IsShowing)
            || (GameResultManager.Instance != null && GameResultManager.Instance.GameEnded);

        if (Time.unscaledTime >= nextBody)
        {
            nextBody = Time.unscaledTime + 0.1f;
            bodyNear = !blocked && FindReportableBody(localPlayer.transform.position);
        }

        if (blocked || !bodyNear) { prompt.SetActive(false); return; }

        if (!prompt.activeSelf)
        {
            prompt.SetActive(true);
            promptText.text = "BODY FOUND!\nPress <color=#FFD54A>" + GameSettings.KeyFor("Report") + "</color> or click to REPORT";
        }
        promptGroup.alpha = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
    }

    private static bool FindReportableBody(Vector2 from)
    {
        var nm = NetworkManager.Singleton;
        foreach (var spawned in nm.SpawnManager.SpawnedObjects.Values)
        {
            if (spawned == null) continue;
            var body = spawned.GetComponent<DeadBody>();
            if (body == null) continue;
            if (Vector2.Distance(from, spawned.transform.position) > ReportRange) continue;

            if (nm.SpawnManager.SpawnedObjects.TryGetValue(body.OriginalPlayerId, out var orig))
            {
                var r = orig.GetComponent<PlayerRole>();
                if (r != null && (r.IsAlive || r.IsBalBalConsumed)) continue;
            }
            return true;
        }
        return false;
    }

    private void OnPromptClicked()
    {
        var p = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        p?.GetComponent<PlayerController>()?.OnReportButtonPressed();
    }

    // ----------------------------------------------------------------- build

    private void Build()
    {
        var cgo = new GameObject("EmergencyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 190;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

        toast = Box(cgo.transform, "Toast", new Color(0.45f, 0.08f, 0.08f, 0.95f), new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(760, 56));
        toastText = Text(toast.transform, 26, Color.white, TextAlignmentOptions.Center);
        Fill(toastText.rectTransform);
        toast.SetActive(false);

        prompt = Box(cgo.transform, "BodyPrompt", new Color(0.55f, 0.07f, 0.07f, 0.95f), new Vector2(0.5f, 0f), new Vector2(0, 300), new Vector2(560, 110));
        promptGroup = prompt.AddComponent<CanvasGroup>();
        promptText = Text(prompt.transform, 30, Color.white, TextAlignmentOptions.Center);
        Fill(promptText.rectTransform);
        var btn = prompt.AddComponent<Button>();
        btn.targetGraphic = prompt.GetComponent<Image>();
        btn.onClick.AddListener(OnPromptClicked);
        prompt.SetActive(false);

        meetPrompt = Box(cgo.transform, "MeetPrompt", new Color(0.05f, 0.4f, 0.38f, 0.95f), new Vector2(0.5f, 0f), new Vector2(0, 430), new Vector2(760, 110));
        meetGroup = meetPrompt.AddComponent<CanvasGroup>();
        meetText = Text(meetPrompt.transform, 28, Color.white, TextAlignmentOptions.Center);
        Fill(meetText.rectTransform);
        var mbtn = meetPrompt.AddComponent<Button>();
        mbtn.targetGraphic = meetPrompt.GetComponent<Image>();
        mbtn.onClick.AddListener(OnMeetPromptClicked);
        meetPrompt.SetActive(false);

        BuildPopup(cgo.transform);
    }

    private static Sprite discSprite, stripeSprite;

    private static Sprite Disc()
    {
        if (discSprite != null) return discSprite;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    // Yellow / black hazard stripes, tiled.
    private static Sprite Stripes()
    {
        if (stripeSprite != null) return stripeSprite;
        const int n = 48;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = ((x + y) % n) < n / 2 ? new Color32(245, 170, 20, 255) : new Color32(25, 25, 25, 255);
        tex.SetPixels32(px); tex.Apply();
        return stripeSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static RectTransform Place(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    // Among Us style: a blue panel with the big red button on a hazard-striped pad, an info box, and a close X.
    private void BuildPopup(Transform canvas)
    {
        popup = new GameObject("EmergencyPopup", typeof(RectTransform), typeof(Image));
        popup.transform.SetParent(canvas, false);
        var dimRt = (RectTransform)popup.transform;
        dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one; dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // also swallows clicks behind it

        var center = new Vector2(0.5f, 0.5f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(popup.transform, false);
        Place(panel, center, Vector2.zero, new Vector2(640f, 780f));
        panel.GetComponent<Image>().color = new Color(0.16f, 0.34f, 0.56f, 1f);

        var title = Text(panel.transform, 52, Color.white, TextAlignmentOptions.Center);
        Place(title.gameObject, center, new Vector2(0f, 330f), new Vector2(600f, 80f));
        title.text = "EMERGENCY";

        var pad = new GameObject("HazardPad", typeof(RectTransform), typeof(Image));
        pad.transform.SetParent(panel.transform, false);
        Place(pad, center, new Vector2(0f, 90f), new Vector2(480f, 400f));
        var padImg = pad.GetComponent<Image>();
        padImg.sprite = Stripes(); padImg.type = Image.Type.Tiled; padImg.raycastTarget = false;

        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(pad.transform, false);
        Place(inner, center, Vector2.zero, new Vector2(400f, 320f));
        inner.GetComponent<Image>().color = new Color(0.72f, 0.74f, 0.78f, 1f);
        inner.GetComponent<Image>().raycastTarget = false;

        var baseGo = new GameObject("ButtonBase", typeof(RectTransform), typeof(Image));
        baseGo.transform.SetParent(inner.transform, false);
        Place(baseGo, center, new Vector2(0f, -14f), new Vector2(270f, 270f));
        redBase = baseGo.GetComponent<Image>(); redBase.sprite = Disc(); redBase.raycastTarget = false;

        var topGo = new GameObject("RedButton", typeof(RectTransform), typeof(Image), typeof(Button));
        topGo.transform.SetParent(inner.transform, false);
        Place(topGo, center, new Vector2(0f, 6f), new Vector2(250f, 250f));
        redTop = topGo.GetComponent<Image>(); redTop.sprite = Disc();
        var rb = topGo.GetComponent<Button>();
        rb.targetGraphic = redTop;
        rb.transition = Selectable.Transition.None;
        rb.onClick.AddListener(OnRedButtonClicked);

        var info = new GameObject("InfoBox", typeof(RectTransform), typeof(Image));
        info.transform.SetParent(panel.transform, false);
        Place(info, center, new Vector2(0f, -250f), new Vector2(560f, 200f));
        var infoImg = info.GetComponent<Image>(); infoImg.color = new Color(0.95f, 0.96f, 0.98f, 1f); infoImg.raycastTarget = false;
        popupInfo = Text(info.transform, 30, new Color(0.1f, 0.12f, 0.15f), TextAlignmentOptions.Center);
        Fill(popupInfo.rectTransform);
        popupInfo.richText = true;

        var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
        close.transform.SetParent(panel.transform, false);
        Place(close, center, new Vector2(300f, 360f), new Vector2(72f, 72f));
        var closeImg = close.GetComponent<Image>(); closeImg.sprite = Disc(); closeImg.color = Color.white;
        close.GetComponent<Button>().targetGraphic = closeImg;
        close.GetComponent<Button>().onClick.AddListener(() => { popup.SetActive(false); Sfx.Play2D("click"); });
        var x = Text(close.transform, 44, new Color(0.1f, 0.1f, 0.12f), TextAlignmentOptions.Center);
        Fill(x.rectTransform); x.text = "X";

        popup.SetActive(false);
    }

    private static GameObject Box(Transform parent, string name, Color c, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.GetComponent<Image>().color = c;
        return go;
    }

    private static TextMeshProUGUI Text(Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
        return t;
    }

    private static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(10, 0); rt.offsetMax = new Vector2(-10, 0);
    }
}
