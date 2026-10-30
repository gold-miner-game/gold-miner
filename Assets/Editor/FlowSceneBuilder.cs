using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Build Flow Scenes: creates the Boot, Map, RolePick (legacy OnGUI), Store,
// and Survival scenes, builds the WorldMap asset in Resources (wired to the level JSONs), and
// registers every scene in Build Settings with Boot first. The map's RolePick node loads the
// existing Select scene ("WHO ARE U"), not RolePick.unity — RolePick.unity is only kept so a
// rebuild does not drop it from the project. Same pattern as GameSceneBuilder / HUDBuilder.
// Re-running keeps an existing WorldMap so hand-tuned icon positions are not overwritten.
public static class FlowSceneBuilder
{
    const string SceneDir = "Assets/Scenes";
    const string MapAssetPath = "Assets/Resources/WorldMap.asset";
    const string LevelDir = "Assets/Resources/Levels/";

    // id, type, level file (null for non-level nodes), display name, map x, map y, prerequisites.
    // The maze (tut_maze) IS the world map hub (always). Level portals live on that maze.
    // Currently two battles: Level 1 + Level 2 (stone → polygon). Unlock uses MapMoney thresholds.
    // x = branch spread (0..1), y = depth (kept for the legacy Map scene tree if opened).
    static readonly (string id, NodeType type, string level, string name, float x, float y, string[] prereqs)[] Nodes =
    {
        ("tut_maze",   NodeType.Tutorial, "level_tutorial",  "World Map",       0.50f, 0.000f, new string[0]),
        ("battle_01",  NodeType.Battle,   "level_01",        "Level 1",         0.50f, 0.143f, new[]{ "tut_maze" }),
        ("battle_02",  NodeType.Battle,   "level_02",        "Level 2",         0.18f, 0.286f, new[]{ "battle_01" }),
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
        BuildScene("Map", typeof(WorldMapController));
        BuildScene("RolePick", typeof(RolePickController));
        BuildScene("Store", typeof(StorePlaceholder));
        BuildScene("Survival", typeof(SurvivalPlaceholder));

        RegisterBuildScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("FlowSceneBuilder: built Boot / Map / RolePick (legacy) / Store / Survival scenes, WorldMap asset, and Build Settings. Map role pick opens Select. Open Boot and press Play.");
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

    static void BuildScene(string name, Type controller)
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
        controllerObject.AddComponent(controller);

        string path = SceneDir + "/" + name + ".unity";
        if (!EditorSceneManager.SaveScene(scene, path))
            Debug.LogError("FlowSceneBuilder: could not save " + path);
    }

    // Boot first (index 0 = the launch scene), then the hub scenes, then the existing Game scene.
    static void RegisterBuildScenes()
    {
        string[] order =
        {
            SceneDir + "/Boot.unity",
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
