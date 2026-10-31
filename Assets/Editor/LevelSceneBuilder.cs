using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Tools > Gold Miner > Build Level Scenes: makes one scene per level file in Resources/Levels, in
// Assets/Scenes/Levels/<level>.unity, laid out from the file (PlayerStart, Hooks, Items; see
// LevelLayout), and adds them to Build Settings. From then on the scene is where the level's layout
// is edited. A scene that already exists is never touched, so re-running only adds new levels.
// Tutorial stage items and all the numbers (speeds, timings, guide) stay in the level file.
public static class LevelSceneBuilder
{
    const string LevelDir = "Assets/Resources/Levels";
    const string SceneDir = "Assets/Scenes/Levels";
    const string PrefabDir = "Assets/Prefabs/";

    [MenuItem("Tools/Gold Miner/Build Level Scenes")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SceneSetup[] before = EditorSceneManager.GetSceneManagerSetup();

        if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets/Scenes", "Levels");

        var levels = new List<TextAsset>();
        foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { LevelDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".json")) levels.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }
        levels.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var scenes = new List<string>();
        int made = 0;
        foreach (TextAsset level in levels)
        {
            string path = SceneDir + "/" + level.name + ".unity";
            scenes.Add(path);
            if (File.Exists(path)) continue;
            if (BuildScene(level, path)) made++;
        }

        Register(scenes);
        if (before.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(before);
        AssetDatabase.SaveAssets();
        Debug.Log("LevelSceneBuilder: " + made + " level scene(s) made, " + (scenes.Count - made) +
                  " kept, in " + SceneDir + ". Open one to edit it, or press Play in it to play that level.");
    }

    [MenuItem("Tools/Gold Miner/Build Level Scenes", true)]
    static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    static bool BuildScene(TextAsset file, string path)
    {
        LevelData data;
        try { data = LevelData.Parse(file.text); }
        catch (System.Exception e)
        {
            Debug.LogError("LevelSceneBuilder: can't read " + file.name + ": " + e.Message);
            return false;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("Level");
        LevelLayout layout = root.AddComponent<LevelLayout>();
        layout.levelFile = file;

        layout.playerStart = Child(root.transform, "PlayerStart", data.pigStart);

        layout.hooks = Child(root.transform, "Hooks", Vector2.zero);
        for (int i = 0; i < data.hooks.Count; i++)
            Child(layout.hooks, "Hook " + (i + 1), data.hooks[i]);

        layout.items = Child(root.transform, "Items", Vector2.zero);
        foreach (LevelData.Item item in data.items)
        {
            GameObject prefab = FindPrefab(item.type);
            if (prefab == null)
            {
                Debug.LogWarning("LevelSceneBuilder: " + file.name + " uses " + item.type + ", but there is no " + item.type + ".prefab under " + PrefabDir);
                continue;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetParent(layout.items, false);
            go.transform.position = item.position;
        }

        bool saved = EditorSceneManager.SaveScene(scene, path);
        if (!saved) Debug.LogError("LevelSceneBuilder: could not save " + path);
        return saved;
    }

    // By file name anywhere under Assets/Prefabs, so sorting prefabs into folders doesn't break it
    static GameObject FindPrefab(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { PrefabDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    static Transform Child(Transform parent, string name, Vector2 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go.transform;
    }

    // Level scenes go after the scenes already listed; the Game scene must be there too, since
    // every level scene loads it
    static void Register(List<string> scenes)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        var seen = new HashSet<string>();
        foreach (var s in list) seen.Add(s.path);
        foreach (string path in scenes)
            if (seen.Add(path)) list.Add(new EditorBuildSettingsScene(path, true));
        if (!seen.Contains("Assets/Scenes/Game.unity"))
            Debug.LogWarning("LevelSceneBuilder: Assets/Scenes/Game.unity is not in Build Settings; level scenes need it.");
        EditorBuildSettings.scenes = list.ToArray();
    }
}
