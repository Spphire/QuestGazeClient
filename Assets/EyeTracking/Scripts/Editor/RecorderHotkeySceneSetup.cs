using EyeTracking.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RecorderHotkeySceneSetup
{
    private const string ScenePath = "Assets/EyeTracking/EyeTrackingScene_RecordQuestCamera.unity";
    private const string ControllerName = "RecorderHotkeyController";
    private const string RecordDotName = "Record_Dot";
    private const string CalibrationDotName = "Calibration_Record_Dot";

    [MenuItem("EyeTracking/Recording/Setup Recorder Hotkey Controller")]
    public static void SetupOpenScene()
    {
        SetupInScene(SceneManager.GetActiveScene().path);
    }

    public static void SetupDefaultSceneFromCommandLine()
    {
        SetupInScene(ScenePath);
    }

    private static void SetupInScene(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            scenePath = ScenePath;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject controller = GameObject.Find(ControllerName);
        if (controller == null)
        {
            controller = new GameObject(ControllerName);
        }

        QuestCameraRecorderHotkeys hotkeys = controller.GetComponent<QuestCameraRecorderHotkeys>();
        if (hotkeys == null)
        {
            hotkeys = controller.AddComponent<QuestCameraRecorderHotkeys>();
        }

        SerializedObject serializedHotkeys = new SerializedObject(hotkeys);
        serializedHotkeys.FindProperty("recorder").objectReferenceValue =
            Object.FindFirstObjectByType<QuestCameraRecorder>();
        serializedHotkeys.FindProperty("calibrationRecorder").objectReferenceValue =
            Object.FindFirstObjectByType<QuestPcCalibrationRecorder>();
        serializedHotkeys.FindProperty("recordDot").objectReferenceValue =
            FindSceneObjectByName(RecordDotName);
        serializedHotkeys.FindProperty("calibrationDot").objectReferenceValue =
            FindSceneObjectByName(CalibrationDotName);
        serializedHotkeys.FindProperty("recordDotName").stringValue = RecordDotName;
        serializedHotkeys.FindProperty("calibrationDotName").stringValue = CalibrationDotName;
        serializedHotkeys.FindProperty("findRecordDotByName").boolValue = true;
        serializedHotkeys.FindProperty("createRecordDotIfMissing").boolValue = true;
        serializedHotkeys.FindProperty("ignoreInputWhileSystemMenuHeld").boolValue = true;
        serializedHotkeys.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[RecorderHotkeySceneSetup] Recorder hotkey controller configured in {scenePath}.");
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        GameObject activeObject = GameObject.Find(objectName);
        if (activeObject != null)
        {
            return activeObject;
        }

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject candidate = allObjects[i];
            if (candidate != null &&
                candidate.name == objectName &&
                candidate.scene.IsValid())
            {
                return candidate;
            }
        }

        return null;
    }
}
