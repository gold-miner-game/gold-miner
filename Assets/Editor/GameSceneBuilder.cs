using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    [MenuItem("Tools/Build Game Scene")]
    public static void BuildGameScene()
    {
        // Validate every asset before replacing the active scene.
        GameObject pigPrefab;
        GameObject dotPrefab;
        GameObject smallPrefab;
        GameObject mediumPrefab;
        GameObject largePrefab;
        Sprite square;
        Sprite triangle;
        try
        {
            pigPrefab = LoadRequired<GameObject>("Assets/Prefabs/Pig.prefab");
            dotPrefab = LoadRequired<GameObject>("Assets/Prefabs/TelegraphDot.prefab");
            smallPrefab = LoadRequired<GameObject>("Assets/Prefabs/Gold_Small.prefab");
            mediumPrefab = LoadRequired<GameObject>("Assets/Prefabs/Gold_Medium.prefab");
            largePrefab = LoadRequired<GameObject>("Assets/Prefabs/Gold_Large.prefab");
            square = LoadRequired<Sprite>("Assets/Sprites/Square.png");
            triangle = LoadRequired<Sprite>("Assets/Sprites/Triangle.png");
        }
        catch (InvalidOperationException exception)
        {
            Debug.LogError(exception.Message);
            EditorUtility.DisplayDialog("Build Game Scene", exception.Message, "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;

        new GameObject("GameManager").AddComponent<GameManager>();
        GameObject pig = InstantiatePrefab(pigPrefab, scene, new Vector3(0f, -1f, 0f));

        GameObject miner = CreateSprite("Miner", square, new Vector3(0f, 4.2f, 0f));
        MinerAI minerAI = miner.AddComponent<MinerAI>();
        Transform hookOrigin = new GameObject("HookOrigin").transform;
        hookOrigin.SetParent(miner.transform);
        hookOrigin.position = new Vector3(0f, 3.4f, 0f);

        GameObject hookObject = CreateSprite("Hook", triangle, new Vector3(0f, 3.4f, 0f));
        hookObject.transform.localScale = Vector3.one * 0.4f;
        hookObject.GetComponent<SpriteRenderer>().sortingOrder = 5;
        Hook hook = hookObject.AddComponent<Hook>();

        TelegraphLine telegraph = new GameObject("Telegraph").AddComponent<TelegraphLine>();
        telegraph.dotPrefab = dotPrefab;
        minerAI.hook = hook;
        minerAI.telegraph = telegraph;
        minerAI.hookOrigin = hookOrigin;
        minerAI.pig = pig.transform;

        InstantiatePrefab(smallPrefab, scene, new Vector3(-4f, -2f, 0f));
        InstantiatePrefab(mediumPrefab, scene, new Vector3(3f, 0f, 0f));
        InstantiatePrefab(largePrefab, scene, new Vector3(5f, -3f, 0f));

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("Could not save the game scene to " + ScenePath);
            return;
        }

        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int existingIndex = buildScenes.FindIndex(entry => entry.path == ScenePath);
        if (existingIndex >= 0)
            buildScenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
        else
            buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        Debug.Log("Built and saved " + ScenePath);
    }

    [MenuItem("Tools/Build Game Scene", true)]
    private static bool CanBuildGameScene()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException("Required asset is missing or has the wrong type: " + path);
        return asset;
    }

    private static GameObject InstantiatePrefab(GameObject prefab, Scene scene, Vector3 position)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.position = position;
        return instance;
    }

    private static GameObject CreateSprite(string name, Sprite sprite, Vector3 position)
    {
        var gameObject = new GameObject(name, typeof(SpriteRenderer));
        gameObject.transform.position = position;
        gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
        return gameObject;
    }
}
