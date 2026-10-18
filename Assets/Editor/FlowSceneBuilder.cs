using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Build Flow Scenes: creates the Boot, Map, RolePick, and Store scenes, builds
// the WorldMap asset in Resources (wired to the level JSONs), and registers every scene in Build
// Settings with Boot first. Same pattern as GameSceneBuilder / HUDBuilder. Re-running keeps an
// existing WorldMap so hand-tuned icon positions are not overwritten.
public static class FlowSceneBuilder
{
    const string SceneDir = "Assets/Scenes";
    const string MapAssetPath = "Assets/Resources/WorldMap.asset";
    const string LevelDir = "Assets/Resources/Levels/";

    // id, type, level file (null for non-level nodes), display name. Order = play order.
    static readonly (string id, NodeType type, string level, string name)[] Nodes =
    {
        ("tut_basics", NodeType.Tutorial, "level_00_eat",    "Tutorial: Eat"),
        ("tut_dodge",  NodeType.Tutorial, "level_01a_dodge", "Tutorial: Dodge"),
        ("tut_block",  NodeType.Tutorial, "level_01b_block", "Tutorial: Block"),
        ("role_pick",  NodeType.RolePick, null,              "Pick Character"),
        ("battle_01",  NodeType.Battle,   "level_01",        "Level 1"),
        ("battle_02",  NodeType.Battle,   "level_02",        "Level 2"),
        ("store_1",    NodeType.Store,    null,              "Store"),
        ("battle_03",  NodeType.Battle,   "level_03",        "Level 3"),
        ("battle_04",  NodeType.Battle,   "level_04",        "Level 4"),
        ("battle_05",  NodeType.Battle,   "level_05",        "Level 5"),
        ("battle_06",  NodeType.Battle,   "level_06",        "Level 6"),
        ("battle_07",  NodeType.Battle,   "level_07",        "Level 7"),
        ("battle_08",  NodeType.Battle,   "level_08",        "Level 8"),
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

        RegisterBuildScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("FlowSceneBuilder: built Boot / Map / RolePick / Store scenes, WorldMap asset, and Build Settings. Open Boot and press Play.");
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
        const int cols = 4;
        int rows = Mathf.CeilToInt(Nodes.Length / (float)cols);

        for (int i = 0; i < Nodes.Length; i++)
        {
            var def = Nodes[i];
            var node = new MapNode
            {
                id = def.id,
                type = def.type,
                displayName = def.name,
                mapPosition = SnakePosition(i, cols, rows),
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

    // A readable serpentine: left-to-right, then right-to-left on the next row, top to bottom.
    static Vector2 SnakePosition(int index, int cols, int rows)
    {
        int row = index / cols;
        int col = index % cols;
        if (row % 2 == 1) col = cols - 1 - col;
        float x = cols <= 1 ? 0.5f : Mathf.Lerp(0.12f, 0.88f, col / (float)(cols - 1));
        float y = rows <= 1 ? 0.5f : Mathf.Lerp(0.20f, 0.80f, row / (float)(rows - 1));
        return new Vector2(x, y);
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
