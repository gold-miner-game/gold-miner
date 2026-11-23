// Copied only into the disposable art-capture project by capture.ps1; never part of the game.
// Started with "-artcapture <folder>", it opens each scene named by "-artscenes A,B,C", waits
// "-artwait" seconds of real time, saves a full-screen PNG (sprites, uGUI and IMGUI alike) and quits.
// "-artframes N" also saves N more frames at 12 fps per scene (NAME_000.png...) to check motion.
// "-artpeel" presses the menu's main button (MainMenu.StartGame) before the Menu frames.
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string dir = null, list = null;
        float seconds = 3f;
        int count = 0;
        bool pressPeel = false;
        for (int i = 0; i < args.Length; i++)
        {
            bool hasValue = i < args.Length - 1;
            if (args[i] == "-artcapture" && hasValue) dir = args[i + 1];
            else if (args[i] == "-artscenes" && hasValue) list = args[i + 1];
            else if (args[i] == "-artwait" && hasValue) float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            else if (args[i] == "-artframes" && hasValue) int.TryParse(args[i + 1], out count);
            else if (args[i] == "-artpeel") pressPeel = true;
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
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(outDir);
        Application.runInBackground = true;
        foreach (string raw in scenes)
        {
            string scene = raw.Trim();
            if (scene.Length == 0) continue;
            SceneManager.LoadScene(scene);
            float until = Time.realtimeSinceStartup + wait;
            while (Time.realtimeSinceStartup < until) yield return null;
            yield return Shot(scene);
            if (frames <= 0) continue;
            if (peel && scene == "Menu")
            {
                var menu = FindFirstObjectByType<MainMenu>();
                MethodInfo start = typeof(MainMenu).GetMethod("StartGame", BindingFlags.Instance | BindingFlags.NonPublic);
                if (menu != null && start != null) start.Invoke(menu, null);
            }
            for (int f = 0; f < frames; f++)
            {
                float next = Time.realtimeSinceStartup + 1f / 12f;
                yield return Shot(scene + "_" + f.ToString("000"));
                while (Time.realtimeSinceStartup < next) yield return null;
                if (SceneManager.GetActiveScene().name != scene && !(peel && scene == "Menu")) break;
            }
        }
        Debug.Log("ART_CAPTURE_DONE");
        Application.Quit();
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
