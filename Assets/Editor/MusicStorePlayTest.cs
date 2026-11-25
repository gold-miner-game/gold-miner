using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Music store logic: the map node and its unlock, the green rock that turns into its portal, buying
// and picking songs, and the panel on the map. Saved values it touches are put back afterwards.
// Run: MusicStorePlayTest.Run (exits the editor with 0 on pass).
public static class MusicStorePlayTest
{
    const string Pending = "gm.tests.musicStore";
    const string WalletKey = "gm.wallet";   // an int; the others are strings
    static readonly string[] SavedKeys = { MusicLibrary.OwnedKey, MusicLibrary.SelectedKey, "gm.progress.completed" };
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    const string Fixture = "{\"songs\":[{\"id\":\"a\",\"title\":\"Song A\",\"price\":100}," +
                           "{\"id\":\"b\",\"title\":\"Song B\",\"price\":250},{\"id\":\"a\",\"title\":\"Duplicate\",\"price\":1}," +
                           "{\"id\":\"\",\"title\":\"No id\",\"price\":1},{\"id\":\"c\",\"price\":-5}]}";

    public static void Run()
    {
        int exit = 0;
        try { EditChecks(); }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        if (exit != 0) { EditorApplication.Exit(exit); return; }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetInt(Pending + ".checks", checks);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    // The real assets: the WorldMap node, the catalogue file and the baked world map's green rock
    static void EditChecks()
    {
        WorldMap map = Resources.Load<WorldMap>("WorldMap");
        MapNode node = map != null ? map.Find(MapHub.MusicStoreId) : null;
        Check(node != null && node.type == NodeType.MusicStore && node.displayName == "Music Store", "WorldMap has the Music Store node");
        MapNode level2 = map.Find("battle_02");
        Check(node.prerequisites.Length == 1 && level2 != null && node.prerequisites[0] == level2.prerequisites[0],
            "Music Store opens exactly when Level 2 opens (both need Level 1)");

        MusicLibrary.Load(Resources.Load<TextAsset>(MusicLibrary.CatalogueResource));
        Check(MusicLibrary.Songs.Count > 0, "Catalogue file loads");

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Levels/world_map.unity", OpenSceneMode.Single);
        GameObject level = Array.Find(scene.GetRootGameObjects(), g => g.name == "Level");
        Transform baked = level != null ? level.transform.Find(MapHub.BakedRootName) : null;
        Check(baked != null, "World map scene has its baked Hub");
        Vector2 at = MapHub.MusicStoreDefaultAt;
        var rocksBefore = baked.GetComponentsInChildren<Rock>().Length;
        HubPortal portal = MapHub.PortalFromGreenRock(baked, MapHub.MusicStoreId, at, 0.42f);
        Check(portal != null && portal.hidden && portal.nodeId == MapHub.MusicStoreId, "A green rock becomes the hidden Music Store portal");
        Check(Vector2.Distance(portal.transform.position, at) < 0.05f && portal.transform.parent.name == "Portals",
            "Portal stands on the chosen tooth, with the other portals");
        Check(baked.GetComponentsInChildren<Rock>().Length == rocksBefore - 1, "The replaced rock is switched off");
        foreach (HubPortal other in baked.GetComponentsInChildren<HubPortal>())
            if (other != portal) Check(Vector2.Distance(other.transform.position, at) > 0.3f, "No other portal on that spot");
        Check(MapHub.PortalFromGreenRock(baked, "x", new Vector2(100f, 100f), 0.42f) == null, "No green rock nearby: no portal");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // drop the unsaved change
    }

    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        checks = SessionState.GetInt(Pending + ".checks", 0);
        var saved = new Dictionary<string, string>();
        foreach (string key in SavedKeys) if (PlayerPrefs.HasKey(key)) saved[key] = PlayerPrefs.GetString(key, "");
        bool hadWallet = PlayerPrefs.HasKey(WalletKey);
        int walletBefore = PlayerPrefs.GetInt(WalletKey, 0);
        int exit = 0;
        try { PlayChecks(); }
        catch (Exception error) { Debug.LogException(error); exit = 1; }
        finally
        {
            foreach (GameObject go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
            foreach (string key in SavedKeys)
            {
                PlayerPrefs.DeleteKey(key);
                if (saved.TryGetValue(key, out string text)) PlayerPrefs.SetString(key, text);
            }
            PlayerPrefs.DeleteKey(WalletKey);
            if (hadWallet) PlayerPrefs.SetInt(WalletKey, walletBefore);
            PlayerPrefs.Save();
            Time.timeScale = 1f;
        }
        EditorApplication.Exit(exit);
    }

    static void PlayChecks()
    {
        Time.timeScale = 1f;
        MusicLibrary.ResetSave();
        MusicLibrary.LoadJson(Fixture);
        Check(MusicLibrary.Songs.Count == 3, "Duplicate and id-less songs are skipped");
        Check(MusicLibrary.Find("c").title == "c" && MusicLibrary.Find("c").price == 0, "Missing title uses the id; prices are never negative");

        var wallet = CrossLevelStatus.Ensure(); objects.Add(wallet.gameObject);
        wallet.setWallet(300);
        Check(MusicLibrary.Buy("a", wallet) == MusicLibrary.BuyResult.Bought && wallet.getWallet() == 200 && MusicLibrary.IsOwned("a"),
            "Buying pays the price from the wallet and keeps the song");
        Check(MusicLibrary.Buy("a", wallet) == MusicLibrary.BuyResult.AlreadyOwned && wallet.getWallet() == 200, "A song is only paid once");
        Check(MusicLibrary.Buy("b", wallet) == MusicLibrary.BuyResult.NotEnoughGold && wallet.getWallet() == 200 && !MusicLibrary.IsOwned("b"),
            "Not enough gold: nothing is paid or given");
        Check(MusicLibrary.Buy("zzz", wallet) == MusicLibrary.BuyResult.UnknownSong, "Unknown songs can't be bought");

        string heard = "unset"; int events = 0;
        Action<string> listen = id => { heard = id; events++; };
        MusicLibrary.Changed += listen;
        Check(!MusicLibrary.Select("b") && MusicLibrary.SelectedId == null && events == 0, "An unowned song can't be picked");
        Check(MusicLibrary.Select("a") && MusicLibrary.SelectedId == "a" && heard == "a" && events == 1, "Picking an owned song tells the sound system");
        Check(MusicLibrary.Select("a") && events == 1, "Picking the same song again sends nothing");
        Check(MusicLibrary.Select(null) && MusicLibrary.SelectedId == null && heard == null && events == 2, "Game music can always be picked");
        MusicLibrary.Select("a");

        // Saved between runs: forget the cache and read PlayerPrefs again
        typeof(MusicLibrary).GetField("owned", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, null);
        Check(MusicLibrary.IsOwned("a") && !MusicLibrary.IsOwned("b") && MusicLibrary.SelectedId == "a", "Owned songs and the pick are saved");

        // Unlock: closed until Level 1 is beaten, open together with Level 2
        WorldMap map = Resources.Load<WorldMap>("WorldMap");
        MapNode node = map.Find(MapHub.MusicStoreId), level2 = map.Find("battle_02");
        if (!GameUnlocks.EverythingUnlocked)
        {
            ProgressStore.ClearCompleted(MapHub.MusicStoreId);
            ProgressStore.ClearCompleted("battle_06");
            Check(!map.IsUnlocked(node) && !map.IsUnlocked(level2), "Sealed before Level 1 is beaten");
            ProgressStore.MarkCompleted("battle_06");
            Check(map.IsUnlocked(node) && map.IsUnlocked(level2), "Unsealed together with Level 2");
        }

        // The panel on the map: freezes the map, buys and picks, leaving counts as a visit
        Time.timeScale = 0.5f;
        wallet.setWallet(250);
        var panel = MusicStorePanel.Show(node, null); objects.Add(panel.gameObject);
        Check(MusicStorePanel.IsOpen && Time.timeScale == 0f, "Opening the store freezes the map");
        Check(MusicStorePanel.Show(node, null) == panel, "Walking in again doesn't open a second store");
        Check(panel.Buy(MusicLibrary.Find("b")) == "Bought Song B" && wallet.getWallet() == 0, "The panel buys with the wallet");
        Check(panel.Pick("b") == "Now playing: Song B" && MusicLibrary.SelectedId == "b", "The panel picks an owned song");
        Check(panel.Pick(null) == "Back to the game's own music" && MusicLibrary.SelectedId == null, "The panel goes back to game music");
        ProgressStore.ClearCompleted(MapHub.MusicStoreId);
        panel.Leave();
        Check(!MusicStorePanel.IsOpen && Time.timeScale == 0.5f && ProgressStore.IsCompleted(MapHub.MusicStoreId),
            "Leaving unfreezes the map and marks the store visited");

        MusicLibrary.Changed -= listen;
        MusicLibrary.Select("a");
        MusicLibrary.ResetSave();
        Check(!MusicLibrary.IsOwned("a") && MusicLibrary.SelectedId == null, "Wiping the save clears songs and the pick");
        Debug.Log("MUSIC_STORE_PLAY_PASS: " + checks + " node, unlock, portal, wallet, pick, save and panel checks.");
    }
}
