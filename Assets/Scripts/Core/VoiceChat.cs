using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Proximity voice chat (Unity Vivox), built in code like the rest of the UI.
//  - Lobby and meetings: everyone hears everyone (all speakers are reported at the same spot).
//  - In a match: you hear players near you, quieter the farther they are.
//  - Dead players talk on a separate "ghost" channel, so the living never hear them.
//    Ghosts still hear the living (they also listen to the living channel).
// Two positional channels per room: "<code>-alive" and "<code>-ghost". The room's join code names them.
// Mic / voice volume, mute and the noise filter live in the Settings screen (see VoiceSettings).
// A small status pill (top-right, left of CHAT) shows whether voice is connected, muted or you are speaking;
// clicking it opens Settings.
public class VoiceChat : MonoBehaviour
{
    // World units. Players closer than ConversationalDistance are heard at full volume,
    // the voice fades out completely at AudibleDistance.
    private const int AudibleDistance = 14;
    private const int ConversationalDistance = 2;
    private const float FadeIntensity = 1.96f; // = 3 / (1 + 1.75 * log10(ConversationalDistance)) for the exponential model
    private const float PositionInterval = 0.3f;

    private static VoiceChat instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("VoiceChat");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VoiceChat>();
    }

    private enum State { Idle, Connecting, Connected, Failed }

    private State state = State.Idle;
    private string roomKey = "";      // join code of the room we are connected to
    private bool inGhost;             // currently also in the ghost channel and transmitting there
    private bool busy;
    private bool vivoxInitialized;
    private float nextPosition, nextCheck, retryAt, nextHud;
    private Transform globalAnchor;

    private string AliveChannel => roomKey + "-alive";
    private string GhostChannel => roomKey + "-ghost";

    private void Awake()
    {
        var anchor = new GameObject("VoiceGlobalAnchor");
        anchor.transform.SetParent(transform, false);
        anchor.transform.position = Vector3.zero;
        globalAnchor = anchor.transform;
        BuildIndicator();
        hudGo.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // =====================================================================

    private static bool InSession(out string code)
    {
        code = "";
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || SceneManager.GetActiveScene().name == "MainMenu") return false;
        var mgr = MultiplayerConnectionManager.Instance;
        if (mgr == null || string.IsNullOrEmpty(mgr.CurrentJoinCode)) return false;
        code = mgr.CurrentJoinCode;
        return true;
    }

    private static PlayerRole LocalRole()
    {
        foreach (var r in FindObjectsByType<PlayerRole>(FindObjectsSortMode.None))
            if (r != null && r.IsOwner) return r;
        return null;
    }

    private void UpdatePushToTalk()
    {
        bool down = false;
        var kb = Keyboard.current;
        if (VoiceSettings.PttEnabled && kb != null && !ChatUI.Typing && !SettingsMenu.IsOpen)
        {
            try { down = kb[VoiceSettings.PttKey].isPressed; } catch { down = false; }
        }
        bool wasHeld = pttHeld;
        if (down) { pttHeld = true; pttReleaseAt = Time.unscaledTime + 0.3f; }
        else if (pttHeld && Time.unscaledTime >= pttReleaseAt) pttHeld = false;
        if (!VoiceSettings.PttEnabled) pttHeld = false;
        if (pttHeld != wasHeld) ApplyMute();
    }

    private void Update()
    {
        UpdatePushToTalk();
        if (Time.unscaledTime >= nextHud)
        {
            nextHud = Time.unscaledTime + 0.1f;
            RefreshIndicator();
        }
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.25f;

        bool want = InSession(out string code);
        if (want && state == State.Idle && !busy && Time.unscaledTime >= retryAt) _ = ConnectAsync(code);
        else if (!want && (state == State.Connected || state == State.Connecting || state == State.Failed) && !busy) _ = DisconnectAsync();
        else if (want && state == State.Failed && code != roomKey) { state = State.Idle; }

        if (state != State.Connected || busy) return;

        // Dead players move to the ghost channel; back in the lobby everyone is "alive" again.
        var role = LocalRole();
        bool started = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted;
        bool dead = started && role != null && !role.IsAlive;
        if (dead != inGhost) _ = SwitchGhostAsync(dead);

        if (Time.unscaledTime >= nextPosition) UpdatePositions(role);
    }

    private void UpdatePositions(PlayerRole role)
    {
        nextPosition = Time.unscaledTime + PositionInterval;
        bool started = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted;
        bool meeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
        bool global = !started || meeting || role == null;
        GameObject who = global ? globalAnchor.gameObject : role.gameObject;
        try
        {
            VivoxService.Instance.Set3DPosition(who, AliveChannel, true);
            if (inGhost) VivoxService.Instance.Set3DPosition(who, GhostChannel, true);
        }
        catch { /* channel still joining; next tick */ }
    }

    // =====================================================================
    // Vivox plumbing
    // =====================================================================

    private static Channel3DProperties Props() =>
        new Channel3DProperties(AudibleDistance, ConversationalDistance, FadeIntensity, AudioFadeModel.ExponentialByDistance);

    private async Task ConnectAsync(string code)
    {
        busy = true;
        state = State.Connecting;
        roomKey = code;
        try
        {
            if (!vivoxInitialized)
            {
                await VivoxService.Instance.InitializeAsync();
                vivoxInitialized = true;
            }
            if (!VivoxService.Instance.IsLoggedIn)
            {
                await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = PlayerNameSafe() });
            }
            await ApplyFilterAsync();
            ApplyMute();
            ApplyVolumes();
            await VivoxService.Instance.JoinPositionalChannelAsync(AliveChannel, ChatCapability.AudioOnly, Props(),
                new ChannelOptions { MakeActiveChannelUponJoining = true });
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, AliveChannel);
            inGhost = false;
            state = State.Connected;
            Debug.Log("[Voice] Joined " + AliveChannel);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Voice] Could not start voice chat: " + e.Message);
            state = State.Failed;
            retryAt = Time.unscaledTime + 5f;
        }
        busy = false;
    }

    // Pushes the saved mic / voice settings to Vivox. Safe to call any time; does nothing until voice is up.
    public static void ApplyVolumes()
    {
        if (instance == null || !instance.vivoxInitialized) return;
        try
        {
            VivoxService.Instance.SetInputDeviceVolume(Mathf.RoundToInt((VoiceSettings.MicVolume - 0.5f) * 100f));
            VivoxService.Instance.SetOutputDeviceVolume(Mathf.RoundToInt((VoiceSettings.VoiceVolume - 0.5f) * 100f));
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] Volume not applied: " + e.Message); }
    }

    // True while the push-to-talk key is held (plus a short tail so the end of a word isn't cut off).
    private static bool pttHeld;
    private float pttReleaseAt;

    public static void ApplyMute()
    {
        if (instance == null || !instance.vivoxInitialized) return;
        try
        {
            bool mute = VoiceSettings.MicMuted || (VoiceSettings.PttEnabled && !pttHeld);
            if (mute) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] Mute not applied: " + e.Message); }
    }

    // Noise filter = echo cancellation (speakers leaking into the mic) + automatic voice detection
    // (the mic only opens for speech, so keyboard clicks and fans stay quiet). Vivox also applies its
    // own noise suppression and automatic gain on top.
    public static async void ApplyFilter()
    {
        if (instance == null || !instance.vivoxInitialized) return;
        await instance.ApplyFilterAsync();
    }

    private async Task ApplyFilterAsync()
    {
        bool on = VoiceSettings.NoiseFilter;
        try
        {
            if (on) VivoxService.Instance.EnableAcousticEchoCancellation();
            else VivoxService.Instance.DisableAcousticEchoCancellation();
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] Echo cancellation: " + e.Message); }
        try
        {
            if (!VivoxService.Instance.IsLoggedIn) return;
            if (on) await VivoxService.Instance.EnableAutoVoiceActivityDetectionAsync();
            else await VivoxService.Instance.DisableAutoVoiceActivityDetectionAsync();
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] Voice detection: " + e.Message); }
    }

    // Short text for the Settings screen.
    public static string StatusText
    {
        get
        {
            if (instance == null) return "off";
            switch (instance.state)
            {
                case State.Connecting: return "connecting...";
                case State.Connected: return "connected";
                case State.Failed: return "unavailable";
                default: return "not in a room";
            }
        }
    }

    private static string PlayerNameSafe()
    {
        string n = PlayerName.Load();
        return string.IsNullOrEmpty(n) ? "Player" : n;
    }

    private async Task SwitchGhostAsync(bool toGhost)
    {
        busy = true;
        try
        {
            if (toGhost)
            {
                await VivoxService.Instance.JoinPositionalChannelAsync(GhostChannel, ChatCapability.AudioOnly, Props(),
                    new ChannelOptions { MakeActiveChannelUponJoining = true });
                await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, GhostChannel);
                inGhost = true;
            }
            else
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, AliveChannel);
                await VivoxService.Instance.LeaveChannelAsync(GhostChannel);
                inGhost = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Voice] Channel switch failed: " + e.Message);
            inGhost = toGhost; // don't retry in a tight loop
        }
        busy = false;
    }

    private async Task DisconnectAsync()
    {
        busy = true;
        try
        {
            if (vivoxInitialized) await VivoxService.Instance.LeaveAllChannelsAsync();
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] Leave failed: " + e.Message); }
        inGhost = false;
        roomKey = "";
        state = State.Idle;
        busy = false;
    }

    // =====================================================================
    // Status indicator
    // =====================================================================

    private GameObject hudGo;
    private Image hudBg, hudDot;
    private TMP_Text hudText;

    private bool LocalSpeaking()
    {
        if (!vivoxInitialized || state != State.Connected || VoiceSettings.MicMuted || (VoiceSettings.PttEnabled && !pttHeld)) return false;
        try
        {
            if (VivoxService.Instance.ActiveChannels.TryGetValue(AliveChannel, out var people))
                foreach (var p in people)
                    if (p.IsSelf) return p.SpeechDetected;
        }
        catch { }
        return false;
    }

    private void RefreshIndicator()
    {
        bool show = state != State.Idle && InSession(out _) && !TaskMiniGame.IsOpen;
        if (hudGo.activeSelf != show) hudGo.SetActive(show);
        if (!show) return;

        Color bg, dot; string text;
        switch (state)
        {
            case State.Connecting:
                bg = new Color(0.16f, 0.18f, 0.24f, 0.95f); dot = new Color(0.7f, 0.74f, 0.8f); text = "VOICE...";
                break;
            case State.Failed:
                bg = new Color(0.4f, 0.14f, 0.14f, 0.95f); dot = new Color(1f, 0.35f, 0.35f); text = "VOICE OFF";
                break;
            default:
                if (VoiceSettings.MicMuted)
                { bg = new Color(0.4f, 0.14f, 0.14f, 0.95f); dot = new Color(1f, 0.35f, 0.35f); text = "MIC MUTED"; }
                else if (VoiceSettings.PttEnabled && !pttHeld)
                { bg = new Color(0.16f, 0.2f, 0.3f, 0.95f); dot = new Color(0.55f, 0.65f, 0.85f); text = "PUSH " + VoiceSettings.PttKeyName; }
                else if (LocalSpeaking())
                { bg = new Color(0.12f, 0.45f, 0.24f, 0.95f); dot = new Color(0.45f, 1f, 0.55f); text = "SPEAKING"; }
                else
                { bg = new Color(0.08f, 0.24f, 0.16f, 0.95f); dot = new Color(0.3f, 0.8f, 0.45f); text = "VOICE ON"; }
                break;
        }
        hudBg.color = bg; hudDot.color = dot; hudText.text = text;
    }

    private void BuildIndicator()
    {
        var canvasGo = new GameObject("VoiceCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        hudGo = new GameObject("VoiceIndicator", typeof(RectTransform), typeof(Image), typeof(Button));
        hudGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)hudGo.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-388, -16); // left of CHAT
        rt.sizeDelta = new Vector2(190, 52);
        hudBg = hudGo.GetComponent<Image>();
        var btn = hudGo.GetComponent<Button>();
        btn.targetGraphic = hudBg;
        btn.onClick.AddListener(() => SettingsMenu.Open());

        var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(hudGo.transform, false);
        var drt = (RectTransform)dotGo.transform;
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0, 0.5f);
        drt.anchoredPosition = new Vector2(16, 0); drt.sizeDelta = new Vector2(18, 18);
        hudDot = dotGo.GetComponent<Image>();
        hudDot.raycastTarget = false;

        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(hudGo.transform, false);
        hudText = tgo.AddComponent<TextMeshProUGUI>();
        hudText.fontSize = 24; hudText.fontStyle = FontStyles.Bold;
        hudText.alignment = TextAlignmentOptions.MidlineLeft; hudText.color = Color.white;
        hudText.raycastTarget = false;
        var trt = (RectTransform)tgo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(44, 0); trt.offsetMax = new Vector2(-8, 0);
    }
}

// Saved on this computer, shown in the Settings screen, applied to Vivox by VoiceChat.
public static class VoiceSettings
{
    private const string MutedKey = "maligno.mic.muted";
    private const string FilterKey = "maligno.mic.filter";
    private const string MicVolKey = "maligno.mic.volume";
    private const string VoiceVolKey = "maligno.voice.volume";
    private const string PttOnKey = "maligno.ptt.enabled";
    private const string PttKeyKey = "maligno.ptt.key";

    // Push to talk: the mic only transmits while this key is held.
    public static bool PttEnabled
    {
        get => PlayerPrefs.GetInt(PttOnKey, 0) == 1;
        set { PlayerPrefs.SetInt(PttOnKey, value ? 1 : 0); VoiceChat.ApplyMute(); }
    }

    public static UnityEngine.InputSystem.Key PttKey
    {
        get => (UnityEngine.InputSystem.Key)PlayerPrefs.GetInt(PttKeyKey, (int)UnityEngine.InputSystem.Key.V);
        set { PlayerPrefs.SetInt(PttKeyKey, (int)value); }
    }

    public static string PttKeyName => PttKey.ToString().ToUpperInvariant();

    public static bool MicMuted
    {
        get => PlayerPrefs.GetInt(MutedKey, 0) == 1;
        set { PlayerPrefs.SetInt(MutedKey, value ? 1 : 0); VoiceChat.ApplyMute(); }
    }

    public static bool NoiseFilter
    {
        get => PlayerPrefs.GetInt(FilterKey, 1) == 1;
        set { PlayerPrefs.SetInt(FilterKey, value ? 1 : 0); VoiceChat.ApplyFilter(); }
    }

    // 0..1, 0.5 = Vivox default loudness.
    public static float MicVolume => PlayerPrefs.GetFloat(MicVolKey, 0.5f);
    public static float VoiceVolume => PlayerPrefs.GetFloat(VoiceVolKey, 0.5f);

    public static void SetMicVolume(float v) { PlayerPrefs.SetFloat(MicVolKey, Mathf.Clamp01(v)); VoiceChat.ApplyVolumes(); }
    public static void SetVoiceVolume(float v) { PlayerPrefs.SetFloat(VoiceVolKey, Mathf.Clamp01(v)); VoiceChat.ApplyVolumes(); }

    public static void Reset()
    {
        PlayerPrefs.DeleteKey(MutedKey); PlayerPrefs.DeleteKey(FilterKey);
        PlayerPrefs.DeleteKey(MicVolKey); PlayerPrefs.DeleteKey(VoiceVolKey);
        PlayerPrefs.DeleteKey(PttOnKey); PlayerPrefs.DeleteKey(PttKeyKey);
        VoiceChat.ApplyMute(); VoiceChat.ApplyVolumes(); VoiceChat.ApplyFilter();
    }
}
