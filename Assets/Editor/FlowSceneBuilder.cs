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
    // Tutorial (formerly Level 1) -> Level 1 -> Level 2 -> Level 3. Keep the stable ids and source
    // assets so existing saves keep their progress. Pick Character opens after the tutorial;
    // the Store opens after Level 2; leaving the Store reveals Level 3.
    // x = branch spread (0..1), y = depth down the trunk (0..1); the controller can draw it either way.
    // GameFlow plays the tutorial before the map opens; its portal allows replays afterward.
    static readonly string[] TutorialLevels = { "level_03" };

    // The world map you walk around in (MapHub): one portal per node below, placed in its grid
    const string HubLevelPath = "Assets/Resources/Hub/world_map.json";

    static readonly (string id, NodeType type, string level, string name, float x, float y, string[] prereqs)[] Nodes =
    {
        ("role_pick",  NodeType.RolePick, null,              "Pick Character",  0.50f, 0.444f, new[]{ ProgressStore.TutorialDoneId }),
        ("store_1",    NodeType.Store,    null,              "Store",           0.50f, 0.333f, new[]{ "battle_02" }),
        ("battle_03",  NodeType.Tutorial, "level_03",        "Tutorial",        0.18f, 0.555f, new string[0]),
        ("battle_06",  NodeType.Battle,   "level_01",        "Level 1",         0.82f, 0.555f, new[]{ ProgressStore.TutorialDoneId }),
        ("battle_02",  NodeType.Battle,   "level_02",        "Level 2",         0.50f, 0.666f, new[]{ "battle_06" }),
        ("battle_04",  NodeType.Battle,   "level_04",        "Level 3",         0.50f, 0.780f, new[]{ "store_1" }),
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

        foreach (string level in TutorialLevels)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(LevelDir + level + ".json");
            if (asset == null) Debug.LogWarning("FlowSceneBuilder: missing tutorial level " + LevelDir + level + ".json");
            map.tutorialLevels.Add(asset);
        }
        map.hubLevel = AssetDatabase.LoadAssetAtPath<TextAsset>(HubLevelPath);
        if (map.hubLevel == null) Debug.LogWarning("FlowSceneBuilder: missing hub level " + HubLevelPath);

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
