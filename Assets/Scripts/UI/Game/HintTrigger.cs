using UnityEngine;
using UnityEngine.EventSystems;

// Added at runtime to the HUD buttons; reports hover to HintUI.
public class HintTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string id;
    public void OnPointerEnter(PointerEventData e) { HintUI.ShowTip(id); }
    public void OnPointerExit(PointerEventData e) { HintUI.HideTip(id); }
    private void OnDisable() { HintUI.HideTip(id); }
}
