using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A small corner minimap that expands into a full map (click it or press M).
//
// It shows the LAYOUT of the map (a one-time snapshot taken with the lights turned up, so the fog of war does not
// darken it), your own position, and markers for the tasks you still have to do. Other players are deliberately
// NOT shown, so the fog of war keeps its meaning.
//
// Nothing needs to be set up in the scene: this builds itself when the Game scene loads.
public class Minimap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Game") return;
        if (FindFirstObjectByType<Minimap>() != null) return;
        new GameObject("Minimap (runtime)").AddComponent<Minimap>();
    }

    private const float MiniDiameter = 270f;   // circle on screen, px
    private const float MiniViewWorld = 14f;    // how many world units the circle shows across (smaller = more zoomed in)
    private const float FullMaxW = 1500f;       // full map size limits, px
    private const float FullMaxH = 900f;
    private static readonly Color Frame = new Color(0.06f, 0.08f, 0.1f, 0.92f);
    private static readonly Color Ring = new Color(0.85f, 0.35f, 0.32f, 1f);
    private static readonly Color PlayerColor = new Color(0.3f, 1f, 0.9f, 1f);
    private static readonly Color TaskColor = new Color(1f, 0.85f, 0.2f, 1f);

    private Bounds mapBounds;
    private Texture mapTexture;
    private bool ready;
    private bool expanded;

    private CanvasGroup group;
    private RectTransform root;
    private RectTransform mapRect;
    private RectTransform playerDot;
    private readonly List<RectTransform> taskDots = new List<RectTransform>();

    private IEnumerator Start()
    {
        // Wait for the map and the HUD to exist.
        GameObject map = null;
        for (int i = 0; i < 300 && map == null; i++)
        {
            map = FindMapRoot();
            if (map == null || GameHudUI.Instance == null) { map = null; yield return null; }
        }
        if (map == null) { Debug.LogWarning("[Minimap] No map found, minimap disabled."); yield break; }
        yield return new WaitForSeconds(0.5f);

        var renderers = map.GetComponentsInChildren<Renderer>();
        bool any = false;
        foreach (var r in renderers)
        {
            if (!any) { mapBounds = r.bounds; any = true; } else mapBounds.Encapsulate(r.bounds);
        }
        if (!any || mapBounds.size.x < 0.1f || mapBounds.size.y < 0.1f) { Debug.LogWarning("[Minimap] Map has no size."); yield break; }
        mapBounds.Expand(new Vector3(0.5f, 0.5f, 0f));

        yield return CaptureMap();
        if (mapTexture == null) yield break;

        BuildUI();
        ready = true;
    }

    private static GameObject FindMapRoot()
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name.StartsWith("MapTemplate")) return go;
        return null;
    }

    // ---------- snapshot ----------

    private IEnumerator CaptureMap()
    {
        const int maxDim = 1024;
        float aspect = mapBounds.size.x / mapBounds.size.y;
        int w = aspect >= 1f ? maxDim : Mathf.Max(64, Mathf.RoundToInt(maxDim * aspect));
        int h = aspect >= 1f ? Mathf.Max(64, Mathf.RoundToInt(maxDim / aspect)) : maxDim;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };

        // Light up the scene, hide anything that is not part of the static map, snapshot, then put everything back.
        var lights = FindObjectsByType<Light2D>(FindObjectsInactive.Exclude);
        var savedLightEnabled = new List<(Light2D l, bool enabled, float intensity)>();
        foreach (var l in lights)
        {
            savedLightEnabled.Add((l, l.enabled, l.intensity));
            if (l.lightType == Light2D.LightType.Global) { l.enabled = true; l.intensity = 1f; }
            else l.enabled = false;
        }

        var hidden = new List<Renderer>();
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            foreach (var r in pc.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }

        var markers = new List<GameObject>();
        foreach (var tz in FindObjectsByType<TaskZone>(FindObjectsInactive.Include))
            foreach (var t in tz.GetComponentsInChildren<Transform>(true))
                if (t != tz.transform && t.name == "Marker" && t.gameObject.activeSelf) { markers.Add(t.gameObject); t.gameObject.SetActive(false); }

        var camGo = new GameObject("MinimapCaptureCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = mapBounds.size.y * 0.5f;
        cam.aspect = aspect;
        cam.transform.position = new Vector3(mapBounds.center.x, mapBounds.center.y, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
        cam.cullingMask = ~((1 << 3) | (1 << 5) | (1 << 6)); // not Player, UI, Ghost
        cam.targetTexture = rt;
        cam.enabled = false;

        try
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Minimap] Snapshot failed: " + e.Message);
            rt.Release(); rt = null;
        }

        foreach (var s in savedLightEnabled) if (s.l != null) { s.l.enabled = s.enabled; s.l.intensity = s.intensity; }
        foreach (var r in hidden) if (r != null) r.enabled = true;
        foreach (var m in markers) if (m != null) m.SetActive(true);
        cam.targetTexture = null;
        Destroy(camGo);

        mapTexture = rt;
        if (rt != null) rt.wrapMode = TextureWrapMode.Clamp;
        yield return null;
    }

    // ---------- UI ----------

    private Image rootImage, maskImage;
    private Mask mask;
    private RawImage raw;
    private RectTransform maskRect;
    private Sprite discSprite;

    private static Sprite MakeDisc()
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    private void BuildUI()
    {
        Canvas canvas = GameHudUI.Instance.GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogWarning("[Minimap] No HUD canvas."); return; }
        canvas = canvas.rootCanvas;
        discSprite = MakeDisc();

        var rootGo = new GameObject("Minimap", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        root = rootGo.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.SetAsFirstSibling(); // behind meeting / mini-game screens
        group = rootGo.GetComponent<CanvasGroup>();
        rootImage = rootGo.GetComponent<Image>();
        rootGo.GetComponent<Button>().onClick.AddListener(Toggle);

        var maskGo = new GameObject("MapMask", typeof(RectTransform), typeof(Image), typeof(Mask));
        maskRect = maskGo.GetComponent<RectTransform>();
        maskRect.SetParent(root, false);
        maskImage = maskGo.GetComponent<Image>();
        maskImage.raycastTarget = false;
        mask = maskGo.GetComponent<Mask>();
        mask.showMaskGraphic = true; // doubles as the dark backdrop behind the map picture
        maskImage.color = new Color(0.04f, 0.05f, 0.07f, 1f);

        var mapGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        mapRect = mapGo.GetComponent<RectTransform>();
        mapRect.SetParent(maskRect, false);
        mapRect.anchorMin = Vector2.zero; mapRect.anchorMax = Vector2.one;
        mapRect.offsetMin = mapRect.offsetMax = Vector2.zero;
        raw = mapGo.GetComponent<RawImage>();
        raw.texture = mapTexture;
        raw.raycastTarget = false;

        playerDot = MakeDot("You", Color.white, false);
        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        var irt = inner.GetComponent<RectTransform>();
        irt.SetParent(playerDot, false);
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(2.5f, 2.5f); irt.offsetMax = new Vector2(-2.5f, -2.5f);
        var iimg = inner.GetComponent<Image>();
        iimg.color = PlayerColor; iimg.raycastTarget = false; iimg.sprite = discSprite;
        playerDot.GetComponent<Image>().sprite = discSprite;

        ApplyLayout();
    }

    private RectTransform MakeDot(string name, Color color, bool diamond)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(maskRect, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        if (diamond) rt.localRotation = Quaternion.Euler(0, 0, 45f);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private Vector2 mapPixelSize;
    private float TaskDotSize => expanded ? 18f : 10f;

    private void ApplyLayout()
    {
        if (expanded)
        {
            float aspect = mapBounds.size.x / mapBounds.size.y;
            float w = FullMaxW, h = w / aspect;
            if (h > FullMaxH) { h = FullMaxH; w = h * aspect; }
            mapPixelSize = new Vector2(w, h);

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(w + 16f, h + 16f);
            rootImage.sprite = null; rootImage.color = Frame;

            maskRect.anchorMin = maskRect.anchorMax = maskRect.pivot = new Vector2(0.5f, 0.5f);
            maskRect.anchoredPosition = Vector2.zero;
            maskRect.sizeDelta = new Vector2(w, h);
            maskImage.sprite = null;
            mask.enabled = false;
            raw.uvRect = new Rect(0, 0, 1, 1);
            playerDot.sizeDelta = new Vector2(20f, 20f);
        }
        else
        {
            mapPixelSize = new Vector2(MiniDiameter, MiniDiameter);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-24f, -72f);
            root.sizeDelta = new Vector2(MiniDiameter + 18f, MiniDiameter + 18f);
            rootImage.sprite = discSprite; rootImage.color = Ring;

            maskRect.anchorMin = maskRect.anchorMax = maskRect.pivot = new Vector2(0.5f, 0.5f);
            maskRect.anchoredPosition = Vector2.zero;
            maskRect.sizeDelta = new Vector2(MiniDiameter, MiniDiameter);
            maskImage.sprite = discSprite;
            mask.enabled = true;
            playerDot.sizeDelta = new Vector2(13f, 13f);
        }
        foreach (var d in taskDots) d.sizeDelta = new Vector2(TaskDotSize, TaskDotSize);
    }

    private void Toggle()
    {
        if (!ready) return;
        expanded = !expanded;
        ApplyLayout();
    }

    // World position -> position inside the map rect (full map mode).
    private Vector2 ToFullMap(Vector3 world)
    {
        float nx = Mathf.InverseLerp(mapBounds.min.x, mapBounds.max.x, world.x) - 0.5f;
        float ny = Mathf.InverseLerp(mapBounds.min.y, mapBounds.max.y, world.y) - 0.5f;
        return new Vector2(nx * mapPixelSize.x, ny * mapPixelSize.y);
    }

    private void Update()
    {
        if (!ready || root == null) return;

        var game = GameStartManager.Instance;
        var nm = NetworkManager.Singleton;
        var local = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        bool inMeeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        bool show = game != null && game.GameStarted && local != null && !inMeeting && !TaskMiniGame.IsOpen;

        if (!show && expanded) { expanded = false; ApplyLayout(); }
        group.alpha = show ? 1f : 0f;
        group.blocksRaycasts = show;
        group.interactable = show;
        if (!show) return;

        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) Toggle();

        Vector3 me = local.transform.position;
        float radiusPx = MiniDiameter * 0.5f - 9f;

        if (expanded)
        {
            playerDot.anchoredPosition = ToFullMap(me);
        }
        else
        {
            // Zoomed in on the player: scroll the map picture under the circle.
            playerDot.anchoredPosition = Vector2.zero;
            float ux = Mathf.InverseLerp(mapBounds.min.x, mapBounds.max.x, me.x);
            float uy = Mathf.InverseLerp(mapBounds.min.y, mapBounds.max.y, me.y);
            float uw = MiniViewWorld / mapBounds.size.x;
            float uh = MiniViewWorld / mapBounds.size.y;
            raw.uvRect = new Rect(ux - uw * 0.5f, uy - uh * 0.5f, uw, uh);
        }

        // Markers for my unfinished tasks.
        int used = 0;
        var role = local.GetComponent<PlayerRole>();
        if (TaskManager.Instance != null && (role == null || !role.IsImpostor))
        {
            foreach (var (zone, done) in TaskManager.Instance.MyTasks())
            {
                if (done || zone == null) continue;
                if (used >= taskDots.Count)
                {
                    var d = MakeDot("Task", TaskColor, true);
                    d.sizeDelta = new Vector2(TaskDotSize, TaskDotSize);
                    taskDots.Add(d);
                }
                var dot = taskDots[used];
                dot.gameObject.SetActive(true);
                var img = dot.GetComponent<Image>();
                if (expanded)
                {
                    dot.anchoredPosition = ToFullMap(zone.transform.position);
                    img.color = TaskColor;
                }
                else
                {
                    Vector2 off = (zone.transform.position - me) / MiniViewWorld * MiniDiameter;
                    bool outside = off.magnitude > radiusPx;
                    if (outside) off = off.normalized * radiusPx; // far tasks stick to the edge, pointing the way
                    dot.anchoredPosition = off;
                    img.color = outside ? new Color(TaskColor.r, TaskColor.g, TaskColor.b, 0.65f) : TaskColor;
                }
                used++;
            }
        }
        for (int i = used; i < taskDots.Count; i++) taskDots[i].gameObject.SetActive(false);
        playerDot.SetAsLastSibling();
    }

    private void OnDestroy()
    {
        if (mapTexture is RenderTexture rt) rt.Release();
        if (root != null) Destroy(root.gameObject);
    }
}
