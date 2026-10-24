#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Keeps Player Settings -> Version in sync with the VERSION file at the project root.
// Edit the VERSION file (or use Maligno > Set Version...) when you cut a release;
// everything else (play mode label, builds, update check) follows automatically.
[InitializeOnLoad]
public class VersionSync : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "VERSION");

    static VersionSync()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.ExitingEditMode) Sync();
        };
        EditorApplication.delayCall += Sync;
    }

    public void OnPreprocessBuild(BuildReport report) { Sync(); }

    public static string Read()
    {
        try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null; }
        catch { return null; }
    }

    public static void Sync()
    {
        string v = Read();
        if (string.IsNullOrEmpty(v) || !System.Version.TryParse(v, out _)) return;
        if (PlayerSettings.bundleVersion == v) return;
        PlayerSettings.bundleVersion = v;
        AssetDatabase.SaveAssets();
        Debug.Log("[VersionSync] Version set to " + v);
    }

    [MenuItem("Maligno/Set Version...")]
    static void OpenWindow() => EditorWindow.GetWindow<VersionWindow>(true, "Set Version");

    class VersionWindow : EditorWindow
    {
        string value;
        void OnEnable() { value = Read() ?? PlayerSettings.bundleVersion; minSize = maxSize = new Vector2(300, 80); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Version (matches the release tag without 'v')");
            value = EditorGUILayout.TextField(value);
            bool ok = System.Version.TryParse(value, out _);
            using (new EditorGUI.DisabledScope(!ok))
                if (GUILayout.Button("Apply"))
                {
                    File.WriteAllText(FilePath, value.Trim() + "\n");
                    Sync();
                    Close();
                }
        }
    }
}
#endif
