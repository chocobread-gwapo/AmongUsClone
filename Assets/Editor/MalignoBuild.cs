#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// One-click Windows build so friends (or a second window on your own PC) can
// play. Output: <project>/Builds/Maligno/Maligno.exe
public static class MalignoBuild
{
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Game.unity",
    };

    [MenuItem("Maligno/20. Build Windows Player")]
    public static void BuildWindows()
    {
        EditorBuildSettings.scenes = System.Array.ConvertAll(Scenes, s => new EditorBuildSettingsScene(s, true));

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Maligno"));
        Directory.CreateDirectory(dir);

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = Path.Combine(dir, "Maligno.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary s = report.summary;
        if (s.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] Succeeded: {opts.locationPathName} ({s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0}s)");
            EditorUtility.RevealInFinder(opts.locationPathName);
        }
        else
        {
            Debug.LogError($"[Build] {s.result}: {s.totalErrors} error(s). See the Console.");
        }
    }
}
#endif
