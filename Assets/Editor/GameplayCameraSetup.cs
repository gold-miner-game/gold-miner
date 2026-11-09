using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owner: Butian (screen layout)
// Makes sure the Game scene's Main Camera has GameplayCamera, so the arena sits clear of the HUD
// in the editor too (the HUD only adds it when the game runs). Checked whenever the Game scene is
// opened, on its own or next to a level scene; adding it marks the scene changed, so save it once.
[InitializeOnLoad]
static class GameplayCameraSetup
{
    const string GameScene = "Game";

    static GameplayCameraSetup()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => Check(scene);
        EditorApplication.delayCall += () =>
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) Check(SceneManager.GetSceneAt(i));
        };
    }

    static void Check(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.isLoaded || scene.name != GameScene) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (!cam.CompareTag("MainCamera") || cam.GetComponent<GameplayCamera>() != null) continue;
                Undo.AddComponent<GameplayCamera>(cam.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("GameplayCameraSetup: added GameplayCamera to " + cam.name + " in the Game scene, so the arena stays clear of the HUD. Save the scene to keep it.", cam);
            }
    }
}
