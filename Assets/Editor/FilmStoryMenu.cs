using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Owner: Claude (film / in-game screenings)
// Film story steps: a TutorialStep + TutorialDialogue whose Film (StreamingAssets/Films) plays full
// screen instead of lines. Tools > Gold Miner > Film Story Step adds one to the open scene; the
// Survival one is also built from the command line:
//   Unity -batchmode -quit -projectPath <proj> -executeMethod FilmStoryMenu.AddSurvivalDeathFilm
public static class FilmStoryMenu
{
    const string SurvivalScene = "Assets/Scenes/Levels/level_survival.unity";
    const string SurvivalStepName = "SurvivalDeathFilm";

    [MenuItem("Tools/Gold Miner/Film Story Step", false, 112)]
    static void CreateInOpenScene()
    {
        TutorialDialogue d = Create("FilmStory", "round_lost", "survival_death.mp4");
        Selection.activeGameObject = d.gameObject;
    }

    // The first time the player dies in Survival: Evil Meowmeow gloats (survival_death.mp4), then the
    // usual lost result ("R retries"). Once per save.
    public static void AddSurvivalDeathFilm()
    {
        var scene = EditorSceneManager.OpenScene(SurvivalScene, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform old = root.name == SurvivalStepName ? root.transform : root.transform.Find(SurvivalStepName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        Create(SurvivalStepName, "round_lost", "survival_death.mp4");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("FilmStoryMenu: added " + SurvivalStepName + " to " + SurvivalScene);
    }

    static TutorialDialogue Create(string name, string startEvent, string film)
    {
        Transform parent = null;
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Tutorial") parent = root.transform;
        if (parent == null) parent = new GameObject("Tutorial").transform;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Film Story Step");
        go.transform.SetParent(parent, false);

        TutorialStep step = go.AddComponent<TutorialStep>();
        step.startOn = TutorialStep.StartTrigger.Event;
        step.startEvent = startEvent;
        step.endOn = TutorialStep.EndTrigger.Manual;   // the film ends it
        step.maxTimes = 1;
        step.onlyFirstTime = true;
        step.oncePerSave = true;
        step.darken = false;                           // the film covers the screen itself

        TutorialDialogue dialogue = go.AddComponent<TutorialDialogue>();
        dialogue.film = film;
        dialogue.afterFilm = new string[0];
        dialogue.hideResult = true;                    // no "YOU LOSE" under the film...
        dialogue.leaveWhenDone = false;                // ...then the usual result: R retries
        dialogue.characters = new TutorialDialogue.Character[0];
        return dialogue;
    }
}
