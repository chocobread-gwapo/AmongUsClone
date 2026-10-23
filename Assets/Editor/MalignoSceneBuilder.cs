#if UNITY_EDITOR
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the project's scenes from code so they're reproducible instead of
// hand-dragged. Run from the Unity menu bar: Maligno > Build ...
// Safe to re-run: each build overwrites its own scene file.
public static class MalignoSceneBuilder
{
    const string ScenesDir = "Assets/Scenes";
    const string GsmPrefabPath = "Assets/Prefabs/Network/GameStartManager.prefab";

    // ---- font: the project-wide pixel font ----
    const string PixelFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Pixel Game SDF.asset";
    static TMP_FontAsset pixelFont;
    static TMP_FontAsset PixelFont
    {
        get
        {
            if (pixelFont == null) pixelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PixelFontPath);
            if (pixelFont == null) Debug.LogWarning("[SceneBuilder] Pixel Game SDF font not found at " + PixelFontPath);
            return pixelFont;
        }
    }

    // Makes the pixel font TextMeshPro's default, so any text created later
    // (by hand or by other builders) uses it automatically.
    [MenuItem("Maligno/Use Pixel Font As Default")]
    public static void SetPixelFontAsDefault()
    {
        var font = PixelFont;
        if (font == null) return;
        var settings = TMP_Settings.instance;
        if (settings == null) { Debug.LogWarning("[SceneBuilder] TMP Settings not found."); return; }
        var so = new SerializedObject(settings);
        var prop = so.FindProperty("m_defaultFontAsset");
        if (prop == null) { Debug.LogWarning("[SceneBuilder] Couldn't find TMP default font property."); return; }
        prop.objectReferenceValue = font;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneBuilder] TMP default font set to Pixel Game SDF.");
    }

    // ---- palette (Among Us-style teal ship interior) ----
    static readonly Color Bg = new Color(0.14f, 0.27f, 0.28f);
    static readonly Color Frame = new Color(0.10f, 0.15f, 0.16f);
    static readonly Color Teal = new Color(0.17f, 0.86f, 0.76f);
    static readonly Color TealDim = new Color(0.27f, 0.47f, 0.47f);
    static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f);
    static readonly Color Ink = new Color(0.08f, 0.16f, 0.17f);

    // =====================================================================
    // Prefab: persistent GameStartManager (spawned by the host at runtime)
    // =====================================================================
    [MenuItem("Maligno/1. Create GameStartManager Prefab")]
    public static GameObject CreateGameStartManagerPrefab()
    {
        var go = new GameObject("GameStartManager");
        go.AddComponent<NetworkObject>();
        go.AddComponent<GameStartManager>();

        // Must survive the Lobby <-> Game network scene switches. The property
        // name differs between NGO versions, so try the known serialized names.
        var so = new SerializedObject(go.GetComponent<NetworkObject>());
        bool set = false;
        foreach (string name in new[] { "DestroyWithScene", "m_DestroyWithScene" })
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean)
            {
                p.boolValue = false;
                set = true;
                break;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (!set)
            Debug.LogWarning("[SceneBuilder] Couldn't auto-uncheck \"Destroy With Scene\" on the GameStartManager prefab's NetworkObject — open the prefab and uncheck it by hand.");

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, GsmPrefabPath);
        Object.DestroyImmediate(go);
        Debug.Log("[SceneBuilder] Saved " + GsmPrefabPath);
        return prefab;
    }

    // =====================================================================
    // MainMenu scene
    // =====================================================================
    [MenuItem("Maligno/2. Build MainMenu Scene")]
    public static void BuildMainMenu()
    {
        SetPixelFontAsDefault();
        var gsmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GsmPrefabPath) ?? CreateGameStartManagerPrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- camera ----
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Bg;
        cam.orthographic = true;
        camGo.AddComponent<AudioListener>();

        // ---- event system (new Input System) ----
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();

        // ---- systems: NetworkManager + connection manager + audio ----
        BuildSystems(gsmPrefab);

        // ---- canvas ----
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvasRt = canvasGo.GetComponent<RectTransform>();

        // background
        var bg = Panel("Background", canvasRt, Bg);
        Stretch(bg);

        // right-hand "porthole" window, like Among Us's space view
        var window = Panel("Window", canvasRt, Frame);
        Anchor(window, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-60, -30), new Vector2(1000, 760));
        var space = Panel("Space", window, Dark);
        Stretch(space, 24);
        var tagline = Label("Tagline", space, "SITIO MALIGNO", 64, FontStyles.Bold, new Color(1, 1, 1, 0.18f));
        Stretch(tagline);

        // title
        var title = Label("Title", canvasRt, "MALIGNO", 130, FontStyles.Bold, Color.white);
        Anchor(title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -50), new Vector2(800, 170));
        title.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // ---- main buttons panel ----
        var mainPanel = Panel("MainButtonsPanel", canvasRt, Frame);
        Anchor(mainPanel, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(60, -90), new Vector2(760, 620));
        var vlg = mainPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 28, 28);
        vlg.spacing = 20;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var playBtn = MenuButton("PlayButton", mainPanel, "PLAY", Teal, Ink, 190, 90);
        var settingsBtn = MenuButton("SettingsButton", mainPanel, "SETTINGS", TealDim, Color.white, 130, 56);
        var quitBtn = MenuButton("QuitButton", mainPanel, "QUIT", TealDim, Color.white, 130, 56);

        // ---- play (host / join) panel ----
        var playPanel = Panel("PlayPanel", canvasRt, Frame);
        Anchor(playPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 760));
        var pv = playPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        pv.padding = new RectOffset(40, 40, 36, 36);
        pv.spacing = 22;
        pv.childControlWidth = pv.childControlHeight = true;
        pv.childForceExpandWidth = true;
        pv.childForceExpandHeight = false;

        Label("PlayHeader", playPanel, "PLAY", 70, FontStyles.Bold, Teal).gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
        var hostBtn = MenuButton("HostButton", playPanel, "HOST GAME", Teal, Ink, 120, 54);

        var codeRoot = Panel("JoinCodePanel", playPanel, Ink);
        codeRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
        var codeText = Label("JoinCodeText", codeRoot, "CODE", 64, FontStyles.Bold, Teal);
        Stretch(codeText);
        codeRoot.gameObject.SetActive(false);

        Label("OrLabel", playPanel, "- or join a friend -", 34, FontStyles.Italic, new Color(1, 1, 1, 0.6f))
            .gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var input = CodeInput("JoinCodeInput", playPanel);
        input.gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
        var joinBtn = MenuButton("JoinButton", playPanel, "JOIN GAME", TealDim, Color.white, 110, 50);
        var status = Label("StatusText", playPanel, "", 32, FontStyles.Normal, new Color(1, 1, 1, 0.85f));
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
        var playBack = MenuButton("BackButton", playPanel, "BACK", Frame, Color.white, 80, 44);
        playBack.GetComponent<Image>().color = new Color(0.22f, 0.3f, 0.31f);

        // ---- settings panel ----
        var setPanel = Panel("SettingsPanel", canvasRt, Frame);
        Anchor(setPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 460));
        var sv = setPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        sv.padding = new RectOffset(40, 40, 36, 36);
        sv.spacing = 26;
        sv.childControlWidth = sv.childControlHeight = true;
        sv.childForceExpandWidth = true;
        sv.childForceExpandHeight = false;
        Label("SettingsHeader", setPanel, "SETTINGS", 70, FontStyles.Bold, Teal).gameObject.AddComponent<LayoutElement>().preferredHeight = 90;
        Label("VolumeLabel", setPanel, "Music volume", 38, FontStyles.Normal, Color.white).gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var slider = VolumeSlider("VolumeSlider", setPanel);
        slider.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var setBack = MenuButton("BackButton", setPanel, "BACK", Frame, Color.white, 80, 44);
        setBack.GetComponent<Image>().color = new Color(0.22f, 0.3f, 0.31f);

        // ---- wiring ----
        var menu = canvasGo.AddComponent<MainMenuUI>();
        SetRef(menu, "playButton", playBtn);
        SetRef(menu, "settingsButton", settingsBtn);
        SetRef(menu, "quitButton", quitBtn);
        SetRef(menu, "mainButtonsPanel", mainPanel.gameObject);
        SetRef(menu, "playPanel", playPanel.gameObject);
        SetRef(menu, "settingsPanel", setPanel.gameObject);
        UnityEventTools.AddPersistentListener(playBack.onClick, menu.OnBackClicked);
        UnityEventTools.AddPersistentListener(setBack.onClick, menu.OnBackClicked);

        var conn = playPanel.gameObject.AddComponent<LobbyConnectionUI>();
        SetRef(conn, "hostButton", hostBtn);
        SetRef(conn, "joinCodeDisplayRoot", codeRoot.gameObject);
        SetRef(conn, "joinCodeText", codeText.GetComponent<TextMeshProUGUI>());
        SetRef(conn, "joinButton", joinBtn);
        SetRef(conn, "joinCodeInputField", input);
        SetRef(conn, "statusText", status.GetComponent<TextMeshProUGUI>());

        // MainMenuUI.Awake hides these panels at runtime; also leave them
        // hidden in the saved scene so the editor view isn't a pile of overlap.
        playPanel.gameObject.SetActive(false);
        setPanel.gameObject.SetActive(false);

        Save(scene, "MainMenu");
    }

    // =====================================================================
    // Lobby scene (Among Us-style dropship waiting room)
    // =====================================================================
    class Stepper { public Button minus, plus; public TMP_Text value; }

    [MenuItem("Maligno/3. Build Lobby Scene")]
    public static void BuildLobby()
    {
        SetPixelFontAsDefault();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- camera (CameraFollow is hooked up by the local player) ----
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.position = new Vector3(0, 0, -10);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraFollow>();

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();

        var lightGo = new GameObject("Global Light 2D");
        var light = lightGo.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Global;
        light.intensity = 1f;

        // ---- the room ----
        var room = new GameObject("Room").transform;
        Block(room, "Floor", new Vector2(0, -0.75f), new Vector2(18, 7.5f), new Color(0.36f, 0.42f, 0.43f), -10, false);
        Block(room, "BackWall", new Vector2(0, 4.25f), new Vector2(18, 2.5f), new Color(0.50f, 0.56f, 0.58f), -10, false);
        Block(room, "BackWallShade", new Vector2(0, 3.1f), new Vector2(18, 0.2f), new Color(0.22f, 0.27f, 0.28f), -9, false);
        Block(room, "Door", new Vector2(0, 4.0f), new Vector2(2.2f, 2.2f), new Color(0.20f, 0.24f, 0.26f), -9, false);
        Block(room, "DoorInner", new Vector2(0, 4.0f), new Vector2(1.6f, 1.7f), new Color(0.32f, 0.38f, 0.40f), -8, false);
        // floor stripes (the yellow guide lines)
        Block(room, "StripeH", new Vector2(0, -2.3f), new Vector2(18, 0.12f), new Color(0.78f, 0.62f, 0.22f), -9, false);
        Block(room, "StripeV1", new Vector2(-2.2f, -0.75f), new Vector2(0.12f, 7.5f), new Color(0.78f, 0.62f, 0.22f), -9, false);
        Block(room, "StripeV2", new Vector2(2.2f, -0.75f), new Vector2(0.12f, 7.5f), new Color(0.78f, 0.62f, 0.22f), -9, false);
        // seat rows on the back wall
        for (int i = 0; i < 5; i++)
        {
            Block(room, "SeatL" + i, new Vector2(-7.4f + i * 0.9f, 3.55f), new Vector2(0.6f, 0.7f), new Color(0.22f, 0.36f, 0.52f), -8, false);
            Block(room, "SeatR" + i, new Vector2(7.4f - i * 0.9f, 3.55f), new Vector2(0.6f, 0.7f), new Color(0.22f, 0.36f, 0.52f), -8, false);
        }
        // crates
        Crate(room, "Crate1", new Vector2(-5.2f, 1.2f), 1.5f);
        Crate(room, "Crate2", new Vector2(5.2f, 0.4f), 1.7f);
        Crate(room, "Crate3", new Vector2(-4.6f, -2.8f), 1.7f);
        Crate(room, "Crate4", new Vector2(4.4f, -3.2f), 1.3f);
        // outer walls (invisible colliders)
        var walls = new GameObject("Walls").transform;
        Wall(walls, "WallTop", new Vector2(0, 3.15f), new Vector2(20, 0.5f));
        Wall(walls, "WallBottom", new Vector2(0, -4.75f), new Vector2(20, 0.5f));
        Wall(walls, "WallLeft", new Vector2(-9.25f, 0), new Vector2(0.5f, 12));
        Wall(walls, "WallRight", new Vector2(9.25f, 0), new Vector2(0.5f, 12));

        // ---- HUD canvas ----
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvasRt = canvasGo.GetComponent<RectTransform>();
        var grey = new Color(0.22f, 0.3f, 0.31f);

        // leave (top-left)
        var leaveBtn = MenuButton("LeaveButton", canvasRt, "LEAVE", grey, Color.white, 80, 40);
        Anchor(leaveBtn.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(260, 80));

        // info panel (right)
        var info = Panel("InfoPanel", canvasRt, Frame);
        Anchor(info, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(480, 620));
        var inner = Panel("Inner", info, new Color(0.13f, 0.15f, 0.16f));
        Stretch(inner, 12);
        var v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(24, 24, 20, 20);
        v.spacing = 12;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        Left(Label("CodeCaption", inner, "ROOM CODE", 30, FontStyles.Normal, Color.white), 44);
        var codeText = Label("CodeText", inner, "-", 84, FontStyles.Bold, Teal);
        Left(codeText, 110);
        Label("Divider", inner, "", 4, FontStyles.Normal, Color.white); // spacer replaced below
        Object.DestroyImmediate(inner.Find("Divider").gameObject);
        var div = Panel("Divider", inner, new Color(1, 1, 1, 0.15f));
        div.gameObject.AddComponent<LayoutElement>().preferredHeight = 3;
        Left(Label("RoomCaption", inner, "ROOM SETTINGS", 30, FontStyles.Normal, Color.white), 50);

        var capRow = Row("CapacityRow", inner, 76);
        var capCaption = Label("Caption", capRow, "CAPACITY", 28, FontStyles.Normal, Color.white);
        capCaption.gameObject.AddComponent<LayoutElement>().preferredWidth = 190;
        var capBox = Panel("CapacityBox", capRow, Dark);
        var capLe = capBox.gameObject.AddComponent<LayoutElement>();
        capLe.flexibleWidth = 1; capLe.preferredHeight = 76;
        var capText = Label("CapacityText", capBox, "0/15", 44, FontStyles.Bold, new Color(1f, 0.5f, 0.2f));
        Stretch(capText);

        Left(Label("GameCaption", inner, "GAME SETTINGS", 30, FontStyles.Normal, Color.white), 50);
        var btnRow = Row("SettingsButtons", inner, 90);
        var editBtn = SmallButton(btnRow, "EditButton", "EDIT", 190, 90, Teal, Ink, 40);
        var viewBtn = SmallButton(btnRow, "ViewButton", "VIEW", 190, 90, Teal, Ink, 40);
        // spacing so the info panel isn't cramped
        var pad = new GameObject("Spacer", typeof(RectTransform));
        pad.transform.SetParent(inner, false);
        pad.AddComponent<LayoutElement>().flexibleHeight = 1;

        // bottom-centre: host START / client waiting box
        var countText = Label("PlayerCount", canvasRt, "PLAYERS: 0", 34, FontStyles.Bold, Color.white);
        Anchor(countText, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 175), new Vector2(700, 50));

        var startBtn = MenuButton("StartButton", canvasRt, "START", Teal, Ink, 110, 72);
        Anchor(startBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(680, 110));
        startBtn.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        startBtn.GetComponentInChildren<TextMeshProUGUI>().rectTransform.offsetMax = Vector2.zero;
        var startBridge = startBtn.gameObject.AddComponent<StartGameButton>();
        UnityEventTools.AddPersistentListener(startBtn.onClick, startBridge.OnStartGameClicked);

        var waitBox = Panel("WaitingBox", canvasRt, new Color(0.13f, 0.15f, 0.16f, 0.9f));
        Anchor(waitBox, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(680, 110));
        var waitText = Label("WaitingText", waitBox, "WAITING FOR HOST", 52, FontStyles.Bold, new Color(1, 1, 1, 0.55f));
        Stretch(waitText);

        // ---- host settings overlay (hidden until EDIT / VIEW) ----
        var overlay = BuildSettingsOverlay(canvasRt);

        // ---- wiring ----
        UnityEventTools.AddPersistentListener(editBtn.onClick, overlay.OpenPanel);
        UnityEventTools.AddPersistentListener(viewBtn.onClick, overlay.OpenPanel);

        var lobby = canvasGo.AddComponent<LobbyUI>();
        SetObjArray(lobby, "hostOnlyObjects", startBtn.gameObject, editBtn.gameObject);
        SetObjArray(lobby, "clientOnlyObjects", waitBox.gameObject, viewBtn.gameObject);
        SetRef(lobby, "joinCodeText", codeText.GetComponent<TextMeshProUGUI>());
        SetRef(lobby, "capacityText", capText.GetComponent<TextMeshProUGUI>());
        SetRef(lobby, "playerCountText", countText.GetComponent<TextMeshProUGUI>());
        SetRef(lobby, "leaveButton", leaveBtn);

        var lobbySpawn = new GameObject("SpawnPoints").AddComponent<SceneSpawnPoints>();
        var lobbyCenter = new GameObject("Center");
        lobbyCenter.transform.SetParent(lobbySpawn.transform, false);
        lobbyCenter.transform.position = new Vector3(0, -0.5f, 0);
        SetRef(lobbySpawn, "center", lobbyCenter.transform);

        overlay.gameObject.SetActive(false);
        Save(scene, "Lobby");
    }

    static HostSettingsPanelUI BuildSettingsOverlay(RectTransform canvasRt)
    {
        var overlay = Panel("HostSettingsOverlay", canvasRt, new Color(0, 0, 0, 0.65f));
        Stretch(overlay);
        overlay.GetComponent<Image>().sprite = null;
        var ui = overlay.gameObject.AddComponent<HostSettingsPanelUI>();

        var win = Panel("Window", overlay, Frame);
        Anchor(win, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 940));
        var title = Label("Title", win, "GAME SETTINGS", 64, FontStyles.Bold, Teal);
        Anchor(title, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(900, 90));

        var close = SmallButton(win, "CloseButton", "X", 80, 80, new Color(0.75f, 0.2f, 0.2f), Color.white, 44);
        var closeRt = close.GetComponent<RectTransform>();
        closeRt.GetComponent<LayoutElement>().ignoreLayout = true;
        Anchor(closeRt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(80, 80));
        UnityEventTools.AddPersistentListener(close.onClick, ui.ClosePanel);

        // scroll view
        var scroll = new GameObject("Scroll View", typeof(RectTransform), typeof(ScrollRect));
        scroll.transform.SetParent(win, false);
        var scrollRt = scroll.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(30, 30); scrollRt.offsetMax = new Vector2(-30, -120);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scroll.transform, false);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        Stretch(viewport.GetComponent<RectTransform>());
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var cRt = content.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1); cRt.pivot = new Vector2(0.5f, 1);
        cRt.offsetMin = cRt.offsetMax = Vector2.zero;
        var cv = content.GetComponent<VerticalLayoutGroup>();
        cv.padding = new RectOffset(20, 20, 10, 20);
        cv.spacing = 14;
        cv.childControlWidth = cv.childControlHeight = true;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = scroll.GetComponent<ScrollRect>();
        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.content = cRt;
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40;

        // --- general ---
        Left(Label("GeneralHeader", content.transform, "GENERAL", 44, FontStyles.Bold, Teal), 60);
        var impRow = Row("ImpostorRow", content.transform, 64);
        Left(Label("Caption", impRow, "Impostors", 34, FontStyles.Normal, Color.white), 64).gameObject.GetComponent<LayoutElement>().preferredWidth = 520;
        var impStepper = MakeStepper(impRow, "ImpostorStepper", 360);

        var killRow = Row("KillCooldownRow", content.transform, 64);
        Left(Label("Caption", killRow, "Kill cooldown", 34, FontStyles.Normal, Color.white), 64).gameObject.GetComponent<LayoutElement>().preferredWidth = 520;
        var killDd = MakeDropdown(killRow, "KillCooldownDropdown", 360);

        var meetRow = Row("MeetingRow", content.transform, 64);
        Left(Label("Caption", meetRow, "Meeting duration", 34, FontStyles.Normal, Color.white), 64).gameObject.GetComponent<LayoutElement>().preferredWidth = 520;
        var meetDd = MakeDropdown(meetRow, "MeetingDurationDropdown", 360);

        // --- roles ---
        string[] malignoNames = { "Duwende", "Tikbalang", "Bal-bal", "Aswang", "Kapre" };
        string[] survivorNames = { "Third Eye", "Reiki", "Albularyo", "Babaylan", "Tarot" };
        var malignoSteppers = RoleSection(content.transform, "MALIGNO ROLES", malignoNames);
        var survivorSteppers = RoleSection(content.transform, "SURVIVOR ROLES", survivorNames);

        // --- assign to HostSettingsPanelUI ---
        var so = new SerializedObject(ui);
        SetStepperProps(so, "impostorCountStepper", impStepper);
        so.FindProperty("killCooldownDropdown").objectReferenceValue = killDd;
        so.FindProperty("meetingDurationDropdown").objectReferenceValue = meetDd;
        var mRows = so.FindProperty("malignoRoleRows");
        mRows.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            var e = mRows.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("type").enumValueIndex = i + 1; // skip None
            SetStepperProps(so, e.propertyPath + ".row.countStepper", malignoSteppers[i * 2]);
            SetStepperProps(so, e.propertyPath + ".row.chanceStepper", malignoSteppers[i * 2 + 1]);
        }
        var sRows = so.FindProperty("survivorRoleRows");
        sRows.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            var e = sRows.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("survivorClass").enumValueIndex = i + 1;
            SetStepperProps(so, e.propertyPath + ".row.countStepper", survivorSteppers[i * 2]);
            SetStepperProps(so, e.propertyPath + ".row.chanceStepper", survivorSteppers[i * 2 + 1]);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return ui;
    }

    // Returns steppers as [count0, chance0, count1, chance1, ...].
    static Stepper[] RoleSection(Transform parent, string header, string[] names)
    {
        Left(Label(header + "Header", parent, header, 44, FontStyles.Bold, Teal), 60);
        var cols = Row(header + "Cols", parent, 40);
        Left(Label("c0", cols, "ROLE", 26, FontStyles.Normal, new Color(1, 1, 1, 0.55f)), 40).gameObject.GetComponent<LayoutElement>().preferredWidth = 360;
        Centered(Label("c1", cols, "# OF ROLE", 26, FontStyles.Normal, new Color(1, 1, 1, 0.55f)), 40, 380);
        Centered(Label("c2", cols, "% CHANCE", 26, FontStyles.Normal, new Color(1, 1, 1, 0.55f)), 40, 380);
        var result = new Stepper[names.Length * 2];
        for (int i = 0; i < names.Length; i++)
        {
            var row = Row(names[i] + "Row", parent, 64);
            Left(Label("Name", row, names[i], 34, FontStyles.Normal, Color.white), 64).gameObject.GetComponent<LayoutElement>().preferredWidth = 360;
            result[i * 2] = MakeStepper(row, "CountStepper", 380);
            result[i * 2 + 1] = MakeStepper(row, "ChanceStepper", 380);
        }
        return result;
    }

    static void SetStepperProps(SerializedObject so, string path, Stepper s)
    {
        so.FindProperty(path + ".minusButton").objectReferenceValue = s.minus;
        so.FindProperty(path + ".plusButton").objectReferenceValue = s.plus;
        so.FindProperty(path + ".valueText").objectReferenceValue = s.value;
    }

    static void SetObjArray(Object target, string field, params Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[SceneBuilder] no field {field}"); return; }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- lobby world helpers ----
    static Sprite SquareSprite => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/Square.png");

    static GameObject Block(Transform parent, string name, Vector2 pos, Vector2 size, Color color, int order, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SquareSprite;
        sr.color = color;
        sr.sortingOrder = order;
        if (collider) go.AddComponent<BoxCollider2D>();
        return go;
    }

    static void Crate(Transform parent, string name, Vector2 pos, float size)
    {
        var c = Block(parent, name, pos, new Vector2(size, size), new Color(0.20f, 0.36f, 0.33f), -5, true);
        var top = new GameObject("Top");
        top.transform.SetParent(c.transform, false);
        top.transform.localPosition = new Vector3(0, 0.3f, 0);
        top.transform.localScale = new Vector3(0.9f, 0.3f, 1);
        var sr = top.AddComponent<SpriteRenderer>();
        sr.sprite = SquareSprite; sr.color = new Color(0.30f, 0.50f, 0.45f); sr.sortingOrder = -4;
    }

    static void Wall(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var col = go.AddComponent<BoxCollider2D>();
        col.size = size;
    }

    // ---- lobby UI helpers ----
    static RectTransform Left(RectTransform label, float height)
    {
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
        var le = label.gameObject.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        return label;
    }

    static void Centered(RectTransform label, float height, float width)
    {
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height; le.preferredWidth = width;
    }

    static RectTransform Row(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        go.AddComponent<LayoutElement>().preferredHeight = height;
        return go.GetComponent<RectTransform>();
    }

    static Button SmallButton(Transform parent, string name, string text, float width, float height, Color fill, Color textColor, float fontSize)
    {
        var rt = Panel(name, parent, fill);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width; le.preferredHeight = height;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        var label = Label("Label", rt, text, fontSize, FontStyles.Bold, textColor);
        Stretch(label);
        return btn;
    }

    static Stepper MakeStepper(Transform parent, string name, float width)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 64;
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        var s = new Stepper();
        s.minus = SmallButton(root.transform, "Minus", "-", 70, 64, TealDim, Color.white, 44);
        var val = Label("Value", root.transform, "0", 36, FontStyles.Bold, Color.white);
        val.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        s.value = val.GetComponent<TextMeshProUGUI>();
        s.plus = SmallButton(root.transform, "Plus", "+", 70, 64, Teal, Ink, 44);
        return s;
    }

    static TMP_Dropdown MakeDropdown(Transform parent, string name, float width)
    {
        var res = new TMP_DefaultControls.Resources
        {
            standard = Skin,
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };
        var go = TMP_DefaultControls.CreateDropdown(res);
        go.name = name;
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 64;
        var dd = go.GetComponent<TMP_Dropdown>();
        if (PixelFont != null) { dd.captionText.font = PixelFont; dd.itemText.font = PixelFont; }
        dd.captionText.fontSize = 32;
        dd.itemText.fontSize = 32;
        var tmpl = dd.template;
        tmpl.sizeDelta = new Vector2(0, 280);
        var item = dd.itemText.transform.parent as RectTransform;
        if (item != null) item.sizeDelta = new Vector2(0, 56);
        return dd;
    }

    // =====================================================================
    // Game scene (map template + core systems)
    // =====================================================================
    const string MapPrefabPath = "Assets/Prefabs/Map/MapTemplate.prefab";
    // Uniform scale applied to the whole map (art, walls, furniture). Everything
    // that is placed by hand in the Game scene (tasks, spawn, camera) is scaled
    // by the same amount. Change this one number to resize the map, then run
    // items 4 and 5.
    const float MapScale = 1.5f;

    static int EnsureWallLayer()
    {
        int existing = LayerMask.NameToLayer("Wall");
        if (existing >= 0) return existing;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = "Wall";
                tm.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return i;
            }
        }
        Debug.LogWarning("[SceneBuilder] No free layer slot for \"Wall\".");
        return 0;
    }

    // The reusable map: backdrop sprite + wall colliders + meeting-table marker.
    [MenuItem("Maligno/4. Build Map Template Prefab")]
    public static GameObject BuildMapPrefab()
    {
        int wallLayer = EnsureWallLayer();

        var root = new GameObject("MapTemplate");
        root.transform.localScale = Vector3.one * MapScale;
        MalignoMapBuilder.Fill(root.transform, wallLayer);

        var table = new GameObject("MeetingTable");
        table.transform.SetParent(root.transform, false);
        table.transform.localPosition = MapTemplateData.MeetingTable;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, MapPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log("[SceneBuilder] Saved " + MapPrefabPath);

        // Let the player's wall-clip raycast see the Wall layer too.
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        var pc = player != null ? player.GetComponent<PlayerController>() : null;
        if (pc != null)
        {
            var so = new SerializedObject(pc);
            var p = so.FindProperty("wallLayerMask");
            if (p != null) { p.intValue |= 1 << wallLayer; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(player); AssetDatabase.SaveAssets(); }
        }
        return prefab;
    }

    [MenuItem("Maligno/5. Build Game Scene")]
    public static void BuildGame()
    {
        SetPixelFontAsDefault();
        var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath) ?? BuildMapPrefab();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.position = new Vector3(MapTemplateData.MeetingTable.x * MapScale, MapTemplateData.MeetingTable.y * MapScale, -10);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraFollow>();

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();

        var lightGo = new GameObject("Global Light 2D");
        var light = lightGo.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Global;
        light.intensity = 0.45f; // dim: each player's own vision light reveals the map around them

        var map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
        map.name = "MapTemplate";

        // ---- spawn: ring around the emergency table ----
        var spawn = new GameObject("SpawnPoints").AddComponent<SceneSpawnPoints>();
        var centerGo = new GameObject("Center");
        centerGo.transform.SetParent(spawn.transform, false);
        centerGo.transform.position = (Vector3)(Vector2)MapTemplateData.MeetingTable * MapScale;
        SetRef(spawn, "center", centerGo.transform);

        // ---- task zones (placeholder positions in every room) ----
        var tasks = new GameObject("Tasks").transform;
        foreach (var t in MapTemplateData.Tasks)
        {
            var go = new GameObject("Task_" + t.room);
            go.transform.SetParent(tasks, false);
            float tx = t.x * MapScale, ty = t.y * MapScale;
            go.transform.position = new Vector3(tx, ty, 0);
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.9f * MapScale;
            col.isTrigger = true;
            go.AddComponent<NetworkObject>();
            var zone = go.AddComponent<TaskZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("taskName").stringValue = t.task;
            so.FindProperty("roomName").stringValue = t.room;
            so.ApplyModifiedPropertiesWithoutUndo();

            var vis = Block(go.transform, "Marker", new Vector2(tx, ty), new Vector2(0.5f, 0.5f), new Color(1f, 0.85f, 0.2f, 0.9f), 5, false);
            vis.transform.rotation = Quaternion.Euler(0, 0, 45);
        }

        // ---- scene-local network systems ----
        foreach (var comp in new System.Type[] { typeof(TaskManager), typeof(GameResultManager), typeof(MeetingManager) })
        {
            var go = new GameObject(comp.Name);
            go.AddComponent<NetworkObject>();
            go.AddComponent(comp);
        }

        // ---- HUD ----
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        BuildGameHud(canvasGo.GetComponent<RectTransform>());

        Save(scene, "Game");
    }

    // =====================================================================
    // Game HUD (pixel-font, Among Us-style layout)
    // =====================================================================
    static TextMeshProUGUI Tmp(RectTransform r) => r.GetComponent<TextMeshProUGUI>();

    static GameObject SaveUiPrefab(GameObject go, string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"Assets/Prefabs/UI/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject MakeTaskEntryPrefab()
    {
        var rt = Label("TaskEntry", null, "Task", 26, FontStyles.Normal, Color.white);
        Tmp(rt).alignment = TextAlignmentOptions.Left;
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
        return SaveUiPrefab(rt.gameObject, "UI_TaskEntry");
    }

    static GameObject MakeVoteButtonPrefab()
    {
        var rt = Panel("VoteButton", null, TealDim);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        btn.colors = colors;
        var label = Label("Label", rt, "Player", 32, FontStyles.Bold, Color.white);
        Stretch(label, 10);
        Tmp(label).alignment = TextAlignmentOptions.Left;
        label.offsetMin = new Vector2(24, 0);
        return SaveUiPrefab(rt.gameObject, "UI_VoteButton");
    }

    // Square action button with a hidden cooldown-number overlay.
    static Button ActionButton(Transform parent, string name, string text, Color fill, Color textColor, Vector2 pos, float size, float fontSize, out TextMeshProUGUI cooldownText)
    {
        var btn = SmallButton(parent, name, text, size, size, fill, textColor, fontSize);
        var rt = btn.GetComponent<RectTransform>();
        Anchor(rt, new Vector2(1, 0), new Vector2(1, 0), pos, new Vector2(size, size));
        var cd = Label("CooldownText", rt, "", 80, FontStyles.Bold, Color.white);
        Stretch(cd);
        cooldownText = Tmp(cd);
        cooldownText.gameObject.SetActive(false);
        return btn;
    }

    static void BuildGameHud(RectTransform canvas)
    {
        var taskEntryPrefab = MakeTaskEntryPrefab();
        var voteBtnPrefab = MakeVoteButtonPrefab();
        var dim = new Color(0, 0, 0, 0.78f);
        var grey = new Color(0.22f, 0.3f, 0.31f);

        var ctl = new GameObject("UI Controllers").transform; // scene-level UI singletons live here
        ctl.SetParent(canvas, false);
        MalignoTarotPopup.AddTo(canvas, ctl, PixelFont);

        // ------------- task panel (top-left) -------------
        var taskPanel = Panel("TaskPanel", canvas, new Color(Frame.r, Frame.g, Frame.b, 0.88f));
        Anchor(taskPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(540, 200));
        var tv = taskPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        tv.padding = new RectOffset(22, 22, 18, 18);
        tv.spacing = 10;
        tv.childControlWidth = tv.childControlHeight = true;
        tv.childForceExpandWidth = true; tv.childForceExpandHeight = false;
        taskPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Left(Label("BarCaption", taskPanel, "TOTAL TASKS COMPLETED", 22, FontStyles.Normal, Color.white), 30);
        var barBack = Panel("BarBack", taskPanel, Dark);
        barBack.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        var barFill = Panel("BarFill", barBack, Teal);
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = new Vector2(0, 1);
        barFill.offsetMin = new Vector2(4, 4); barFill.offsetMax = new Vector2(-4, -4);
        var barLabel = Label("BarLabel", barBack, "", 22, FontStyles.Bold, Color.white);
        Stretch(barLabel);
        Left(Label("ListHeader", taskPanel, "TASKS", 28, FontStyles.Bold, Teal), 40);
        var listGo = new GameObject("TaskListContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
        listGo.transform.SetParent(taskPanel, false);
        var lv = listGo.GetComponent<VerticalLayoutGroup>();
        lv.spacing = 4;
        lv.childControlWidth = lv.childControlHeight = true;
        lv.childForceExpandWidth = true; lv.childForceExpandHeight = false;

        var bar = taskPanel.gameObject.AddComponent<TaskProgressBarUI>();
        SetRef(bar, "fill", barFill);
        SetRef(bar, "label", Tmp(barLabel));
        var taskList = taskPanel.gameObject.AddComponent<TaskListUI>();
        SetRef(taskList, "listContainer", listGo.transform);
        SetRef(taskList, "entryPrefab", taskEntryPrefab);
        SetRef(taskList, "containerRect", listGo.GetComponent<RectTransform>());

        // ------------- action buttons (bottom-right) -------------
        var actions = new GameObject("ActionButtons", typeof(RectTransform)).GetComponent<RectTransform>();
        actions.SetParent(canvas, false);
        Stretch(actions);

        var reportBtn = ActionButton(actions, "ReportButton", "REPORT", new Color(0.95f, 0.7f, 0.15f), Ink, new Vector2(-300, 40), 220, 34, out _);
        var useBtn = ActionButton(actions, "UseButton", "USE", Teal, Ink, new Vector2(-40, 40), 240, 56, out _);
        var killBtn = ActionButton(actions, "KillButton", "KILL", new Color(0.85f, 0.15f, 0.15f), Color.white, new Vector2(-40, 300), 220, 48, out var killCd);
        var abilityBtn = ActionButton(actions, "AbilityButton", "ABILITY", new Color(0.55f, 0.3f, 0.8f), Color.white, new Vector2(-280, 300), 220, 34, out var abilityCd);

        var useBridge = useBtn.gameObject.AddComponent<UseButton>();
        UnityEventTools.AddPersistentListener(useBtn.onClick, useBridge.OnUseClicked);
        var reportBridge = reportBtn.gameObject.AddComponent<ReportButton>();
        UnityEventTools.AddPersistentListener(reportBtn.onClick, reportBridge.OnReportClicked);
        var killBridge = killBtn.gameObject.AddComponent<KillButton>();   // adds its own CanvasGroup
        UnityEventTools.AddPersistentListener(killBtn.onClick, killBridge.OnKillClicked);
        var abilityBridge = abilityBtn.gameObject.AddComponent<AbilityButton>();
        UnityEventTools.AddPersistentListener(abilityBtn.onClick, abilityBridge.OnAbilityClicked);

        // ------------- call meeting (top-right) -------------
        var meetingBtn = SmallButton(canvas, "CallMeetingButton", "MEETING", 300, 90, grey, Color.white, 34);
        Anchor(meetingBtn.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(300, 90));
        var meetingBridge = meetingBtn.gameObject.AddComponent<CallMeetingButton>();
        UnityEventTools.AddPersistentListener(meetingBtn.onClick, meetingBridge.OnCallMeetingClicked);

        // ------------- HUD visibility + cooldown controllers -------------
        var hudGo = new GameObject("GameHud"); hudGo.transform.SetParent(ctl, false);
        var hud = hudGo.AddComponent<GameHudUI>();
        SetObjArray(hud, "hudElements", taskPanel.gameObject, actions.gameObject, meetingBtn.gameObject);
        SetObjArray(hud, "deadRestrictedButtons", meetingBtn.gameObject, reportBtn.gameObject, abilityBtn.gameObject);

        var cdGo = new GameObject("AbilityCooldown"); cdGo.transform.SetParent(ctl, false);
        var cd = cdGo.AddComponent<AbilityCooldownUI>();
        SetRef(cd, "killCooldownRoot", killBtn.gameObject);
        SetRef(cd, "killCooldownText", killCd);
        SetRef(cd, "abilityButton", abilityBtn);
        SetRef(cd, "abilityCooldownText", abilityCd);

        // ------------- voting / meeting overlay -------------
        var voting = Panel("VotingPanel", canvas, dim);
        voting.GetComponent<Image>().sprite = null;
        Stretch(voting);
        var vwin = Panel("Window", voting, Frame);
        Anchor(vwin, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 940));
        var vtitle = Label("Title", vwin, "WHO IS THE MALIGNO?", 64, FontStyles.Bold, Teal);
        Anchor(vtitle, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(1200, 90));
        var vtimer = Label("Timer", vwin, "VOTING ENDS IN 30s", 32, FontStyles.Bold, Color.white);
        Anchor(vtimer, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -105), new Vector2(900, 50));
        var gridGo = new GameObject("PlayerListContainer", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGo.transform.SetParent(vwin, false);
        var grt = gridGo.GetComponent<RectTransform>();
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = new Vector2(40, 130); grt.offsetMax = new Vector2(-40, -140);
        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(710, 72);
        grid.spacing = new Vector2(20, 14);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        var judgeBtn = SmallButton(vwin, "JudgeModeButton", "JUDGE MODE", 760, 80, new Color(0.55f, 0.3f, 0.8f), Color.white, 30);
        Anchor(judgeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(760, 80));
        judgeBtn.gameObject.SetActive(false);

        var confirm = Panel("ConfirmationPanel", voting, Frame);
        Anchor(confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 380));
        var confirmText = Label("ConfirmText", confirm, "Vote?", 44, FontStyles.Bold, Color.white);
        Anchor(confirmText, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(820, 140));
        var yes = SmallButton(confirm, "ConfirmButton", "CONFIRM", 360, 90, Teal, Ink, 38);
        Anchor(yes.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(-20, 40), new Vector2(360, 90));
        var no = SmallButton(confirm, "CancelButton", "CANCEL", 360, 90, grey, Color.white, 38);
        Anchor(no.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(20, 40), new Vector2(360, 90));
        confirm.gameObject.SetActive(false);

        var resultPanel = Panel("MeetingResultPanel", canvas, dim);
        resultPanel.GetComponent<Image>().sprite = null;
        Stretch(resultPanel);
        var resultText = Label("ResultText", resultPanel, "", 72, FontStyles.Bold, Color.white);
        Stretch(resultText, 120);
        resultPanel.gameObject.SetActive(false);

        var meetGo = new GameObject("MeetingUI"); meetGo.transform.SetParent(ctl, false);
        var meetUi = meetGo.AddComponent<MeetingUI>();
        SetRef(meetUi, "votingPanel", voting.gameObject);
        SetRef(meetUi, "playerListContainer", gridGo.transform);
        SetRef(meetUi, "voteButtonPrefab", voteBtnPrefab);
        SetRef(meetUi, "timerText", Tmp(vtimer));
        SetRef(meetUi, "resultPanel", resultPanel.gameObject);
        SetRef(meetUi, "resultText", Tmp(resultText));
        SetObjArray(meetUi, "hudToHideDuringMeeting", taskPanel.gameObject, actions.gameObject, meetingBtn.gameObject);
        SetRef(meetUi, "judgeModeButton", judgeBtn.gameObject);
        SetRef(meetUi, "judgeModeButtonText", judgeBtn.GetComponentInChildren<TextMeshProUGUI>());
        SetRef(meetUi, "confirmationPanel", confirm.gameObject);
        SetRef(meetUi, "confirmationText", Tmp(confirmText));
        UnityEventTools.AddPersistentListener(judgeBtn.onClick, meetUi.ToggleJudgeMode);
        UnityEventTools.AddPersistentListener(yes.onClick, meetUi.ConfirmPendingAction);
        UnityEventTools.AddPersistentListener(no.onClick, meetUi.CancelPendingAction);
        voting.gameObject.SetActive(false);

        // ------------- role reveal overlay -------------
        var rootRt = new GameObject("RoleRevealPanel", typeof(RectTransform)).GetComponent<RectTransform>();
        rootRt.SetParent(canvas, false);
        Stretch(rootRt);
        var tint = Panel("BackgroundTint", rootRt, new Color(0.03f, 0.05f, 0.06f, 1f));
        tint.GetComponent<Image>().sprite = null;
        Stretch(tint);
        var glow = Panel("GlowStreak", rootRt, Color.white);
        glow.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/GlowStreak.png");
        glow.GetComponent<Image>().type = Image.Type.Simple;
        glow.GetComponent<Image>().raycastTarget = false;
        Anchor(glow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1800, 520));
        var portrait = Panel("Portrait", rootRt, Color.white);
        var pImg = portrait.GetComponent<Image>();
        pImg.sprite = null; pImg.type = Image.Type.Simple; pImg.preserveAspect = true; pImg.raycastTarget = false;
        Anchor(portrait, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(420, 420));
        var vig = Panel("Vignette", rootRt, Color.white);
        vig.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/Vignette.png");
        vig.GetComponent<Image>().type = Image.Type.Simple;
        vig.GetComponent<Image>().raycastTarget = false;
        Stretch(vig);
        var header = Label("HeaderText", rootRt, "YOU ARE A VILLAGER", 96, FontStyles.Bold, Color.white);
        Anchor(header, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(1700, 130));
        var nameT = Label("NameText", rootRt, "", 64, FontStyles.Bold, Teal);
        Anchor(nameT, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 250), new Vector2(1400, 90));
        var descT = Label("DescriptionText", rootRt, "", 38, FontStyles.Normal, new Color(1, 1, 1, 0.9f));
        Anchor(descT, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(1500, 110));
        var dismiss = SmallButton(rootRt, "DismissButton", "", 10, 10, new Color(0, 0, 0, 0), Color.white, 10);
        dismiss.GetComponent<LayoutElement>().ignoreLayout = true;
        Stretch(dismiss.GetComponent<RectTransform>());
        dismiss.GetComponent<Image>().sprite = null;
        dismiss.GetComponent<Image>().color = new Color(0, 0, 0, 0);

        var revealGo = new GameObject("RoleRevealUI"); revealGo.transform.SetParent(ctl, false);
        var reveal = revealGo.AddComponent<RoleRevealUI>();
        SetRef(reveal, "root", rootRt.gameObject);
        SetRef(reveal, "backgroundTint", tint.GetComponent<Image>());
        SetRef(reveal, "portraitImage", pImg);
        SetRef(reveal, "glowStreak", glow.GetComponent<Image>());
        SetRef(reveal, "vignette", vig.GetComponent<Image>());
        SetRef(reveal, "headerText", Tmp(header));
        SetRef(reveal, "nameText", Tmp(nameT));
        SetRef(reveal, "descriptionText", Tmp(descT));
        UnityEventTools.AddPersistentListener(dismiss.onClick, reveal.Dismiss);
        rootRt.gameObject.SetActive(false);

        // ------------- game over overlay -------------
        var over = Panel("GameOverPanel", canvas, dim);
        over.GetComponent<Image>().sprite = null;
        Stretch(over);
        var overText = Label("GameOverText", over, "", 110, FontStyles.Bold, Color.white);
        Anchor(overText, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(1700, 300));
        var lobbyBtn = SmallButton(over, "ReturnToLobbyButton", "RETURN TO LOBBY", 760, 110, Teal, Ink, 48);
        Anchor(lobbyBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(760, 110));
        var overGo = new GameObject("GameOverUI"); overGo.transform.SetParent(ctl, false);
        var overUi = overGo.AddComponent<GameOverUI>();
        SetRef(overUi, "gameOverPanel", over.gameObject);
        SetRef(overUi, "gameOverText", Tmp(overText));
        UnityEventTools.AddPersistentListener(lobbyBtn.onClick, overUi.OnReturnToLobbyClicked);
        over.gameObject.SetActive(false);

        // ------------- world-space marker spawners (ability reveals) -------------
        var markers = new GameObject("RevealUIs");
        var reiki = markers.AddComponent<ReikiRevealUI>();
        SetRef(reiki, "auraMarkerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Abilities/AuraMarker.prefab"));
        var third = markers.AddComponent<ThirdEyeRevealUI>();
        SetRef(third, "residueMarkerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Abilities/FootprintResidue.prefab"));
    }

    static void BuildSystems(GameObject gsmPrefab)
    {
        var go = new GameObject("NetworkManager");
        var transport = go.AddComponent<UnityTransport>();
        var nm = go.AddComponent<NetworkManager>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = true;
        nm.NetworkConfig.TickRate = 30;
        nm.RunInBackground = true;

        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        if (player != null) nm.NetworkConfig.PlayerPrefab = player;
        else Debug.LogWarning("[SceneBuilder] Assets/Prefabs/Player.prefab not found — assign the Player Prefab on NetworkManager.");

        // The Editor auto-adds the default Network Prefabs List to a new
        // NetworkManager; DedupeNetworkPrefabLists() (run before saving) makes
        // sure it's present exactly once.
        EditorUtility.SetDirty(nm);

        var conn = go.AddComponent<MultiplayerConnectionManager>();
        SetRef(conn, "gameStartManagerPrefab", gsmPrefab);
        SetInt(conn, "maxPlayers", 15);

        var audioGo = new GameObject("AudioManager");
        var audio = audioGo.AddComponent<AudioManager>();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath("b3ff17a6c006a1840842b89e074d9837"));
        if (clip != null) SetRef(audio, "backgroundMusic", clip);
    }

    // ---------------------------------------------------------------------
    // UI helpers
    // ---------------------------------------------------------------------
    static Sprite Skin => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    static RectTransform Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Skin;
        img.type = Image.Type.Sliced;
        img.color = color;
        return go.GetComponent<RectTransform>();
    }

    static RectTransform Label(string name, Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (PixelFont != null) t.font = PixelFont;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return go.GetComponent<RectTransform>();
    }

    static Button MenuButton(string name, Transform parent, string text, Color fill, Color textColor, float height, float fontSize)
    {
        var rt = Panel(name, parent, fill);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        var label = Label("Label", rt, text, fontSize, FontStyles.Bold, textColor);
        Stretch(label);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;
        label.offsetMax = new Vector2(-40, 0);
        return btn;
    }

    static TMP_InputField CodeInput(string name, Transform parent)
    {
        var root = Panel(name, parent, Color.white);
        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(root, false);
        Stretch(area.GetComponent<RectTransform>(), 16);

        var placeholder = Label("Placeholder", area.transform, "Enter Code", 50, FontStyles.Italic, new Color(0.2f, 0.2f, 0.2f, 0.5f));
        Stretch(placeholder);
        var text = Label("Text", area.transform, "", 50, FontStyles.Normal, new Color(0.1f, 0.1f, 0.1f));
        Stretch(text);

        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area.GetComponent<RectTransform>();
        input.textComponent = text.GetComponent<TextMeshProUGUI>();
        input.placeholder = placeholder.GetComponent<TextMeshProUGUI>();
        input.characterLimit = 8;
        input.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
        return input;
    }

    static Slider VolumeSlider(string name, Transform parent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var bg = Panel("Background", root.transform, Ink);
        bg.anchorMin = new Vector2(0, 0.35f);
        bg.anchorMax = new Vector2(1, 0.65f);
        bg.offsetMin = bg.offsetMax = Vector2.zero;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0, 0.35f);
        faRt.anchorMax = new Vector2(1, 0.65f);
        faRt.offsetMin = faRt.offsetMax = Vector2.zero;
        var fill = Panel("Fill", fillArea.transform, Teal);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0, 1);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>());
        var handle = Panel("Handle", handleArea.transform, Color.white);
        handle.sizeDelta = new Vector2(36, 0);
        handle.anchorMin = new Vector2(0, 0);
        handle.anchorMax = new Vector2(0, 1);

        var slider = root.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0.5f;
        root.AddComponent<MenuVolumeSlider>();
        return slider;
    }

    static void Stretch(RectTransform rt, float inset = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // Assigns a [SerializeField] private field by name.
    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no serialized field \"{field}\"."); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetInt(Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) return;
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Ensures the NetworkManager references the default prefab list exactly
    // once (a duplicate makes every NetworkPrefab report a duplicate hash).
    static void DedupeNetworkPrefabLists()
    {
        var nm = Object.FindFirstObjectByType<NetworkManager>();
        if (nm == null || nm.NetworkConfig == null) return;
        var lists = nm.NetworkConfig.Prefabs.NetworkPrefabsLists;
        for (int i = lists.Count - 1; i >= 0; i--)
            if (lists.IndexOf(lists[i]) != i) lists.RemoveAt(i);
        if (lists.Count == 0)
        {
            var def = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (def != null) lists.Add(def);
        }
        EditorUtility.SetDirty(nm);
    }

    // Scene-placed NetworkObjects created from code have GlobalObjectIdHash == 0
    // until NetworkObject.OnValidate runs against a SAVED scene (that's what
    // lets the server spawn them on scene load). Without this, RPCs on
    // in-scene managers throw NullReferenceException at runtime.
    static void FixSceneNetworkObjectHashes(Scene scene, string path)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        int fixedCount = 0, zero = 0;
        foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (no.gameObject.scene != scene) continue;
            onValidate?.Invoke(no, null);
            EditorUtility.SetDirty(no);
            fixedCount++;
            var hash = new SerializedObject(no).FindProperty("GlobalObjectIdHash");
            if (hash != null && hash.uintValue == 0) zero++;
        }
        if (fixedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SceneBuilder] Refreshed {fixedCount} scene NetworkObject(s); {zero} still have a zero hash.");
        }
    }

    static void Save(Scene scene, string name)
    {
        if (!AssetDatabase.IsValidFolder(ScenesDir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        string path = $"{ScenesDir}/{name}.unity";
        DedupeNetworkPrefabLists();
        EditorSceneManager.SaveScene(scene, path);
        FixSceneNetworkObjectHashes(scene, path);
        Debug.Log("[SceneBuilder] Saved " + path);
        UpdateBuildSettings();
    }

    // Build order: MainMenu, Lobby, Game (only the ones that exist yet).
    [MenuItem("Maligno/Update Build Settings")]
    public static void UpdateBuildSettings()
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
        foreach (string n in new[] { "MainMenu", "Lobby", "Game" })
        {
            string p = $"{ScenesDir}/{n}.unity";
            if (System.IO.File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    // Among Us style: only the player's feet are solid, so bodies can overlap
    // furniture. The tall capsule stays as a trigger so kill/report/task
    // detection (which find players through their colliders) keep working.
    [MenuItem("Maligno/6. Fix Player Feet Collider")]
    public static void FixPlayerFeetCollider()
    {
        const string path = "Assets/Prefabs/Player/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var capsule = root.GetComponent<CapsuleCollider2D>();
            if (capsule != null) capsule.isTrigger = true;

            BoxCollider2D feet = null;
            foreach (var b in root.GetComponents<BoxCollider2D>()) { feet = b; break; }
            if (feet == null)
            {
                // Only on first creation; item 7 / 8 size it to the sprite's shadow.
                feet = root.AddComponent<BoxCollider2D>();
                feet.offset = new Vector2(0f, -0.85f);
                feet.size = new Vector2(0.6f, 0.3f);
            }
            feet.isTrigger = false;

            // Zero friction so sliding along tables and walls never sticks.
            const string matPath = "Assets/Physics/PlayerNoFriction.physicsMaterial2D";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(matPath);
            if (mat == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Physics")) AssetDatabase.CreateFolder("Assets", "Physics");
                mat = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            feet.sharedMaterial = mat;

            // Physics runs at a fixed rate while the screen renders faster, so
            // without interpolation the player (and the camera following it)
            // visibly stutters whenever movement is pressed against a collider.
            var body = root.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log("[SceneBuilder] Player feet collider set (capsule is now a trigger).");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
