// Copied only into the disposable art-capture project by capture.ps1; never part of the game.
// Started with "-artcapture <folder>", it opens each scene named by "-artscenes A,B,C", waits
// "-artwait" seconds of real time, saves a full-screen PNG (sprites, uGUI and IMGUI alike) and quits.
// "-artframes N" also saves N more frames at 12 fps per scene (NAME_000.png...) to check motion.
// "-artpeel" presses the menu's main button (MainMenu.StartGame) before the Menu frames.
// A scene entry "NAME:action" also triggers a moment a plain load never shows, through the game's own
// methods, then waits "-arttriggerwait" seconds (default 2.5) and saves NAME-action.png:
//   win / fail / crush   GameManager.WinRound / EndRound(false) / Crush: result panels, CAUGHT
//   reward               SkillUnlockSummary.PlayEveryTime, then a win: the skill reward demo
//   pause                HUDController.OpenPause: the "Quit Game?" popup
//   confirm              MainMenu page = ConfirmNew: the New Game confirmation
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ArtCaptureRunner : MonoBehaviour
{
    string outDir;
    string[] scenes;
    float wait = 3f;
    int frames;
    bool peel;
    float triggerWait = 2.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string dir = null, list = null;
        float seconds = 3f;
        int count = 0;
        bool pressPeel = false;
        float triggerSeconds = 2.5f;
        for (int i = 0; i < args.Length; i++)
        {
            bool hasValue = i < args.Length - 1;
            if (args[i] == "-artcapture" && hasValue) dir = args[i + 1];
            else if (args[i] == "-artscenes" && hasValue) list = args[i + 1];
            else if (args[i] == "-artwait" && hasValue) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            else if (args[i] == "-artframes" && hasValue) int.TryParse(args[i + 1], out count);
            else if (args[i] == "-artpeel") pressPeel = true;
            else if (args[i] == "-arttriggerwait" && hasValue) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out triggerSeconds);
        }
        if (string.IsNullOrEmpty(dir)) return;
        var go = new GameObject("Art Capture");
        DontDestroyOnLoad(go);
        var runner = go.AddComponent<ArtCaptureRunner>();
        runner.outDir = dir;
        runner.scenes = (list ?? "Menu").Split(',');
        runner.wait = seconds;
        runner.frames = count;
        runner.peel = pressPeel;
        runner.triggerWait = triggerSeconds;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(outDir);
        Application.runInBackground = true;
        foreach (string raw in scenes)
        {
            string entry = raw.Trim();
            if (entry.Length == 0) continue;
            string[] parts = entry.Split(':');
            string scene = parts[0], action = parts.Length > 1 ? parts[1] : null;
            if (action == "reward") SkillUnlockSummary.PlayEveryTime = true;
            SceneManager.LoadScene(scene);
            float until = Time.realtimeSinceStartup + wait;
            while (Time.realtimeSinceStartup < until) yield return null;
            if (action != null)
            {
                Trigger(action);
                float after = Time.realtimeSinceStartup + triggerWait;
                while (Time.realtimeSinceStartup < after) yield return null;
                scene = scene + "-" + action;
            }
            yield return Shot(scene);
            if (frames <= 0) continue;
            if (peel && scene == "Menu")
            {
                var menu = FindFirstObjectByType<MainMenu>();
                MethodInfo start = typeof(MainMenu).GetMethod("StartGame", BindingFlags.Instance | BindingFlags.NonPublic);
                if (menu != null && start != null) start.Invoke(menu, null);
            }
            string loaded = parts[0];
            for (int f = 0; f < frames; f++)
            {
                float next = Time.realtimeSinceStartup + 1f / 12f;
                yield return Shot(scene + "_" + f.ToString("000"));
                while (Time.realtimeSinceStartup < next) yield return null;
                if (SceneManager.GetActiveScene().name != loaded && !(peel && scene == "Menu")) break;
            }
        }
        Debug.Log("ART_CAPTURE_DONE");
        Application.Quit();
    }

    static void Trigger(string action)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager gm = GameManager.Instance;
        switch (action)
        {
            case "win":
            case "reward":
                if (gm != null) gm.WinRound("art capture");
                break;
            case "fail":
                if (gm != null) typeof(GameManager).GetMethod("EndRound", Private)?.Invoke(gm, new object[] { false, "art capture" });
                break;
            case "crush":
                if (gm != null) gm.Crush("art capture");
                break;
            case "pause":
                var hud = FindFirstObjectByType<HUDController>();
                if (hud != null) typeof(HUDController).GetMethod("OpenPause", Private)?.Invoke(hud, null);
                break;
            case "confirm":
                var menu = FindFirstObjectByType<MainMenu>();
                FieldInfo page = typeof(MainMenu).GetField("page", Private);
                if (menu != null && page != null) page.SetValue(menu, Enum.Parse(page.FieldType, "ConfirmNew"));
                break;
        }
        Debug.Log("ART_CAPTURE_TRIGGER " + action);
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), shot.EncodeToPNG());
        Destroy(shot);
        Debug.Log("ART_CAPTURE_SAVED " + name);
    }
}
