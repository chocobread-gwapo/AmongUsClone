using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// End-of-match screen: VICTORY / DEFEAT for the viewer, which side won and
// why, and a full role reveal of every player (who the Maligno were, which
// classes the villagers had, and who survived). Built in code at runtime on
// top of the scene's GameOverPanel so no scene edits are needed.
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI gameOverText;

    private GameObject overlay;

    private static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    private static readonly Color Red = new Color(0.95f, 0.25f, 0.25f);
    private static readonly Color Blue = new Color(0.4f, 0.8f, 1f);
    private static readonly Color Dim = new Color(0.7f, 0.72f, 0.78f);

    private void Awake()
    {
        Instance = this;
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    // Plain-text fallback, kept for any caller that only has a message.
    public void Show(string message)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        Sfx.Play2D("gameover");
        if (gameOverText != null) gameOverText.text = message;
        GameHudUI.Instance?.Hide();
    }

    public void ShowResult(bool crewWon, string headline, string reason,
        ulong[] ids, int[] roles, int[] types, int[] classes, bool[] alive,
        int tasksDone, int tasksTotal)
    {
        if (gameOverPanel == null) { Show(headline + " - " + reason); return; }
        gameOverPanel.SetActive(true);
        GameHudUI.Instance?.Hide();

        ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        bool viewerIsMaligno = false;
        for (int i = 0; i < ids.Length; i++) if (ids[i] == me) viewerIsMaligno = roles[i] == 1;
        bool viewerWon = crewWon != viewerIsMaligno;
        Sfx.Play2D(viewerWon ? "alltasks" : "gameover");

        if (overlay != null) Destroy(overlay);
        if (gameOverText != null) gameOverText.gameObject.SetActive(false);
        overlay = BuildOverlay(viewerWon, crewWon, headline, reason, ids, roles, types, classes, alive, tasksDone, tasksTotal, me);
        StartCoroutine(Reveal(overlay));
    }

    // Called by GameStartManager's NotifyReturnedToLobbyClientRpc once the
    // host has reset match state, so the panel clears for everyone.
    public void Hide()
    {
        if (overlay != null) { Destroy(overlay); overlay = null; }
        if (gameOverText != null) gameOverText.gameObject.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    // Wired to the "Return to Lobby" button's OnClick inside GameOverPanel -
    // only the host's click actually does anything (HostReturnToLobby() is
    // server-gated internally), everyone else's click is a harmless no-op.
    public void OnReturnToLobbyClicked()
    {
        GameStartManager.Instance?.HostReturnToLobby();
    }

    // ---------------------------------------------------------------- build

    private GameObject BuildOverlay(bool viewerWon, bool crewWon, string headline, string reason,
        ulong[] ids, int[] roles, int[] types, int[] classes, bool[] alive,
        int tasksDone, int tasksTotal, ulong me)
    {
        var root = new GameObject("ResultOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        root.transform.SetParent(gameOverPanel.transform, false);
        root.transform.SetAsLastSibling();
        var rt = (RectTransform)root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var bg = root.GetComponent<Image>();
        bg.color = viewerWon ? new Color(0.02f, 0.05f, 0.09f, 1f) : new Color(0.07f, 0.01f, 0.01f, 1f);
        root.GetComponent<CanvasGroup>().alpha = 0f;

        Color sideColor = crewWon ? Blue : Red;
        MakeText(root.transform, "Title", viewerWon ? "VICTORY" : "DEFEAT", 96, viewerWon ? Gold : Red,
            new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(1000, 110));
        MakeText(root.transform, "Side", (crewWon ? "VILLAGERS WIN" : "THE MALIGNO WIN") + ": " + headline, 34, sideColor,
            new Vector2(0.5f, 1f), new Vector2(0, -165), new Vector2(1400, 50));
        MakeText(root.transform, "Reason", reason, 24, Dim,
            new Vector2(0.5f, 1f), new Vector2(0, -210), new Vector2(1400, 40));

        // Roster, Maligno first (that's the reveal people want), then by player id.
        var order = new List<int>();
        for (int i = 0; i < ids.Length; i++) order.Add(i);
        order.Sort((a, b) => roles[a] != roles[b] ? roles[b].CompareTo(roles[a]) : ids[a].CompareTo(ids[b]));

        var list = new GameObject("Roster", typeof(RectTransform), typeof(VerticalLayoutGroup));
        list.transform.SetParent(root.transform, false);
        var lrt = (RectTransform)list.transform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f); lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0, -270); lrt.sizeDelta = new Vector2(980, 0);
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8; vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = list.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Row(list.transform, "PLAYER", "ROLE", "FATE", Dim, new Color(0, 0, 0, 0), true);
        foreach (int i in order)
        {
            bool mal = roles[i] == 1;
            string role = mal ? "Maligno (" + MalignoName((MalignoType)types[i]) + ")" : SurvivorName((SurvivorClass)classes[i]);
            string name = PlayerLabel.DisplayName(ids[i]) + (ids[i] == me ? "  (you)" : "");
            Row(list.transform, name, role, alive[i] ? "Survived" : "Eliminated",
                mal ? Red : Blue,
                mal ? new Color(0.45f, 0.05f, 0.05f, 0.55f) : new Color(0.08f, 0.2f, 0.3f, 0.55f),
                false, PlayerLabel.ColorFor(ids[i]), alive[i]);
        }

        if (tasksTotal > 0)
            MakeText(root.transform, "Tasks", $"Chores finished: {tasksDone} / {tasksTotal}", 24, Dim,
                new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(900, 40));

        bool host = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        if (host) MakeButton(root.transform, "RETURN TO LOBBY");
        else MakeText(root.transform, "Wait", "Waiting for the host to return to the lobby...", 26, Dim,
            new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(1100, 44));
        return root;
    }

    private void Row(Transform parent, string name, string role, string fate, Color roleColor, Color bg,
        bool header, Color? swatch = null, bool alive = true)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        go.GetComponent<LayoutElement>().preferredHeight = header ? 30 : 48;
        var h = go.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 14, 4, 4); h.spacing = 12;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = true;

        var sw = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        sw.transform.SetParent(go.transform, false);
        sw.GetComponent<Image>().color = swatch ?? new Color(0, 0, 0, 0);
        var sle = sw.GetComponent<LayoutElement>(); sle.preferredWidth = 28; sle.preferredHeight = 28;

        Cell(go.transform, name, header ? 22 : 28, header ? Dim : Color.white, 360);
        Cell(go.transform, role, header ? 22 : 28, header ? Dim : roleColor, 380);
        Cell(go.transform, fate, header ? 22 : 26, header ? Dim : (alive ? new Color(0.5f, 0.9f, 0.5f) : Dim), 160);
    }

    private static void Cell(Transform parent, string text, float size, Color color, float width)
    {
        var go = new GameObject("Cell", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        go.GetComponent<LayoutElement>().preferredWidth = width;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Color color,
        Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, anchor.y >= 1f ? 1f : 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = size > 60 ? FontStyles.Bold : FontStyles.Normal;
        t.raycastTarget = false;
        return t;
    }

    private void MakeButton(Transform parent, string label)
    {
        var go = new GameObject("ReturnButton", typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 60); rt.sizeDelta = new Vector2(420, 70);
        go.GetComponent<Image>().color = new Color(0.15f, 0.45f, 0.75f, 1f);
        go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(OnReturnToLobbyClicked);
        MakeText(go.transform, "Label", label, 32, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 60));
    }

    // Fade the overlay in.
    private IEnumerator Reveal(GameObject root)
    {
        var cg = root.GetComponent<CanvasGroup>();
        float t = 0;
        while (t < 0.6f && cg != null)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Clamp01(t / 0.6f);
            yield return null;
        }
        if (cg != null) cg.alpha = 1f;
    }

    private static string MalignoName(MalignoType t)
    {
        switch (t)
        {
            case MalignoType.Duwende: return "Duwende";
            case MalignoType.Tikbalang: return "Tikbalang";
            case MalignoType.BalBal: return "Bal-Bal";
            case MalignoType.Aswang: return "Aswang";
            case MalignoType.Kapre: return "Kapre";
            default: return "Unknown";
        }
    }

    private static string SurvivorName(SurvivorClass c)
    {
        switch (c)
        {
            case SurvivorClass.ThirdEyeUser: return "Villager (Third Eye)";
            case SurvivorClass.ReikiPractitioner: return "Villager (Reiki)";
            case SurvivorClass.Albularyo: return "Villager (Albularyo)";
            case SurvivorClass.Babaylan: return "Villager (Babaylan)";
            case SurvivorClass.TarotUser: return "Villager (Tarot)";
            default: return "Villager";
        }
    }
}
