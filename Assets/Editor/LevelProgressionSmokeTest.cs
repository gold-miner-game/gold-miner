using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Batch: Unity -batchmode -projectPath <isolated project> -executeMethod LevelProgressionSmokeTest.Run
public static class LevelProgressionSmokeTest
{
    static readonly string[] Keys = { "gm.progress.completed", "gm.progress.bestMargins", "gm.progress.revealed" };

    public static void Run()
    {
        bool[] existed = Keys.Select(PlayerPrefs.HasKey).ToArray();
        string[] saved = Keys.Select(key => PlayerPrefs.GetString(key, "")).ToArray();
        int result = 0;
        try
        {
            WorldMap map = Resources.Load<WorldMap>("WorldMap");
            Require(map != null, "WorldMap exists");
            MapNode tutorial = map.Find("battle_03"), first = map.Find("battle_06"), second = map.Find("battle_02"), third = map.Find("battle_04");
            Require(tutorial.type == NodeType.Tutorial && tutorial.displayName == "Tutorial", "old Level 1 is Tutorial");
            Require(first.displayName == "Level 1" && second.displayName == "Level 2" && third.displayName == "Level 3", "levels renumbered");
            Require(map.nodes.Count(node => node.type == NodeType.Battle) == 3, "three regular levels");

            SetState("");
            Require(!map.IsUnlocked(first), "new Level 1 locked before tutorial");
            ProgressStore.MarkCompleted(ProgressStore.TutorialDoneId);
            Require(map.IsUnlocked(first), "finishing tutorial unlocks new Level 1");
            Require(map.IsUnlocked(map.Find("role_pick")), "tutorial unlocks character selection");
            Require(!map.IsUnlocked(second) && !map.IsUnlocked(third), "later levels remain locked");
            Require(!ProgressStore.WasRevealed(first.id), "new Level 1 remains eligible for reveal animation");
            ProgressStore.RecordBestMargin(first.id, 999999);
            Require(!map.IsUnlocked(second), "high earnings alone do not unlock the next level");
            SetState(ProgressStore.TutorialDoneId);

            ProgressStore.MarkCompleted(first.id);
            Require(ProgressStore.GetBestMargin(first.id) == 0, "no earnings needed for progression");
            Require(map.IsUnlocked(second), "winning Level 1 immediately unlocks Level 2");
            Require(!ProgressStore.WasRevealed(second.id), "Level 2 remains eligible for reveal animation");
            Require(!map.IsUnlocked(map.Find("store_1")), "store stays locked before Level 2 win");
            ProgressStore.MarkCompleted(second.id);
            Require(ProgressStore.GetBestMargin(second.id) == 0, "Level 2 needs no earnings to reveal Level 3");
            Require(map.IsUnlocked(map.Find("store_1")), "Level 2 win opens the store");
            Require(!map.IsUnlocked(third), "Level 3 stays sealed before leaving the store");
            ProgressStore.MarkCompleted("store_1"); // StorePlaceholder.Leave completes this node.
            Require(map.IsUnlocked(third), "leaving the store unlocks Level 3 without a purchase");
            Require(!ProgressStore.WasRevealed(third.id), "Level 3 is eligible for its unseal animation");

            SetState("tut_block");
            Require(map.IsUnlocked(first), "legacy tutorial saves retain unlock");
            SetState("battle_03");
            Require(map.IsUnlocked(first), "old Level 1 completion retains unlock");

            var load = typeof(LevelLoader).GetMethod("LoadFromResources", BindingFlags.Static | BindingFlags.NonPublic);
            var ordered = (TextAsset[])load.Invoke(null, null);
            Require(ordered.Take(4).Select(asset => asset.name).SequenceEqual(new[] { "level_03", "level_01", "level_02", "level_04" }), "tutorial then regular level order");
            Debug.Log("LEVEL_PROGRESSION_PASS: Tutorial -> Level 1 -> Level 2 -> Store exit -> Level 3; reveal and legacy saves verified.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            result = 1;
        }
        finally
        {
            for (int i = 0; i < Keys.Length; i++)
                if (existed[i]) PlayerPrefs.SetString(Keys[i], saved[i]); else PlayerPrefs.DeleteKey(Keys[i]);
            PlayerPrefs.Save();
            ResetCaches();
        }
        EditorApplication.Exit(result);
    }

    static void SetState(string completed)
    {
        PlayerPrefs.SetString(Keys[0], completed);
        PlayerPrefs.SetString(Keys[1], "");
        PlayerPrefs.SetString(Keys[2], "");
        ResetCaches();
    }

    static void ResetCaches()
    {
        foreach (string field in new[] { "completed", "bestMargins" })
            typeof(ProgressStore).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
    }

    static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Level progression: " + description);
    }
}
