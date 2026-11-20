using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exercises the actual console completion/execution and real Elsa/level_04 scene.
public static class ConsoleGoodsPlayTest
{
    const string Pending = "gm.tests.consoleGoods";
    static readonly string[] Goods = { "swift", "ration", "shield", "brace", "time_machine" };
    static IEnumerator test;
    static int checks, frame = -1;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Call(object instance, string method, params object[] args)
        => instance.GetType().GetMethod(method, Hidden).Invoke(instance, args);
    static void Command(string id)
    {
        var console = UnityEngine.Object.FindFirstObjectByType<DeveloperConsole>();
        string title = id == "brace" ? "Life" : id == "time_machine" ? "Time_Machine"
            : char.ToUpperInvariant(id[0]) + id.Substring(1);
        Call(console, "SetOpen", true);
        typeof(DeveloperConsole).GetField("command", Hidden).SetValue(console, "good " + title.Substring(0, 2));
        Call(console, "CompleteCommand");
        Check((string)typeof(DeveloperConsole).GetField("command", Hidden).GetValue(console) == "good " + title,
            "Tab completes " + title);
        Call(console, "ExecuteCommand");
        Check(((string)typeof(DeveloperConsole).GetField("feedback", Hidden).GetValue(console)).StartsWith("Granted "),
            "Enter grants " + title);
        Call(console, "SetOpen", false);
    }

    public static void Run()
    {
        if (PlayerSettings.productName != "GoldMinerStoreDemoCapture") throw new Exception("Use the isolated project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount < 3 || frame == Time.frameCount) return;
        frame = Time.frameCount;
        if (test == null)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false); test = Exercise();
        }
        try { if (test.MoveNext()) return; }
        catch (Exception error) { Debug.LogException(error); test = null; EditorApplication.Exit(1); return; }
        test = null; EditorApplication.Exit(0);
    }

    static IEnumerator Exercise()
    {
        Application.runInBackground = true; Application.targetFrameRate = 60;
        Time.timeScale = 1f;
        GameUnlocks.UnlockEverything(); ProgressStore.SelectedCharacter = "4";
        // A restarted map creates its wallet record before the saved Elsa is rebound on level entry.
        CrossLevelStatus.StartFresh(typeof(PigStatus));
        typeof(LevelLoader).GetProperty("Tutorial").SetValue(null, true);
        var hub = new GameObject("Restarted map hub").AddComponent<MapHub>();
        foreach (string id in Goods)
        {
            Command(id);
            Check(CrossLevelStatus.Active.getSkill(id).armed, id + " is reserved on the restarted map");
        }
        UnityEngine.Object.DestroyImmediate(hub.gameObject);
        Call(GameFlow.Instance, "ApplyChosenCharacter");
        Check(CrossLevelStatus.Active is PenguinStatus, "Level entry restores the saved Elsa record type");
        foreach (string id in Goods) Check(CrossLevelStatus.Active.getSkill(id).armed,
            id + " survives record rebinding from map wallet to saved champion");
        LevelLoader.Requested = Resources.Load<TextAsset>("Levels/level_04");
        LevelLoader.ScriptedTutorial = false; LevelLoader.WaitToAdvance = true;
        AsyncOperation loading = SceneManager.LoadSceneAsync(LevelLayout.SceneFor(LevelLoader.Requested));
        while (!loading.isDone || UnityEngine.Object.FindFirstObjectByType<LevelLoader>() == null) yield return null;
        yield return null; yield return null;
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerCharacter>();
        var gm = GameManager.Instance;
        var record = CrossLevelStatus.Active;
        Check(player is PenguinController && gm.IsPlaying && CrossLevelStatus.StoreRoundActive,
            "Elsa is playing the real general level_04");
        foreach (string id in Goods) Check(record.getSkill(id).active && !record.getSkill(id).armed,
            id + " activates on entry to the next normal level");
        Check(gm.BonusLives == 1, "Reserved Life adds battle HP on entry");
        var reservedTime = player.GetComponent<TimeMovesWithPlayer>();
        Check(reservedTime != null && reservedTime.enabled, "Reserved Time Machine attaches during level initialization");
        float entryUntil = Time.realtimeSinceStartup + .7f;
        while (Time.realtimeSinceStartup < entryUntil) yield return null;
        Check(Time.timeScale < .15f, "Map-reserved Time Machine actually slows the next normal level");
        record.onLevelEnded();
        UnityEngine.Object.DestroyImmediate(reservedTime);
        gm.SetBonusLives(0); gm.SetLives(gm.startingLives - 1);
        // Keep enemies from ending the test while the effect is observed.
        foreach (MinerAI miner in UnityEngine.Object.FindObjectsByType<MinerAI>(FindObjectsSortMode.None)) miner.enabled = false;
        foreach (Hook hook in UnityEngine.Object.FindObjectsByType<Hook>(FindObjectsSortMode.None)) hook.StopShot();
        float baseSpeed = player.CurrentSpeed;
        int lives = gm.Lives;
        foreach (string id in Goods)
        {
            Command(id);
            Check(record.getSkill(id).active && !record.getSkill(id).armed, id + " activates through the console in a normal level");
        }
        Check(Mathf.Approximately(player.CurrentSpeed, baseSpeed * CrossLevelStatus.SwiftBoost), "Swift doubles actual speed");
        player.Digest(100); Check(player.DigestLeft == 0f, "Ration removes actual digestion");
        Check(gm.Lives == lives + 1 && gm.BonusLives == 1, "Life immediately adds one bonus HP");
        int protectedLives = gm.Lives; gm.PlayerHit(); Check(gm.Lives == protectedLives, "Shield absorbs an actual hit");
        var time = player.GetComponent<TimeMovesWithPlayer>();
        Check(time != null && time.enabled && time.player == player, "Time Machine is attached to Elsa and enabled");
        float until = Time.realtimeSinceStartup + .7f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Check(Time.timeScale < .15f, "Time Machine actually slows the world while Elsa stands still; scale=" + Time.timeScale);
        typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(player, Vector2.right);
        player.enabled = false;
        until = Time.realtimeSinceStartup + .25f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Check(Time.timeScale > .95f, "Time returns to normal when the player moves");
        typeof(PlayerCharacter).GetProperty("MoveInput").SetValue(player, Vector2.zero);
        time.enabled = false;
        int spare = record.getSkill("time_machine").stock;
        Command("time_machine");
        Check(time.enabled && record.getSkill("time_machine").stock == spare + 1,
            "Granting an active Time Machine re-enables its effect without wasting spare stock");
        UnityEngine.Object.DestroyImmediate(time);
        Command("time_machine");
        time = player.GetComponent<TimeMovesWithPlayer>();
        Check(time != null && time.enabled, "Grant repairs a missing Time Machine component even when item is already active");
        until = Time.realtimeSinceStartup + .7f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Check(Time.timeScale < .15f, "Repaired effect really slows the clock");
        Debug.Log("CONSOLE_GENERAL_LEVEL_PASS: " + checks + " completion, grant, actual effect, and recovery checks in Elsa level_04.");

        // Also cover the separately discovered map-to-Survival flag and starting-HP bugs.
        typeof(LevelLoader).GetProperty("Tutorial").SetValue(null, true);
        record.onLevelEnded();
        foreach (string id in Goods) { Command(id); Check(record.getSkill(id).armed, id + " waits on the map"); }
        loading = SceneManager.LoadSceneAsync("Survival");
        while (!loading.isDone) yield return null;
        yield return null; yield return null;
        var survival = UnityEngine.Object.FindFirstObjectByType<SurvivalLoader>();
        survival.enabled = false;
        gm = GameManager.Instance;
        player = UnityEngine.Object.FindFirstObjectByType<PlayerCharacter>();
        Check(!LevelLoader.Tutorial && CrossLevelStatus.StoreRoundActive, "Survival clears the previous map/tutorial flag");
        foreach (string id in Goods) Check(record.getSkill(id).active && !record.getSkill(id).armed,
            id + " activates after the real Survival transition");
        Check(gm.Lives == survival.startingLives + 1 && gm.BonusLives == 1, "Reserved Life is included in Survival starting HP");
        Check(PlayerCharacter.Record == record && record.hasNoDigest() && record.isShielded(),
            "Survival player receives the goods record");
        time = player.GetComponent<TimeMovesWithPlayer>();
        Check(time != null && time.enabled, "Reserved Time Machine attaches in Survival");
        until = Time.realtimeSinceStartup + .7f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Check(Time.timeScale < .15f, "Reserved Time Machine slows Survival too");
        Debug.Log("CONSOLE_GOODS_PLAY_PASS: " + checks + " normal-level console and cross-scene goods checks.");
    }
}
