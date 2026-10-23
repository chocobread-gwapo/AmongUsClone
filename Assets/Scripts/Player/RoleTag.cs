using TMPro;
using Unity.Netcode;
using UnityEngine;

// Extra line under YOUR OWN nametag ("PLAYER 1 / Blue") that says what you are this match:
// a Maligno type, a Survivor class, or plain VILLAGER / MALIGNO. Only the local owner ever
// builds it, so nobody else's role is leaked. It is a clone of the WorldLabel text so it
// shares the font, fog-of-war hiding and sorting.
public class RoleTag : MonoBehaviour
{
    private WorldLabel label;
    private PlayerRole role;
    private TextMeshPro tag;
    private string lastText;

    private void Awake()
    {
        label = GetComponent<WorldLabel>();
        role = GetComponent<PlayerRole>();
    }

    private void OnDestroy()
    {
        if (tag != null) Destroy(tag.gameObject);
    }

    private static string MalignoName(MalignoType t)
    {
        switch (t)
        {
            case MalignoType.Duwende: return "DUWENDE";
            case MalignoType.Tikbalang: return "TIKBALANG";
            case MalignoType.BalBal: return "BAL BAL";
            case MalignoType.Aswang: return "ASWANG";
            case MalignoType.Kapre: return "KAPRE";
            default: return "MALIGNO";
        }
    }

    private static string SurvivorName(SurvivorClass c)
    {
        switch (c)
        {
            case SurvivorClass.ThirdEyeUser: return "THIRD EYE";
            case SurvivorClass.ReikiPractitioner: return "REIKI PRACTITIONER";
            case SurvivorClass.Albularyo: return "ALBULARYO";
            case SurvivorClass.Babaylan: return "BABAYLAN";
            case SurvivorClass.TarotUser: return "TAROT USER";
            default: return "VILLAGER";
        }
    }

    private string BuildText()
    {
        if (role == null || GameStartManager.Instance == null || !GameStartManager.Instance.GameStarted) return "";
        return role.IsImpostor
            ? $"<color=#E85050>{MalignoName(role.CurrentMalignoType)}</color>"
            : $"<color=#2AD4BC>{SurvivorName(role.CurrentSurvivorClass)}</color>";
    }

    private void LateUpdate()
    {
        TextMeshPro baseText = label != null ? label.Text : null;
        if (baseText == null) return;

        if (tag == null)
        {
            tag = Instantiate(baseText, baseText.transform.parent);
            tag.name = "RoleTag";
            tag.fontSize = baseText.fontSize * 0.8f;
            tag.richText = true;
        }

        string text = BuildText();
        if (text != lastText) { tag.text = text; lastText = text; }

        // Same visibility as the nametag above it (fog of war, ghosts) and hidden when empty.
        tag.enabled = baseText.enabled && text.Length > 0;
        if (!tag.enabled) return;

        // Hang the tag just below the existing label, whatever its height is.
        baseText.ForceMeshUpdate();
        tag.ForceMeshUpdate();
        tag.transform.position = baseText.transform.position;
        float shift = (baseText.bounds.min.y - 0.03f) - tag.bounds.max.y;
        tag.transform.position += new Vector3(0f, shift, 0f);
    }
}
