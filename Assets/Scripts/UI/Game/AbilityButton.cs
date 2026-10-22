using Unity.Netcode;
using UnityEngine;

// Same bridge pattern as UseButton.cs / ReportButton.cs, for the new
// on-screen ABILITY button (Tikbalang/Aswang/Kapre share this one button —
// each ability component gates itself to its own MalignoType).
//
// Also owns this button's visibility now — same CanvasGroup pattern as
// KillButton.cs (see that file's comment for why CanvasGroup instead of
// SetActive). PlayerRole drives it: shown for any role/class that's
// actually wired through PlayerController.TryUseAbility(), hidden for a
// plain Villager (no class) AND for Babaylan specifically — Babaylan's
// "Judge Skill" is meeting-only and isn't wired through this button at all
// (MeetingUI calls BabaylanAbility.TryJudge() directly via its own Judge
// Mode toggle), so showing this button for them did nothing but confuse.
[RequireComponent(typeof(CanvasGroup))]
public class AbilityButton : MonoBehaviour
{
    public static AbilityButton Instance { get; private set; }

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        Instance = this;
        canvasGroup = GetComponent<CanvasGroup>();
        SetVisible(false); // hidden until PlayerRole says otherwise
    }

    public void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    public void OnAbilityClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        PlayerController controller = localPlayer.GetComponent<PlayerController>();
        controller?.OnAbilityButtonPressed();
    }
}