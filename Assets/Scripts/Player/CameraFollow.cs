using Unity.Netcode;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    private Transform target;

    // Still callable by PlayerController, but no longer required: the local
    // player object survives scene loads (Lobby -> Game), so a camera that
    // only got its target once at spawn would be left without one in the
    // next scene. This camera now finds the local player on its own.
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            var nm = NetworkManager.Singleton;
            var player = nm != null && nm.IsClient && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (player == null) return;

            target = player.transform;
            // Snap on first acquisition so the view doesn't sweep across the map.
            transform.position = target.position + offset;
            return;
        }

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
