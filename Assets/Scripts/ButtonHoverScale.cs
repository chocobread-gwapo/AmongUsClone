using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Lightweight hover-grow feedback for UI buttons — no Animator Controller
// needed. Attach this to any button (same GameObject as the Button
// component). On hover it smoothly scales up; on exit it scales back down.
//
// The "dims briefly when clicked" half of the request doesn't need this
// script at all — it's Unity's own Button Transition (Color Tint) feature:
// set the button's "Pressed Color" to a darker shade and a short Fade
// Duration (e.g. 0.05–0.1s), and Unity handles the dim-then-return
// automatically. See the setup notes below.
[RequireComponent(typeof(RectTransform))]
public class ButtonHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Scale multiplier while hovered — 1.1 = 10% bigger.")]
    [SerializeField] private float hoverScale = 1.1f;

    [Tooltip("How long the grow/shrink transition takes, in seconds.")]
    [SerializeField] private float duration = 0.1f;

    private Vector3 baseScale;
    private Coroutine activeTween;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        StartTween(baseScale * hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        StartTween(baseScale);
    }

    private void OnDisable()
    {
        // Snap back instantly if the button gets hidden mid-hover (e.g. the
        // Host Settings panel closing right as the mouse was over a button)
        // — otherwise it could reappear later still stuck at hover size.
        if (activeTween != null) StopCoroutine(activeTween);
        transform.localScale = baseScale;
    }

    private void StartTween(Vector3 target)
    {
        if (activeTween != null) StopCoroutine(activeTween);
        activeTween = StartCoroutine(ScaleTo(target));
    }

    private IEnumerator ScaleTo(Vector3 target)
    {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < duration)
        {
            // Unscaled so it still animates correctly even if Time.timeScale
            // is ever changed (e.g. a future pause-on-meeting feature).
            t += Time.unscaledDeltaTime;
            transform.localScale = Vector3.Lerp(start, target, duration > 0f ? t / duration : 1f);
            yield return null;
        }
        transform.localScale = target;
    }
}