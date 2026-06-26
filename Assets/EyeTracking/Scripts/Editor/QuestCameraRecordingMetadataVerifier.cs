using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class QuestCameraRecordingMetadataVerifier
{
    [MenuItem("EyeTracking/Diagnostics/Verify Quest Camera Metadata Json...")]
    public static void VerifyMetadataFromMenu()
    {
        string path = EditorUtility.OpenFilePanel(
            "Verify Quest Camera Metadata",
            Application.persistentDataPath,
            "json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        VerifyMetadataFile(path);
    }

    public static void VerifyMetadataFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Quest camera metadata json was not found.", path);
        }

        Metadata metadata = JsonUtility.FromJson<Metadata>(File.ReadAllText(path));
        if (metadata == null)
        {
            throw new InvalidOperationException("Quest camera metadata json could not be parsed.");
        }

        string failure = FirstFailure(metadata, path);
        if (!string.IsNullOrEmpty(failure))
        {
            throw new InvalidOperationException(
                $"Quest camera metadata verification failed: {failure}. path={path}");
        }

        Debug.Log(
            "[QuestCameraRecordingMetadataVerifier] OK " +
            $"samples={metadata.trajectorySampleCount} leftFrames={metadata.leftFrameCount} " +
            $"rightFrames={metadata.rightFrameCount} gazeHits={metadata.gazeHitSampleCount} path={path}");
    }

    private static string FirstFailure(Metadata metadata, string metadataPath)
    {
        if (metadata.schemaVersion != "quest_camera_recorder_metadata_v4" &&
            metadata.schemaVersion != "quest_camera_recorder_metadata_v5")
        {
            return "unexpected_schema_version";
        }

        if (!metadata.recordRightCamera)
        {
            return "right_camera_recording_disabled";
        }

        if (!metadata.recordSynchronizedTrajectory)
        {
            return "synchronized_trajectory_disabled";
        }

        if (metadata.leftFrameCount <= 0)
        {
            return "missing_left_video_frames";
        }

        if (metadata.rightFrameCount <= 0)
        {
            return "missing_right_video_frames";
        }

        if (metadata.trajectorySampleCount <= 0)
        {
            return "missing_trajectory_samples";
        }

        string directory = !string.IsNullOrEmpty(metadata.outputDirectory)
            ? metadata.outputDirectory
            : Path.GetDirectoryName(metadataPath);
        string trajectoryPath = Path.Combine(directory ?? string.Empty, metadata.trajectoryFileName ?? "trajectory.jsonl");
        if (!File.Exists(trajectoryPath))
        {
            return "missing_trajectory_jsonl";
        }

        bool hasLeftPassthroughPose = false;
        bool hasRightPassthroughPose = false;
        bool hasLeftEyePose = false;
        bool hasRightEyePose = false;
        bool hasGazePoint3D = false;
        bool hasEyeGazeVizPoint3D = false;
        bool hasLeftGazePointProjection = false;
        bool hasRightGazePointProjection = false;
        int parsedTrajectorySamples = 0;
        foreach (string line in File.ReadLines(trajectoryPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            parsedTrajectorySamples++;
            TrajectorySample sample = JsonUtility.FromJson<TrajectorySample>(line);
            if (sample == null)
            {
                return "invalid_trajectory_jsonl_sample";
            }

            hasLeftPassthroughPose |= sample.hasLeftCameraPose && IsPose(sample.leftPassthroughCameraPose);
            hasRightPassthroughPose |= sample.hasRightCameraPose && IsPose(sample.rightPassthroughCameraPose);
            hasLeftEyePose |= sample.hasLeftEyePose && IsPose(sample.leftEyePose) && IsPoint(sample.leftEyePosition);
            hasRightEyePose |= sample.hasRightEyePose && IsPose(sample.rightEyePose) && IsPoint(sample.rightEyePosition);
            hasGazePoint3D |= IsPoint(sample.gazePoint3DWorld);
            hasEyeGazeVizPoint3D |= IsPoint(sample.gazePoint3DWorld) &&
                                    sample.gazePoint3DSource == "eyegazepose_vizobj_transform";
            hasLeftGazePointProjection |= sample.hasLeftGazePointProjection &&
                                          IsPoint2(sample.leftGazePointPixel) &&
                                          IsPoint2(sample.leftGazePointViewport) &&
                                          IsFinite(sample.leftGazePointCameraZ);
            hasRightGazePointProjection |= sample.hasRightGazePointProjection &&
                                           IsPoint2(sample.rightGazePointPixel) &&
                                           IsPoint2(sample.rightGazePointViewport) &&
                                           IsFinite(sample.rightGazePointCameraZ);
        }

        if (parsedTrajectorySamples <= 0)
        {
            return "empty_trajectory_jsonl";
        }

        if (!hasLeftPassthroughPose)
        {
            return "missing_left_passthrough_camera_pose_samples";
        }

        if (!hasRightPassthroughPose)
        {
            return "missing_right_passthrough_camera_pose_samples";
        }

        if (metadata.recordHmdEyePoses && !hasLeftEyePose)
        {
            return "missing_left_eye_pose_samples";
        }

        if (metadata.recordHmdEyePoses && !hasRightEyePose)
        {
            return "missing_right_eye_pose_samples";
        }

        if (!hasGazePoint3D)
        {
            return "missing_3d_gaze_point_samples";
        }

        if (!hasEyeGazeVizPoint3D)
        {
            return "missing_eyegazepose_vizobj_3d_gaze_point_samples";
        }

        if (!hasLeftGazePointProjection)
        {
            return "missing_left_2d_gaze_projection_samples";
        }

        if (!hasRightGazePointProjection)
        {
            return "missing_right_2d_gaze_projection_samples";
        }

        return null;
    }

    private static bool IsPose(double[] values)
    {
        return values != null && values.Length == 7 && AllFinite(values);
    }

    private static bool IsPoint(double[] values)
    {
        return values != null && values.Length == 3 && AllFinite(values);
    }

    private static bool IsPoint2(double[] values)
    {
        return values != null && values.Length == 2 && AllFinite(values);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool AllFinite(double[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
            {
                return false;
            }
        }

        return true;
    }

    [Serializable]
    private sealed class Metadata
    {
        public string schemaVersion;
        public bool recordRightCamera;
        public bool recordSynchronizedTrajectory;
        public bool recordHmdEyePoses;
        public string outputDirectory;
        public string trajectoryFileName;
        public int leftFrameCount;
        public int rightFrameCount;
        public int trajectorySampleCount;
        public int gazeHitSampleCount;
    }

    [Serializable]
    private sealed class TrajectorySample
    {
        public bool hasLeftCameraPose;
        public bool hasRightCameraPose;
        public double[] leftPassthroughCameraPose;
        public double[] rightPassthroughCameraPose;
        public bool hasLeftEyePose;
        public bool hasRightEyePose;
        public double[] leftEyePose;
        public double[] rightEyePose;
        public double[] leftEyePosition;
        public double[] rightEyePosition;
        public bool hasGazeHit;
        public double[] gazePoint3DWorld;
        public string gazePoint3DSource;
        public bool hasLeftGazePointProjection;
        public double[] leftGazePointViewport;
        public double[] leftGazePointPixel;
        public double leftGazePointCameraZ;
        public bool hasRightGazePointProjection;
        public double[] rightGazePointViewport;
        public double[] rightGazePointPixel;
        public double rightGazePointCameraZ;
    }
}
