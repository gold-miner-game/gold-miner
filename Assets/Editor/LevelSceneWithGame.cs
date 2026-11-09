using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Owner: Penny (Level map & tutorial)
// Opening a level scene (Assets/Scenes/Levels) also opens the Game scene next to it, so the level
// is edited with the camera framing and the HUD in view (and TutorialUIHoles can pick HUD parts).
// The level scene stays the active one; Play works the same as with it alone. Turn it off with
// Tools > Gold Miner > Open Game Scene With Levels.
[InitializeOnLoad]
static class LevelSceneWithGame
{
    const string MenuPath = "Tools/Gold Miner/Open Game Scene With Levels";
    const string PrefKey = "GoldMiner.OpenGameWithLevels";
    const string GamePath = "Assets/Scenes/Game.unity";
    const string LevelDir = "Assets/Scenes/Levels/";

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static LevelSceneWithGame()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (!Enabled || mode != OpenSceneMode.Single || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!scene.path.StartsWith(LevelDir)) return;

        // After the open has finished: opening another scene from inside the callback is not safe
        EditorApplication.delayCall += () =>
        {
            if (!scene.isLoaded || SceneManager.GetSceneByPath(GamePath).isLoaded) return;
            EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
        };
    }

    [MenuItem(MenuPath, false, 140)]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
