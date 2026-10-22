using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
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
        toast.SetActive(false); prompt.SetActive(false);
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
            return;
        }

        UpdateMeetingButton(mm);
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

        int limit = mm.EmergencyCallsPerPlayer;
        int left = MeetingManager.LocalCallsLeft < 0 ? limit : MeetingManager.LocalCallsLeft;
        float cd = mm.EmergencyCooldownRemaining;
        bool usable = left > 0 && cd <= 0f;
        meetingGroup.alpha = usable ? 1f : 0.45f;

        if (meetingLabel != null && meetingBase != null)
        {
            string extra = left <= 0 ? "(0 left)" : cd > 0f ? "(" + Mathf.CeilToInt(cd) + "s)" : "(" + left + " left)";
            meetingLabel.text = meetingBase + " " + extra;
        }
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
