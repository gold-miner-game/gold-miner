// Copied only into the disposable art-capture project by capture.ps1; never part of the game.
// Started with "-artcapture <folder>", it opens each scene named by "-artscenes A,B,C", waits
// "-artwait" seconds of real time, saves a full-screen PNG (sprites, uGUI and IMGUI alike) and quits.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ArtCaptureRunner : MonoBehaviour
{
    string outDir;
    string[] scenes;
    float wait = 3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string dir = null, list = null;
        float seconds = 3f;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-artcapture") dir = args[i + 1];
            else if (args[i] == "-artscenes") list = args[i + 1];
            else if (args[i] == "-artwait") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }
        if (string.IsNullOrEmpty(dir)) return;
        var go = new GameObject("Art Capture");
        DontDestroyOnLoad(go);
        var runner = go.AddComponent<ArtCaptureRunner>();
        runner.outDir = dir;
        runner.scenes = (list ?? "Menu").Split(',');
        runner.wait = seconds;
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
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, scene + ".png"), shot.EncodeToPNG());
            Destroy(shot);
            Debug.Log("ART_CAPTURE_SAVED " + scene);
        }
        Debug.Log("ART_CAPTURE_DONE");
        Application.Quit();
    }
}
