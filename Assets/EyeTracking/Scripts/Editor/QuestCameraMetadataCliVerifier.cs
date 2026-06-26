using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class QuestCameraMetadataCliVerifier
{
    public static void VerifyMetadataFromCommandLine()
    {
        string path = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-metadataPath")
            {
                path = args[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Missing -metadataPath <quest_camera_metadata.json>.");
        }

        QuestCameraRecordingMetadataVerifier.VerifyMetadataFile(path);
        Debug.Log("[QuestCameraMetadataCliVerifier] OK " + Path.GetFullPath(path));
    }
}
