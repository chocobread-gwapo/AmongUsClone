using Unity.Netcode;
using UnityEngine;

// Separate from PlayerController deliberately — PlayerController disables
// itself for non-owned player instances, but every client needs to see
// EVERY player's label correctly, not just their own. OnNetworkSpawn runs
// for all players on all clients regardless of ownership, so this works
// correctly without any ownership checks.
public class PlayerLabel : NetworkBehaviour
{
    // Placeholder palette until a real cosmetic-color system exists (per the
    // PRD's lobby customizer, not built yet). Deterministic per client, so
    // everyone always sees the same color name for the same player.
    private static readonly (string Name, Color Color)[] Palette =
    {
        ("Red", new Color(0.78f, 0.12f, 0.12f)),
        ("Blue", new Color(0.13f, 0.35f, 0.85f)),
        ("Green", new Color(0.16f, 0.6f, 0.2f)),
        ("Pink", new Color(0.93f, 0.4f, 0.75f)),
        ("Orange", new Color(0.95f, 0.55f, 0.1f)),
        ("Yellow", new Color(0.95f, 0.85f, 0.15f)),
        ("Purple", new Color(0.5f, 0.2f, 0.75f)),
        ("Cyan", new Color(0.2f, 0.8f, 0.85f)),
    };

    // Same name every client shows above a player's head, for UI lists/messages.
    public static string DisplayName(ulong ownerClientId)
    {
        var (colorName, _) = Palette[ownerClientId % (ulong)Palette.Length];
        return $"Player {ownerClientId + 1} ({colorName})";
    }

    public static Color ColorFor(ulong ownerClientId)
    {
        return Palette[ownerClientId % (ulong)Palette.Length].Color;
    }

    // Two-line rich-text nametag; shared so ability disguises can restore/copy it.
    public static string RichLabel(ulong ownerClientId)
    {
        var (colorName, color) = Palette[ownerClientId % (ulong)Palette.Length];
        string hex = ColorUtility.ToHtmlStringRGB(color);
        return $"<b>Player {ownerClientId + 1}</b>\n<color=#{hex}>{colorName}</color>";
    }

    public override void OnNetworkSpawn()
    {
        WorldLabel label = GetComponent<WorldLabel>();
        if (label != null)
        {
            // OwnerClientId is visible to everyone for a spawned object (not secret),
            // so this produces the same label on every client's screen.
            // Host is typically client 0, so "Player 1" = host, matching your request.
            // Two-line rich-text label: bold name on top, colored "team color"
            // name below — matches the reference HUD's nametag style.
            label.SetLabel(RichLabel(OwnerClientId));
        }
    }
}