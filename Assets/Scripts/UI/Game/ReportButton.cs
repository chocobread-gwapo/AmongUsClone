using Unity.Netcode;
using UnityEngine;

// Same bridge pattern as UseButton.cs / CallMeetingButton.cs, for the
// on-screen REPORT button.
public class ReportButton : MonoBehaviour
{
    public void OnReportClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        PlayerController controller = localPlayer.GetComponent<PlayerController>();
        controller?.OnReportButtonPressed();
    }
}