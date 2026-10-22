using Unity.Netcode;
using UnityEngine;

// Same bridge pattern as UseButton.cs / ReportButton.cs / AbilityButton.cs,
// for a dedicated on-screen KILL button. Unlike USE (which is contextual —
// kill OR interact with a task), this always attempts a kill specifically.
//
// Also owns this button's visibility: hidden by default, shown only once
// PlayerRole confirms (via its owner-only ClientRpc) that THIS client is an
// Impostor. Uses a CanvasGroup rather than SetActive(false) on the whole
// GameObject — if the GameObject itself started inactive, its Awake() (and
// so this singleton's Instance) would never run until something else
// activated it first, which nothing would, since the only thing that shows
// it IS this Instance. CanvasGroup sidesteps that: the object stays active,
// Awake() always runs normally, and alpha/interactable/blocksRaycasts do
// the actual hiding.
[RequireComponent(typeof(CanvasGroup))]
public class KillButton : MonoBehaviour
{
    public static KillButton Instance { get; private set; }

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

    public void OnKillClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        PlayerController controller = localPlayer.GetComponent<PlayerController>();
        controller?.OnKillButtonPressed();
    }
}