using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Menu: PSX > Build Windows. Also callable from the command line:
/// Unity.exe -batchmode -projectPath . -executeMethod PSXBuild.BuildWindows -quit
/// Output: Builds/Windows/EVICTION.exe (Builds/ is gitignored). Ships only the EVICTION demo scene.
/// </summary>
public static class PSXBuild
{
    const string OutPath = "Builds/Windows/EVICTION.exe";

    [MenuItem("PSX/Build Windows")]
    public static void BuildWindows()
    {
        PlayerSettings.productName = "EVICTION";
        PlayerSettings.bundleVersion = "2.0";
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Eviction_Intro.unity" },
            locationPathName = OutPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        Debug.Log($"[PSX] Build {s.result}: {s.outputPath} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors)");

        if (Application.isBatchMode && s.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
