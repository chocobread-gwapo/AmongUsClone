using Unity.Netcode;
using UnityEngine;

// Controls whether a dead player's sprite is visible, PER VIEWER — not a
// single shared state, since the rule depends on who's looking:
//   - A living player never sees a ghost at all (fully invisible).
//   - A ghost (dead) player CAN see other ghosts, faded the same way bodies
//     always were (30% alpha).
//   - A living player is always fully visible to everyone, regardless of
//     who's looking.
//
// This can't be driven purely by PlayerRole's isAlive NetworkVariable
// callback, because the right answer for THIS player's visibility can
// change the moment the LOCAL viewer dies or revives — not just when this
// player's own alive state changes. So it re-evaluates every frame instead,
// same approach as WorldLabel's vision-based hiding.
public class GhostVisibility : MonoBehaviour
{
    private PlayerRole myRole;
    private SpriteRenderer spriteRenderer;
    private Color aliveRgb = Color.white;
    private bool tinted;

    private static readonly Color GhostTint = new Color(0.65f, 0.85f, 1f);

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) aliveRgb = spriteRenderer.color;
    }

    private void LateUpdate()
    {
        if (myRole == null || spriteRenderer == null) return;

        if (myRole.IsAlive)
        {
            if (tinted)
            {
                tinted = false;
                Color c0 = aliveRgb; c0.a = 1f;
                spriteRenderer.color = c0;
            }
            SetAlpha(1f);
            return;
        }

        bool localViewerIsAlive = true; // default: don't hide prematurely before the local player object exists
        NetworkObject localPlayerObj = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject
            : null;

        if (localPlayerObj != null)
        {
            PlayerRole localRole = localPlayerObj.GetComponent<PlayerRole>();
            if (localRole != null) localViewerIsAlive = localRole.IsAlive;
        }

        if (localViewerIsAlive)
        {
            SetAlpha(0f);
            return;
        }

        // Ghost seen by another ghost: spooky blue tint with a slow flicker.
        tinted = true;
        float a = 0.32f + 0.1f * Mathf.Sin(Time.time * 2.2f + (name.GetHashCode() & 255) * 0.37f);
        Color c = GhostTint; c.a = a;
        spriteRenderer.color = c;
    }

    private void SetAlpha(float alpha)
    {
        if (Mathf.Approximately(spriteRenderer.color.a, alpha)) return; // skip redundant writes every frame
        Color c = spriteRenderer.color;
        c.a = alpha;
        spriteRenderer.color = c;
    }
}