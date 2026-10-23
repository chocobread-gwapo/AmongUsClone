#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// One-shot project tidy-up. Moves go through AssetDatabase, so every GUID (and
// every scene / prefab reference to it) is preserved.
//   Maligno/Cleanup/Report Unused Assets  -> logs what nothing in the build uses
//   Maligno/Cleanup/Apply Cleanup         -> deletes leftovers + sorts into folders
public static class ProjectCleanup
{
    static readonly string[] Delete =
    {
        "Assets/Scenes/Game_Old.unity",
        "Assets/Prefabs/TaskListEntry.prefab",
        "Assets/Prefabs/VoteButtonPrefab.prefab",
        "Assets/Scripts/StartGameButtonUI.cs",
        "Assets/Scripts/SettingsButtonUI.cs",
        "Assets/Scripts/TaskCounterUI.cs",
        "Assets/Scripts/ButtonHoverScale.cs",
        "Assets/Prefabs/Abilities/HealGlow.prefab",
        "Assets/Scripts/UI/Game/ReturnToLobbyButton.cs",
        "Assets/Welcome",
    };

    // destination folder -> file names (relative to the old location)
    static readonly (string folder, string[] files)[] Moves =
    {
        ("Assets/Scripts/Abilities/Maligno", new[] { "Assets/Scripts/DuwendeAbility.cs", "Assets/Scripts/TikbalangAbility.cs", "Assets/Scripts/BalBalAbility.cs", "Assets/Scripts/AswangAbility.cs", "Assets/Scripts/KapreAbility.cs" }),
        ("Assets/Scripts/Abilities/Survivor", new[] { "Assets/Scripts/ThirdEyeAbility.cs", "Assets/Scripts/ReikiAbility.cs", "Assets/Scripts/AlbularyoAbility.cs", "Assets/Scripts/BabaylanAbility.cs", "Assets/Scripts/TarotUserAbility.cs" }),
        ("Assets/Scripts/Abilities/Effects", new[] { "Assets/Scripts/NunoMoundTrap.cs", "Assets/Scripts/CurseStatus.cs", "Assets/Scripts/CursePulse.cs", "Assets/Scripts/FootprintTracker.cs", "Assets/Scripts/ResidueFade.cs", "Assets/Scripts/AuraFade.cs", "Assets/Scripts/HealGlowDisplay.cs", "Assets/Scripts/HealGlowFade.cs", "Assets/Scripts/TarotBuffFloatText.cs" }),
        ("Assets/Scripts/Player", new[] { "Assets/Scripts/PlayerController.cs", "Assets/Scripts/PlayerRole.cs", "Assets/Scripts/PlayerVision.cs", "Assets/Scripts/PlayerLabel.cs", "Assets/Scripts/CharacterSpriteAnimator.cs", "Assets/Scripts/CameraFollow.cs", "Assets/Scripts/GhostVisibility.cs", "Assets/Scripts/YSortedSprite.cs", "Assets/Scripts/TargetOutline.cs", "Assets/Scripts/Killable.cs", "Assets/Scripts/DeadBody.cs", "Assets/Scripts/Direction4.cs", "Assets/Scripts/WorldLabel.cs" }),
        ("Assets/Scripts/Data", new[] { "Assets/Scripts/MalignoType.cs", "Assets/Scripts/SurvivorClass.cs" }),
        ("Assets/Scripts/Core", new[] { "Assets/Scripts/MultiplayerConnectionManager.cs", "Assets/Scripts/GameStartManager.cs", "Assets/Scripts/GameResultManager.cs", "Assets/Scripts/MeetingManager.cs", "Assets/Scripts/TaskManager.cs", "Assets/Scripts/TaskZone.cs", "Assets/Scripts/SceneSpawnPoints.cs", "Assets/Scripts/AudioManager.cs" }),
        ("Assets/Scripts/UI/Menus", new[] { "Assets/Scripts/MainMenuUI.cs", "Assets/Scripts/MenuVolumeSlider.cs", "Assets/Scripts/LobbyConnectionUI.cs" }),
        ("Assets/Scripts/UI/Lobby", new[] { "Assets/Scripts/LobbyUI.cs", "Assets/Scripts/HostSettingsPanelUI.cs", "Assets/Scripts/StartGameButton.cs" }),
        ("Assets/Scripts/UI/Game", new[] { "Assets/Scripts/GameHudUI.cs", "Assets/Scripts/AbilityButton.cs", "Assets/Scripts/KillButton.cs", "Assets/Scripts/UseButton.cs", "Assets/Scripts/ReportButton.cs", "Assets/Scripts/CallMeetingButton.cs", "Assets/Scripts/AbilityCooldownUI.cs", "Assets/Scripts/TaskListUI.cs", "Assets/Scripts/TaskProgressBarUI.cs", "Assets/Scripts/MeetingUI.cs", "Assets/Scripts/RoleRevealUI.cs", "Assets/Scripts/GameOverUI.cs", "Assets/Scripts/ReikiRevealUI.cs", "Assets/Scripts/ThirdEyeRevealUI.cs", "Assets/Scripts/TarotCardPopupUI.cs" }),
        ("Assets/Input", new[] { "Assets/Scripts/PlayerControls.inputactions" }),
        ("Assets/Prefabs/Player", new[] { "Assets/Prefabs/Player.prefab", "Assets/Prefabs/DeadBody.prefab" }),
        ("Assets/Prefabs/Abilities", new[] { "Assets/Prefabs/AuraMarker.prefab", "Assets/Prefabs/Banana Trunk.prefab", "Assets/Prefabs/CurseMarker.prefab", "Assets/Prefabs/FootprintResidue.prefab", "Assets/Prefabs/Nuno Mound Trap.prefab", "Assets/Prefabs/Smoke Cloud.prefab" }),
        ("Assets/Prefabs/Map", new[] { "Assets/Prefabs/MapTemplate.prefab" }),
        ("Assets/Prefabs/Network", new[] { "Assets/Prefabs/GameStartManager.prefab" }),
        ("Assets/Sprites/Map", new[] { "Assets/Sprites/MapTemplate.png" }),
        ("Assets/Sprites/UI", new[] { "Assets/Sprites/GlowStreak.png", "Assets/Sprites/Vignette.png", "Assets/Sprites/Square.png" }),
        ("Assets/Sprites/Tarot", new[] { "Assets/Sprites/TarotCardBackDesign.jpg", "Assets/Sprites/TheFool.png", "Assets/Sprites/TheStar.png" }),
        ("Assets/Sprites/Characters", new[] { "Assets/Sprites/CharacterTemplate.png", "Assets/Sprites/DeadBody.png" }),
    };

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    [MenuItem("Maligno/Cleanup/Report Unused Assets")]
    public static void Report()
    {
        var roots = new List<string>();
        foreach (var s in EditorBuildSettings.scenes) roots.Add(s.path);
        roots.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.Contains("Player") || p.Contains("Network")));
        var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));

        var unused = AssetDatabase.FindAssets("", new[] { "Assets/Prefabs", "Assets/Sprites", "Assets/Shaders", "Assets/Audio", "Assets/Scripts", "Assets/Input" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !AssetDatabase.IsValidFolder(p) && !used.Contains(p))
            .OrderBy(p => p);
        Debug.Log("[Cleanup] Assets nothing in the build depends on (scripts may still be used from code):\n" + string.Join("\n", unused));
    }

    [MenuItem("Maligno/Cleanup/Apply Cleanup")]
    public static void Apply()
    {
        int deleted = 0, moved = 0, failed = 0;
        foreach (var p in Delete)
        {
            if (AssetDatabase.DeleteAsset(p)) deleted++;
            else Debug.LogWarning("[Cleanup] Couldn't delete (already gone?): " + p);
        }
        foreach (var (folder, files) in Moves)
        {
            EnsureFolder(folder);
            foreach (var f in files)
            {
                if (!System.IO.File.Exists(f) && !AssetDatabase.IsValidFolder(f)) { Debug.LogWarning("[Cleanup] Missing, skipped: " + f); continue; }
                string err = AssetDatabase.MoveAsset(f, folder + "/" + System.IO.Path.GetFileName(f));
                if (string.IsNullOrEmpty(err)) moved++;
                else { failed++; Debug.LogError($"[Cleanup] Move failed for {f}: {err}"); }
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Cleanup] Done: {deleted} deleted, {moved} moved, {failed} failed.");
    }
}
#endif
