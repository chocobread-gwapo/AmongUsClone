using System.Collections;
using UnityEngine;

// Shows this player's own HealGlow child marker briefly when they're healed
// by Albularyo. Purely local — AlbularyoAbility's targeted ClientRpc calls
// ShowGlow() only on the clients it already decided are close enough to see
// it, same restriction as before, just without a separate spawned object:
// since the marker is a permanent child of the Player prefab, it already
// follows the player for free, no HealGlowFollow/HealGlowUI needed.
public class HealGlowDisplay : MonoBehaviour
{
    [Tooltip("Child visual (e.g. a green glow marker) shown briefly above this player when healed. Starts disabled, same as CurseMarker.")]
    [SerializeField] private GameObject healGlowVisual;

    private Coroutine hideRoutine;

    public void ShowGlow(float duration)
    {
        if (healGlowVisual == null) return;

        if (hideRoutine != null) StopCoroutine(hideRoutine);

        healGlowVisual.SetActive(true);
        healGlowVisual.GetComponent<HealGlowFade>()?.Initialize(duration);
        hideRoutine = StartCoroutine(HideAfter(duration));
    }

    private IEnumerator HideAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        healGlowVisual.SetActive(false);
        hideRoutine = null;
    }
}