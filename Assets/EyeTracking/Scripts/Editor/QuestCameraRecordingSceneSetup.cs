using EyeTracking.Recording;
using EyeTracking.UI;
using EyeTracking;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class QuestCameraRecordingSceneSetup
{
    private const int RecorderPanelLayer = 5;
    private const string ScenePath = "Assets/EyeTracking/EyeTrackingScene_RecordQuestCamera.unity";
    private const string MainScenePath = "Assets/EyeTracking/EyeTrackingScene.unity";
    private static readonly string[] ScenePaths = { ScenePath, MainScenePath };
    private const string AutoSetupSessionKey = "EyeTracking.QuestCameraRecordingSceneSetup.AutoSetupCompleted.v50";
    private const string SetupVersion = "v50-recorder-telemetry-hands-and-controllers-20260616";
    private const string PhysicalSurfaceName = "PhysicalInteractionSurface";
    private const string MeshVisualRootName = "RecorderMeshVisuals";
    private static bool forceAutoStartRecordingForCurrentBuild;

    [InitializeOnLoadMethod]
    private static void AutoSetupAfterScriptsReload()
    {
        if (Application.isBatchMode || SessionState.GetBool(AutoSetupSessionKey, false))
        {
            return;
        }

        Debug.Log("[QuestCameraRecordingSceneSetup] Loaded " + SetupVersion + "; scheduling recorder scene safety setup.");
        SessionState.SetBool(AutoSetupSessionKey, true);
        EditorApplication.delayCall += DelayedAutoSetup;
    }

    private static void DelayedAutoSetup()
    {
        EditorApplication.delayCall -= DelayedAutoSetup;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            SessionState.SetBool(AutoSetupSessionKey, false);
            EditorApplication.delayCall += DelayedAutoSetup;
            return;
        }

        ConfigureScene(saveScene: true, restoreActiveScene: true);
    }

    [MenuItem("EyeTracking/Diagnostics/Print Quest Camera Recording Setup Version")]
    public static void PrintSetupVersion()
    {
        Debug.Log("[QuestCameraRecordingSceneSetup] Active editor setup version: " + SetupVersion);
    }

    [MenuItem("EyeTracking/Diagnostics/Validate Quest Camera Recording Scene")]
    public static void ValidateRecordingScene()
    {
        ConfigureScene(saveScene: true, restoreActiveScene: false);
        GameObject panel = GameObject.Find("QuestCameraRecorderPanel") ?? GameObject.Find("RecorderUIEmptyPanel");
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        bool hasPointableCanvasModule = eventSystem != null && eventSystem.GetComponent<PointableCanvasModule>() != null;
        QuestCameraRecorder recorder = Object.FindFirstObjectByType<QuestCameraRecorder>();
        QuestCameraRecorderCommandBridge commandBridge = Object.FindFirstObjectByType<QuestCameraRecorderCommandBridge>();
        QuestCameraRecorderHotkeys hotkeys = Object.FindFirstObjectByType<QuestCameraRecorderHotkeys>();

        Debug.Log(
            "[QuestCameraRecordingSceneSetup] Validation " + SetupVersion +
            $" recorder={recorder != null}" +
            $" commandBridge={commandBridge != null}" +
            $" hotkeys={hotkeys != null}" +
            $" recorderPanelRemoved={panel == null}" +
            $" pointableCanvasModule={hasPointableCanvasModule}");
    }
    [MenuItem("EyeTracking/Setup Quest Camera Recording Scene")]
    public static void SetupScene()
    {
        ConfigureScene(saveScene: true, restoreActiveScene: false);
    }

    [MenuItem("EyeTracking/UI/Remove Recorder Panel From Recording Scene")]
    public static void RebuildSafeRecorderPanel()
    {
        ConfigureScene(saveScene: true, restoreActiveScene: false);
    }

    public static void ConfigureSceneForBuild()
    {
        ConfigureSceneForBuild(forceAutoStartRecordingForCurrentBuild);
    }

    public static void ConfigureSceneForBuild(bool autoStartRecording)
    {
        ConfigureScene(saveScene: true, restoreActiveScene: true, autoStartRecording: autoStartRecording);
    }

    public static void BeginTelemetrySmokeBuildAutoStartMode()
    {
        forceAutoStartRecordingForCurrentBuild = true;
    }

    public static void EndTelemetrySmokeBuildAutoStartMode()
    {
        forceAutoStartRecordingForCurrentBuild = false;
    }

    private static void ConfigureScene(bool saveScene, bool restoreActiveScene, bool autoStartRecording = false)
    {
        ConfigureOculusProjectConfig();

        var previouslyActiveScene = EditorSceneManager.GetActiveScene();
        string previousScenePath = previouslyActiveScene.path;
        for (int i = 0; i < ScenePaths.Length; i++)
        {
            ConfigureSingleScene(ScenePaths[i], saveScene, autoStartRecording);
        }

        if (restoreActiveScene &&
            !string.IsNullOrEmpty(previousScenePath) &&
            SceneAssetExists(previousScenePath))
        {
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
        }
    }

    private static void ConfigureOculusProjectConfig()
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
        {
            Debug.LogWarning("[QuestCameraRecordingSceneSetup] OculusProjectConfig is missing; cannot configure hand tracking support.");
            return;
        }

        bool changed = false;
        if (projectConfig.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersAndHands)
        {
            projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            changed = true;
        }

        if (projectConfig.handTrackingFrequency != OVRProjectConfig.HandTrackingFrequency.MAX)
        {
            projectConfig.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.MAX;
            changed = true;
        }

        if (!projectConfig.isPassthroughCameraAccessEnabled)
        {
            projectConfig.isPassthroughCameraAccessEnabled = true;
            changed = true;
        }

        if (projectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.Supported)
        {
            projectConfig.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            changed = true;
        }

        if (projectConfig.renderModelSupport != OVRProjectConfig.RenderModelSupport.Disabled)
        {
            projectConfig.renderModelSupport = OVRProjectConfig.RenderModelSupport.Disabled;
            changed = true;
        }

        if (projectConfig.trackedKeyboardSupport != OVRProjectConfig.TrackedKeyboardSupport.None)
        {
            projectConfig.trackedKeyboardSupport = OVRProjectConfig.TrackedKeyboardSupport.None;
            changed = true;
        }

        if (projectConfig.requiresSystemKeyboard)
        {
            projectConfig.requiresSystemKeyboard = false;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        OVRProjectConfig.CommitProjectConfig(projectConfig);
        AssetDatabase.SaveAssets();
        Debug.Log("[QuestCameraRecordingSceneSetup] OculusProjectConfig configured for ControllersAndHands, MAX hand tracking, optional passthrough, passthrough camera access, and no tracked keyboard requirement.");
    }

    private static void ConfigureSingleScene(string scenePath, bool saveScene, bool autoStartRecording)
    {
        if (!SceneAssetExists(scenePath))
        {
            Debug.LogWarning("[QuestCameraRecordingSceneSetup] Scene not found: " + scenePath);
            return;
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        PassthroughCameraAccess left = GetOrCreateCameraAccess(
            "LeftCameraAccess",
            PassthroughCameraAccess.CameraPositionType.Left);

        PassthroughCameraAccess right = GetOrCreateCameraAccess(
            "RightCameraAccess",
            PassthroughCameraAccess.CameraPositionType.Right);

        var recorderObject = GameObject.Find("QuestCameraRecorder");
        if (recorderObject == null)
        {
            recorderObject = new GameObject("QuestCameraRecorder");
        }

        var recorder = recorderObject.GetComponent<QuestCameraRecorder>();
        if (recorder == null)
        {
            recorder = recorderObject.AddComponent<QuestCameraRecorder>();
        }

        var telemetrySender = recorderObject.GetComponent<QuestRecordingTelemetrySender>();
        if (telemetrySender == null)
        {
            telemetrySender = recorderObject.AddComponent<QuestRecordingTelemetrySender>();
        }

        var pcCalibrationRecorder = recorderObject.GetComponent<QuestPcCalibrationRecorder>();
        if (pcCalibrationRecorder == null)
        {
            pcCalibrationRecorder = recorderObject.AddComponent<QuestPcCalibrationRecorder>();
        }

        var pcaRelay = recorderObject.GetComponent<QuestPcaCheckerboardRelay>();
        if (pcaRelay == null)
        {
            pcaRelay = recorderObject.AddComponent<QuestPcaCheckerboardRelay>();
        }

        ConfigureTelemetrySender(telemetrySender);
        ConfigurePcCalibrationRecorder(pcCalibrationRecorder, recorder, left, right);
        ConfigurePcaRelay(pcaRelay, left, right);

        var commandBridge = recorderObject.GetComponent<QuestCameraRecorderCommandBridge>();
        if (commandBridge == null)
        {
            commandBridge = recorderObject.AddComponent<QuestCameraRecorderCommandBridge>();
        }

        var serializedRecorder = new SerializedObject(recorder);
        serializedRecorder.FindProperty("leftCameraAccess").objectReferenceValue = left;
        serializedRecorder.FindProperty("rightCameraAccess").objectReferenceValue = right;
        serializedRecorder.FindProperty("recordRightCamera").boolValue = true;
        serializedRecorder.FindProperty("fps").intValue = 60;
        serializedRecorder.FindProperty("autoStartRecording").boolValue = autoStartRecording;
        serializedRecorder.FindProperty("recordSynchronizedTrajectory").boolValue = true;
        SetObjectReference(serializedRecorder, "telemetrySender", telemetrySender);
        SetObjectReference(serializedRecorder, "gazeReceiver", Object.FindFirstObjectByType<PaperTrackerOscReceiver>());
        SetFloat(serializedRecorder, "gazeRayOffset", 0.1f);
        SetFloat(serializedRecorder, "gazeMaxDistance", 10f);
        serializedRecorder.ApplyModifiedPropertiesWithoutUndo();

        ConfigureCommandBridge(commandBridge, recorder);
        ConfigureRecorderHotkeys(recorder, pcCalibrationRecorder);

        foreach (var manager in Object.FindObjectsByType<OVRManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None))
        {
            var serializedManager = new SerializedObject(manager);
            var requestCameraAccess = serializedManager.FindProperty("requestPassthroughCameraAccessPermissionOnStartup");
            if (requestCameraAccess != null)
            {
                requestCameraAccess.boolValue = true;
            }

            var simultaneousHandsAndControllersEnabled = serializedManager.FindProperty("SimultaneousHandsAndControllersEnabled");
            if (simultaneousHandsAndControllersEnabled != null)
            {
                simultaneousHandsAndControllersEnabled.boolValue = true;
            }

            var launchSimultaneousHandsControllersOnStartup = serializedManager.FindProperty("launchSimultaneousHandsControllersOnStartup");
            if (launchSimultaneousHandsControllersOnStartup != null)
            {
                launchSimultaneousHandsControllersOnStartup.boolValue = true;
            }

            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        int removedPanelCount = RemoveRecorderPanelsFromScene();
        ConfigureBoxAuthoringTriggerCombo();
        EnsurePointableCanvasModule();
        EnableControllerRays();
        EnsureRayInteractableSelectSurfaces();

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
        {
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[QuestCameraRecordingSceneSetup] Scene configured and saved. recorderPanelsRemoved={removedPanelCount}: {scenePath}");
    }

    private static void ConfigureTelemetrySender(QuestRecordingTelemetrySender telemetrySender)
    {
        if (telemetrySender == null)
        {
            return;
        }

        OVRCameraRig cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
        Transform trackingSpace = cameraRig != null && cameraRig.trackingSpace != null
            ? cameraRig.trackingSpace
            : FindChildRecursive(cameraRig != null ? cameraRig.transform : null, "TrackingSpace");
        Transform leftControllerAnchor = cameraRig != null && cameraRig.leftControllerAnchor != null
            ? cameraRig.leftControllerAnchor
            : FindChildRecursive(cameraRig != null ? cameraRig.transform : null, "LeftControllerAnchor");
        Transform rightControllerAnchor = cameraRig != null && cameraRig.rightControllerAnchor != null
            ? cameraRig.rightControllerAnchor
            : FindChildRecursive(cameraRig != null ? cameraRig.transform : null, "RightControllerAnchor");

        SerializedObject serializedSender = new SerializedObject(telemetrySender);
        SetBool(serializedSender, "sendTelemetry", true);
        SetString(serializedSender, "host", "10.128.0.227");
        SetInt(serializedSender, "port", 9100);
        SetBool(serializedSender, "autoResolveTrackingSpace", true);
        SetObjectReference(serializedSender, "trackingSpace", trackingSpace);
        SetBool(serializedSender, "autoResolveControllerAnchors", true);
        SetObjectReference(serializedSender, "leftControllerAnchor", leftControllerAnchor);
        SetObjectReference(serializedSender, "rightControllerAnchor", rightControllerAnchor);
        SetBool(serializedSender, "autoResolveInteractionControllerRefs", true);
        SetInt(serializedSender, "maxPacketsPerSecond", 90);
        SetBool(serializedSender, "sendLifecycleMessages", true);
        SetBool(serializedSender, "logTelemetryStatus", true);
        SetInt(serializedSender, "sampleStatusLogInterval", 120);
        serializedSender.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(telemetrySender);
    }

    private static void ConfigurePcaRelay(
        QuestPcaCheckerboardRelay pcaRelay,
        PassthroughCameraAccess leftCameraAccess,
        PassthroughCameraAccess rightCameraAccess)
    {
        if (pcaRelay == null)
        {
            return;
        }

        SerializedObject serializedRelay = new SerializedObject(pcaRelay);
        SetObjectReference(serializedRelay, "leftCameraAccess", leftCameraAccess);
        SetObjectReference(serializedRelay, "rightCameraAccess", rightCameraAccess);
        SetBool(serializedRelay, "sendLeft", true);
        SetBool(serializedRelay, "sendRight", true);
        SetBool(serializedRelay, "relayEnabled", false);
        SetString(serializedRelay, "serverUrl", "http://10.128.0.227:9101");
        SetFloat(serializedRelay, "intervalSeconds", 0.5f);
        SetInt(serializedRelay, "jpegQuality", 70);
        SetBool(serializedRelay, "viewportFlipY", true);
        SetFloat(serializedRelay, "roundTripDistanceMeters", 1.0f);
        SetBool(serializedRelay, "logStatus", true);
        SetBool(serializedRelay, "enableFileCommands", true);
        SetString(serializedRelay, "commandFileName", "pca_relay_command.txt");
        SetFloat(serializedRelay, "commandPollIntervalSeconds", 0.25f);
        SetBool(serializedRelay, "deleteCommandAfterRead", true);
        serializedRelay.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pcaRelay);
    }

    private static void ConfigurePcCalibrationRecorder(
        QuestPcCalibrationRecorder pcCalibrationRecorder,
        QuestCameraRecorder recorder,
        PassthroughCameraAccess leftCameraAccess,
        PassthroughCameraAccess rightCameraAccess)
    {
        if (pcCalibrationRecorder == null)
        {
            return;
        }

        SerializedObject serializedRecorder = new SerializedObject(pcCalibrationRecorder);
        SetObjectReference(serializedRecorder, "recorder", recorder);
        SetObjectReference(serializedRecorder, "leftCameraAccess", leftCameraAccess);
        SetObjectReference(serializedRecorder, "rightCameraAccess", rightCameraAccess);
        SetBool(serializedRecorder, "recordRightCamera", true);
        SetString(serializedRecorder, "serverUrl", "http://10.128.0.227:9101");
        SetFloat(serializedRecorder, "frameIntervalSeconds", 1f / 15f);
        SetInt(serializedRecorder, "jpegQuality", 75);
        SetBool(serializedRecorder, "flipVertical", true);
        SetBool(serializedRecorder, "logStatus", true);
        serializedRecorder.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pcCalibrationRecorder);
    }

    private static void ConfigureCommandBridge(QuestCameraRecorderCommandBridge commandBridge, QuestCameraRecorder recorder)
    {
        if (commandBridge == null)
        {
            return;
        }

        SerializedObject serializedBridge = new SerializedObject(commandBridge);
        SetObjectReference(serializedBridge, "recorder", recorder);
        SetBool(serializedBridge, "enableFileCommands", true);
        SetString(serializedBridge, "commandFileName", "record_command.txt");
        SetFloat(serializedBridge, "pollIntervalSeconds", 0.25f);
        SetFloat(serializedBridge, "startReadyTimeoutSeconds", 30f);
        SetBool(serializedBridge, "deleteCommandAfterRead", true);
        SetBool(serializedBridge, "logCommandStatus", true);
        serializedBridge.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(commandBridge);
    }

    private static void ConfigureRecorderHotkeys(
        QuestCameraRecorder recorder,
        QuestPcCalibrationRecorder pcCalibrationRecorder)
    {
        const string controllerName = "RecorderHotkeyController";
        GameObject controller = GameObject.Find(controllerName);
        if (controller == null)
        {
            controller = new GameObject(controllerName);
        }

        QuestCameraRecorderHotkeys hotkeys = controller.GetComponent<QuestCameraRecorderHotkeys>();
        if (hotkeys == null)
        {
            hotkeys = controller.AddComponent<QuestCameraRecorderHotkeys>();
        }

        SerializedObject serializedHotkeys = new SerializedObject(hotkeys);
        SetObjectReference(serializedHotkeys, "recorder", recorder);
        SetObjectReference(serializedHotkeys, "calibrationRecorder", pcCalibrationRecorder);
        SetObjectReference(serializedHotkeys, "recordDot", FindSceneObjectByName("Record_Dot"));
        SetObjectReference(serializedHotkeys, "calibrationDot", FindSceneObjectByName("Calibration_Record_Dot"));
        SetString(serializedHotkeys, "recordDotName", "Record_Dot");
        SetString(serializedHotkeys, "calibrationDotName", "Calibration_Record_Dot");
        SetBool(serializedHotkeys, "findRecordDotByName", true);
        SetBool(serializedHotkeys, "createRecordDotIfMissing", true);
        SetBool(serializedHotkeys, "ignoreInputWhileSystemMenuHeld", true);
        serializedHotkeys.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(hotkeys);
        EditorUtility.SetDirty(controller);
    }

    private static bool SceneAssetExists(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            return false;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absoluteScenePath = Path.Combine(projectRoot, scenePath);
        return File.Exists(absoluteScenePath);
    }

    private static PassthroughCameraAccess GetOrCreateCameraAccess(
        string objectName,
        PassthroughCameraAccess.CameraPositionType cameraPosition)
    {
        var gameObject = GameObject.Find(objectName);
        if (gameObject == null)
        {
            gameObject = new GameObject(objectName);
        }

        var cameraAccess = gameObject.GetComponent<PassthroughCameraAccess>();
        if (cameraAccess == null)
        {
            cameraAccess = gameObject.AddComponent<PassthroughCameraAccess>();
        }

        var serializedAccess = new SerializedObject(cameraAccess);
        serializedAccess.FindProperty("CameraPosition").enumValueIndex = (int)cameraPosition;
        serializedAccess.FindProperty("RequestedResolution").vector2IntValue = new Vector2Int(640, 480);
        serializedAccess.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(cameraAccess);

        return cameraAccess;
    }

    private static int RemoveRecorderPanelsFromScene()
    {
        string[] panelNames =
        {
            "QuestCameraRecorderPanel",
            "RecorderUIEmptyPanel",
            "RecorderPhysicalPanel",
            "RecorderFallbackPanel",
            "RecorderSafeUnityPanel",
            "FlatUnityCanvas",
            "HandleCanvas2"
        };
        int removed = 0;
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
            {
                continue;
            }

            for (int j = 0; j < panelNames.Length; j++)
            {
                if (candidate.name != panelNames[j])
                {
                    continue;
                }

                Object.DestroyImmediate(candidate.gameObject);
                removed++;
                break;
            }
        }

        return removed;
    }

    private static void ClearRecorderGraphicMaterials(GameObject panel)
    {
        if (panel == null)
        {
            return;
        }

        Graphic[] graphics = panel.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null)
            {
                continue;
            }

            graphic.material = null;
            EditorUtility.SetDirty(graphic);
        }
    }

    private static void RemoveDuplicateComponents<T>(GameObject root) where T : Component
    {
        if (root == null)
        {
            return;
        }

        T[] components = root.GetComponents<T>();
        for (int i = components.Length - 1; i >= 1; i--)
        {
            Object.DestroyImmediate(components[i]);
        }
    }

    private static void EnsureSafeUnityPanel(GameObject panel)
    {
        Transform existing = panel.transform.Find("RecorderSafeUnityPanel");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        Transform existingFallback = panel.transform.Find("RecorderFallbackPanel");
        if (existingFallback != null)
        {
            Object.DestroyImmediate(existingFallback.gameObject);
        }

        GameObject root = CreateRectObject("RecorderFallbackPanel", panel.transform);
        root.SetActive(true);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.localPosition = Vector3.zero;
        rootRect.localRotation = Quaternion.identity;
        rootRect.localScale = Vector3.one;
        rootRect.sizeDelta = new Vector2(360f, 220f);

        GameObject canvasObject = CreateRectObject("Canvas", root.transform);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        CanvasGroup canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localPosition = Vector3.zero;
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.001f;
        canvasRect.sizeDelta = new Vector2(360f, 220f);
        AddInteractionSdkCanvasRayTarget(canvasObject, canvas, canvasRect.sizeDelta);

        RectTransform safePanel = CreateImage(canvasObject.transform, "Background", new Color(0.07f, 0.09f, 0.1f, 0.92f));
        safePanel.anchorMin = new Vector2(0.5f, 0.5f);
        safePanel.anchorMax = new Vector2(0.5f, 0.5f);
        safePanel.pivot = new Vector2(0.5f, 0.5f);
        safePanel.anchoredPosition = Vector2.zero;
        safePanel.sizeDelta = new Vector2(360f, 220f);

        RectTransform handle = CreateImage(canvasObject.transform, "RecorderHandler", new Color(0.16f, 0.20f, 0.22f, 1f));
        handle.anchorMin = new Vector2(0.5f, 1f);
        handle.anchorMax = new Vector2(0.5f, 1f);
        handle.pivot = new Vector2(0.5f, 1f);
        handle.anchoredPosition = Vector2.zero;
        handle.sizeDelta = new Vector2(360f, 48f);
        EnsureHandleCollider(handle);
        ISurface handleSurface = EnsureUnionClippedPlaneSurface(handle, handle.sizeDelta);
        EnsureGrabbablePanelRoot(root.transform, handleSurface);

        Text title = CreateText(handle, "Title", "Quest Camera Recorder", 20, TextAnchor.MiddleLeft);
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = new Vector2(14f, 0f);
        title.rectTransform.offsetMax = new Vector2(-14f, 0f);

        Text status = CreateText(canvasObject.transform, "StatusText", "Waiting for camera", 22, TextAnchor.MiddleCenter);
        status.rectTransform.sizeDelta = new Vector2(320f, 42f);
        status.rectTransform.anchoredPosition = new Vector2(0f, 32f);

        RectTransform start = CreateButton(canvasObject.transform, "StartButton", "Start", new Color(0.08f, 0.38f, 0.24f, 1f));
        start.sizeDelta = new Vector2(124f, 46f);
        start.anchoredPosition = new Vector2(-78f, -62f);

        RectTransform stop = CreateButton(canvasObject.transform, "StopButton", "Stop", new Color(0.46f, 0.10f, 0.12f, 1f));
        stop.sizeDelta = new Vector2(124f, 46f);
        stop.anchoredPosition = new Vector2(78f, -62f);

        RecorderControlPanel control = panel.GetComponent<RecorderControlPanel>();
        if (control == null)
        {
            control = panel.AddComponent<RecorderControlPanel>();
        }

        SerializedObject serializedControl = new SerializedObject(control);
        SetObjectReference(serializedControl, "recorder", Object.FindFirstObjectByType<QuestCameraRecorder>());
        SetObjectReference(serializedControl, "statusText", null);
        SetObjectReference(serializedControl, "statusTextLegacy", status);
        SetObjectReference(serializedControl, "startButton", start.GetComponent<Button>());
        SetObjectReference(serializedControl, "stopButton", stop.GetComponent<Button>());
        serializedControl.ApplyModifiedPropertiesWithoutUndo();

        Button startButton = start.GetComponent<Button>();
        Button stopButton = stop.GetComponent<Button>();
        startButton.onClick.RemoveAllListeners();
        stopButton.onClick.RemoveAllListeners();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(startButton.onClick, control.StartRecording);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(stopButton.onClick, control.StopRecording);

        BaseLinkDragController dragController = handle.GetComponent<BaseLinkDragController>();
        if (dragController == null)
        {
            dragController = handle.gameObject.AddComponent<BaseLinkDragController>();
        }

        dragController.Configure(root.transform, handle);

        RecorderPanelPointerDragController pointerDragController = handle.GetComponent<RecorderPanelPointerDragController>();
        if (pointerDragController == null)
        {
            pointerDragController = handle.gameObject.AddComponent<RecorderPanelPointerDragController>();
        }

        pointerDragController.Configure(root.transform);

        DisableNonSafeRenderers(panel, root.transform);
        HideFallbackCanvasVisuals(root.transform);

        EditorUtility.SetDirty(control);
        EditorUtility.SetDirty(root);
    }

    private static void RemoveNonPhysicalPanelChildren(Transform panelRoot)
    {
        if (panelRoot == null)
        {
            return;
        }

        for (int i = panelRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = panelRoot.GetChild(i);
            if (child == null || child.name == "RecorderPhysicalPanel")
            {
                continue;
            }

            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void RemoveFallbackPanel(GameObject panel)
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform existing = transforms[i];
            if (existing != null && existing.name == "RecorderFallbackPanel")
            {
                Object.DestroyImmediate(existing.gameObject);
            }
        }
    }

    private static void EnsurePhysicalPanel(GameObject panel)
    {
        Transform existing = panel.transform.Find("RecorderPhysicalPanel");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject("RecorderPhysicalPanel");
        root.layer = panel.layer;
        root.transform.SetParent(panel.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        CreatePhysicalPlate(root.transform, "Background", new Vector3(0f, -0.024f, 0.004f), new Vector3(0.36f, 0.172f, 0.006f), new Color(0.08f, 0.1f, 0.11f, 1f));
        Collider bar = CreatePhysicalPlate(root.transform, "RecorderHandler", new Vector3(0f, 0.086f, -0.002f), new Vector3(0.36f, 0.048f, 0.01f), new Color(0.16f, 0.22f, 0.25f, 1f));
        Collider start = CreatePhysicalPlate(root.transform, "StartButton", new Vector3(-0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.1f, 0.5f, 0.28f, 1f));
        Collider stop = CreatePhysicalPlate(root.transform, "StopButton", new Vector3(0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.58f, 0.16f, 0.16f, 1f));
        EnsurePhysicalPanelMeshVisuals(root.transform);

        PhysicalRecorderPanelController controller = root.AddComponent<PhysicalRecorderPanelController>();
        RecorderControlPanel control = panel.GetComponent<RecorderControlPanel>();
        controller.Configure(panel.transform, control != null ? control.Recorder : Object.FindFirstObjectByType<QuestCameraRecorder>(), bar, start, stop);
        ConfigurePhysicalPanelController(controller);
        EnsurePhysicalPanelGrabInteraction(root.transform, bar);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(controller);
    }

    private static void ConfigurePhysicalPanelController(PhysicalRecorderPanelController controller)
    {
        if (controller == null)
        {
            return;
        }

        SerializedObject serializedController = new SerializedObject(controller);
        SetFloat(serializedController, "rayHitPadding", 0.28f);
        SetFloat(serializedController, "dragBarNearRayTolerance", 0.55f);
        SetFloat(serializedController, "panelDragFallbackTolerance", 1.25f);
        SetFloat(serializedController, "directHandDragStartTolerance", 1.25f);
        SerializedProperty enableOvrFallback = serializedController.FindProperty("enableOvrControllerRayFallback");
        if (enableOvrFallback != null)
        {
            enableOvrFallback.boolValue = true;
        }
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static Collider CreatePhysicalPlate(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name;
        plate.layer = parent.gameObject.layer;
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPosition;
        plate.transform.localRotation = Quaternion.identity;
        plate.transform.localScale = localScale;

        Renderer renderer = plate.GetComponent<Renderer>();
        if (renderer != null)
        {
            Object.DestroyImmediate(renderer);
        }

        MeshFilter meshFilter = plate.GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            Object.DestroyImmediate(meshFilter);
        }

        Collider collider = plate.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = true;
            InflatePhysicalCollider(collider, name);
            EditorUtility.SetDirty(collider);
        }

        EditorUtility.SetDirty(plate);
        return collider;
    }

    private static void RemovePhysicalVisibleCanvas(Transform physicalRoot)
    {
        if (physicalRoot == null)
        {
            return;
        }

        Transform existing = physicalRoot.Find("RecorderVisibleCanvas");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static void DisablePhysicalPanelRenderers(Transform physicalRoot)
    {
        if (physicalRoot == null)
        {
            return;
        }

        Renderer[] renderers = physicalRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = false;
                EditorUtility.SetDirty(renderers[i]);
            }
        }
    }

    private static void InflatePhysicalCollider(Collider collider, string plateName)
    {
        if (collider is not BoxCollider boxCollider)
        {
            return;
        }

        if (plateName == "RecorderHandler")
        {
            boxCollider.size = new Vector3(1.8f, 3.2f, 18f);
            return;
        }

        if (plateName == "StartButton" || plateName == "StopButton")
        {
            boxCollider.size = new Vector3(1.8f, 2.2f, 14f);
        }
    }

    private static void EnsurePhysicalVisibleCanvas(Transform physicalRoot)
    {
        if (physicalRoot == null)
        {
            return;
        }

        Transform existing = physicalRoot.Find("RecorderVisibleCanvas");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject canvasObject = CreateRectObject("RecorderVisibleCanvas", physicalRoot);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;
        canvasObject.AddComponent<CanvasScaler>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localPosition = new Vector3(0f, -0.005f, -0.011f);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.001f;
        canvasRect.sizeDelta = new Vector2(360f, 220f);

        CreateVisualImage(canvasRect, "Background", new Vector2(0f, -24f), new Vector2(360f, 172f), new Color(0.075f, 0.095f, 0.105f, 0.96f));
        CreateVisualImage(canvasRect, "RecorderHandlerVisual", new Vector2(0f, 86f), new Vector2(360f, 48f), new Color(0.16f, 0.22f, 0.25f, 1f));
        CreateVisualImage(canvasRect, "StartButtonVisual", new Vector2(-78f, -65f), new Vector2(124f, 46f), new Color(0.10f, 0.50f, 0.28f, 1f));
        CreateVisualImage(canvasRect, "StopButtonVisual", new Vector2(78f, -65f), new Vector2(124f, 46f), new Color(0.58f, 0.16f, 0.16f, 1f));

        Text title = CreateText(canvasRect, "Title", "Quest Camera Recorder", 20, TextAnchor.MiddleLeft);
        title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.anchoredPosition = Vector2.zero;
        title.rectTransform.sizeDelta = new Vector2(330f, 48f);

        Text start = CreateText(canvasRect, "StartText", "Start", 22, TextAnchor.MiddleCenter);
        start.rectTransform.anchoredPosition = new Vector2(-78f, -65f);
        start.rectTransform.sizeDelta = new Vector2(124f, 46f);

        Text stop = CreateText(canvasRect, "StopText", "Stop", 22, TextAnchor.MiddleCenter);
        stop.rectTransform.anchoredPosition = new Vector2(78f, -65f);
        stop.rectTransform.sizeDelta = new Vector2(124f, 46f);

        EditorUtility.SetDirty(canvasObject);
    }

    private static void EnsurePhysicalPanelMeshVisuals(Transform physicalRoot)
    {
        if (physicalRoot == null)
        {
            return;
        }

        RemovePhysicalVisibleCanvas(physicalRoot);
        StripPhysicalPanelVisualComponents(physicalRoot);
        EnsurePhysicalMeshVisuals(physicalRoot);
    }

    private static void EnsurePhysicalPanelCanvasVisuals(Transform physicalRoot)
    {
        if (physicalRoot == null)
        {
            return;
        }

        RemovePhysicalMeshVisuals(physicalRoot);
        StripPhysicalPanelVisualComponents(physicalRoot);
        EnsurePhysicalVisibleCanvas(physicalRoot);
    }

    private static void RemovePhysicalMeshVisuals(Transform physicalRoot)
    {
        Transform existing = physicalRoot != null ? physicalRoot.Find(MeshVisualRootName) : null;
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static void EnsurePhysicalMeshVisuals(Transform physicalRoot)
    {
        Transform existing = physicalRoot.Find(MeshVisualRootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject root = new GameObject(MeshVisualRootName);
        root.layer = physicalRoot.gameObject.layer;
        root.transform.SetParent(physicalRoot, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        CreateMeshVisualPlate(root.transform, "BackgroundMesh", new Vector3(0f, -0.024f, -0.018f), new Vector2(0.36f, 0.172f), new Color(0.075f, 0.095f, 0.105f, 1f));
        CreateMeshVisualPlate(root.transform, "RecorderHandlerMesh", new Vector3(0f, 0.086f, -0.020f), new Vector2(0.36f, 0.048f), new Color(0.16f, 0.22f, 0.25f, 1f));
        CreateMeshVisualPlate(root.transform, "StartButtonMesh", new Vector3(-0.078f, -0.065f, -0.022f), new Vector2(0.124f, 0.046f), new Color(0.10f, 0.50f, 0.28f, 1f));
        CreateMeshVisualPlate(root.transform, "StopButtonMesh", new Vector3(0.078f, -0.065f, -0.022f), new Vector2(0.124f, 0.046f), new Color(0.58f, 0.16f, 0.16f, 1f));
        CreateMeshVisualPlate(root.transform, "BuildMarkerMesh_v43", new Vector3(-0.168f, 0.104f, -0.024f), new Vector2(0.024f, 0.024f), new Color(1.0f, 0.78f, 0.0f, 1f));
        EditorUtility.SetDirty(root);
    }

    private static void CreateMeshVisualPlate(Transform parent, string name, Vector3 localPosition, Vector2 size, Color color)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = name;
        plate.layer = parent.gameObject.layer;
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPosition;
        plate.transform.localRotation = Quaternion.identity;
        plate.transform.localScale = new Vector3(size.x, size.y, 1f);

        Collider collider = plate.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        Renderer renderer = plate.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = EnsurePhysicalPanelMaterial(name, color);
            renderer.sharedMaterial = material;
            renderer.enabled = material != null;
            EditorUtility.SetDirty(renderer);
        }

        EditorUtility.SetDirty(plate);
    }

    private static void StripPhysicalPanelVisualComponents(Transform physicalRoot)
    {
        Renderer[] renderers = physicalRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && !IsUnderChild(renderer.transform, MeshVisualRootName))
            {
                Object.DestroyImmediate(renderer);
            }
        }

        MeshFilter[] meshFilters = physicalRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter != null && !IsUnderChild(meshFilter.transform, MeshVisualRootName))
            {
                Object.DestroyImmediate(meshFilter);
            }
        }
    }

    private static bool IsUnderChild(Transform transform, string childName)
    {
        while (transform != null)
        {
            if (transform.name == childName)
            {
                return true;
            }

            transform = transform.parent;
        }

        return false;
    }

    private static RectTransform CreateVisualImage(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        RectTransform rect = CreateImage(parent, name, color);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = rect.GetComponent<Image>();
        if (image != null)
        {
            image.material = null;
            image.raycastTarget = false;
            EditorUtility.SetDirty(image);
        }

        return rect;
    }

    private static Material EnsureRecorderPanelUIMaterial()
    {
        const string materialFolder = "Assets/EyeTracking/Materials";
        const string materialPath = materialFolder + "/RecorderPanelUIDefault.mat";
        if (!AssetDatabase.IsValidFolder(materialFolder))
        {
            AssetDatabase.CreateFolder("Assets/EyeTracking", "Materials");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        Shader shader = Shader.Find("UI/Default") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader") ??
                        Shader.Find("UI/Default Correct") ??
                        Shader.Find("UI/Default (Overlay)");
        if (shader == null)
        {
            return RecorderPanelMaterialSanitizer.SafeGraphicMaterial();
        }

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.name = "RecorderPanelUIDefault";
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Material EnsurePhysicalPanelMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("EnvironmentDepth/OcclusionLit") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("UI/Default") ??
                        Shader.Find("Oculus/Unlit") ??
                        Shader.Find("Oculus/Unlit Transparent Color") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader") ??
                        Shader.Find("Universal Render Pipeline/Simple Lit") ??
                        Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Unlit/Color");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader)
        {
            name = "RecorderPanelSerializedUnlit_" + name,
            hideFlags = HideFlags.None
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_ReceiveShadows"))
        {
            material.SetFloat("_ReceiveShadows", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0f);
        }

        if (material.HasProperty("_SpecularHighlights"))
        {
            material.SetFloat("_SpecularHighlights", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_EnvironmentDepthBias"))
        {
            material.SetFloat("_EnvironmentDepthBias", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }

    private static void EnsurePhysicalPanelGrabInteraction(Transform physicalRoot, Collider dragBar)
    {
        if (physicalRoot == null || dragBar == null)
        {
            return;
        }

        Transform targetRoot = physicalRoot.parent != null ? physicalRoot.parent : physicalRoot;
        DisablePhysicalColliderSurface(dragBar);
        ISurface handleSurface = EnsurePhysicalUnionSurface(dragBar);
        ISurface panelSurface = EnsurePhysicalPanelUnionSurface(physicalRoot, dragBar) ?? handleSurface;
        EnsureGrabbablePanelRoot(targetRoot, panelSurface);
        EnsureBarRayInteractable(dragBar.transform, targetRoot, handleSurface);
    }

    private static void EnsureBarRayInteractable(Transform bar, Transform targetRoot, ISurface handleSurface)
    {
        if (bar == null || targetRoot == null || handleSurface == null)
        {
            return;
        }

        Grabbable grabbable = targetRoot.GetComponent<Grabbable>();
        MoveRelativeToTargetProvider movementProvider = targetRoot.GetComponent<MoveRelativeToTargetProvider>();
        RayInteractable rayInteractable = bar.GetComponent<RayInteractable>();
        if (rayInteractable == null)
        {
            rayInteractable = bar.gameObject.AddComponent<RayInteractable>();
        }

        rayInteractable.InjectSurface(handleSurface);
        rayInteractable.InjectOptionalSelectSurface(handleSurface);
        ForceUnlimitedRayInteractors(rayInteractable);
        if (grabbable != null)
        {
            rayInteractable.InjectOptionalPointableElement(grabbable);
        }

        if (movementProvider != null)
        {
            rayInteractable.InjectOptionalMovementProvider(movementProvider);
        }

        EditorUtility.SetDirty(rayInteractable);
    }

    private static void EnsureRayInteractable(
        Transform target,
        ISurface surface,
        Grabbable grabbable = null,
        MoveRelativeToTargetProvider movementProvider = null)
    {
        if (target == null || surface == null)
        {
            return;
        }

        RayInteractable rayInteractable = target.GetComponent<RayInteractable>();
        if (rayInteractable == null)
        {
            rayInteractable = target.gameObject.AddComponent<RayInteractable>();
        }

        rayInteractable.InjectSurface(surface);
        rayInteractable.InjectOptionalSelectSurface(surface);
        if (grabbable != null)
        {
            rayInteractable.InjectOptionalPointableElement(grabbable);
        }

        if (movementProvider != null)
        {
            rayInteractable.InjectOptionalMovementProvider(movementProvider);
        }

        EditorUtility.SetDirty(rayInteractable);
    }

    private static ISurface EnsurePhysicalUnionSurface(Collider barCollider)
    {
        if (barCollider == null)
        {
            return null;
        }

        Transform parent = barCollider.transform;
        Transform existing = parent.Find(PhysicalSurfaceName);
        if (existing != null)
        {
            UnionClippedPlaneSurface existingUnion = existing.GetComponent<UnionClippedPlaneSurface>();
            if (existingUnion != null)
            {
                ConfigurePhysicalSurface(existing, barCollider);
                return existingUnion;
            }

            ClippedPlaneSurface existingSurface = existing.GetComponent<ClippedPlaneSurface>();
            if (existingSurface != null)
            {
                Object.DestroyImmediate(existingSurface);
            }
        }

        GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject(PhysicalSurfaceName);
        surfaceObject.layer = parent.gameObject.layer;
        if (surfaceObject.transform.parent != parent)
        {
            surfaceObject.transform.SetParent(parent, false);
        }

        ConfigurePhysicalSurface(surfaceObject.transform, barCollider);

        PlaneSurface planeSurface = surfaceObject.AddComponent<PlaneSurface>();
        planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, true);
        BoundsClipper boundsClipper = surfaceObject.AddComponent<BoundsClipper>();
        boundsClipper.Size = Vector3.one;

        UnionClippedPlaneSurface clippedSurface = surfaceObject.AddComponent<UnionClippedPlaneSurface>();
        clippedSurface.InjectAllClippedPlaneSurface(planeSurface, new IBoundsClipper[] { boundsClipper });

        EditorUtility.SetDirty(surfaceObject);
        EditorUtility.SetDirty(planeSurface);
        EditorUtility.SetDirty(boundsClipper);
        EditorUtility.SetDirty(clippedSurface);
        return clippedSurface;
    }

    private static ISurface EnsurePhysicalPanelUnionSurface(Transform physicalRoot, Collider fallbackCollider)
    {
        if (physicalRoot == null)
        {
            return null;
        }

        Transform existing = physicalRoot.Find("PhysicalPanelWideInteractionSurface");
        GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject("PhysicalPanelWideInteractionSurface");
        surfaceObject.layer = physicalRoot.gameObject.layer;
        if (surfaceObject.transform.parent != physicalRoot)
        {
            surfaceObject.transform.SetParent(physicalRoot, false);
        }

        ConfigurePanelWideSurface(surfaceObject.transform, physicalRoot, fallbackCollider);

        PlaneSurface planeSurface = surfaceObject.GetComponent<PlaneSurface>();
        if (planeSurface == null)
        {
            planeSurface = surfaceObject.AddComponent<PlaneSurface>();
        }

        planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, true);

        BoundsClipper boundsClipper = surfaceObject.GetComponent<BoundsClipper>();
        if (boundsClipper == null)
        {
            boundsClipper = surfaceObject.AddComponent<BoundsClipper>();
        }

        boundsClipper.Size = Vector3.one;

        UnionClippedPlaneSurface clippedSurface = surfaceObject.GetComponent<UnionClippedPlaneSurface>();
        if (clippedSurface == null)
        {
            clippedSurface = surfaceObject.AddComponent<UnionClippedPlaneSurface>();
        }

        clippedSurface.InjectAllClippedPlaneSurface(planeSurface, new IBoundsClipper[] { boundsClipper });

        EditorUtility.SetDirty(surfaceObject);
        EditorUtility.SetDirty(planeSurface);
        EditorUtility.SetDirty(boundsClipper);
        EditorUtility.SetDirty(clippedSurface);
        return clippedSurface;
    }

    private static void DisablePhysicalColliderSurface(Collider barCollider)
    {
        if (barCollider == null)
        {
            return;
        }

        ColliderSurface surface = barCollider.GetComponent<ColliderSurface>();
        if (surface != null)
        {
            surface.enabled = false;
            EditorUtility.SetDirty(surface);
        }
    }

    private static void ConfigurePhysicalSurface(Transform surface, Collider barCollider)
    {
        if (surface == null || barCollider == null)
        {
            return;
        }

        Bounds localBounds = LocalColliderBounds(barCollider);
        float width = Mathf.Max(0.001f, Mathf.Abs(barCollider.transform.localScale.x));
        float height = Mathf.Max(0.001f, Mathf.Abs(barCollider.transform.localScale.y));
        float visualFrontZ = localBounds.min.z - 0.002f;
        surface.localPosition = new Vector3(0f, 0f, visualFrontZ);
        surface.localRotation = Quaternion.identity;
        surface.localScale = new Vector3(width, height, 0.01f);
        EditorUtility.SetDirty(surface);
    }

    private static void ConfigurePanelWideSurface(Transform surface, Transform physicalRoot, Collider fallbackCollider)
    {
        Bounds localBounds = new Bounds(Vector3.zero, new Vector3(0.38f, 0.22f, 0.02f));
        bool hasBounds = false;
        Collider[] colliders = physicalRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.transform == surface || collider.name == PhysicalSurfaceName)
            {
                continue;
            }

            Bounds bounds = LocalColliderBoundsInRoot(collider, physicalRoot);
            if (!hasBounds)
            {
                localBounds = bounds;
                hasBounds = true;
            }
            else
            {
                localBounds.Encapsulate(bounds);
            }
        }

        if (!hasBounds && fallbackCollider != null)
        {
            localBounds = LocalColliderBoundsInRoot(fallbackCollider, physicalRoot);
        }

        localBounds.Expand(new Vector3(0.04f, 0.04f, 0.01f));
        float visualFrontZ = localBounds.min.z - 0.004f;
        surface.localPosition = new Vector3(localBounds.center.x, localBounds.center.y, visualFrontZ);
        surface.localRotation = Quaternion.identity;
        surface.localScale = new Vector3(Mathf.Max(0.001f, localBounds.size.x), Mathf.Max(0.001f, localBounds.size.y), 0.01f);
        EditorUtility.SetDirty(surface);
    }

    private static Bounds LocalColliderBoundsInRoot(Collider collider, Transform root)
    {
        Bounds worldBounds = collider.bounds;
        Vector3 min = root.InverseTransformPoint(worldBounds.min);
        Vector3 max = root.InverseTransformPoint(worldBounds.max);
        return new Bounds((min + max) * 0.5f, new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z)));
    }

    private static Bounds LocalColliderBounds(Collider collider)
    {
        if (collider is BoxCollider boxCollider)
        {
            return new Bounds(boxCollider.center, boxCollider.size);
        }

        Vector3 localSize = collider.transform.InverseTransformVector(collider.bounds.size);
        return new Bounds(Vector3.zero, new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)));
    }

    private static void HideFallbackCanvasVisuals(Transform fallback)
    {
        if (fallback == null)
        {
            return;
        }

        Canvas[] canvases = fallback.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].enabled = false;
            EditorUtility.SetDirty(canvases[i]);
        }

        Graphic[] graphics = fallback.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            graphics[i].enabled = false;
            graphics[i].raycastTarget = false;
            EditorUtility.SetDirty(graphics[i]);
        }

        GraphicRaycaster[] raycasters = fallback.GetComponentsInChildren<GraphicRaycaster>(true);
        for (int i = 0; i < raycasters.Length; i++)
        {
            raycasters[i].enabled = false;
            EditorUtility.SetDirty(raycasters[i]);
        }
    }

    private static void EnsureGrabbablePanelRoot(Transform root, ISurface handleSurface)
    {
        if (root == null || handleSurface == null)
        {
            return;
        }

        MoveRelativeToTargetProvider movementProvider = root.GetComponent<MoveRelativeToTargetProvider>();
        if (movementProvider == null)
        {
            movementProvider = root.gameObject.AddComponent<MoveRelativeToTargetProvider>();
        }

        Grabbable grabbable = root.GetComponent<Grabbable>();
        if (grabbable == null)
        {
            grabbable = root.gameObject.AddComponent<Grabbable>();
        }

        grabbable.InjectOptionalTargetTransform(root);
        grabbable.InjectOptionalThrowWhenUnselected(false);
        SerializedObject serializedGrabbable = new SerializedObject(grabbable);
        SerializedProperty maxGrabPoints = serializedGrabbable.FindProperty("_maxGrabPoints");
        if (maxGrabPoints != null)
        {
            // Match UMIVR RecorderUI: leave selection capacity unrestricted.
            maxGrabPoints.intValue = -1;
            serializedGrabbable.ApplyModifiedPropertiesWithoutUndo();
        }

        RayInteractable rootRayInteractable = root.GetComponent<RayInteractable>();
        if (rootRayInteractable == null)
        {
            rootRayInteractable = root.gameObject.AddComponent<RayInteractable>();
        }

        rootRayInteractable.InjectOptionalPointableElement(grabbable);
        rootRayInteractable.InjectSurface(handleSurface);
        rootRayInteractable.InjectOptionalSelectSurface(handleSurface);
        rootRayInteractable.InjectOptionalMovementProvider(movementProvider);
        ForceUnlimitedRayInteractors(rootRayInteractable);

        EditorUtility.SetDirty(movementProvider);
        EditorUtility.SetDirty(grabbable);
        EditorUtility.SetDirty(rootRayInteractable);
    }

    private static void ForceUnlimitedRayInteractors(RayInteractable rayInteractable)
    {
        if (rayInteractable == null)
        {
            return;
        }

        SerializedObject serializedRay = new SerializedObject(rayInteractable);
        SerializedProperty maxInteractors = serializedRay.FindProperty("_maxInteractors");
        if (maxInteractors != null)
        {
            maxInteractors.intValue = -1;
        }

        SerializedProperty maxSelectingInteractors = serializedRay.FindProperty("_maxSelectingInteractors");
        if (maxSelectingInteractors != null)
        {
            maxSelectingInteractors.intValue = -1;
        }

        serializedRay.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rayInteractable);
    }

    private static void EnsureRayInteractableSelectSurfaces()
    {
        RayInteractable[] rayInteractables = Object.FindObjectsByType<RayInteractable>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < rayInteractables.Length; i++)
        {
            RayInteractable rayInteractable = rayInteractables[i];
            if (rayInteractable == null)
            {
                continue;
            }

            SerializedObject serializedRay = new SerializedObject(rayInteractable);
            SerializedProperty surface = serializedRay.FindProperty("_surface");
            SerializedProperty selectSurface = serializedRay.FindProperty("_selectSurface");
            if (surface == null ||
                selectSurface == null ||
                surface.objectReferenceValue == null ||
                selectSurface.objectReferenceValue != null)
            {
                continue;
            }

            selectSurface.objectReferenceValue = surface.objectReferenceValue;
            serializedRay.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rayInteractable);
        }
    }

    private static void AddInteractionSdkCanvasRayTarget(GameObject canvasObject, Canvas canvas, Vector2 canvasSize)
    {
        if (canvasObject == null || canvas == null)
        {
            return;
        }

        PointableCanvas pointableCanvas = canvasObject.GetComponent<PointableCanvas>();
        if (pointableCanvas == null)
        {
            pointableCanvas = canvasObject.AddComponent<PointableCanvas>();
        }

        pointableCanvas.InjectCanvas(canvas);

        ISurface surface = EnsureUnionClippedPlaneSurface(canvasObject.transform, canvasSize);
        RayInteractable rayInteractable = canvasObject.GetComponent<RayInteractable>();
        if (rayInteractable == null)
        {
            rayInteractable = canvasObject.AddComponent<RayInteractable>();
        }

        rayInteractable.InjectOptionalPointableElement(pointableCanvas);
        rayInteractable.InjectSurface(surface);
        rayInteractable.InjectOptionalSelectSurface(surface);

        EditorUtility.SetDirty(pointableCanvas);
        EditorUtility.SetDirty(rayInteractable);
    }

    private static ISurface EnsureUnionClippedPlaneSurface(RectTransform parent, Vector2 size)
    {
        return EnsureUnionClippedPlaneSurface((Transform)parent, size);
    }

    private static ISurface EnsureUnionClippedPlaneSurface(Transform parent, Vector2 size)
    {
        Transform existing = parent.Find("InteractionSurface");
        if (existing != null)
        {
            UnionClippedPlaneSurface existingUnion = existing.GetComponent<UnionClippedPlaneSurface>();
            if (existingUnion != null)
            {
                ConfigureSurface(existing, size);
                return existingUnion;
            }

            ClippedPlaneSurface existingSurface = existing.GetComponent<ClippedPlaneSurface>();
            if (existingSurface != null)
            {
                Object.DestroyImmediate(existingSurface);
            }
        }

        GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject("InteractionSurface");
        surfaceObject.layer = parent.gameObject.layer;
        if (surfaceObject.transform.parent != parent)
        {
            surfaceObject.transform.SetParent(parent, false);
        }

        ConfigureSurface(surfaceObject.transform, size);

        PlaneSurface planeSurface = surfaceObject.GetComponent<PlaneSurface>();
        if (planeSurface == null)
        {
            planeSurface = surfaceObject.AddComponent<PlaneSurface>();
        }

        planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, true);
        BoundsClipper boundsClipper = surfaceObject.GetComponent<BoundsClipper>();
        if (boundsClipper == null)
        {
            boundsClipper = surfaceObject.AddComponent<BoundsClipper>();
        }

        boundsClipper.Size = Vector3.one;

        UnionClippedPlaneSurface clippedSurface = surfaceObject.GetComponent<UnionClippedPlaneSurface>();
        if (clippedSurface == null)
        {
            clippedSurface = surfaceObject.AddComponent<UnionClippedPlaneSurface>();
        }

        clippedSurface.InjectAllClippedPlaneSurface(planeSurface, new IBoundsClipper[] { boundsClipper });

        EditorUtility.SetDirty(surfaceObject);
        EditorUtility.SetDirty(planeSurface);
        EditorUtility.SetDirty(boundsClipper);
        EditorUtility.SetDirty(clippedSurface);
        return clippedSurface;
    }

    private static void ConfigureSurface(Transform surface, Vector2 size)
    {
        surface.localPosition = Vector3.zero;
        surface.localRotation = Quaternion.identity;
        surface.localScale = new Vector3(size.x * 0.001f, size.y * 0.001f, 0.01f);
        EditorUtility.SetDirty(surface);
    }

    private static void SetActiveIfFound(Transform root, string childName, bool active)
    {
        Transform child = root.Find(childName);
        if (child == null)
        {
            return;
        }

        child.gameObject.SetActive(active);
        EditorUtility.SetDirty(child.gameObject);
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

    private static void DisableNonSafeRenderers(GameObject panel, Transform safeRoot)
    {
        Renderer[] renderers = panel.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null ||
                safeRoot != null &&
                (targetRenderer.transform == safeRoot || targetRenderer.transform.IsChildOf(safeRoot)))
            {
                continue;
            }

            targetRenderer.enabled = false;
            EditorUtility.SetDirty(targetRenderer);
        }
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.intValue = value;
        }
    }

    private static void SetString(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.stringValue = value;
        }
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ConfigureBoxAuthoringTriggerCombo()
    {
        foreach (BoxVolumeAuthoringTool tool in Object.FindObjectsByType<BoxVolumeAuthoringTool>(FindObjectsSortMode.None))
        {
            SerializedObject serializedTool = new SerializedObject(tool);
            SerializedProperty requireCombo = serializedTool.FindProperty("requireRightAAndBWithTrigger");
            SerializedProperty triggerThreshold = serializedTool.FindProperty("controllerTriggerThreshold");
            if (requireCombo != null)
            {
                requireCombo.boolValue = true;
            }

            if (triggerThreshold != null)
            {
                triggerThreshold.floatValue = 0.72f;
            }

            serializedTool.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tool);
        }

        foreach (MonsterGrounding grounding in Object.FindObjectsByType<MonsterGrounding>(FindObjectsSortMode.None))
        {
            SerializedObject serializedGrounding = new SerializedObject(grounding);
            SerializedProperty requireCombo = serializedGrounding.FindProperty("requireRightAAndBWithTrigger");
            SerializedProperty triggerThreshold = serializedGrounding.FindProperty("controllerTriggerThreshold");
            SerializedProperty continuousGrounding = serializedGrounding.FindProperty("enableContinuousGrounding");
            if (requireCombo != null)
            {
                requireCombo.boolValue = true;
            }

            if (triggerThreshold != null)
            {
                triggerThreshold.floatValue = 0.72f;
            }

            if (continuousGrounding != null)
            {
                continuousGrounding.boolValue = false;
            }

            serializedGrounding.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grounding);
        }
    }

    private static void EnsureHandleCollider(GameObject panel)
    {
        RectTransform handler = panel.transform.Find("HandleCanvas2/Canvas/Menu/RecorderHandler") as RectTransform;
        RectTransform safeHandler = panel.transform.Find("RecorderSafeUnityPanel/Canvas/Panel/SafeDragBar") as RectTransform;
        RectTransform fallbackHandler = panel.transform.Find("RecorderFallbackPanel/Canvas/RecorderHandler") as RectTransform;
        if (fallbackHandler != null)
        {
            EnsureHandleCollider(fallbackHandler);
        }

        if (safeHandler != null)
        {
            EnsureHandleCollider(safeHandler);
        }

        if (handler == null)
        {
            return;
        }

        EnsureHandleCollider(handler);
    }

    private static void EnsureHandleCollider(RectTransform handler)
    {
        BoxCollider collider = handler.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = handler.gameObject.AddComponent<BoxCollider>();
        }

        Rect rect = handler.rect;
        collider.isTrigger = true;
        collider.center = new Vector3(rect.center.x, rect.center.y, 0f);
        collider.size = new Vector3(Mathf.Max(1f, rect.width + 140f), Mathf.Max(1f, rect.height + 120f), 120f);
        EditorUtility.SetDirty(collider);
    }

    private static RectTransform CreateButton(Transform parent, string name, string label, Color color)
    {
        RectTransform buttonRect = CreateImage(parent, name, color);
        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonRect.GetComponent<Image>();

        Text text = CreateText(buttonRect, "Text", label, 20, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        return buttonRect;
    }

    private static RectTransform CreateImage(Transform parent, string name, Color color)
    {
        GameObject imageObject = CreateRectObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.material = null;
        image.color = color;
        image.raycastTarget = true;
        return image.rectTransform;
    }

    private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = CreateRectObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.material = null;
        text.font = BuiltInUiFont();
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static Font BuiltInUiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        try
        {
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    private static GameObject CreateRectObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        gameObject.layer = parent != null ? parent.gameObject.layer : 0;
        return gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static bool IsTextMeshProGraphic(Graphic graphic)
    {
        System.Type type = graphic.GetType();
        while (type != null)
        {
            if (type.FullName != null && type.FullName.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static void RemoveRoundedBoxComponents(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour.GetType().Name != "RoundedBoxUIProperties")
            {
                continue;
            }

            Object.DestroyImmediate(behaviour);
        }
    }

    private static void RemoveLegacyPanelVisuals(GameObject panel)
    {
        string[] legacyVisualRoots =
        {
            "FlatUnityCanvas",
            "HandleCanvas2",
            "RecorderSafeUnityPanel",
            "Surface",
            "Backplate",
            "GradientEffect"
        };

        for (int i = 0; i < legacyVisualRoots.Length; i++)
        {
            Transform legacyRoot = panel.transform.Find(legacyVisualRoots[i]);
            if (legacyRoot == null)
            {
                continue;
            }

            Object.DestroyImmediate(legacyRoot.gameObject);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
        {
            return;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.layer = layer;
            EditorUtility.SetDirty(transforms[i].gameObject);
        }
    }

    private static void EnableControllerRays()
    {
        string[] handNames = { "Left Hand", "Right Hand" };
        for (int i = 0; i < handNames.Length; i++)
        {
            GameObject hand = GameObject.Find(handNames[i]);
            if (hand == null)
            {
                continue;
            }

            Transform[] children = hand.GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < children.Length; j++)
            {
                Transform child = children[j];
                if (child.name != "Ray")
                {
                    continue;
                }

                child.gameObject.SetActive(true);
                child.gameObject.layer = RecorderPanelLayer;
                EditorUtility.SetDirty(child.gameObject);
            }
        }
    }

    private static void EnsurePointableCanvasModule()
    {
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
            EditorUtility.SetDirty(eventSystemObject);
        }

        if (eventSystem.GetComponent<PointableCanvasModule>() == null)
        {
            eventSystem.gameObject.AddComponent<PointableCanvasModule>();
            EditorUtility.SetDirty(eventSystem.gameObject);
        }
    }

}

public sealed class QuestCameraRecordingSceneBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        QuestCameraRecordingSceneSetup.ConfigureSceneForBuild();
    }
}
