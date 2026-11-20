using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run only in the disposable demo project, never against the player's saved progress.
public static class StoreMapGoodsPlayTest
{
    const string Pending = "gm.tests.mapGoods";
    static readonly string[] Goods = { "swift", "ration", "shield", "brace", "time_machine" };
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
    static GameObject Make(string name) { var go = new GameObject(name); objects.Add(go); return go; }
    static void Tutorial(bool value) => typeof(LevelLoader).GetProperty("Tutorial").SetValue(null, value);

    public static void Run()
    {
        if (PlayerSettings.productName != "GoldMinerStoreDemoCapture")
            throw new InvalidOperationException("Use the isolated capture project for this test.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int exit = 0;
        try { Execute(); }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (GameObject go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear(); Tutorial(false); Time.timeScale = 1f;
        }
        EditorApplication.Exit(exit);
    }

    static void Execute()
    {
        Time.timeScale = 1f;
        Tutorial(true);
        var gm = Make("Map game manager").AddComponent<GameManager>();
        gm.startingLives = 3; gm.SyncLivesFromStart();
        var hub = Make("Map hub").AddComponent<MapHub>();
        var record = CrossLevelStatus.Ensure(); objects.Add(record.gameObject);
        var playerObject = Make("Map walker");
        playerObject.AddComponent<BoxCollider2D>();
        var player = playerObject.AddComponent<MrHydeController>();
        float mapSpeed = player.CurrentSpeed;
        Check(gm.IsPlaying && !CrossLevelStatus.StoreRoundActive, "A running map is not a goods round");
        foreach (string id in Goods)
        {
            StoreView.GrantConsoleItem(id);
            PlayerSkill item = record.getSkill(id);
            Check(item.armed && !item.active && item.secondsLeft == 0f && item.stock == 0,
                id + " is reserved, not activated, on the map");
        }
        Check(player.CurrentSpeed == mapSpeed && record.getSpeedMultiplier() == 1f, "Swift does not speed up the map");
        Check(!record.hasNoDigest() && !record.isShielded(), "Ration and Shield do not affect the map");
        Check(gm.Lives == 3 && gm.BonusLives == 0 && record.getBraceBonusLives() == 0
            && LevelLoader.PendingBraceBonusLives() == 0, "Brace changes neither map HP nor its displayed pips");
        TimeMovesWithPlayer.ApplyStoreEffect(player, record);
        Check(player.GetComponent<TimeMovesWithPlayer>() == null && Time.timeScale == 1f,
            "Time Machine does not attach to or slow down the map walker");

        // Map identity is sufficient even during transitions before the tutorial flag is set.
        Tutorial(false);
        record.onLevelStarted();
        Check(!CrossLevelStatus.StoreRoundActive, "Map identity blocks premature round activation");
        foreach (string id in Goods) Check(record.getSkill(id).armed && !record.getSkill(id).active,
            id + " survives a map level-start callback");
        StoreView.GrantConsoleItem("shield");
        Check(record.getSkill("shield").armed && record.getSkill("shield").stock == 1,
            "A second armed Shield stays as spare stock");

        var loaderObject = Make("Hub HP loader"); loaderObject.SetActive(false);
        var loader = loaderObject.AddComponent<LevelLoader>();
        var level = new LevelData { IsMaze = true }; level.parameters["starting_lives"] = 3;
        typeof(LevelLoader).GetField("level", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, level);
        loader.ApplyHubLives(); hub.ApplySavedHp(gm); hub.Revive(gm);
        Check(gm.BonusLives == 0 && record.getSkill("brace").armed, "Hub loading and revival preserve Brace for battle");

        UnityEngine.Object.DestroyImmediate(hub.gameObject);
        Tutorial(true); record.onLevelStarted();
        foreach (string id in Goods) Check(record.getSkill(id).armed, id + " also waits through tutorials");
        Tutorial(false); record.onLevelStarted();
        foreach (string id in Goods) Check(!record.getSkill(id).armed && record.getSkill(id).active,
            id + " activates in the next battle");
        Check(record.getSpeedMultiplier() == CrossLevelStatus.SwiftBoost && record.hasNoDigest()
            && record.isShielded() && record.getBraceBonusLives() == 1, "Battle receives the reserved goods effects");
        Check(Mathf.Approximately(record.getSkill("shield").secondsLeft, CrossLevelStatus.ShieldDuration),
            "Shield begins with its full duration in battle");
        TimeMovesWithPlayer.ApplyStoreEffect(player, record);
        Check(player.GetComponent<TimeMovesWithPlayer>() != null, "Time Machine attaches in battle");
        Check(record.getSkill("shield").stock == 1, "Battle does not consume the spare Shield");
        record.onLevelEnded(); record.onLevelStarted();
        foreach (string id in Goods) Check(!record.getSkill(id).active && !record.getSkill(id).armed,
            id + " does not leak into a second battle");
        Check(record.getSpeedMultiplier() == 1f, "Swift resets after its battle");
        int before = gm.Lives;
        StoreView.GrantConsoleItem("brace");
        Check(gm.Lives == before + 1 && gm.BonusLives == 1, "In-battle Brace grants still work immediately");
        Debug.Log("STORE_MAP_GOODS_PASS: " + checks + " map, tutorial, battle, stock, HP, and time-effect checks.");
    }
}
