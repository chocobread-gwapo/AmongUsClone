using Unity.Netcode;
using UnityEngine;

// Bridge for the on-screen USE button — same reason CallMeetingButton.cs
// exists: a UI Button's OnClick can't target a ServerRpc or a method on a
// runtime-spawned player object directly, so this finds the LOCAL player
// each time it's clicked and forwards the call.
public class UseButton : MonoBehaviour
{
    public void OnUseClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        PlayerController controller = localPlayer.GetComponent<PlayerController>();
        controller?.OnUseButtonPressed();
    }
}