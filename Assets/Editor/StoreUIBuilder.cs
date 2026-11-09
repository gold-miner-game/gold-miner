using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Owner: Zhan (store)
// Tools > Gold Miner > Build Store UI: lays the store out as objects in the open scene (the Store
// scene): the wallet bar with the Leave button, the counter the goods stand on, and the tip, all
// under StoreView > StoreCanvas. Move and restyle them in the Scene view; StoreView uses them when
// the store opens. The goods are still made when it opens, one per offer under the Counter.
// Running it again does nothing while the store is there: delete StoreCanvas to build it afresh.
public static class StoreUIBuilder
{
    [MenuItem("Tools/Gold Miner/Build Store UI")]
    static void Build()
    {
        StoreView view = Object.FindFirstObjectByType<StoreView>(FindObjectsInactive.Include);
        if (view == null)
        {
            view = new GameObject("StoreView").AddComponent<StoreView>();
            Undo.RegisterCreatedObjectUndo(view.gameObject, "Build Store UI");
        }
        if (view.transform.Find("StoreCanvas") != null)
        {
            EditorUtility.DisplayDialog("Store UI is already here",
                "StoreView already has a StoreCanvas. Edit it in the Scene view, or delete StoreCanvas and run this again to start over.", "OK");
            Selection.activeGameObject = view.gameObject;
            return;
        }

        view.BuildInScene();
        foreach (Transform child in view.GetComponentsInChildren<Transform>(true))
            if (child != view.transform) Undo.RegisterCreatedObjectUndo(child.gameObject, "Build Store UI");
        EditorUtility.SetDirty(view);

        // The scene's opener (shows the store and handles Leave)
        if (Object.FindFirstObjectByType<StorePlaceholder>() == null)
        {
            var opener = new GameObject("StorePlaceholder");
            opener.AddComponent<StorePlaceholder>();
            Undo.RegisterCreatedObjectUndo(opener, "Build Store UI");
        }

        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Selection.activeGameObject = view.gameObject;
    }

    [MenuItem("Tools/Gold Miner/Build Store UI", true)]
    static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // Only First Time steps (the store's introduction) play again on the next visit
    [MenuItem("Tools/Gold Miner/Reset Tutorials Seen", false, 131)]
    static void ResetSeen()
    {
        TutorialStep.ResetSeen();
        Debug.Log("Tutorials seen were reset: Only First Time steps will play again.");
    }

    // A tick in the menu: while it's on, Only First Time steps play every time (for testing in the
    // editor; a built game still plays them once)
    const string ReplayKey = "GoldMiner.ReplaySeenTutorials";
    const string ReplayMenu = "Tools/Gold Miner/Replay Only First Time Steps";

    [InitializeOnLoadMethod]
    static void RestoreReplay() { TutorialStep.ReplaySeen = EditorPrefs.GetBool(ReplayKey, false); }

    [MenuItem(ReplayMenu, false, 132)]
    static void ToggleReplay()
    {
        TutorialStep.ReplaySeen = !TutorialStep.ReplaySeen;
        EditorPrefs.SetBool(ReplayKey, TutorialStep.ReplaySeen);
    }

    [MenuItem(ReplayMenu, true)]
    static bool ToggleReplayCheck()
    {
        Menu.SetChecked(ReplayMenu, TutorialStep.ReplaySeen);
        return true;
    }
}
