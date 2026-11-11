using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    // Batch: Unity -batchmode -buildTarget WebGL -projectPath ... -executeMethod WebGLBuild.Run
    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("GOLD_MINER_WEB_OUTPUT");
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Builds/WebGL");
        string revision = Environment.GetEnvironmentVariable("GOLD_MINER_REVISION") ?? "local";
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0 || scenes[0] != "Assets/Scenes/Boot.unity")
            throw new InvalidOperationException("The browser build must start in Boot.");
        foreach (string scene in scenes)
            if (!File.Exists(scene)) throw new FileNotFoundException("Enabled build scene is missing", scene);

        // GitHub Pages serves static files without custom compression or isolation headers.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.threadsSupport = false;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.initialMemorySize = 128;
        PlayerSettings.WebGL.maximumMemorySize = 1024;
        PlayerSettings.WebGL.template = "PROJECT:GoldMiner";
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.bundleVersion = "gold-process";
        Directory.CreateDirectory(output);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Web build failed: " + report.summary.result + "; errors=" + report.summary.totalErrors);
        File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
        File.WriteAllText(Path.Combine(output, "build-info.json"), JsonUtility.ToJson(new BuildInfo
        {
            revision = revision, tag = "gold-process", unity = Application.unityVersion,
            builtUtc = DateTime.UtcNow.ToString("O")
        }, true));
        Debug.Log("GOLD_MINER_WEB_BUILD_PASS " + output + " bytes=" + report.summary.totalSize);
        EditorApplication.Exit(0);
    }

    [Serializable]
    class BuildInfo { public string revision, tag, unity, builtUtc; }
}
