using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tool: Tools > Add Level Loader puts a LevelLoader in the open scene (or updates the one
// there) with the item prefabs from Assets/Prefabs. Levels are read from Assets/Resources/Levels.
public static class LevelLoaderSetup
{
    static readonly string[] ItemPrefabs = { "Gold_Small", "Gold_Medium", "Gold_Large", "Diamond", "Rock", "Life" };

    [MenuItem("Tools/Add Level Loader")]
    public static void AddLevelLoader()
    {
        LevelLoader loader = Object.FindFirstObjectByType<LevelLoader>();
        if (loader == null)
        {
            loader = new GameObject("LevelLoader").AddComponent<LevelLoader>();
            Undo.RegisterCreatedObjectUndo(loader.gameObject, "Add Level Loader");
        }
        else Undo.RecordObject(loader, "Update Level Loader");

        var prefabs = new List<GameObject>();
        foreach (string name in ItemPrefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
            if (prefab != null) prefabs.Add(prefab);
            else Debug.LogWarning("LevelLoaderSetup: missing Assets/Prefabs/" + name + ".prefab");
        }
        loader.itemPrefabs = prefabs.ToArray();

        EditorUtility.SetDirty(loader);
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        Selection.activeGameObject = loader.gameObject;
        Debug.Log("LevelLoaderSetup: LevelLoader ready with " + prefabs.Count + " item prefabs; save the scene to keep it");
    }

    [MenuItem("Tools/Add Level Loader", true)]
    static bool CanAdd()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
