using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runtime-built task mini-games. TaskZone.TryComplete() opens one; only when
// the player finishes it does the real completion request go to the server.
// Everything is built in code (no scene setup needed) and torn down on close,
// meeting start or game over.
public class TaskMiniGame : MonoBehaviour
{
    public enum Kind { Hold, Swipe, Wiring, Targets }

    public static bool IsOpen { get; private set; }
    private static TaskMiniGame current;

    private static readonly Color Ink = new Color(0.06f, 0.1f, 0.11f, 1f);
    private static readonly Color Panel = new Color(0.1f, 0.15f, 0.17f, 1f);
    private static readonly Color Teal = new Color(0.15f, 0.83f, 0.72f, 1f);
    private static readonly Color Grey = new Color(0.27f, 0.42f, 0.42f, 1f);

    private RectTransform content;
    private TextMeshProUGUI hint;
    private Action onSuccess;
    private bool finished;
    private float finishAt;

    // Which mini-game a task uses, picked from its name so existing scenes keep working.
    public static Kind KindFor(string taskName)
    {
        string n = (taskName ?? "").ToLowerInvariant();
        if (n.Contains("swipe")) return Kind.Swipe;
        if (n.Contains("wiring")) return Kind.Wiring;
        if (n.Contains("course") || n.Contains("align") || n.Contains("filter") ||
            n.Contains("camera") || n.Contains("asteroid")) return Kind.Targets;
        return Kind.Hold;
    }

    public static void Open(string room, string taskName, Action onSuccess)
    {
        if (IsOpen) return;
        var go = new GameObject("TaskMiniGame");
        var mg = go.AddComponent<TaskMiniGame>();
        mg.Build(room, taskName, onSuccess);
    }

    public static void CloseIfOpen()
    {
        if (current != null) current.Close();
    }

    // ---------------- build ----------------

    private void Build(string room, string taskName, Action success)
    {
        current = this;
        IsOpen = true;
        onSuccess = success;

        var canvasGo = gameObject;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = MakeRect("Dim", transform, Vector2.zero, Color.black * 0.6f);
        Stretch(dim);
        dim.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        var win = MakeRect("Window", transform, new Vector2(1000, 640), Panel);
        win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
        win.anchoredPosition = Vector2.zero;

        var title = MakeText("Title", win, string.IsNullOrEmpty(room) ? taskName : $"{room}: {taskName}", 44, Teal);
        Place(title, new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(900, 70));

        hint = MakeText("Hint", win, "", 30, Color.white);
        Place(hint, new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(900, 50));

        var close = MakeButton("Close", win, "X", Grey, 70, 70, 36);
        Place(close.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-50, -50), new Vector2(70, 70));
        close.onClick.AddListener(Close);

        content = MakeRect("Content", win, new Vector2(900, 440), new Color(0, 0, 0, 0));
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.anchoredPosition = new Vector2(0, -50);

        switch (KindFor(taskName))
        {
            case Kind.Swipe: StartSwipe(); break;
            case Kind.Wiring: StartWiring(); break;
            case Kind.Targets: StartTargets(); break;
            default: StartHold(); break;
        }
    }

    private void Update()
    {
        if (finished)
        {
            if (Time.unscaledTime >= finishAt) { var cb = onSuccess; onSuccess = null; Close(); cb?.Invoke(); }
            return;
        }

        bool meeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        bool over = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        if (meeting || over) { Close(); return; }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    private void Complete()
    {
        if (finished) return;
        finished = true;
        finishAt = Time.unscaledTime + 0.6f;
        hint.text = "TASK COMPLETE";
        hint.color = Teal;
    }

    private void Close()
    {
        if (current == this) current = null;
        IsOpen = false;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        IsOpen = false;
    }

    // ---------------- Hold ----------------

    private class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held;
        public void OnPointerDown(PointerEventData e) => Held = true;
        public void OnPointerUp(PointerEventData e) => Held = false;
        public void OnPointerExit(PointerEventData e) => Held = false;
    }

    private void StartHold()
    {
        hint.text = "Press and hold until the bar fills";
        var barBg = MakeRect("BarBg", content, new Vector2(700, 50), Ink);
        barBg.anchoredPosition = new Vector2(0, 120);
        var fill = MakeRect("Fill", barBg, new Vector2(0, 0), Teal);
        fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1); fill.pivot = new Vector2(0, 0.5f);
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero; fill.sizeDelta = new Vector2(0, 0);

        var btn = MakeButton("HoldBtn", content, "HOLD", Teal, 420, 150, 54);
        btn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -60);
        btn.GetComponentInChildren<TextMeshProUGUI>().color = Ink;
        var hold = btn.gameObject.AddComponent<HoldButton>();

        float progress = 0f;
        StartCoroutine(Loop(() =>
        {
            if (finished) return;
            progress = Mathf.Clamp01(progress + (hold.Held ? 1f / 3f : -0.5f) * Time.unscaledDeltaTime);
            fill.sizeDelta = new Vector2(700f * progress, 0);
            if (progress >= 1f) Complete();
        }));
    }

    // ---------------- Swipe ----------------

    private class DragCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public float MinX, MaxX;
        public Action<float> OnRelease; // normalized 0..1 progress
        public Action OnBegin;
        RectTransform rt;
        void Awake() { rt = (RectTransform)transform; }
        public void OnBeginDrag(PointerEventData e) => OnBegin?.Invoke();
        public void OnDrag(PointerEventData e)
        {
            var p = rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x + e.delta.x / rt.lossyScale.x, MinX, MaxX);
            rt.anchoredPosition = p;
            if (p.x >= MaxX) OnRelease?.Invoke(1f);
        }
        public void OnEndDrag(PointerEventData e) { OnRelease?.Invoke((rt.anchoredPosition.x - MinX) / (MaxX - MinX)); }
    }

    private void StartSwipe()
    {
        hint.text = "Drag the card across, not too fast, not too slow";
        var track = MakeRect("Track", content, new Vector2(760, 20), Ink);
        track.anchoredPosition = new Vector2(0, -20);
        var card = MakeRect("Card", content, new Vector2(140, 200), Teal);
        card.anchoredPosition = new Vector2(-380, -20);
        var cardTxt = MakeText("T", card, "CARD", 28, Ink);
        Stretch(cardTxt);

        float startTime = 0f;
        var drag = card.gameObject.AddComponent<DragCard>();
        drag.MinX = -380; drag.MaxX = 380;
        drag.OnBegin = () => startTime = Time.unscaledTime;
        drag.OnRelease = p =>
        {
            if (finished) return;
            if (p < 1f) { card.anchoredPosition = new Vector2(-380, -20); return; }
            float t = Time.unscaledTime - startTime;
            if (t < 0.5f) { hint.text = "Too fast, try again"; card.anchoredPosition = new Vector2(-380, -20); }
            else if (t > 1.6f) { hint.text = "Too slow, try again"; card.anchoredPosition = new Vector2(-380, -20); }
            else Complete();
        };
    }

    // ---------------- Wiring ----------------

    private void StartWiring()
    {
        hint.text = "Pick a wire on the left, then its match on the right";
        Color[] cols = { new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.4f, 0.9f), new Color(0.95f, 0.85f, 0.2f), new Color(0.25f, 0.75f, 0.35f) };
        var order = new List<int> { 0, 1, 2, 3 };
        for (int i = order.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }

        int selected = -1, done = 0;
        var left = new Button[4];
        var right = new Button[4];

        for (int i = 0; i < 4; i++)
        {
            int id = i;
            left[i] = MakeButton($"L{i}", content, "", cols[i], 150, 70, 30);
            left[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(-250, 150 - i * 100);
            left[i].onClick.AddListener(() => { if (left[id].interactable) selected = id; });

            int colorId = order[i];
            right[i] = MakeButton($"R{i}", content, "", cols[colorId], 150, 70, 30);
            right[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(250, 150 - i * 100);
            int slot = i;
            right[i].onClick.AddListener(() =>
            {
                if (!right[slot].interactable || selected < 0) return;
                if (selected == colorId)
                {
                    left[selected].interactable = false; right[slot].interactable = false;
                    left[selected].GetComponentInChildren<TextMeshProUGUI>().text = "OK";
                    right[slot].GetComponentInChildren<TextMeshProUGUI>().text = "OK";
                    selected = -1; done++;
                    if (done >= 4) Complete();
                }
                else { selected = -1; hint.text = "Wrong match, try again"; }
            });
        }
    }

    // ---------------- Targets ----------------

    private void StartTargets()
    {
        hint.text = "Click each target as it appears";
        int remaining = 6;
        var btn = MakeButton("Target", content, "", Teal, 110, 110, 30);
        var rt = btn.GetComponent<RectTransform>();
        var counter = MakeText("Counter", content, "6", 40, Color.white);
        Place(counter, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(200, 60));

        void Move() => rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-380f, 380f), UnityEngine.Random.Range(-130f, 130f));
        Move();
        btn.onClick.AddListener(() =>
        {
            if (finished) return;
            remaining--;
            counter.text = remaining.ToString();
            if (remaining <= 0) { btn.gameObject.SetActive(false); Complete(); }
            else Move();
        });
    }

    // ---------------- ui helpers ----------------

    private System.Collections.IEnumerator Loop(Action tick)
    {
        while (true) { tick(); yield return null; }
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = color.a > 0.01f;
        return rt;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    private static Button MakeButton(string name, Transform parent, string label, Color fill, float w, float h, float fontSize)
    {
        var rt = MakeRect(name, parent, new Vector2(w, h), fill);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        var t = MakeText("Label", rt, label, fontSize, Color.white);
        Stretch(t);
        return b;
    }

    private static void Place(Graphic g, Vector2 anchor, Vector2 pos, Vector2 size) => Place((RectTransform)g.transform, anchor, pos, size);

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(Graphic g) => Stretch((RectTransform)g.transform);

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
