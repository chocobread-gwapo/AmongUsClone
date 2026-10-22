using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The lobby's GAME SETTINGS window (Among Us style): a left column with the title,
// a hint box and three tabs, and a scrolling right side.
//   GAME PRESETS   : one click applies a bundle of settings
//   GAME SETTINGS  : Maligno / Villagers / Meetings / Tasks, each row a  - value +  stepper
//   ROLES SETTINGS : how many of each role may appear (# OF ROLE) and how likely (% CHANCE)
// Only the host can change anything; everyone else sees the live values with greyed-out buttons.
// All values live on GameStartManager (synced to every client).
//
// The whole window is built here in code, so there is nothing to wire in the Inspector.
// The old hand-made widgets under this object are hidden; OpenPanel/ClosePanel keep working
// for the existing EDIT/VIEW and X buttons.
public class HostSettingsPanelUI : MonoBehaviour
{
    private static readonly Color FrameC = new Color(0.40f, 0.43f, 0.45f);
    private static readonly Color DarkC = new Color(0.035f, 0.035f, 0.035f);
    private static readonly Color BlockC = new Color(0.29f, 0.29f, 0.29f);
    private static readonly Color HeaderC = new Color(0.27f, 0.27f, 0.27f);
    private static readonly Color TealC = new Color(0.27f, 0.52f, 0.50f);
    private static readonly Color TealSelC = new Color(0.52f, 0.80f, 0.77f);
    private static readonly Color TealTextC = new Color(0.78f, 0.96f, 0.93f);
    private static readonly Color CrewC = new Color(0.07f, 0.53f, 0.75f);
    private static readonly Color CrewHeadC = new Color(0.04f, 0.31f, 0.43f);
    private static readonly Color CrewTagC = new Color(0.42f, 0.78f, 0.95f);
    private static readonly Color MalC = new Color(0.80f, 0.13f, 0.13f);
    private static readonly Color MalHeadC = new Color(0.30f, 0.06f, 0.06f);
    private static readonly Color MalTagC = new Color(0.78f, 0.32f, 0.32f);
    private static readonly Color OffC = new Color(0.36f, 0.36f, 0.36f);
    private static readonly Color CheckC = new Color(0.20f, 0.88f, 0.76f);

    private enum Tab { Presets, Game, Roles }

    private GameObject overlay;
    private RectTransform content, scrollRT, strip;
    private ScrollRect scroll;
    private TextMeshProUGUI helpText, footerText;
    private readonly List<Image> tabBorder = new List<Image>();
    private readonly List<Image> tabFill = new List<Image>();
    private readonly List<TextMeshProUGUI> tabLabel = new List<TextMeshProUGUI>();
    private readonly List<Image> stripBorder = new List<Image>();
    private readonly List<Image> stripFill = new List<Image>();
    private readonly List<Action> refreshers = new List<Action>();
    private Tab tab = Tab.Game;
    private int roleFilter; // 0 all, 1 villagers, 2 maligno
    private float nextRefresh;

    private static GameStartManager G => GameStartManager.Instance;
    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    // ------------------------------------------------------------ open / close

    private void Awake()
    {
        // Hide the old hand-built widgets; this window replaces them.
        for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(false);
        foreach (var g in GetComponents<Graphic>()) g.enabled = false;
        foreach (var l in GetComponents<LayoutGroup>()) l.enabled = false;
    }

    // Deferred by one frame so the click that opened/closed us fully finishes first
    // (same trick as before: avoids eating the next click on whatever sits underneath).
    public void OpenPanel() { RunNextFrame(true); }
    public void ClosePanel() { RunNextFrame(false); }

    private void RunNextFrame(bool active)
    {
        if (EventSystem.current != null)
            EventSystem.current.StartCoroutine(SetActiveNextFrame(gameObject, active));
        else
            gameObject.SetActive(active);
    }

    private static IEnumerator SetActiveNextFrame(GameObject target, bool active)
    {
        yield return null;
        if (target != null) target.SetActive(active);
    }

    private void OnEnable()
    {
        if (overlay == null) Build();
        overlay.SetActive(true);
        ShowTab(tab);
    }

    private void OnDisable()
    {
        if (overlay != null) overlay.SetActive(false);
    }

    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
    }

    private void Update()
    {
        if (overlay == null || !overlay.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var r in refreshers) r();
        if (footerText != null && string.IsNullOrEmpty(footerText.text) && !IsHost)
            footerText.text = "VIEW ONLY: the host sets the rules.";
    }

    // ------------------------------------------------------------------- build

    private void Build()
    {
        overlay = new GameObject("GameSettingsOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var sc = overlay.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        var back = Img("Backdrop", overlay.transform, new Color(0f, 0f, 0f, 0.62f), true);
        Stretch(back.rectTransform, 0, 0, 0, 0);

        var frame = Img("Frame", overlay.transform, FrameC, true);
        frame.rectTransform.anchorMin = frame.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        frame.rectTransform.sizeDelta = new Vector2(1560, 780);
        var inner = Img("Inner", frame.transform, DarkC);
        Stretch(inner.rectTransform, 16, 16, 16, 16);

        BuildLeftColumn(inner.transform);
        BuildRightSide(inner.transform);
        BuildCloseButton(frame.transform);
    }

    private void BuildLeftColumn(Transform parent)
    {
        var left = NewRT("Left", parent);
        left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0, 1);
        left.pivot = new Vector2(0, 0.5f);
        left.sizeDelta = new Vector2(470, 0); left.anchoredPosition = Vector2.zero;

        var title = Txt(left, "GAME SETTINGS", 62, Color.white, TextAlignmentOptions.TopLeft);
        title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0, 1);
        title.rectTransform.anchoredPosition = new Vector2(32, -26); title.rectTransform.sizeDelta = new Vector2(-40, 90);

        var border = Img("HelpBorder", left, Color.white);
        TopLeft(border.rectTransform, 32, 150, 410, 136);
        var box = Img("HelpBox", border.transform, DarkC);
        Stretch(box.rectTransform, 4, 4, 4, 4);
        helpText = Txt(box.transform, "", 26, Color.white, TextAlignmentOptions.MidlineLeft);
        helpText.textWrappingMode = TextWrappingModes.Normal;
        Stretch(helpText.rectTransform, 18, 8, 14, 8);

        string[] names = { "GAME PRESETS", "GAME SETTINGS", "ROLES SETTINGS" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = Img("Tab" + i, left, Color.white, true);
            TopLeft(b.rectTransform, 32, 318 + i * 100, 410, 84);
            var f = Img("Fill", b.transform, TealC);
            Stretch(f.rectTransform, 4, 4, 4, 4);
            var t = Txt(f.transform, names[i], 40, TealTextC, TextAlignmentOptions.MidlineRight);
            Stretch(t.rectTransform, 10, 0, 22, 0);
            var btn = b.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; btn.targetGraphic = b;
            btn.onClick.AddListener(() => { Sfx.Play2D("click"); ShowTab((Tab)idx); });
            tabBorder.Add(b); tabFill.Add(f); tabLabel.Add(t);
        }

        footerText = Txt(left, "", 24, new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.BottomLeft);
        footerText.textWrappingMode = TextWrappingModes.Normal;
        footerText.rectTransform.anchorMin = new Vector2(0, 0); footerText.rectTransform.anchorMax = new Vector2(1, 0);
        footerText.rectTransform.pivot = new Vector2(0, 0);
        footerText.rectTransform.anchoredPosition = new Vector2(32, 22); footerText.rectTransform.sizeDelta = new Vector2(-50, 70);
    }

    private void BuildRightSide(Transform parent)
    {
        var right = NewRT("Right", parent);
        right.anchorMin = Vector2.zero; right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(490, 18); right.offsetMax = new Vector2(-64, -18);

        // Category strip (roles tab only).
        strip = NewRT("Strip", right);
        strip.anchorMin = new Vector2(0, 1); strip.anchorMax = new Vector2(1, 1);
        strip.pivot = new Vector2(0, 1); strip.sizeDelta = new Vector2(0, 76); strip.anchoredPosition = Vector2.zero;
        string[] cats = { "ALL", "VILLAGERS", "MALIGNO" };
        Color[] catCols = { HeaderC, CrewC, MalC };
        float[] catW = { 130, 250, 230 };
        float x = 0;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = Img("Cat" + i, strip, Color.white, true);
            TopLeft(b.rectTransform, x, 0, catW[i], 70);
            var f = Img("Fill", b.transform, catCols[i]);
            Stretch(f.rectTransform, 4, 4, 4, 4);
            var t = Txt(f.transform, cats[i], 30, Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform, 0, 0, 0, 0);
            var btn = b.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; btn.targetGraphic = b;
            btn.onClick.AddListener(() => { Sfx.Play2D("click"); roleFilter = idx; ShowTab(Tab.Roles); });
            stripBorder.Add(b); stripFill.Add(f);
            x += catW[i] + 12;
        }

        // Scrolling area.
        scrollRT = NewRT("Scroll", right);
        scrollRT.anchorMin = Vector2.zero; scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = Vector2.zero; scrollRT.offsetMax = Vector2.zero;
        var viewport = Img("Viewport", scrollRT, new Color(0, 0, 0, 0.01f), true);
        Stretch(viewport.rectTransform, 0, 0, 0, 0);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewRT("Content", viewport.transform);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 0; vlg.padding = new RectOffset(0, 0, 8, 40);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Slim scrollbar with a teal handle, to the right of the content.
        var track = Img("Scrollbar", parent, new Color(0.16f, 0.16f, 0.16f), true);
        track.rectTransform.anchorMin = new Vector2(1, 0); track.rectTransform.anchorMax = new Vector2(1, 1);
        track.rectTransform.pivot = new Vector2(1, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(10, -80); track.rectTransform.anchoredPosition = new Vector2(-26, 0);
        var slide = NewRT("Sliding Area", track.transform);
        Stretch(slide, 0, 0, 0, 0);
        var handle = Img("Handle", slide, TealC, true);
        handle.rectTransform.sizeDelta = Vector2.zero;
        var sb = track.gameObject.AddComponent<Scrollbar>();
        sb.handleRect = handle.rectTransform; sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;

        scroll = right.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.viewport = viewport.rectTransform;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
    }

    private void BuildCloseButton(Transform frame)
    {
        var b = Img("Close", frame, Color.white, true);
        b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(1, 1);
        b.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        b.rectTransform.sizeDelta = new Vector2(78, 78); b.rectTransform.anchoredPosition = new Vector2(-14, -14);
        var f = Img("Fill", b.transform, Color.black);
        Stretch(f.rectTransform, 5, 5, 5, 5);
        var t = Txt(f.transform, "X", 58, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(t.rectTransform, 0, 0, 0, 0);
        var btn = b.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None; btn.targetGraphic = b;
        btn.onClick.AddListener(() => { Sfx.Play2D("click"); ClosePanel(); });
    }

    // -------------------------------------------------------------------- tabs

    private void ShowTab(Tab t)
    {
        tab = t;
        for (int i = 0; i < 3; i++)
        {
            bool sel = i == (int)t;
            tabBorder[i].color = sel ? Color.white : TealC;
            tabFill[i].color = sel ? TealSelC : TealC;
            tabLabel[i].color = sel ? Color.white : TealTextC;
        }
        helpText.text = !IsHost ? "Only the host can change settings. You can see them here."
            : t == Tab.Presets ? "Pick a preset to apply a bundle of settings."
            : t == Tab.Game ? "Edit individual settings for your game lobby."
            : "Edit role settings for your lobby.";
        if (footerText != null) footerText.text = "";

        strip.gameObject.SetActive(t == Tab.Roles);
        scrollRT.offsetMax = new Vector2(0, t == Tab.Roles ? -90 : 0);
        if (t == Tab.Roles)
            for (int i = 0; i < 3; i++)
            {
                bool sel = i == roleFilter;
                stripBorder[i].color = sel ? Color.white : new Color(0, 0, 0, 0);
            }

        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        refreshers.Clear();

        if (t == Tab.Presets) BuildPresets();
        else if (t == Tab.Game) BuildGame();
        else BuildRoles();

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.verticalNormalizedPosition = 1f;
        RefreshAll();
    }

    // ------------------------------------------------------------- presets tab

    private void BuildPresets()
    {
        Section("GAME PRESETS");
        for (int i = 0; i < GameStartManager.PresetNames.Length; i++)
        {
            int idx = i;
            PresetButton(GameStartManager.PresetNames[i], GameStartManager.PresetBlurbs[i], TealC, () =>
            {
                G?.ServerApplyPreset(idx);
                footerText.text = "Applied " + GameStartManager.PresetNames[idx] + ".";
            });
        }
        Spacer(14);
        PresetButton("RESET TO DEFAULTS", "Puts every game setting back to normal.", new Color(0.50f, 0.30f, 0.27f), () =>
        {
            G?.ServerResetSettings();
            footerText.text = "Game settings reset.";
        });
    }

    private void PresetButton(string title, string blurb, Color color, Action onClick)
    {
        var holder = NewRT("PresetRow", content);
        holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 108;
        var b = Img("Button", holder, color, true);
        Stretch(b.rectTransform, 40, 8, 40, 8);
        var tTitle = Txt(b.transform, title, 40, Color.white, TextAlignmentOptions.TopLeft);
        Stretch(tTitle.rectTransform, 26, 10, 20, 0);
        var tBlurb = Txt(b.transform, blurb, 24, TealTextC, TextAlignmentOptions.BottomLeft);
        Stretch(tBlurb.rectTransform, 26, 0, 20, 12);
        var btn = b.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None; btn.targetGraphic = b;
        btn.onClick.AddListener(() =>
        {
            if (!IsHost) return;
            Sfx.Play2D("click");
            onClick();
            RefreshAll();
        });
        refreshers.Add(() => b.color = IsHost ? color : Color.Lerp(color, OffC, 0.7f));
    }

    // ----------------------------------------------------------- game settings

    private void BuildGame()
    {
        Section("LOBBY");
        Row(GameStartManager.Setting.Capacity);
        Spacer(12);
        Section("MALIGNO");
        Row(GameStartManager.Setting.Impostors);
        Row(GameStartManager.Setting.KillCooldown);
        Row(GameStartManager.Setting.MalignoVision);
        Row(GameStartManager.Setting.KillDistance);
        Spacer(12);
        Section("VILLAGERS");
        Row(GameStartManager.Setting.PlayerSpeed);
        Row(GameStartManager.Setting.CrewVision);
        Spacer(12);
        Section("MEETINGS");
        Row(GameStartManager.Setting.EmergencyMeetings);
        Row(GameStartManager.Setting.EmergencyCooldown);
        Row(GameStartManager.Setting.DiscussionTime);
        Row(GameStartManager.Setting.VotingTime);
        Row(GameStartManager.Setting.AnonymousVotes);
        Row(GameStartManager.Setting.ConfirmEjects);
        Spacer(12);
        Section("TASKS");
        Row(GameStartManager.Setting.CommonTasks);
        Row(GameStartManager.Setting.LongTasks);
        Row(GameStartManager.Setting.ShortTasks);
        Row(GameStartManager.Setting.TaskBarUpdates);
    }

    private void Row(GameStartManager.Setting s)
    {
        var info = GameStartManager.Info(s);
        var row = NewRT("Row", content);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;

        var label = Img("Label", row, BlockC);
        Place(label.rectTransform, 130, 0, 420, 64);
        var lt = Txt(label.transform, info.label, 27, Color.white, TextAlignmentOptions.MidlineRight);
        Stretch(lt.rectTransform, 10, 0, 22, 0);

        if (info.toggle) Checkbox(row, 580, () => G != null && G.Get(s) > 0.5f, v => G?.ServerSet(s, v ? 1f : 0f));
        else Stepper(row, 580, 190,
            () => G != null ? info.Format(G.Get(s)) : "-",
            () => G != null && G.Get(s) > info.min + 0.0001f,
            () => G != null && G.Get(s) < info.max - 0.0001f,
            dir => G?.ServerSet(s, G.Get(s) + dir * info.step));
    }

    // -------------------------------------------------------------- roles tab

    private static readonly string[] SurvivorNames = { "Third Eye", "Reiki", "Albularyo", "Babaylan", "Tarot Reader" };
    private static readonly string[] MalignoNames = { "Duwende", "Tikbalang", "Bal Bal", "Aswang", "Kapre" };

    private void BuildRoles()
    {
        Section("ROLE AMOUNTS");
        if (roleFilter != 2)
        {
            Tag("VILLAGER ROLES", CrewTagC, new Color(0.02f, 0.2f, 0.3f));
            TableHeader(CrewHeadC);
            var list = GameStartManager.AllKnownSurvivorClasses;
            for (int i = 0; i < list.Count; i++)
            {
                SurvivorClass c = list[i];
                RoleRow(SurvivorNames[Mathf.Clamp(i, 0, SurvivorNames.Length - 1)], CrewC,
                    () => G != null ? G.GetSurvivorMaxCount(c) : 0, v => G?.ServerSetSurvivorMaxCount(c, v),
                    () => G != null ? G.GetSurvivorChance(c) : 0, v => G?.ServerSetSurvivorChance(c, v));
            }
            Spacer(24);
        }
        if (roleFilter != 1)
        {
            Tag("MALIGNO ROLES", MalTagC, new Color(0.25f, 0.04f, 0.04f));
            TableHeader(MalHeadC);
            var list = GameStartManager.AllKnownMalignoTypes;
            for (int i = 0; i < list.Count; i++)
            {
                MalignoType m = list[i];
                RoleRow(MalignoNames[Mathf.Clamp(i, 0, MalignoNames.Length - 1)], MalC,
                    () => G != null ? G.GetMalignoMaxCount(m) : 0, v => G?.ServerSetMalignoMaxCount(m, v),
                    () => G != null ? G.GetMalignoChance(m) : 0, v => G?.ServerSetMalignoChance(m, v));
            }
        }
    }

    private void Tag(string text, Color bg, Color fg)
    {
        var holder = NewRT("Tag", content);
        holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
        var b = Img("Bg", holder, bg);
        b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(1, 0.5f);
        b.rectTransform.pivot = new Vector2(1, 0.5f);
        b.rectTransform.sizeDelta = new Vector2(330, 44); b.rectTransform.anchoredPosition = new Vector2(-10, 0);
        var t = Txt(b.transform, text, 26, fg, TextAlignmentOptions.MidlineRight, FontStyles.Italic);
        Stretch(t.rectTransform, 10, 0, 18, 0);
    }

    private void TableHeader(Color bg)
    {
        var row = NewRT("Header", content);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var a = Img("Blank", row, bg);
        Place(a.rectTransform, 0, 0, 330, 46);
        var c1 = Img("Count", row, bg);
        Place(c1.rectTransform, 336, 0, 300, 46);
        Txt(c1.transform, "# OF ROLE", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold).rectTransform.StretchFull();
        var c2 = Img("Chance", row, bg);
        Place(c2.rectTransform, 642, 0, 300, 46);
        Txt(c2.transform, "% CHANCE", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold).rectTransform.StretchFull();
    }

    private void RoleRow(string name, Color nameColor, Func<int> getCount, Action<int> setCount, Func<int> getChance, Action<int> setChance)
    {
        var row = NewRT("RoleRow", content);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;
        var n = Img("Name", row, nameColor);
        Place(n.rectTransform, 0, 0, 330, 58);
        var nt = Txt(n.transform, name, 28, Color.white, TextAlignmentOptions.MidlineRight);
        Stretch(nt.rectTransform, 10, 0, 22, 0);

        Stepper(row, 342, 160, () => getCount().ToString(),
            () => getCount() > 0, () => getCount() < 3, dir => setCount(getCount() + dir));
        Stepper(row, 648, 160, () => getChance().ToString(),
            () => getChance() > 0, () => getChance() < 100, dir => setChance(getChance() + dir * 5));
    }

    // ------------------------------------------------------------- UI pieces

    private void Section(string text)
    {
        var holder = NewRT("Section", content);
        holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        var bar = Img("Bar", holder, HeaderC);
        Stretch(bar.rectTransform, 0, 2, 380, 2);
        var t = Txt(bar.transform, text, 26, new Color(0.85f, 0.85f, 0.85f), TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
        Stretch(t.rectTransform, 24, 0, 10, 0);
        var line = Img("Line", holder, new Color(0.3f, 0.3f, 0.3f));
        line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
        line.rectTransform.pivot = new Vector2(0.5f, 0); line.rectTransform.sizeDelta = new Vector2(0, 2);
    }

    private void Spacer(float h)
    {
        var s = NewRT("Spacer", content);
        s.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
    }

    // [-] [ value ] [+]; width = 56 + 8 + boxW + 8 + 56.
    private void Stepper(Transform parent, float x, float boxW, Func<string> text, Func<bool> canMinus, Func<bool> canPlus, Action<int> step)
    {
        var minus = SquareButton(parent, x, false);
        var box = Img("ValueBorder", parent, Color.white);
        Place(box.rectTransform, x + 64, 0, boxW, 56);
        var inner = Img("Value", box.transform, Color.black);
        Stretch(inner.rectTransform, 3, 3, 3, 3);
        var vt = Txt(inner.transform, "", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        vt.rectTransform.StretchFull();
        var plus = SquareButton(parent, x + 72 + boxW, true);

        minus.btn.onClick.AddListener(() => { if (IsHost && canMinus()) { Sfx.Play2D("click"); step(-1); RefreshAll(); } });
        plus.btn.onClick.AddListener(() => { if (IsHost && canPlus()) { Sfx.Play2D("click"); step(1); RefreshAll(); } });
        refreshers.Add(() =>
        {
            vt.text = text();
            SetEnabled(minus, IsHost && canMinus());
            SetEnabled(plus, IsHost && canPlus());
        });
    }

    private void Checkbox(Transform parent, float x, Func<bool> get, Action<bool> set)
    {
        var b = Img("Check", parent, Color.white, true);
        Place(b.rectTransform, x, 0, 56, 56);
        var inner = Img("Inner", b.transform, Color.black);
        Stretch(inner.rectTransform, 3, 3, 3, 3);
        var tickShort = Img("TickA", inner.transform, CheckC);
        tickShort.rectTransform.sizeDelta = new Vector2(16, 7); tickShort.rectTransform.anchoredPosition = new Vector2(-8, -5);
        tickShort.rectTransform.localRotation = Quaternion.Euler(0, 0, -45);
        var tickLong = Img("TickB", inner.transform, CheckC);
        tickLong.rectTransform.sizeDelta = new Vector2(32, 7); tickLong.rectTransform.anchoredPosition = new Vector2(7, 1);
        tickLong.rectTransform.localRotation = Quaternion.Euler(0, 0, 50);
        var btn = b.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None; btn.targetGraphic = b;
        btn.onClick.AddListener(() => { if (IsHost) { Sfx.Play2D("click"); set(!get()); RefreshAll(); } });
        refreshers.Add(() =>
        {
            bool on = get();
            tickShort.enabled = on; tickLong.enabled = on;
            b.color = IsHost ? Color.white : new Color(0.6f, 0.6f, 0.6f);
        });
    }

    private struct SqBtn { public Button btn; public Image bg; public Image[] bars; }

    private SqBtn SquareButton(Transform parent, float x, bool plus)
    {
        var bg = Img(plus ? "Plus" : "Minus", parent, Color.white, true);
        Place(bg.rectTransform, x, 0, 56, 56);
        var bars = new List<Image>();
        var h = Img("H", bg.transform, Color.black);
        h.rectTransform.sizeDelta = new Vector2(24, 8); bars.Add(h);
        if (plus)
        {
            var v = Img("V", bg.transform, Color.black);
            v.rectTransform.sizeDelta = new Vector2(8, 24); bars.Add(v);
        }
        var btn = bg.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None; btn.targetGraphic = bg;
        return new SqBtn { btn = btn, bg = bg, bars = bars.ToArray() };
    }

    private static void SetEnabled(SqBtn b, bool on)
    {
        b.bg.color = on ? Color.white : OffC;
        foreach (var bar in b.bars) bar.color = on ? Color.black : new Color(0.16f, 0.16f, 0.16f);
    }

    // ------------------------------------------------------- tiny UI helpers

    private static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Img(string name, Transform parent, Color c, bool raycast = false)
    {
        var rt = NewRT(name, parent);
        var im = rt.gameObject.AddComponent<Image>();
        im.color = c; im.raycastTarget = raycast;
        // Centered by default so child bars (glyphs, ticks) sit in the middle of their parent.
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        return im;
    }

    private static TextMeshProUGUI Txt(Transform parent, string s, float size, Color c, TextAlignmentOptions al, FontStyles st = FontStyles.Normal)
    {
        var rt = NewRT("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = s; t.fontSize = size; t.color = c; t.alignment = al; t.fontStyle = st;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    private static void Stretch(RectTransform rt, float l, float b, float r, float t)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }

    // Fixed box measured from the parent's top-left corner.
    private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
    }

    // Fixed box at x from the parent's left edge, vertically centered.
    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }
}

internal static class RectExt
{
    public static void StretchFull(this RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
