using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Build Flow Scenes: creates the Boot, Menu, Map, RolePick, Store and Survival scenes, builds
// the WorldMap asset in Resources (wired to the level JSONs), and registers every scene in Build
// Settings with Boot first. Same pattern as GameSceneBuilder / HUDBuilder. Re-running keeps an
// existing WorldMap, and the existing Map scene, so hand-tuned nodes and map layout are not overwritten.
public static class FlowSceneBuilder
{
    const string SceneDir = "Assets/Scenes";
    const string MapAssetPath = "Assets/Resources/WorldMap.asset";
    const string LevelDir = "Assets/Resources/Levels/";

    // id, type, level file (null for non-level nodes), display name, map x, map y, prerequisites.
    // The map is a tree. The trunk is the tutorial + role pick. The Store is not on that path: the
    // map draws it in the top-left corner (its x/y here are unused) and unlocks it from role pick,
    // so it is reachable as soon as you have a character, not gated behind a battle. Level 1
    // then splits into a left and a right path that merge back at Level 8, with Survive Mode after it
    // (SurvivalLoader). Add a level = add a row
    // here (and drop a level JSON); point its prereqs at whatever should unlock it.
    // x = branch spread (0..1), y = depth down the trunk (0..1); the controller can draw it either way.
    static readonly (string id, NodeType type, string level, string name, float x, float y, string[] prereqs)[] Nodes =
    {
        ("tut_basics", NodeType.Tutorial, "level_00_eat",    "Tutorial: Eat",   0.50f, 0.000f, new string[0]),
        ("tut_dodge",  NodeType.Tutorial, "level_01a_dodge", "Tutorial: Dodge", 0.50f, 0.111f, new[]{ "tut_basics" }),
        ("tut_block",  NodeType.Tutorial, "level_01b_block", "Tutorial: Block", 0.50f, 0.222f, new[]{ "tut_dodge" }),
        ("role_pick",  NodeType.RolePick, null,              "Pick Character",  0.50f, 0.333f, new[]{ "tut_block" }),
        ("store_1",    NodeType.Store,    null,              "Store",           0.18f, 0.444f, new[]{ "role_pick" }),
        ("battle_01",  NodeType.Battle,   "level_01",        "Level 1",         0.50f, 0.444f, new[]{ "role_pick" }),
        ("battle_02",  NodeType.Battle,   "level_02",        "Level 2",         0.18f, 0.555f, new[]{ "battle_01" }),
        ("battle_03",  NodeType.Battle,   "level_03",        "Level 3",         0.82f, 0.555f, new[]{ "battle_01" }),
        ("battle_04",  NodeType.Battle,   "level_04",        "Level 4",         0.18f, 0.666f, new[]{ "battle_02" }),
        ("battle_05",  NodeType.Battle,   "level_05",        "Level 5",         0.82f, 0.666f, new[]{ "battle_03" }),
        ("battle_06",  NodeType.Battle,   "level_06",        "Level 6",         0.18f, 0.777f, new[]{ "battle_04" }),
        ("battle_07",  NodeType.Battle,   "level_07",        "Level 7",         0.82f, 0.777f, new[]{ "battle_05" }),
        ("battle_08",  NodeType.Battle,   "level_08",        "Level 8",         0.50f, 0.888f, new[]{ "battle_06", "battle_07" }),
        ("survival",   NodeType.Survival, null,              "Survive Mode",    0.50f, 1.000f, new[]{ "battle_08" }),
    };

    [MenuItem("Tools/Gold Miner/Build Flow Scenes")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!AssetDatabase.IsValidFolder(SceneDir))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        BuildWorldMap();

        BuildScene("Boot", typeof(Boot));
        BuildScene("Menu", typeof(MainMenu));
        BuildMapScene();
        BuildScene("RolePick", typeof(RolePickController));
        BuildScene("Store", typeof(StorePlaceholder));
        BuildScene("Survival", typeof(SurvivalLoader), c => WireSurvival((SurvivalLoader)c));

        RegisterBuildScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("FlowSceneBuilder: built Boot / Menu / Map / RolePick / Store / Survival scenes, WorldMap asset, and Build Settings. Open Boot and press Play.");
    }

    [MenuItem("Tools/Gold Miner/Build Flow Scenes", true)]
    static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // Create the WorldMap asset if it's missing. Existing assets are left alone so tuned icon
    // positions survive a rebuild.
    static void BuildWorldMap()
    {
        if (AssetDatabase.LoadAssetAtPath<WorldMap>(MapAssetPath) != null)
        {
            Debug.Log("FlowSceneBuilder: kept existing " + MapAssetPath + " (delete it to regenerate).");
            return;
        }

        WorldMap map = ScriptableObject.CreateInstance<WorldMap>();

        for (int i = 0; i < Nodes.Length; i++)
        {
            var def = Nodes[i];
            var node = new MapNode
            {
                id = def.id,
                type = def.type,
                displayName = def.name,
                mapPosition = new Vector2(def.x, def.y),
                prerequisites = def.prereqs,
            };
            if (def.level != null)
            {
                node.levelAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LevelDir + def.level + ".json");
                if (node.levelAsset == null)
                    Debug.LogWarning("FlowSceneBuilder: missing level " + LevelDir + def.level + ".json");
            }
            map.nodes.Add(node);
        }

        AssetDatabase.CreateAsset(map, MapAssetPath);
        Debug.Log("FlowSceneBuilder: created " + MapAssetPath + " with " + map.nodes.Count + " nodes.");
    }

    // Survival builds its arena at runtime; it only needs the prefabs
    static void WireSurvival(SurvivalLoader loader)
    {
        const string dir = "Assets/Prefabs/";
        loader.champions = new[] { "MrHyde", "Cat", "Squirrel", "Frog" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(dir + n + ".prefab")).ToArray();
        (string name, float weight)[] items =
            { ("Gold_Small", 40f), ("Gold_Medium", 28f), ("Gold_Large", 14f), ("Diamond", 10f), ("Life", 3f) };
        loader.items = items.Select(i => new SurvivalLoader.ItemChance
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + i.name + ".prefab"),
            weight = i.weight,
        }).ToArray();
        loader.telegraphDot = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "TelegraphDot.prefab");
    }

    // The map is edited by hand in the Scene view, so an existing Map scene is kept: it only gets the
    // map UI if it has none yet, plus a card for any new WorldMap node (MapUIBuilder)
    static void BuildMapScene()
    {
        string path = SceneDir + "/Map.unity";
        if (!File.Exists(path))
        {
            BuildScene("Map", typeof(WorldMapController), c => MapUIBuilder.Build((WorldMapController)c));
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        MapUIBuilder.Build(null);
        if (!EditorSceneManager.SaveScene(scene))
            Debug.LogError("FlowSceneBuilder: could not save " + path);
    }

    static void BuildScene(string name, Type controller, Action<Component> configure = null)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        var controllerObject = new GameObject(controller.Name);
        Component added = controllerObject.AddComponent(controller);
        configure?.Invoke(added);

        string path = SceneDir + "/" + name + ".unity";
        if (!EditorSceneManager.SaveScene(scene, path))
            Debug.LogError("FlowSceneBuilder: could not save " + path);
    }

    // Boot first (index 0 = the launch scene; it opens the Menu), then the hub scenes, then the existing Game scene.
    static void RegisterBuildScenes()
    {
        string[] order =
        {
            SceneDir + "/Boot.unity",
            SceneDir + "/Menu.unity",
            SceneDir + "/Map.unity",
            SceneDir + "/RolePick.unity",
            SceneDir + "/Store.unity",
            SceneDir + "/Survival.unity",
            SceneDir + "/Game.unity",
        };

        var existing = new Dictionary<string, EditorBuildSettingsScene>();
        foreach (var s in EditorBuildSettings.scenes) existing[s.path] = s;

        var result = new List<EditorBuildSettingsScene>();
        var seen = new HashSet<string>();
        foreach (string path in order)
        {
            result.Add(new EditorBuildSettingsScene(path, true));
            seen.Add(path);
        }
        // Keep any other scenes already registered (e.g. extra battle scenes), after ours.
        foreach (var s in EditorBuildSettings.scenes)
            if (seen.Add(s.path)) result.Add(s);

        EditorBuildSettings.scenes = result.ToArray();
    }
}
