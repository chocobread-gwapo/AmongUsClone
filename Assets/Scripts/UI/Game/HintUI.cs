using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// First-time-player help, built in code (no scene setup):
//  * a one-line "YOUR GOAL" strip at the top during a match,
//  * a short how-to card right after the role reveal (first few matches),
//  * hover tooltips on the USE / REPORT / KILL / ABILITY / MEETING buttons.
// All of it can be switched off with HINTS in the Settings menu.
public class HintUI : MonoBehaviour
{
    private static HintUI instance;

    private Canvas canvas;
    private GameObject goalPill, card, tip;
    private TextMeshProUGUI goalText, cardBody, tipTitle, tipBody;
    private RectTransform tipRt;
    private bool prevReveal;
    private float cardHideAt;
    private float nextAttach;
    private readonly HashSet<Button> attached = new HashSet<Button>();
    private string tipId;

    private const string Gold = "#FFD54A";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("HintUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HintUI>();
        instance.Build();
        SceneManager.sceneLoaded += (s, m) => instance.OnScene();
    }

    private void OnScene()
    {
        attached.Clear();
        prevReveal = false;
        card.SetActive(false);
        tip.SetActive(false);
        nextAttach = 0f;
    }

    // ------------------------------------------------------------------ loop

    private void Update()
    {
        bool inGame = SceneManager.GetActiveScene().name == "Game";
        bool hintsOn = GameSettings.HintsOn;
        var local = LocalRole();

        if (!inGame || local == null)
        {
            goalPill.SetActive(false);
            card.SetActive(false);
            tip.SetActive(false);
            return;
        }

        if (Time.unscaledTime >= nextAttach)
        {
            AttachTooltips();
            nextAttach = Time.unscaledTime + (attached.Count >= 3 ? 2f : 0.5f);
        }

        bool reveal = RoleRevealUI.Instance != null && RoleRevealUI.Instance.IsShowing;
        bool ended = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        bool meeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;

        // Goal strip.
        bool showGoal = hintsOn && !reveal && !ended && !meeting;
        goalPill.SetActive(showGoal);
        if (showGoal)
        {
            bool mal = local.IsImpostor;
            goalText.text = "<color=" + (mal ? "#FF6B6B" : "#7CCBFF") + ">YOUR GOAL</color>   " + RoleInfo.Goal(mal, local.IsAlive);
        }

        // How-to card: pops when the role reveal closes, first few matches only.
        if (prevReveal && !reveal && hintsOn && !ended && GameSettings.HintCardsShown < 3)
        {
            ShowCard(local);
            GameSettings.HintCardsShown = GameSettings.HintCardsShown + 1;
        }
        prevReveal = reveal;
        if (card.activeSelf && (Time.unscaledTime > cardHideAt || reveal || ended || meeting)) card.SetActive(false);

        // Tooltip follows the mouse.
        if (tip.activeSelf) PlaceTip();
    }

    private PlayerRole LocalRole()
    {
        var nm = NetworkManager.Singleton;
        var po = nm != null ? nm.LocalClient?.PlayerObject : null;
        return po != null ? po.GetComponent<PlayerRole>() : null;
    }

    // ------------------------------------------------------------------ card

    private void ShowCard(PlayerRole r)
    {
        bool mal = r.IsImpostor;
        string K(string a) => "<color=" + Gold + ">" + GameSettings.KeyFor(a) + "</color>";
        var lines = new List<string>();
        lines.Add("Move with " + K("Move") + ".");
        if (mal)
        {
            lines.Add("Press " + K("Kill") + " near a Villager to eliminate them. Kills have a cooldown.");
            var (n, how) = RoleInfo.Maligno(r.CurrentMalignoType);
            lines.Add("Press " + K("Ability") + ": <color=" + Gold + ">" + n + "</color>. " + how);
            lines.Add("Blend in. Pretend to do chores. Don't be seen.");
        }
        else
        {
            lines.Add("Press " + K("Interact") + " at a task spot to do a chore. Finish them all to win.");
            lines.Add("Press " + K("Report") + " beside a body, or use the emergency table in the middle, to call a meeting. Vote out the Maligno.");
            var cls = r.CurrentSurvivorClass;
            if (cls != SurvivorClass.None)
            {
                var (n, how) = RoleInfo.Survivor(cls);
                string key = cls == SurvivorClass.Babaylan ? "In a meeting" : "Press " + K("Ability");
                lines.Add(key + ": <color=" + Gold + ">" + n + "</color>. " + how);
            }
        }
        lines.Add("<color=#9AA7AB>Hover the buttons for details. Hints can be turned off in Settings.</color>");
        cardBody.text = string.Join("\n\n", lines);
        card.SetActive(true);
        cardHideAt = Time.unscaledTime + 18f;
    }

    // -------------------------------------------------------------- tooltips

    private void AttachTooltips()
    {
        foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include))
        {
            if (attached.Contains(b)) continue;
            string kind = null;
            int n = b.onClick.GetPersistentEventCount();
            for (int i = 0; i < n && kind == null; i++)
            {
                var t = b.onClick.GetPersistentTarget(i);
                if (t is AbilityButton) kind = "ability";
                else if (t is KillButton) kind = "kill";
                else if (t is UseButton) kind = "use";
                else if (t is ReportButton) kind = "report";
                else if (t is CallMeetingButton) kind = "meeting";
            }
            if (kind == null) continue;
            var trig = b.gameObject.GetComponent<HintTrigger>();
            if (trig == null) trig = b.gameObject.AddComponent<HintTrigger>();
            trig.id = kind;
            attached.Add(b);
        }
    }

    public static void ShowTip(string id) { if (instance != null) instance.ShowTipInternal(id); }
    public static void HideTip(string id) { if (instance != null && instance.tipId == id) { instance.tip.SetActive(false); instance.tipId = null; } }

    private void ShowTipInternal(string id)
    {
        if (!GameSettings.HintsOn) return;
        var r = LocalRole();
        string title, body;
        string K(string a) => "  [" + GameSettings.KeyFor(a) + "]";
        switch (id)
        {
            case "kill":
                title = "KILL" + K("Kill");
                body = "Eliminate a Villager standing close to you. Then wait for the cooldown. Don't do it in front of witnesses.";
                break;
            case "use":
                title = "USE" + K("Interact");
                body = "Stand beside a task spot to do a chore. Finishing every chore wins the game for the village.";
                break;
            case "report":
                title = "REPORT" + K("Report");
                body = "Report a dead body you are standing next to. This calls a meeting so everyone can discuss and vote.";
                break;
            case "meeting":
                title = "MEETING";
                body = "Stand at the emergency table in the middle and press USE to call everyone together. Each player gets a limited number of calls, and there is a cooldown after the match starts and after every meeting.";
                break;
            default: // ability
                title = "ABILITY" + K("Ability");
                body = "Your special power.";
                if (r != null)
                {
                    var info = r.IsImpostor ? RoleInfo.Maligno(r.CurrentMalignoType) : RoleInfo.Survivor(r.CurrentSurvivorClass);
                    if (!string.IsNullOrEmpty(info.name)) { title = info.name.ToUpperInvariant() + K("Ability"); body = info.how; }
                }
                body += "\n<color=#9AA7AB>The number on the button is its cooldown.</color>";
                break;
        }
        tipTitle.text = title; tipBody.text = body; tipId = id;
        tip.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
        PlaceTip();
    }

    private void PlaceTip()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 p = mouse.position.ReadValue();
        float s = canvas.scaleFactor;
        float sw = Screen.width, sh = Screen.height;
        tipRt.pivot = new Vector2(p.x > sw * 0.55f ? 1f : 0f, p.y < sh * 0.5f ? 0f : 1f);
        tipRt.anchoredPosition = new Vector2(p.x / s + (tipRt.pivot.x > 0.5f ? -18 : 18), p.y / s + (tipRt.pivot.y < 0.5f ? 18 : -18));
    }

    // ----------------------------------------------------------------- build

    private void Build()
    {
        var cgo = new GameObject("HintCanvas", typeof(Canvas), typeof(CanvasScaler));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

        // Goal strip (top centre).
        goalPill = Box(cgo.transform, "Goal", new Color(0.03f, 0.06f, 0.08f, 0.82f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(1100, 46));
        goalText = Text(goalPill.transform, 24, Color.white, TextAlignmentOptions.Center);
        goalText.enableAutoSizing = true; goalText.fontSizeMin = 14; goalText.fontSizeMax = 24;
        Stretch((RectTransform)goalText.transform);
        goalPill.SetActive(false);

        // How-to card (bottom centre, above the buttons).
        card = Box(cgo.transform, "Card", new Color(0.04f, 0.07f, 0.09f, 0.95f), new Vector2(0.5f, 0f), new Vector2(0, 190), new Vector2(980, 0));
        var v = card.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(32, 32, 22, 22); v.spacing = 10;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var head = Text(card.transform, 32, new Color(0.15f, 0.83f, 0.72f), TextAlignmentOptions.MidlineLeft);
        head.text = "HOW TO PLAY";
        cardBody = Text(card.transform, 24, Color.white, TextAlignmentOptions.TopLeft);
        var cb = card.AddComponent<Button>(); cb.targetGraphic = card.GetComponent<Image>();
        cb.onClick.AddListener(() => card.SetActive(false));
        card.SetActive(false);

        // Tooltip.
        tip = Box(cgo.transform, "Tip", new Color(0.03f, 0.06f, 0.08f, 0.97f), new Vector2(0, 0), Vector2.zero, new Vector2(520, 0));
        tipRt = (RectTransform)tip.transform;
        tipRt.anchorMin = tipRt.anchorMax = Vector2.zero;
        var tv = tip.AddComponent<VerticalLayoutGroup>();
        tv.padding = new RectOffset(20, 20, 14, 14); tv.spacing = 6;
        tv.childControlWidth = true; tv.childControlHeight = true; tv.childForceExpandWidth = true; tv.childForceExpandHeight = false;
        tip.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        tipTitle = Text(tip.transform, 28, new Color(0.15f, 0.83f, 0.72f), TextAlignmentOptions.MidlineLeft);
        tipBody = Text(tip.transform, 22, Color.white, TextAlignmentOptions.TopLeft);
        tip.GetComponent<Image>().raycastTarget = false;
        tip.SetActive(false);
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(10, 0); rt.offsetMax = new Vector2(-10, 0);
    }
}
