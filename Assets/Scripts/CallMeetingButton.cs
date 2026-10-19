using UnityEngine;

// Unity's Button OnClick (Inspector) can only call parameterless methods
// (or ones with a single primitive param). RequestCallMeetingServerRpc takes
// a ServerRpcParams parameter under the hood, so it can't be wired directly —
// this wrapper gives the Button a plain, no-argument method to call instead.
public class CallMeetingButton : MonoBehaviour
{
    public void OnCallMeetingClicked()
    {
        if (MeetingManager.Instance != null)
        {
            MeetingManager.Instance.RequestCallMeetingServerRpc();
        }
        else
        {
            Debug.LogWarning("MeetingManager.Instance is null — is it in the scene with a NetworkObject?");
        }
    }
}