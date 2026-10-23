using UnityEngine;
using UnityEngine.UI;

// Drives AudioManager's volume from the MainMenu's Settings panel slider.
[RequireComponent(typeof(Slider))]
public class MenuVolumeSlider : MonoBehaviour
{
    private void Start()
    {
        Slider slider = GetComponent<Slider>();
        if (AudioManager.Instance != null) slider.SetValueWithoutNotify(AudioManager.Instance.GetVolume());
        slider.onValueChanged.AddListener(value => AudioManager.Instance?.SetVolume(value));
    }
}
