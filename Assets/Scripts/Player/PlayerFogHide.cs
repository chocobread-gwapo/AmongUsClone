using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Other players are only drawn while they are inside YOUR vision radius. Beyond it they fade out
// and their renderers are switched off, so a dark map can't leak who is where. Entering the radius
// fades them back in. Added at runtime to every player that is not the local one; the nametag hides
// itself separately (WorldLabel), so TextMeshPro renderers are left alone here.
//
// Runs AFTER GhostVisibility (which sets the main sprite's alpha every frame) so the fade is a
// multiplier on top of whatever alpha that script wants.
[DefaultExecutionOrder(1000)]
public class PlayerFogHide : MonoBehaviour
{
    [Tooltip("Extra distance beyond the vision radius before a player disappears, so edges don't flicker.")]
    [SerializeField] private float buffer = 0.25f;

    [Tooltip("Seconds to fade in when entering vision.")]
    [SerializeField] private float fadeInTime = 0.3f;

    [Tooltip("Seconds to fade out when leaving vision.")]
    [SerializeField] private float fadeOutTime = 0.5f;

    private Renderer[] renderers;
    private readonly HashSet<Renderer> hiddenByMe = new HashSet<Renderer>();
    private SpriteRenderer mainSprite;
    private readonly Dictionary<SpriteRenderer, float> baseAlpha = new Dictionary<SpriteRenderer, float>();
    private float nextRefresh;
    private bool wantShown = true;
    private float fade = 1f;

    private void Awake()
    {
        mainSprite = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        if (Time.time >= nextRefresh || renderers == null)
        {
            nextRefresh = Time.time + 1f; // pick up children added later (curse marker, props)
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        wantShown = ShouldShow();
        float target = wantShown ? 1f : 0f;
        float time = wantShown ? fadeInTime : fadeOutTime;
        float step = time <= 0.001f ? 1f : Time.deltaTime / time;
        fade = Mathf.MoveTowards(fade, target, step);

        bool visible = fade > 0.01f;
        foreach (var r in renderers)
        {
            if (r == null || r.GetComponent<TMP_Text>() != null) continue;
            // TargetOutline manages its own copies (they are off unless a target is highlighted).
            if (r.gameObject.name.StartsWith("OutlineGhost")) continue;
            // PlayerLookRenderer mirrors the main sprite (fade included) for the clothing layers.
            if (r.gameObject.name.StartsWith(PlayerLookRenderer.LayerPrefix)) continue;

            if (!visible)
            {
                if (r.enabled) { r.enabled = false; hiddenByMe.Add(r); }
            }
            else if (hiddenByMe.Remove(r))
            {
                r.enabled = true; // only re-enable what we switched off ourselves
            }

            var sr = r as SpriteRenderer;
            if (sr == null) continue;

            if (sr == mainSprite)
            {
                // GhostVisibility already wrote this frame's alpha; scale it.
                if (fade < 1f)
                {
                    Color c = sr.color;
                    c.a *= fade;
                    sr.color = c;
                }
            }
            else
            {
                if (!baseAlpha.ContainsKey(sr)) baseAlpha[sr] = sr.color.a;
                if (fade < 1f || !Mathf.Approximately(sr.color.a, baseAlpha[sr]))
                {
                    Color c = sr.color;
                    c.a = baseAlpha[sr] * fade;
                    sr.color = c;
                }
            }
        }
    }

    private bool ShouldShow()
    {
        // Lobby (no match yet) and before the local player exists: show everybody.
        var game = GameStartManager.Instance;
        if (game == null || !game.GameStarted) return true;
        var local = PlayerVision.LocalInstance;
        if (local == null) return true;

        float reach = local.CurrentVisionRadius + buffer + (wantShown ? 0.15f : 0f); // small hysteresis
        return Vector2.Distance(transform.position, local.transform.position) <= reach;
    }
}
