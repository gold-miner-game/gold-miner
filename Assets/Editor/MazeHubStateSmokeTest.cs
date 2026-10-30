using System;
using UnityEditor;
using UnityEngine;

// Batch: Unity -batchmode -quit -projectPath ... -executeMethod MazeHubStateSmokeTest.Run
// Editor: Tools > Gold Miner > Smoke Test MazeHubState
public static class MazeHubStateSmokeTest
{
    [MenuItem("Tools/Gold Miner/Smoke Test MazeHubState")]
    public static void RunFromMenu()
    {
        int code = RunCore();
        EditorUtility.DisplayDialog(
            "MazeHubState smoke test",
            code == 0 ? "PASS — eaten minerals survive prefs reload." : "FAIL — see Console.",
            "OK");
    }

    public static void Run()
    {
        EditorApplication.Exit(RunCore());
    }

    static int RunCore()
    {
        int failures = 0;
        try
        {
            MazeHubState.Clear();

            // Simulate pig eating G1/G2 before entering Level 1 (the old Capture wipe bug).
            MazeHubState.MarkTreasureEaten("G1");
            MazeHubState.MarkTreasureEaten("G2");
            MazeHubState.MarkTreasureEaten("D2");
            MazeHubState.MazeBank = 800;
            MazeHubState.Lives = 5;
            MazeHubState.PlayerPos = new Vector2(1.25f, -2.5f);
            MazeHubState.Persist();
            string snap = MazeHubState.SerializeForTest();
            if (!snap.Contains("G1") || !snap.Contains("G2") || !snap.Contains("D2"))
            {
                Fail(ref failures, "serialize missing eaten ids: " + snap);
            }

            // Simulate domain-reload / new scene: wipe statics, reload prefs.
            MazeHubState.Clear();
            // Clear() deletes prefs — restore from snap to mimic PlayerPrefs surviving Clear of statics only.
            PlayerPrefs.SetString("gm.mazeHub.v1", snap);
            PlayerPrefs.Save();
            MazeHubState.ForceReloadFromPrefs();

            if (!MazeHubState.HasSave) Fail(ref failures, "HasSave false after reload");
            if (!MazeHubState.TreasureWasEaten("G1")) Fail(ref failures, "G1 not eaten after reload");
            if (!MazeHubState.TreasureWasEaten("G2")) Fail(ref failures, "G2 not eaten after reload");
            if (!MazeHubState.TreasureWasEaten("D2")) Fail(ref failures, "D2 not eaten after reload");
            if (MazeHubState.TreasureWasEaten("G3")) Fail(ref failures, "G3 should not be eaten");
            if (MazeHubState.MazeBank != 800) Fail(ref failures, "bank=" + MazeHubState.MazeBank);

            // Old bug: Capture rebuilt from empty TreasureById and wiped eaten.
            // New rule: MarkTreasuresEaten merges; clearing then re-adding only missing must keep G1.
            var keep = new System.Collections.Generic.HashSet<string>(MazeHubState.EatenTreasureIds);
            MazeHubState.MarkTreasuresEaten(new[] { "G1", "G2", "D2" }); // no-op adds
            if (MazeHubState.EatenTreasureIds.Count != keep.Count)
                Fail(ref failures, "merge changed count unexpectedly");

            // Round-trip after "capture" that must NOT clear eaten.
            string after = MazeHubState.SerializeForTest();
            MazeHubState.Clear();
            if (!MazeHubState.TryParseForTest(after))
                Fail(ref failures, "TryParseForTest failed on: " + after);
            if (!MazeHubState.TreasureWasEaten("G1") || !MazeHubState.TreasureWasEaten("D2"))
                Fail(ref failures, "eaten lost after capture-style roundtrip");

            MazeHubState.Clear();
        }
        catch (Exception e)
        {
            Debug.LogError("MazeHubStateSmokeTest EXCEPTION: " + e);
            failures++;
        }

        if (failures == 0) Debug.Log("MazeHubStateSmokeTest: PASS");
        else Debug.LogError("MazeHubStateSmokeTest: FAIL (" + failures + ")");
        return failures == 0 ? 0 : 1;
    }

    static void Fail(ref int failures, string msg)
    {
        failures++;
        Debug.LogError("MazeHubStateSmokeTest: " + msg);
    }
}
