using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// Phase 3 matchmaking: wraps Unity Gaming Services' unified Multiplayer
// Services (Session) package so Jem and friends can test over the internet
// without port forwarding. Host creates a Relay-backed Session and gets a
// short join code; clients type that code in to connect through Relay.
//
// IMPORTANT ABOUT THIS SCRIPT: the Multiplayer Services SDK's own docs do
// NOT explicitly confirm whether WithRelayNetwork() auto-starts Netcode for
// GameObjects' NetworkManager. This script assumes it does NOT (the safer,
// more standard assumption — the Session API's job is just to allocate
// Relay and hand you a join code; NGO's NetworkManager.StartHost()/
// StartClient() is still your own call, same as it always was). If testing
// shows the session already starts NGO on its own, the StartHost()/
// StartClient() calls below become harmless no-ops to remove.
//
// Put this on a single persistent GameObject in your bootstrap/menu scene
// (DontDestroyOnLoad), alongside (or instead of) wherever NetworkManager
// currently lives, since NetworkManager must already exist in the scene
// with its Network Transport set to "Unity Transport" before this runs.
//
// SCENE STRUCTURE (3 scenes now, same split real Among Us uses):
//   MainMenu -> Lobby (waiting room: Start Game / Settings) -> Game (the match)
// MainMenu itself is never a networked scene — NGO only starts once the
// player hits Host or Join, at which point the very first networked scene
// is Lobby. Game is loaded later by GameStartManager.HostStartGame(), and
// the trip back is GameStartManager.HostReturnToLobby() — this script only
// owns the FIRST load, into Lobby.
[DefaultExecutionOrder(-500)]
public class MultiplayerConnectionManager : MonoBehaviour
{
    public static MultiplayerConnectionManager Instance { get; private set; }

    [Header("Session Settings")]
    // Session size is always the largest; the Capacity host setting locks it earlier.
    private const int HardMaxPlayers = 15;

    [Tooltip("Name of your Lobby scene (exact name, must be added in Build Settings). The host loads it the moment hosting starts — NGO's Scene Management (already enabled on your NetworkManager) automatically syncs every connected client into it too, so clients don't need to do anything extra. This is NOT the Game scene anymore — GameStartManager.HostStartGame() loads that one later, once the host presses Start.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Prefab with a NetworkObject + GameStartManager component. Spawned once, by the host only, right before the first Lobby load, so GameStartManager.Instance already exists by the time Lobby's HostSettingsPanelUI reads it. On its NetworkObject component, UNCHECK \"Destroy With Scene\" — that's what lets it survive the Lobby <-> Game scene switches instead of getting wiped with the rest of each scene.")]
    [SerializeField] private GameObject gameStartManagerPrefab;

    public string CurrentJoinCode { get; private set; } = "";
    // True from the moment the player chooses to leave until the next host/join, so the
    // disconnect screen does not pop up for a disconnect they asked for.
    public bool IsLeaving { get; private set; }
    public bool IsSignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

    // Fired with a human-readable status string any time something worth
    // telling the player happens (signing in, session created, join failed,
    // etc.) — a UI script can subscribe to this instead of polling.
    public event Action<string> OnStatusChanged;
    public event Action<string> OnJoinCodeReady; // fired once the host's join code is known
    public event Action<string> OnConnectionFailed;

    private ISession currentSession;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ---------- Sign-in ----------

    // Call this once at startup (e.g. from a bootstrap scene's Start(), or
    // right before the player hits Host/Join if you'd rather delay it).
    // Safe to call more than once — it no-ops if already initialized/signed in.
    public async Task EnsureSignedInAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                OnStatusChanged?.Invoke("Connecting to matchmaking service...");
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Multiplayer] Signed in anonymously. PlayerId: {AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            OnStatusChanged?.Invoke("Couldn't connect to the matchmaking service. Check your internet connection.");
            throw;
        }
    }

    // ---------- Host ----------

    // Creates a Relay-backed Session and starts NGO as host. Returns the
    // join code to show the host, so they can share it with friends.
    public async Task<string> HostGameAsync()
    {
        IsLeaving = false;
        try
        {
            await EnsureSignedInAsync();
            OnStatusChanged?.Invoke("Creating session...");

            var options = new SessionOptions
            {
                MaxPlayers = HardMaxPlayers,
                IsPrivate = false
            }.WithRelayNetwork();

            currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            CurrentJoinCode = currentSession.Code;
            HookLock();
            RefreshLock();

            Debug.Log($"[Multiplayer] Session created. Join code: {CurrentJoinCode}");
            OnJoinCodeReady?.Invoke(CurrentJoinCode);
            OnStatusChanged?.Invoke($"Session ready. Join code: {CurrentJoinCode}");

            // The Session/Relay allocation above only sets up the transport —
            // NGO itself still needs to be told to actually start as host.
            // IMPORTANT: creating the session with WithRelayNetwork() already
            // starts NGO's NetworkManager as host automatically — confirmed by
            // testing (calling NetworkManager.Singleton.StartHost() here throws
            // "Can't start while listening", since it's already listening by
            // this point). So we do NOT call StartHost() ourselves.
            Debug.Log($"[Multiplayer] NetworkManager.Singleton.IsListening after session creation: {NetworkManager.Singleton.IsListening}, IsHost: {NetworkManager.Singleton.IsHost}");

            // Spawn the persistent match-settings object BEFORE loading
            // Lobby, so Lobby's HostSettingsPanelUI finds a live
            // GameStartManager.Instance the moment it runs Start().
            if (NetworkManager.Singleton.IsServer && GameStartManager.Instance == null)
            {
                if (gameStartManagerPrefab == null)
                {
                    Debug.LogError("[Multiplayer] gameStartManagerPrefab isn't assigned — GameStartManager will never exist, and Lobby/Game will be missing match settings entirely.");
                }
                else
                {
                    GameObject gsmInstance = Instantiate(gameStartManagerPrefab);
                    NetworkObject gsmNetObj = gsmInstance.GetComponent<NetworkObject>();
                    if (gsmNetObj == null)
                    {
                        Debug.LogError("[Multiplayer] gameStartManagerPrefab has no NetworkObject component — destroying the unspawned instance.");
                        Destroy(gsmInstance);
                    }
                    else
                    {
                        gsmNetObj.Spawn();
                        Debug.Log("[Multiplayer] Spawned persistent GameStartManager.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(lobbySceneName))
            {
                if (NetworkManager.Singleton.SceneManager == null)
                {
                    Debug.LogError("[Multiplayer] NetworkManager.Singleton.SceneManager is null — Enable Scene Management is probably off on the NetworkManager component.");
                }
                else
                {
                    // Only the server decides scene loads. With Scene Management
                    // enabled on NetworkManager, every client that connects (now
                    // or later) gets synced into this scene automatically.
                    var status = NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
                    Debug.Log($"[Multiplayer] LoadScene(\"{lobbySceneName}\") status: {status}");
                    if (status != SceneEventProgressStatus.Started)
                    {
                        Debug.LogError($"[Multiplayer] Scene load did not start. Status: {status}. Common causes: scene name doesn't exactly match a scene in Build Settings, or the scene isn't added to Build Settings at all.");
                    }
                }
            }

            return CurrentJoinCode;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            OnConnectionFailed?.Invoke("Failed to create session: " + e.Message);
            return null;
        }
    }

    // Capacity (host setting): the online session is created with the largest size and then
    // LOCKED once the lobby holds `capacity` players, so the join code stops working when full
    // and starts working again when someone leaves or the host raises the limit.
    private int capacity = 10;
    private bool lockHooked;

    public void ApplyCapacity(int n)
    {
        capacity = Mathf.Clamp(n, 1, HardMaxPlayers);
        RefreshLock();
    }

    private void HookLock()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || lockHooked) return;
        lockHooked = true;
        nm.OnClientConnectedCallback += _ => RefreshLock();
        nm.OnClientDisconnectCallback += _ => RefreshLock();
    }

    private async void RefreshLock()
    {
        try
        {
            if (currentSession == null || !currentSession.IsHost) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            bool full = nm.ConnectedClientsIds.Count >= capacity;
            var host = currentSession.AsHost();
            if (host.IsLocked == full) return;
            host.IsLocked = full;
            await host.SavePropertiesAsync();
        }
        catch (Exception e) { Debug.LogWarning("[Multiplayer] Couldn't update lobby lock: " + e.Message); }
    }

    // ---------- Client ----------

    public async Task JoinGameAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            OnConnectionFailed?.Invoke("Enter a join code first.");
            return;
        }

        joinCode = joinCode.Trim().ToUpperInvariant();
        IsLeaving = false;

        try
        {
            await EnsureSignedInAsync();
            OnStatusChanged?.Invoke("Joining session...");

            currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
            CurrentJoinCode = joinCode;

            Debug.Log($"[Multiplayer] Joined session {currentSession.Id}. IsListening: {NetworkManager.Singleton.IsListening}, IsClient: {NetworkManager.Singleton.IsClient}");
            OnStatusChanged?.Invoke("Joined! Connecting...");

            // Same as the host side — joining a Relay-backed session already
            // starts NGO's client connection automatically. No manual
            // StartClient() call needed (and calling it here would also throw
            // "Can't start while listening" once already connected).
            //
            // No scene-load call needed here either: NGO's Scene Management
            // automatically syncs a newly-connecting client into whatever
            // scene the server is CURRENTLY in — Lobby if they join before
            // Start Game, or straight into Game if they join mid-match.
        }
        catch (SessionException se)
        {
            // Thrown for bad/expired join codes, full sessions, etc. —
            // SessionException carries a reason worth showing the player.
            Debug.LogException(se);
            OnConnectionFailed?.Invoke("Couldn't join: invalid or expired code.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            OnConnectionFailed?.Invoke("Couldn't join: " + e.Message);
        }
    }

    // ---------- Leaving / cleanup ----------

    // Call when returning to the main menu, so the Relay allocation and
    // UGS session are released properly instead of just abandoning them.
    public async Task LeaveSessionAsync()
    {
        IsLeaving = true;
        try
        {
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
            {
                NetworkManager.Singleton.Shutdown();
            }

            if (currentSession != null)
            {
                await currentSession.LeaveAsync();
                currentSession = null;
            }

            CurrentJoinCode = "";
            OnStatusChanged?.Invoke("Left session.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ---------- Host migration ("keep the group together") ----------
    //
    // When the host drops, the match state is gone (it lived on the host), but the UGS session
    // the players joined still exists and elects a new host by itself. The new host starts a
    // fresh Relay session and posts its join code as a property on the OLD session; everyone
    // else watches for that property and joins the new session, landing in a fresh Lobby.

    private const string NextCodeKey = "maligno.next";

    public bool CanMigrate => currentSession != null && !IsLeaving;

    // Returns true once this player is hosting or has joined the new session.
    public async Task<bool> TryMigrateAsync(Action<string> status)
    {
        var old = currentSession;
        if (old == null) return false;

        try
        {
            var nm = NetworkManager.Singleton;
            float t = 0f;
            while (nm != null && (nm.IsListening || nm.ShutdownInProgress) && t < 5f && !IsLeaving)
            {
                await Task.Delay(200);
                t += 0.2f;
            }

            status?.Invoke("The host left.\nChoosing a new host...");

            float waited = 0f;
            while (waited < 45f && !IsLeaving)
            {
                if (old.IsHost)
                {
                    status?.Invoke("You are the new host.\nSetting up a new lobby...");
                    string code = await HostGameAsync();
                    if (string.IsNullOrEmpty(code)) { currentSession = old; return false; }

                    try
                    {
                        old.AsHost().SetProperty(NextCodeKey, new SessionProperty(code, VisibilityPropertyOptions.Member));
                        await old.AsHost().SavePropertiesAsync();
                    }
                    catch (Exception e) { Debug.LogException(e); }

                    // Give the others a few seconds to read the new code before the old session closes.
                    await Task.Delay(8000);
                    try { await old.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Migrate] " + e.Message); }
                    return true;
                }

                if (old.Properties.TryGetValue(NextCodeKey, out var prop) && !string.IsNullOrEmpty(prop.Value))
                {
                    status?.Invoke("Joining the new lobby...");
                    try { await old.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Migrate] " + e.Message); }
                    currentSession = null;
                    await JoinGameAsync(prop.Value);
                    return currentSession != null;
                }

                await Task.Delay(1000);
                waited += 1f;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        return false;
    }

    private void OnApplicationQuit()
    {
        // Shut the network down in an orderly way first (this runs before
        // NetworkManager's own quit handler thanks to the execution order
        // above). Otherwise stopping Play mode while hosting spams
        // NullReferenceExceptions from NetworkSceneManager.Dispose.
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            try { nm.Shutdown(); }
            catch (Exception e) { Debug.LogWarning("[Net] Shutdown on quit: " + e.Message); }
        }

        // Best-effort cleanup — fire and forget, app is closing anyway.
        if (currentSession != null)
        {
            _ = currentSession.LeaveAsync();
            currentSession = null;
        }
    }
}
