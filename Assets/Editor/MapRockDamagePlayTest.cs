using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapRockDamagePlayTest
{
    const string Pending = "gm.tests.mapRockDamage";
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;
    static bool hadDamage, hadHp;
    static string savedDamage;
    static int savedHp;
    static bool hadCharacter, hadWallet;
    static string savedCharacter;
    static int savedWallet, previousChampion;
    static Rock restored;
    static float finishAt;
    static bool waiting;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static GameObject Make(string name) { var go = new GameObject(name); objects.Add(go); return go; }
    static Rock Wall(Vector2 at, float stage = 0f)
    {
        var go = Make("Persistent map wall"); go.transform.position = at;
        go.AddComponent<SpriteRenderer>(); go.AddComponent<BoxCollider2D>();
        var rock = go.AddComponent<Rock>(); rock.MakeWall(); rock.breakStageTime = stage; return rock;
    }
    static GameManager Round()
    {
        var gm = Make("Map damage round").AddComponent<GameManager>(); gm.invincibleTime = 0f;
        typeof(GameManager).GetProperty("IsPlaying").SetValue(gm,true); return gm;
    }
    public static void Run() { SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode(); }
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || !SessionState.GetBool(Pending,false)) return;
        try
        {
            if (!waiting) Execute();
            else if (Time.time >= finishAt)
            {
                Check(restored != null && restored.gameObject.activeSelf && restored.GetComponent<Collider2D>().enabled,
                    "reviving cancels delayed destruction so restored walls remain solid");
                Check(restored.Smash() && PlayerPrefs.HasKey(MapRockDamage.SaveKey), "fixture has damage before a fresh play session");
                typeof(MapRockDamage).GetMethod("BeginNewPlay",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                Check(restored.gameObject.activeSelf && restored.GetComponent<Collider2D>().enabled && !PlayerPrefs.HasKey(MapRockDamage.SaveKey),
                    "starting a fresh play session clears damage and restores the map");
                Debug.Log("MAP_ROCK_DAMAGE_PASS: " + checks + " persistence, level death, map death and restoration checks");
                Finish(0);
            }
        }
        catch(Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Execute()
    {
        hadDamage = PlayerPrefs.HasKey(MapRockDamage.SaveKey); savedDamage = PlayerPrefs.GetString(MapRockDamage.SaveKey);
        hadHp = PlayerPrefs.HasKey(MapHub.HpKey); savedHp = PlayerPrefs.GetInt(MapHub.HpKey);
        hadCharacter = PlayerPrefs.HasKey("gm.progress.character"); savedCharacter = PlayerPrefs.GetString("gm.progress.character");
        hadWallet = PlayerPrefs.HasKey("gm.wallet"); savedWallet = PlayerPrefs.GetInt("gm.wallet"); previousChampion = ChampionSpawner.Chosen;
        MapRockDamage.ResetDamage(); Time.timeScale = 1f;
        var hub = Make("First map visit").AddComponent<MapHub>();
        hub.enabled = false; // This isolated fixture has no map UI/font resources.
        var layoutScene = SceneManager.CreateScene("Map layout fixture");
        Transform bakedRoot = Make("Baked map layout").transform;
        SceneManager.MoveGameObjectToScene(bakedRoot.gameObject,layoutScene);
        var wall = Wall(new Vector2(17,23)); wall.transform.SetParent(bakedRoot,true);
        Check(wall.gameObject.scene != hub.gameObject.scene, "fixture matches the map's additive layout and shared Game scenes");
        MapRockDamage.Bind(hub,bakedRoot);
        Check(wall.GetComponent<MapRockDamage>() != null, "baked barriers in the separate map scene are registered");
        Check(wall.Smash() && !wall.gameObject.activeSelf, "Hulk's successful smash removes the map barrier immediately");
        Check(PlayerPrefs.HasKey(MapRockDamage.SaveKey), "map damage is saved when the smash is accepted");
        string damage = PlayerPrefs.GetString(MapRockDamage.SaveKey);
        UnityEngine.Object.DestroyImmediate(hub.gameObject); UnityEngine.Object.DestroyImmediate(wall.gameObject);
        UnityEngine.Object.DestroyImmediate(bakedRoot.gameObject);
        var battleWall = Wall(new Vector2(17,23));
        Check(battleWall.GetComponent<MapRockDamage>() == null && battleWall.gameObject.activeSelf, "ordinary levels do not inherit map wall damage");
        var battle = Round(); battle.SetLives(1); battle.PlayerHit();
        Check(battle.PlayerDied && PlayerPrefs.GetString(MapRockDamage.SaveKey) == damage, "dying inside a level preserves map damage");
        UnityEngine.Object.DestroyImmediate(battleWall.gameObject); UnityEngine.Object.DestroyImmediate(battle.gameObject);
        // Execute the real selection iterator through record replacement, stopping
        // before its scene-load step because the fixture reconstructs that scene.
        var select = Make("Character select fixture").AddComponent<ChampionSelect>(); select.enabled = false;
        var leave = (IEnumerator)typeof(ChampionSelect).GetMethod("Leave",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(select,new object[] { 1 });
        Check(leave.MoveNext() && leave.MoveNext() && ChampionSpawner.Chosen == 1, "real character selection replaces Hulk with the cat");
        if (CrossLevelStatus.Active != null) objects.Add(CrossLevelStatus.Active.gameObject);
        Check(PlayerPrefs.GetString(MapRockDamage.SaveKey) == damage, "character selection preserves saved map damage");
        // Recreate the map and force a reload from saved data, rather than the static cache.
        typeof(MapRockDamage).GetMethod("ResetStatics",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        hub = Make("Return to map").AddComponent<MapHub>(); hub.enabled = false;
        bakedRoot = Make("Recreated baked map layout").transform;
        SceneManager.MoveGameObjectToScene(bakedRoot.gameObject,layoutScene);
        restored = Wall(new Vector2(17,23));
        restored.transform.SetParent(bakedRoot,true);
        var untouched = Wall(new Vector2(18,23)); untouched.transform.SetParent(bakedRoot,true);
        MapRockDamage.Bind(hub,bakedRoot);
        Check(!restored.gameObject.activeSelf && untouched.gameObject.activeSelf, "returning to the map restores only the saved destruction");
        MapHub.RefillHp();
        Check(!restored.gameObject.activeSelf && PlayerPrefs.GetString(MapRockDamage.SaveKey) == damage, "winning a level and refilling map HP cannot repair barriers");
        var cat = Make("Cat on map").AddComponent<CatController>();
        typeof(MapHub).GetField("walker",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hub,cat);
        var gm = Round(); gm.SetLives(2);
        gm.PlayerHit(); Check(!restored.gameObject.activeSelf, "surviving a map hit preserves damage");
        gm.PlayerHit();
        Check(restored.gameObject.activeSelf && restored.GetComponent<Collider2D>().enabled
            && restored.GetComponent<SpriteRenderer>().enabled && !PlayerPrefs.HasKey(MapRockDamage.SaveKey),
            "map death as the cat restores artwork and collision and clears saved damage");
        Check(restored.Smash(), "restored walls can be smashed again");
        gm.Crush("Map death test");
        Check(restored.gameObject.activeSelf && !PlayerPrefs.HasKey(MapRockDamage.SaveKey), "other lethal map damage also resets destruction");
        restored.breakStageTime = .15f;
        Check(restored.Smash() && PlayerPrefs.HasKey(MapRockDamage.SaveKey), "timed destruction is saved before its animation finishes");
        hub.Revive(gm);
        Check(restored.gameObject.activeSelf && restored.GetComponent<SpriteRenderer>().enabled, "reviving restores a barrier broken mid-animation");
        waiting = true; finishAt = Time.time + .5f;
    }
    static void Finish(int code)
    {
        SessionState.SetBool(Pending,false); EditorApplication.update -= Poll;
        foreach(var go in objects) if(go != null) UnityEngine.Object.DestroyImmediate(go);
        if(hadDamage) PlayerPrefs.SetString(MapRockDamage.SaveKey,savedDamage); else PlayerPrefs.DeleteKey(MapRockDamage.SaveKey);
        if(hadHp) PlayerPrefs.SetInt(MapHub.HpKey,savedHp); else PlayerPrefs.DeleteKey(MapHub.HpKey);
        if(hadCharacter) PlayerPrefs.SetString("gm.progress.character",savedCharacter); else PlayerPrefs.DeleteKey("gm.progress.character");
        if(hadWallet) PlayerPrefs.SetInt("gm.wallet",savedWallet); else PlayerPrefs.DeleteKey("gm.wallet");
        ChampionSpawner.Chosen = previousChampion;
        PlayerPrefs.Save(); GameManager.Instance = null; EditorApplication.Exit(code);
    }
}
