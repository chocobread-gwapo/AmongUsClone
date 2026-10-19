using TMPro;
using UnityEngine;

// Generic label component — attach to any object you want a name/debug tag
// floating above it. Works for Players, Tasks, Dummy, or anything else later.
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private TextMeshPro labelText; // child TMP (3D) object, positioned above the sprite

    public void SetLabel(string text)
    {
        if (labelText != null)
        {
            labelText.text = text;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name} has a WorldLabel but no TextMeshPro assigned.");
        }
    }
}