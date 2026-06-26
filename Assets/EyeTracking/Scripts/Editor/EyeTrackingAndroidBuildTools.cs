using System.IO;
using EyeTracking.Recording;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EyeTrackingAndroidBuildTools
{
    private const string BuildRequestPath = "Build/EyeTrackingBuild/request_android_build.txt";
    private static bool buildRequestQueued;
    private static bool buildRequestRunning;
    private static double nextBuildWaitLogTime;

    private static readonly string[] EyeTrackingScenes =
    {
        "Assets/EyeTracking/EyeTrackingScene_RecordQuestCamera.unity",
        "Assets/EyeTracking/EyeTrackingScene.unity"
    };

    [InitializeOnLoadMethod]
    private static void QueueRequestedBuild()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        EditorApplication.update -= PollForRequestedBuild;
        EditorApplication.update += PollForRequestedBuild;
        PollForRequestedBuild();
    }

    private static void PollForRequestedBuild()
    {
        if (!File.Exists(BuildRequestPath))
        {
            buildRequestQueued = false;
            return;
        }

        if (buildRequestRunning)
        {
            return;
        }

        if (!buildRequestQueued)
        {
            buildRequestQueued = true;
            nextBuildWaitLogTime = 0;
            Debug.Log("[EyeTrackingAndroidBuildTools] Android build request detected; waiting for active Unity Editor to become idle.");
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextBuildWaitLogTime)
            {
                nextBuildWaitLogTime = now + 10.0;
                Debug.Log(
                    "[EyeTrackingAndroidBuildTools] Waiting before Android build. " +
                    $"isCompiling={EditorApplication.isCompiling} isUpdating={EditorApplication.isUpdating}");
            }

            return;
        }

        RunRequestedBuild();
    }

    private static void RunRequestedBuild()
    {
        if (buildRequestRunning)
        {
            return;
        }

        buildRequestRunning = true;
        try
        {
            File.Delete(BuildRequestPath);
            BuildAndroidApk();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            buildRequestQueued = false;
            buildRequestRunning = false;
        }
    }

    [MenuItem("EyeTracking/Build/Build Android APK")]
    public static void BuildAndroidApkMenu()
    {
        BuildAndroidApk();
    }

    public static void BuildAndroidApk()
    {
        const string outputDirectory = "Build/EyeTrackingBuild";
        const string outputPath = outputDirectory + "/EyeTrackingTest.apk";

        Directory.CreateDirectory(outputDirectory);
        RemoveObsoleteAndroidResFolder();
        QuestCameraRecordingSceneSetup.ConfigureSceneForBuild();

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.stripUnusedMeshComponents = false;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EyeTrackingScenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"[EyeTrackingAndroidBuildTools] Build result={summary.result} output={outputPath} size={summary.totalSize}");

        if (summary.result != BuildResult.Succeeded)
        {
            throw new System.Exception($"EyeTracking Android build failed: {summary.result}");
        }
    }

    public static void BuildTelemetrySmokeAutoStartApk()
    {
        const string outputDirectory = "Build/EyeTrackingBuild";
        const string outputPath = outputDirectory + "/EyeTrackingTestTelemetrySmoke.apk";

        Directory.CreateDirectory(outputDirectory);
        RemoveObsoleteAndroidResFolder();
        QuestCameraRecordingSceneSetup.BeginTelemetrySmokeBuildAutoStartMode();

        try
        {
            QuestCameraRecordingSceneSetup.ConfigureSceneForBuild(autoStartRecording: true);
            VerifyAutoStartRecordingEnabled(EyeTrackingScenes[0]);

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.stripUnusedMeshComponents = false;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = EyeTrackingScenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[EyeTrackingAndroidBuildTools] Telemetry smoke build result={summary.result} output={outputPath} size={summary.totalSize}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new System.Exception($"EyeTracking telemetry smoke Android build failed: {summary.result}");
            }
        }
        finally
        {
            QuestCameraRecordingSceneSetup.EndTelemetrySmokeBuildAutoStartMode();
            QuestCameraRecordingSceneSetup.ConfigureSceneForBuild(autoStartRecording: false);
        }
    }

    private static void RemoveObsoleteAndroidResFolder()
    {
        const string resFolder = "Assets/Plugins/Android/res";
        const string resMeta = "Assets/Plugins/Android/res.meta";

        if (Directory.Exists(resFolder))
        {
            Directory.Delete(resFolder, true);
        }

        if (File.Exists(resMeta))
        {
            File.Delete(resMeta);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    }

    private static void VerifyAutoStartRecordingEnabled(string scenePath)
    {
        EditorSceneManager.OpenScene(scenePath);
        QuestCameraRecorder recorder = Object.FindFirstObjectByType<QuestCameraRecorder>();
        if (recorder == null)
        {
            throw new System.Exception("Telemetry smoke build could not find QuestCameraRecorder.");
        }

        SerializedObject serializedRecorder = new SerializedObject(recorder);
        SerializedProperty autoStart = serializedRecorder.FindProperty("autoStartRecording");
        if (autoStart == null || !autoStart.boolValue)
        {
            throw new System.Exception("Telemetry smoke build autoStartRecording was not enabled.");
        }
    }
}
