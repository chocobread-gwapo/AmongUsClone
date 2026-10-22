using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// What happens when someone drops mid-game.
//  * A CLIENT whose connection to the host ends (host quit, crash, or their own internet
//    dropped) gets a "CONNECTION LOST" screen with a button back to the main menu.
//  * The HOST, when a player drops: the match re-checks its win conditions, an open vote
//    finishes without them, their body stays reportable, and everyone gets a notice.
// There is no host migration: with Relay the host owns the match, so if the host leaves the
// match ends for everyone.
public class DisconnectHandler : MonoBehaviour
{
    private static DisconnectHandler instance;

    private NetworkManager subscribedTo;
    private GameObject overlay;
    private TextMeshProUGUI reasonText;
    private bool quitting;
    private bool shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("DisconnectHandler");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DisconnectHandler>();
        instance.Build();
    }

    private void OnApplicationQuit() { quitting = true; }

    private void OnDestroy()
    {
        if (subscribedTo != null) subscribedTo.OnClientDisconnectCallback -= OnDisconnected;
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == subscribedTo) return;
        if (subscribedTo != null) subscribedTo.OnClientDisconnectCallback -= OnDisconnected;
        subscribedTo = nm;
        if (nm != null) nm.OnClientDisconnectCallback += OnDisconnected;
    }

    private void OnDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || quitting) return;
        var conn = MultiplayerConnectionManager.Instance;
        if (conn != null && conn.IsLeaving) return;

        if (nm.IsServer)
        {
            if (clientId != nm.LocalClientId) StartCoroutine(AfterPlayerDropped(clientId));
            return;
        }

        if (clientId == nm.LocalClientId) ShowLost(nm.DisconnectReason);
    }

    // ------------------------------------------------------------- host side

    private IEnumerator AfterPlayerDropped(ulong clientId)
    {
        // Let NGO despawn the player's object first so the win check counts the right players.
        yield return null;
        yield return null;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) yield break;

        string who = PlayerLabel.DisplayName(clientId);
        var result = GameResultManager.Instance;
        bool inMatch = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted
            && result != null && !result.GameEnded;

        if (result != null) result.ServerBroadcastNotice(who + " left the game.");
        if (!inMatch) yield break;

        MeetingManager.Instance?.ServerOnPlayerLeft(clientId);
        result.CheckWinConditions();
    }

    // ----------------------------------------------------------- client side

    private void ShowLost(string reason)
    {
        if (shown) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        shown = true;
        reasonText.text = string.IsNullOrEmpty(reason)
            ? "The connection to the host was lost.\nThe host may have quit, or your internet dropped."
            : reason;
        overlay.SetActive(true);
    }

    private async void OnBackClicked()
    {
        overlay.SetActive(false);
        shown = false;
        if (MultiplayerConnectionManager.Instance != null)
            await MultiplayerConnectionManager.Instance.LeaveSessionAsync();
        SceneManager.LoadScene("MainMenu");
    }

    private void Build()
    {
        var cgo = new GameObject("DisconnectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

        overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(cgo.transform, false);
        var ort = (RectTransform)overlay.transform;
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one; ort.offsetMin = ort.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 1f);

        var title = Text(overlay.transform, 72, new Color(0.9f, 0.3f, 0.3f), new Vector2(0, 120), new Vector2(1400, 110));
        title.text = "CONNECTION LOST";
        reasonText = Text(overlay.transform, 32, Color.white, new Vector2(0, 10), new Vector2(1400, 140));

        var btn = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(overlay.transform, false);
        var brt = (RectTransform)btn.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = new Vector2(0, -150); brt.sizeDelta = new Vector2(520, 90);
        btn.GetComponent<Image>().color = new Color(0.15f, 0.7f, 0.62f);
        btn.GetComponent<Button>().onClick.AddListener(OnBackClicked);
        var label = Text(btn.transform, 34, new Color(0.02f, 0.06f, 0.07f), Vector2.zero, Vector2.zero);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.text = "BACK TO MAIN MENU";

        overlay.SetActive(false);
    }

    private static TextMeshProUGUI Text(Transform parent, float size, Color color, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        return t;
    }
}
