using Unity.Netcode;
using UnityEngine;

// Hides the in-match HUD (task panel, USE/REPORT/ABILITY/Call Meeting
// buttons, kill cooldown badge, etc.) until a match actually starts, and
// hides it again after Return to Lobby. Same show/hide pattern as
// StartGameButtonUI — GameStartManager drives both from the same two events.
//
// Also continuously hides the buttons a dead player can't use (Call Meeting,
// Report, Ability) — the server already rejects these for ghosts, this is
// just the matching visual so a dead player isn't staring at buttons that
// silently do nothing. USE stays visible/untouched since ghosts can still
// complete tasks.
public class GameHudUI : MonoBehaviour
{
    public static GameHudUI Instance { get; private set; }

    [Tooltip("Every HUD element that should only be visible DURING a match — not in the lobby, not after returning to it.")]
    [SerializeField] private GameObject[] hudElements;

    [Tooltip("Buttons a dead player can't use — Call Meeting, Report, Ability. Hidden whenever the LOCAL player is dead, shown again on revival (ServerResetForNewGame). Don't include USE here; ghosts can still do tasks.")]
    [SerializeField] private GameObject[] deadRestrictedButtons;

    private bool? lastKnownAliveState; // null = not yet evaluated

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // The Game scene only exists during a match (the lobby is its own
        // scene now), so the HUD is simply visible from the start.
        SetVisible(true);
    }

    private void Update()
    {
        // Only bother checking while the HUD is actually showing (i.e. during
        // a match) — cheap no-op otherwise.
        if (hudElements == null || hudElements.Length == 0 || !hudElements[0].activeSelf) return;

        NetworkObject localPlayerObj = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject
            : null;

        // Fail-open: if we can't determine alive state yet, don't hide anything.
        bool isAlive = true;
        if (localPlayerObj != null)
        {
            PlayerRole role = localPlayerObj.GetComponent<PlayerRole>();
            if (role != null) isAlive = role.IsAlive;
        }

        if (lastKnownAliveState == isAlive) return; // no change, skip redundant SetActive calls
        lastKnownAliveState = isAlive;

        if (deadRestrictedButtons != null)
        {
            foreach (var obj in deadRestrictedButtons)
            {
                if (obj != null) obj.SetActive(isAlive);
            }
        }
    }

    public void Show() => SetVisible(true);
    public void Hide() => SetVisible(false);

    private void SetVisible(bool visible)
    {
        if (hudElements == null) return;
        foreach (var obj in hudElements)
        {
            if (obj != null) obj.SetActive(visible);
        }

        if (visible)
        {
            // Force a fresh evaluation next Update() when the HUD reappears
            // (e.g. a new match after someone died in the last one).
            lastKnownAliveState = null;
        }
    }
}
