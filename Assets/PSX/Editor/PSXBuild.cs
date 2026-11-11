using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Menu: PSX > Build Windows. Also callable from the command line:
/// Unity.exe -batchmode -projectPath . -executeMethod PSXBuild.BuildWindows -quit
/// Output: Builds/Windows/PSX-Test.exe (Builds/ is gitignored).
/// </summary>
public static class PSXBuild
{
    const string OutPath = "Builds/Windows/PSX-Test.exe";

    [MenuItem("PSX/Build Windows")]
    public static void BuildWindows()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/PSXCity.unity" },
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
