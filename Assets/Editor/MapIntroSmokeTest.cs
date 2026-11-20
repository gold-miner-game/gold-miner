using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public static class MapIntroSmokeTest
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/Temp/ConsoleSmokeProject/Assets"))
            throw new Exception("Run in the isolated smoke-test project.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MapIntroRegression";
        const string key = "tutorial_seen/MapIntroRegression/Story", listKey = "tutorial_seen_list";
        bool hadKey = PlayerPrefs.HasKey(key), hadList = PlayerPrefs.HasKey(listKey);
        int saved = PlayerPrefs.GetInt(key); string list = PlayerPrefs.GetString(listKey);
        bool replay = TutorialStep.ReplaySeen;
        GameObject go = null;
        try
        {
            PlayerPrefs.DeleteKey(key); PlayerPrefs.SetString(listKey, "");
            TutorialStep.ReplaySeen = true;
            Func<TutorialStep> create = () =>
            {
                go = new GameObject("Story"); SceneManager.MoveGameObjectToScene(go, scene);
                var step = go.AddComponent<TutorialStep>(); step.onlyFirstTime = true; step.oncePerSave = true;
                step.freezeGame = false; step.darken = false; step.onStart = new UnityEvent();
                typeof(TutorialStep).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(step, null);
                return step;
            };
            var first = create(); first.Play();
            Check(PlayerPrefs.GetInt(key) == 1, "Intro must save immediately before completion");
            UnityEngine.Object.DestroyImmediate(go); go = null;
            var continued = create(); continued.Play();
            Check(continued.Current == TutorialStep.State.Done && continued.TimesPlayed == 0,
                "Continue must skip an interrupted intro even with editor replay enabled");
            UnityEngine.Object.DestroyImmediate(go); go = null;
            TutorialStep.ResetSeen();
            var fresh = create();
            Check(fresh.Current == TutorialStep.State.Waiting, "New save reset must allow the intro again");
            fresh.Play(); Check(fresh.TimesPlayed == 1, "Fresh save must play its intro");
            Debug.Log("MAP INTRO PASS: immediate save, interrupted Continue, editor replay, new save reset");
        }
        finally
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (hadKey) PlayerPrefs.SetInt(key, saved); else PlayerPrefs.DeleteKey(key);
            if (hadList) PlayerPrefs.SetString(listKey, list); else PlayerPrefs.DeleteKey(listKey);
            PlayerPrefs.Save(); TutorialStep.ReplaySeen = replay;
        }
    }
}
