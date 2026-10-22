using UnityEditor;
using UnityEngine;

public static class MalignoPerfMenu
{
    [MenuItem("Maligno/24. Profile Performance (Play mode)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Perf] Enter Play mode first (ideally inside a match)."); return; }
        PerfProbe.Run(10f);
    }
}
