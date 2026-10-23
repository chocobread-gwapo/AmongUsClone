#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Play mode only: pops the end-of-match screen with fake data so the layout
// can be checked without finishing a real match.
public static class MalignoGameOverPreview
{
    [MenuItem("Maligno/21. Preview Game Over - Villagers Win (Play mode)")]
    static void PreviewWin() { Run(true); }

    [MenuItem("Maligno/22. Preview Game Over - Maligno Win (Play mode)")]
    static void PreviewLose() { Run(false); }

    static void Run(bool crewWon)
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
        if (GameOverUI.Instance == null) { Debug.LogWarning("No GameOverUI in the scene."); return; }
        GameOverUI.Instance.ShowResult(
            crewWon,
            crewWon ? "TAGUMPAY ANG BARYO" : "NAGHARI ANG DILIM",
            crewWon ? "The villagers finished every chore before the Maligno could stop them."
                    : "The Maligno now equal or outnumber the living villagers.",
            new ulong[] { 0, 1, 2, 3, 4 },
            new int[] { 0, 1, 0, 0, 1 },
            new int[] { 0, (int)MalignoType.Aswang, 0, 0, (int)MalignoType.Duwende },
            new int[] { (int)SurvivorClass.TarotUser, 0, (int)SurvivorClass.Babaylan, 0, 0 },
            new bool[] { true, false, true, false, true },
            crewWon ? 12 : 7, 12);
    }
}
#endif
