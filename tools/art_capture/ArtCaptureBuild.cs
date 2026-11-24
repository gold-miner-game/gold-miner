// Copied only into the disposable art-capture project (Assets/ArtCapture/Editor) by capture.ps1.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class ArtCaptureBuild
{
    public static void Build()
    {
        if (PlayerSettings.productName != "GoldMinerArtCapture")
            throw new InvalidOperationException("Build only in the isolated art-capture project.");
        if (!File.ReadAllText("Assets/Scripts/PlayAnalytics.cs").Contains("const string FormUrl = \"\";"))
            throw new InvalidOperationException("Disable analytics in the art-capture project first.");
        string exe = Environment.GetEnvironmentVariable("ART_CAPTURE_EXE");
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("Set ART_CAPTURE_EXE.");
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Art capture build failed: " + report.summary.result);
    }
}
