using UnityEngine;

// Saved on this computer; sent to the host when the player spawns (see PlayerLabel).
// The username is edited from the ACCOUNT screen on the main menu (see FindGameUI).
public static class PlayerName
{
    private const string Key = "maligno.username";
    public static string Load() { return PlayerLabel.SanitizeName(PlayerPrefs.GetString(Key, "")); }
    public static void Save(string name) { PlayerPrefs.SetString(Key, PlayerLabel.SanitizeName(name)); PlayerPrefs.Save(); }
}
