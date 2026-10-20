using Unity.Netcode;
using UnityEngine;

// Same bridge pattern as UseButton.cs / ReportButton.cs, for the new
// on-screen ABILITY button (Tikbalang/Aswang/Kapre share this one button —
// each ability component gates itself to its own MalignoType).
public class AbilityButton : MonoBehaviour
{
    public void OnAbilityClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        PlayerController controller = localPlayer.GetComponent<PlayerController>();
        controller?.OnAbilityButtonPressed();
    }
}