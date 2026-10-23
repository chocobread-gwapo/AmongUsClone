using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Saved player settings: master / music / effects volume, fullscreen, and
// keybind overrides. Stored in PlayerPrefs and re-applied on every scene load
// so every scene (and the music that survives scene changes) stays in sync.
public static class GameSettings
{
    private const string KeyMaster = "maligno.vol.master";
    private const string KeyMusic = "maligno.vol.music";
    private const string KeySfx = "maligno.vol.sfx";
    private const string KeyFullscreen = "maligno.fullscreen";
    private const string KeyBindings = "maligno.bindings";

    public const float DefaultMaster = 1f;
    public const float DefaultMusic = 0.5f;
    public const float DefaultSfx = 0.8f;

    public static float Master { get; private set; } = DefaultMaster;
    public static float Music { get; private set; } = DefaultMusic;
    public static float Sfx_ { get; private set; } = DefaultSfx;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Master = PlayerPrefs.GetFloat(KeyMaster, DefaultMaster);
        Music = PlayerPrefs.GetFloat(KeyMusic, DefaultMusic);
        Sfx_ = PlayerPrefs.GetFloat(KeySfx, DefaultSfx);
        if (PlayerPrefs.HasKey(KeyFullscreen)) Screen.fullScreen = PlayerPrefs.GetInt(KeyFullscreen) == 1;
        SceneManager.sceneLoaded += (s, m) => ApplyAll();
        ApplyAll();
    }

    public static void ApplyAll()
    {
        AudioListener.volume = Master;
        global::Sfx.MasterVolume = Sfx_;
        if (AudioManager.Instance != null) AudioManager.Instance.SetVolume(Music);
        LoadBindings();
    }

    public static void SetMaster(float v) { Master = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyMaster, Master); AudioListener.volume = Master; }
    public static void SetMusic(float v) { Music = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyMusic, Music); AudioManager.Instance?.SetVolume(Music); }
    public static void SetSfx(float v) { Sfx_ = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeySfx, Sfx_); global::Sfx.MasterVolume = Sfx_; }

    public static void SetFullscreen(bool on)
    {
        Screen.fullScreen = on;
        PlayerPrefs.SetInt(KeyFullscreen, on ? 1 : 0);
    }

    // ---- keybinds ------------------------------------------------------

    // Our own controls asset is the one whose Player map has the Kill + Ability
    // actions (Unity's default InputSystem_Actions also has a "Player" map).
    private static bool IsOurs(InputActionAsset a)
    {
        if (a == null) return false;
        var map = a.FindActionMap("Player", false);
        return map != null && map.FindAction("Kill", false) != null && map.FindAction("Ability", false) != null;
    }

    public static InputActionAsset FindAsset()
    {
        foreach (var a in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            if (IsOurs(a)) return a;
        // Not referenced by the current scene (e.g. main menu): load it from Resources.
        var loaded = Resources.Load<InputActionAsset>("PlayerControls");
        return IsOurs(loaded) ? loaded : null;
    }

    public static void LoadBindings()
    {
        string json = PlayerPrefs.GetString(KeyBindings, "");
        if (string.IsNullOrEmpty(json)) return;
        Resources.Load<InputActionAsset>("PlayerControls"); // make sure it is loaded so it is found below
        foreach (var a in Resources.FindObjectsOfTypeAll<InputActionAsset>())
        {
            if (!IsOurs(a)) continue;
            try { a.LoadBindingOverridesFromJson(json); } catch (System.Exception e) { Debug.LogWarning("[Settings] Could not load keybinds: " + e.Message); }
        }
    }

    public static void SaveBindings(InputActionAsset asset)
    {
        if (asset == null) return;
        PlayerPrefs.SetString(KeyBindings, asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    public static void ResetBindings(InputActionAsset asset)
    {
        if (asset != null) asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(KeyBindings);
    }

    // ---- hints ---------------------------------------------------------

    private const string KeyHints = "maligno.hints";
    private const string KeyHintCards = "maligno.hintcards";

    public static bool HintsOn
    {
        get => PlayerPrefs.GetInt(KeyHints, 1) == 1;
        set { PlayerPrefs.SetInt(KeyHints, value ? 1 : 0); if (value) PlayerPrefs.SetInt(KeyHintCards, 0); }
    }

    // How many matches the how-to card has been shown for (it stops after a few).
    public static int HintCardsShown
    {
        get => PlayerPrefs.GetInt(KeyHintCards, 0);
        set => PlayerPrefs.SetInt(KeyHintCards, value);
    }

    // Current key for an action ("Interact", "Kill", "Report", "Ability", "Move"),
    // as shown in the Settings menu, so hint text always matches the player's binds.
    public static string KeyFor(string actionName)
    {
        var asset = FindAsset();
        var action = asset != null ? asset.FindActionMap("Player", false)?.FindAction(actionName, false) : null;
        if (action == null) return "?";
        if (actionName == "Move")
        {
            string up = "", down = "", left = "", right = "";
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var b = action.bindings[i];
                if (!b.isPartOfComposite) continue;
                string k = action.GetBindingDisplayString(i).ToUpperInvariant();
                if (b.name == "up") up = k; else if (b.name == "down") down = k; else if (b.name == "left") left = k; else if (b.name == "right") right = k;
            }
            return up + left + down + right;
        }
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (b.isComposite || b.isPartOfComposite) continue;
            if (!b.effectivePath.StartsWith("<Keyboard>")) continue;
            return action.GetBindingDisplayString(i).ToUpperInvariant();
        }
        return "?";
    }

    public static void ResetAudio()
    {
        SetMaster(DefaultMaster); SetMusic(DefaultMusic); SetSfx(DefaultSfx);
    }
}
