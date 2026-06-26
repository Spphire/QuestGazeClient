using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Meta.XR;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using EyeTracking.Recording;
using EyeTracking.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class RecorderPanelSceneVerifier
{
    private static readonly string[] ScenePaths =
    {
        "Assets/EyeTracking/EyeTrackingScene_RecordQuestCamera.unity",
        "Assets/EyeTracking/EyeTrackingScene.unity"
    };

    [MenuItem("EyeTracking/Diagnostics/Verify Recorder Panel Scenes")]
    public static void VerifyRecorderPanelScenes()
    {
        QuestCameraRecordingSceneSetup.ConfigureSceneForBuild();

        List<string> failures = new List<string>();
        VerifyOculusProjectConfig(failures);
        VerifyAndroidManifest(failures);
        string previousScenePath = EditorSceneManager.GetActiveScene().path;

        for (int i = 0; i < ScenePaths.Length; i++)
        {
            string scenePath = ScenePaths[i];
            if (!SceneAssetExists(scenePath))
            {
                failures.Add($"{scenePath}: scene_not_found");
                continue;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            VerifyOpenScene(scenePath, failures);
        }

        if (!string.IsNullOrEmpty(previousScenePath) && SceneAssetExists(previousScenePath))
        {
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Recorder panel scene verification failed:\n" + string.Join("\n", failures));
        }

        Debug.Log("[RecorderPanelSceneVerifier] OK: recorder panel scenes use Quest-safe mesh visuals on UI layer 5 and UMIVR-style root/bar ray grabbables.");
    }

    private static void VerifyAndroidManifest(List<string> failures)
    {
        const string manifestRelativePath = "Assets/Plugins/Android/AndroidManifest.xml";
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string manifestPath = Path.Combine(projectRoot, manifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            failures.Add($"{manifestRelativePath}: missing_manifest");
            return;
        }

        XmlDocument manifest = new XmlDocument();
        manifest.Load(manifestPath);
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/uses-feature",
            "oculus.software.handtracking",
            "required",
            "false",
            failures,
            "optional_handtracking_feature");
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/uses-feature",
            "com.oculus.feature.PASSTHROUGH",
            "required",
            "false",
            failures,
            "optional_passthrough_feature");
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/uses-feature",
            "com.oculus.feature.CONTEXTUAL_BOUNDARYLESS_APP",
            "required",
            "true",
            failures,
            "contextual_boundaryless_feature");
        VerifyManifestAndroidNamedNodeAbsent(
            manifest,
            "/manifest/uses-feature",
            "com.oculus.feature.BOUNDARYLESS_APP",
            failures,
            "legacy_boundaryless_feature");
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/uses-feature",
            "android.hardware.camera",
            "required",
            "false",
            failures,
            "optional_android_camera_feature");
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/uses-permission",
            "com.oculus.permission.HAND_TRACKING",
            null,
            null,
            failures,
            "handtracking_permission");
        VerifyManifestAndroidNamedNode(
            manifest,
            "/manifest/application/meta-data",
            "com.oculus.handtracking.frequency",
            "value",
            "MAX",
            failures,
            "handtracking_frequency");
    }

    private static void VerifyOculusProjectConfig(List<string> failures)
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
        {
            failures.Add("Assets/Oculus/OculusProjectConfig.asset: missing_project_config");
            return;
        }

        if (projectConfig.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersAndHands)
        {
            failures.Add(
                $"Assets/Oculus/OculusProjectConfig.asset: handTrackingSupport={projectConfig.handTrackingSupport}_expected_ControllersAndHands");
        }

        if (projectConfig.handTrackingFrequency != OVRProjectConfig.HandTrackingFrequency.MAX)
        {
            failures.Add(
                $"Assets/Oculus/OculusProjectConfig.asset: handTrackingFrequency={projectConfig.handTrackingFrequency}_expected_MAX");
        }

        if (!projectConfig.isPassthroughCameraAccessEnabled)
        {
            failures.Add("Assets/Oculus/OculusProjectConfig.asset: isPassthroughCameraAccessEnabled_disabled");
        }

        if (projectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.Supported)
        {
            failures.Add(
                $"Assets/Oculus/OculusProjectConfig.asset: insightPassthroughSupport={projectConfig.insightPassthroughSupport}_expected_Supported");
        }

        if (projectConfig.renderModelSupport != OVRProjectConfig.RenderModelSupport.Disabled)
        {
            failures.Add(
                $"Assets/Oculus/OculusProjectConfig.asset: renderModelSupport={projectConfig.renderModelSupport}_expected_Disabled");
        }

        if (projectConfig.trackedKeyboardSupport != OVRProjectConfig.TrackedKeyboardSupport.None)
        {
            failures.Add(
                $"Assets/Oculus/OculusProjectConfig.asset: trackedKeyboardSupport={projectConfig.trackedKeyboardSupport}_expected_None");
        }

        if (projectConfig.requiresSystemKeyboard)
        {
            failures.Add("Assets/Oculus/OculusProjectConfig.asset: requiresSystemKeyboard_enabled");
        }
    }

    private static void VerifyManifestAndroidNamedNode(
        XmlDocument manifest,
        string xpath,
        string androidName,
        string attributeName,
        string expectedAttributeValue,
        List<string> failures,
        string label)
    {
        const string androidNamespace = "http://schemas.android.com/apk/res/android";
        XmlElement element = FindManifestAndroidNamedNode(manifest, xpath, androidName);
        if (element == null)
        {
            failures.Add($"Assets/Plugins/Android/AndroidManifest.xml: missing_{label} name={androidName}");
            return;
        }

        if (string.IsNullOrEmpty(attributeName))
        {
            return;
        }

        string value = element.GetAttribute(attributeName, androidNamespace);
        if (value != expectedAttributeValue)
        {
            failures.Add(
                $"Assets/Plugins/Android/AndroidManifest.xml: {label}_{attributeName}={value}_expected_{expectedAttributeValue}");
        }
    }

    private static void VerifyManifestAndroidNamedNodeAbsent(
        XmlDocument manifest,
        string xpath,
        string androidName,
        List<string> failures,
        string label)
    {
        XmlElement element = FindManifestAndroidNamedNode(manifest, xpath, androidName);
        if (element != null)
        {
            failures.Add($"Assets/Plugins/Android/AndroidManifest.xml: unexpected_{label} name={androidName}");
        }
    }

    private static XmlElement FindManifestAndroidNamedNode(
        XmlDocument manifest,
        string xpath,
        string androidName)
    {
        const string androidNamespace = "http://schemas.android.com/apk/res/android";
        XmlNodeList nodes = manifest.SelectNodes(xpath);
        if (nodes == null)
        {
            return null;
        }

        foreach (XmlNode node in nodes)
        {
            if (node is XmlElement element &&
                element.GetAttribute("name", androidNamespace) == androidName)
            {
                return element;
            }
        }

        return null;
    }

    private static void VerifyOpenScene(string scenePath, List<string> failures)
    {
        GameObject panel = GameObject.Find("QuestCameraRecorderPanel") ?? GameObject.Find("RecorderUIEmptyPanel");
        if (panel == null)
        {
            failures.Add($"{scenePath}: missing_panel_root");
            return;
        }

        VerifyDefaultLayer(scenePath, panel.transform, failures);

        Transform physical = panel.transform.Find("RecorderPhysicalPanel");
        if (physical == null)
        {
            failures.Add($"{scenePath}: missing_RecorderPhysicalPanel");
            return;
        }

        Transform bar = physical.Find("RecorderHandler");
        if (bar == null)
        {
            failures.Add($"{scenePath}: missing_RecorderHandler");
            return;
        }

        VerifyNoLegacyVisuals(scenePath, panel.transform, failures);
        VerifyPanelMeshVisuals(scenePath, panel.transform, physical, failures);
        VerifyRayGrab(scenePath, panel.transform, bar, failures);
        VerifyOvrManagerHandAndControllerMode(scenePath, failures);
        VerifyAllRayInteractablesHaveSelectSurface(scenePath, failures);
        VerifyRecorder(scenePath, failures);
    }

    private static void VerifyOvrManagerHandAndControllerMode(string scenePath, List<string> failures)
    {
        OVRManager[] managers = UnityEngine.Object.FindObjectsByType<OVRManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (managers.Length == 0)
        {
            failures.Add($"{scenePath}: missing_OVRManager");
            return;
        }

        for (int i = 0; i < managers.Length; i++)
        {
            OVRManager manager = managers[i];
            if (manager == null)
            {
                continue;
            }

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty simultaneousHandsAndControllersEnabled = serializedManager.FindProperty("SimultaneousHandsAndControllersEnabled");
            if (simultaneousHandsAndControllersEnabled == null || !simultaneousHandsAndControllersEnabled.boolValue)
            {
                failures.Add($"{scenePath}: OVRManager[{i}]_SimultaneousHandsAndControllersEnabled_disabled");
            }

            SerializedProperty launchOnStartup = serializedManager.FindProperty("launchSimultaneousHandsControllersOnStartup");
            if (launchOnStartup == null || !launchOnStartup.boolValue)
            {
                failures.Add($"{scenePath}: OVRManager[{i}]_launchSimultaneousHandsControllersOnStartup_disabled");
            }
        }
    }

    private static void VerifyNoLegacyVisuals(string scenePath, Transform panel, List<string> failures)
    {
        string[] legacyNames = { "FlatUnityCanvas", "HandleCanvas2", "RecorderSafeUnityPanel", "RecorderFallbackPanel" };
        for (int i = 0; i < legacyNames.Length; i++)
        {
            Transform legacy = panel.Find(legacyNames[i]);
            if (legacy != null && legacy.gameObject.activeSelf)
            {
                failures.Add($"{scenePath}: active_legacy_visual={legacyNames[i]}");
            }
        }
    }

    private static void VerifyPanelMeshVisuals(string scenePath, Transform panel, Transform physical, List<string> failures)
    {
        Transform visibleCanvas = physical.Find("RecorderVisibleCanvas");
        if (visibleCanvas != null && visibleCanvas.gameObject.activeSelf)
        {
            failures.Add($"{scenePath}: active_RecorderVisibleCanvas_should_be_disabled_for_mesh_panel");
        }

        Transform meshVisuals = physical.Find("RecorderMeshVisuals");
        if (meshVisuals == null || !meshVisuals.gameObject.activeSelf)
        {
            failures.Add($"{scenePath}: missing_active_RecorderMeshVisuals");
            return;
        }

        Graphic[] graphics = panel.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            failures.Add($"{scenePath}: active_panel_graphic_should_be_disabled={PathOf(graphic.transform)} type={graphic.GetType().Name}");
        }

        Canvas[] canvases = panel.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!IsUnderNamedAncestor(canvas.transform, "RecorderVisibleCanvas") &&
                canvas.transform.name != "RecorderVisibleCanvas")
            {
                failures.Add($"{scenePath}: active_non_visible_canvas={PathOf(canvas.transform)}");
            }
        }

        Renderer[] renderers = meshVisuals.GetComponentsInChildren<Renderer>(true);
        int enabledRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            enabledRendererCount++;
            Material material = renderer.sharedMaterial;
            string shaderName = material != null && material.shader != null ? material.shader.name : "null";
            if (shaderName == "Hidden/InternalErrorShader" ||
                material == null ||
                material.shader == null)
            {
                failures.Add($"{scenePath}: mesh_visual_renderer={PathOf(renderer.transform)} uses_error_or_missing_shader");
            }
        }

        if (enabledRendererCount < 5)
        {
            failures.Add($"{scenePath}: expected_at_least_5_enabled_mesh_visual_renderers found={enabledRendererCount}");
        }

        if (meshVisuals.Find("BuildMarkerMesh_v43") == null)
        {
            failures.Add($"{scenePath}: missing_BuildMarkerMesh_v43");
        }
    }

    private static void VerifyDefaultLayer(string scenePath, Transform panel, List<string> failures)
    {
        Transform physical = panel.Find("RecorderPhysicalPanel");
        if (panel.gameObject.layer != 5)
        {
            failures.Add($"{scenePath}: panel_root_layer={panel.gameObject.layer}_expected_ui_5");
        }

        if (physical != null && physical.gameObject.layer != 5)
        {
            failures.Add($"{scenePath}: physical_panel_layer={physical.gameObject.layer}_expected_ui_5");
        }

        Transform bar = physical != null ? physical.Find("RecorderHandler") : null;
        if (bar != null && bar.gameObject.layer != 5)
        {
            failures.Add($"{scenePath}: recorder_handler_layer={bar.gameObject.layer}_expected_ui_5");
        }
    }

    private static void VerifyMeshVisuals(string scenePath, Transform physical, List<string> failures)
    {
        Transform visibleCanvas = physical.Find("RecorderVisibleCanvas");
        if (visibleCanvas != null && visibleCanvas.gameObject.activeSelf)
        {
            failures.Add($"{scenePath}: active_RecorderVisibleCanvas_should_be_removed_for_quest_mesh_visuals");
        }

        Transform meshVisuals = physical.Find("RecorderMeshVisuals");
        if (meshVisuals == null || !meshVisuals.gameObject.activeSelf)
        {
            failures.Add($"{scenePath}: missing_active_RecorderMeshVisuals");
            return;
        }

        Renderer[] renderers = meshVisuals.GetComponentsInChildren<Renderer>(true);
        int enabledRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            enabledRendererCount++;
            Material material = renderer.sharedMaterial;
            string shaderName = material != null && material.shader != null ? material.shader.name : "null";
            if (shaderName == "Hidden/InternalErrorShader")
            {
                failures.Add($"{scenePath}: mesh_visual_renderer={PathOf(renderer.transform)} uses_error_shader");
            }

            if (shaderName != "Universal Render Pipeline/Unlit" &&
                shaderName != "EnvironmentDepth/OcclusionLit" &&
                shaderName != "Sprites/Default" &&
                shaderName != "UI/Default" &&
                shaderName != "Oculus/Unlit" &&
                shaderName != "Oculus/Unlit Transparent Color" &&
                shaderName != "Universal Render Pipeline/Lit" &&
                shaderName != "Universal Render Pipeline/Simple Lit")
            {
                failures.Add($"{scenePath}: mesh_visual_renderer={PathOf(renderer.transform)} shader={shaderName}_expected_builtin_quest_safe_shader");
            }
        }

        if (enabledRendererCount < 5)
        {
            failures.Add($"{scenePath}: expected_at_least_5_enabled_mesh_visual_renderers found={enabledRendererCount}");
        }

        if (meshVisuals.Find("BuildMarkerMesh_v43") == null)
        {
            failures.Add($"{scenePath}: missing_BuildMarkerMesh_v43");
        }
    }

    private static void VerifyRayGrab(string scenePath, Transform root, Transform bar, List<string> failures)
    {
        Grabbable grabbable = root.GetComponent<Grabbable>();
        MoveRelativeToTargetProvider movementProvider = root.GetComponent<MoveRelativeToTargetProvider>();
        RayInteractable rootRay = root.GetComponent<RayInteractable>();
        RayInteractable barRay = bar.GetComponent<RayInteractable>();

        if (grabbable == null)
        {
            failures.Add($"{scenePath}: missing_root_Grabbable");
        }

        if (movementProvider == null)
        {
            failures.Add($"{scenePath}: missing_root_MoveRelativeToTargetProvider");
        }

        VerifyRayInteractable(scenePath, "root", rootRay, failures, requireUmivrPlaneSurface: true, requireNullSelectSurface: false);
        VerifyRayInteractable(scenePath, "bar", barRay, failures, requireUmivrPlaneSurface: true, requireNullSelectSurface: false);
        VerifyBarSurfaceSize(scenePath, barRay, failures);
        VerifyWideInteractionSurface(scenePath, bar, failures);
        Collider barCollider = bar.GetComponent<Collider>();
        if (barCollider == null)
        {
            failures.Add($"{scenePath}: missing_bar_collider");
        }
        else if (!barCollider.isTrigger)
        {
            failures.Add($"{scenePath}: bar_collider_should_be_trigger");
        }

        ColliderSurface colliderSurface = bar.GetComponent<ColliderSurface>();
        if (colliderSurface != null && colliderSurface.enabled)
        {
            failures.Add($"{scenePath}: enabled_bar_ColliderSurface_should_use_UMIVR_UnionClippedPlaneSurface");
        }

        PhysicalRecorderPanelController controller = root.GetComponentInChildren<PhysicalRecorderPanelController>(true);
        if (controller == null)
        {
            failures.Add($"{scenePath}: missing_PhysicalRecorderPanelController");
        }
        else if (!controller.EnableOvrControllerRayFallback)
        {
            failures.Add($"{scenePath}: disabled_ovr_controller_ray_fallback");
        }
    }

    private static void VerifyWideInteractionSurface(string scenePath, Transform bar, List<string> failures)
    {
        Transform physical = bar != null ? bar.parent : null;
        Transform wideSurface = physical != null ? physical.Find("PhysicalPanelWideInteractionSurface") : null;
        if (wideSurface == null)
        {
            failures.Add($"{scenePath}: missing_PhysicalPanelWideInteractionSurface");
            return;
        }

        if (wideSurface.localScale.x < 0.35f || wideSurface.localScale.y < 0.18f)
        {
            failures.Add($"{scenePath}: wide_surface_too_small scale={wideSurface.localScale}");
        }
    }

    private static void VerifyBarSurfaceSize(string scenePath, RayInteractable barRay, List<string> failures)
    {
        if (barRay == null || barRay.Surface is not Component component)
        {
            return;
        }

        Vector3 scale = component.transform.localScale;
        if (Mathf.Abs(scale.x - 0.36f) > 0.02f || Mathf.Abs(scale.y - 0.048f) > 0.02f)
        {
            failures.Add($"{scenePath}: bar_surface_scale={scale} expected_approx_0.36x0.048");
        }
    }

    private static void VerifyRecorder(string scenePath, List<string> failures)
    {
        QuestCameraRecorder recorder = UnityEngine.Object.FindFirstObjectByType<QuestCameraRecorder>();
        if (recorder == null)
        {
            failures.Add($"{scenePath}: missing_QuestCameraRecorder");
            return;
        }

        SerializedObject serializedRecorder = new SerializedObject(recorder);
        VerifyObjectReference(scenePath, serializedRecorder, "leftCameraAccess", failures);
        VerifyObjectReference(scenePath, serializedRecorder, "rightCameraAccess", failures);
        VerifyObjectReference(scenePath, serializedRecorder, "gazeReceiver", failures);
        VerifyObjectReference(scenePath, serializedRecorder, "telemetrySender", failures);
        VerifyBool(scenePath, serializedRecorder, "recordRightCamera", true, failures);
        VerifyBool(scenePath, serializedRecorder, "recordSynchronizedTrajectory", true, failures);
        VerifyTelemetrySender(scenePath, recorder, serializedRecorder, failures);
        VerifyCommandBridge(scenePath, recorder, failures);
    }

    private static void VerifyCommandBridge(
        string scenePath,
        QuestCameraRecorder recorder,
        List<string> failures)
    {
        QuestCameraRecorderCommandBridge commandBridge = UnityEngine.Object.FindFirstObjectByType<QuestCameraRecorderCommandBridge>();
        if (commandBridge == null)
        {
            failures.Add($"{scenePath}: missing_QuestCameraRecorderCommandBridge");
            return;
        }

        if (commandBridge.gameObject != recorder.gameObject)
        {
            failures.Add($"{scenePath}: command_bridge_should_be_on_recorder_gameobject bridge={PathOf(commandBridge.transform)}");
        }

        SerializedObject serializedBridge = new SerializedObject(commandBridge);
        SerializedProperty recorderProperty = serializedBridge.FindProperty("recorder");
        if (recorderProperty == null || recorderProperty.objectReferenceValue != recorder)
        {
            failures.Add($"{scenePath}: command_bridge_missing_recorder_reference");
        }

        SerializedProperty enableCommands = serializedBridge.FindProperty("enableFileCommands");
        if (enableCommands == null || !enableCommands.boolValue)
        {
            failures.Add($"{scenePath}: command_bridge_file_commands_disabled");
        }

        SerializedProperty commandFile = serializedBridge.FindProperty("commandFileName");
        if (commandFile == null || string.IsNullOrWhiteSpace(commandFile.stringValue))
        {
            failures.Add($"{scenePath}: command_bridge_command_file_empty");
        }
    }

    private static void VerifyTelemetrySender(
        string scenePath,
        QuestCameraRecorder recorder,
        SerializedObject serializedRecorder,
        List<string> failures)
    {
        SerializedProperty senderProperty = serializedRecorder.FindProperty("telemetrySender");
        QuestRecordingTelemetrySender sender = senderProperty != null
            ? senderProperty.objectReferenceValue as QuestRecordingTelemetrySender
            : null;
        if (sender == null)
        {
            return;
        }

        if (sender.gameObject != recorder.gameObject)
        {
            failures.Add($"{scenePath}: recorder_telemetrySender_should_be_on_recorder_gameobject sender={PathOf(sender.transform)}");
        }

        SerializedObject serializedSender = new SerializedObject(sender);
        SerializedProperty sendTelemetry = serializedSender.FindProperty("sendTelemetry");
        if (sendTelemetry == null || !sendTelemetry.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_sendTelemetry_disabled");
        }

        SerializedProperty host = serializedSender.FindProperty("host");
        if (host == null || string.IsNullOrWhiteSpace(host.stringValue))
        {
            failures.Add($"{scenePath}: telemetry_host_empty");
        }

        SerializedProperty port = serializedSender.FindProperty("port");
        if (port == null || port.intValue <= 0 || port.intValue > 65535)
        {
            string value = port != null ? port.intValue.ToString() : "missing";
            failures.Add($"{scenePath}: telemetry_port={value}_expected_1_to_65535");
        }

        SerializedProperty maxPackets = serializedSender.FindProperty("maxPacketsPerSecond");
        if (maxPackets == null || maxPackets.intValue <= 0)
        {
            string value = maxPackets != null ? maxPackets.intValue.ToString() : "missing";
            failures.Add($"{scenePath}: telemetry_maxPacketsPerSecond={value}_expected_positive");
        }

        SerializedProperty lifecycle = serializedSender.FindProperty("sendLifecycleMessages");
        if (lifecycle == null || !lifecycle.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_lifecycle_messages_disabled");
        }

        SerializedProperty statusLogs = serializedSender.FindProperty("logTelemetryStatus");
        if (statusLogs == null || !statusLogs.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_status_logs_disabled");
        }

        SerializedProperty autoTrackingSpace = serializedSender.FindProperty("autoResolveTrackingSpace");
        if (autoTrackingSpace == null || !autoTrackingSpace.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_autoResolveTrackingSpace_disabled");
        }

        VerifyTelemetryTransformReference(
            scenePath,
            serializedSender,
            "trackingSpace",
            "TrackingSpace",
            failures);

        SerializedProperty autoControllerAnchors = serializedSender.FindProperty("autoResolveControllerAnchors");
        if (autoControllerAnchors == null || !autoControllerAnchors.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_autoResolveControllerAnchors_disabled");
        }

        SerializedProperty autoInteractionControllerRefs = serializedSender.FindProperty("autoResolveInteractionControllerRefs");
        if (autoInteractionControllerRefs == null || !autoInteractionControllerRefs.boolValue)
        {
            failures.Add($"{scenePath}: telemetry_autoResolveInteractionControllerRefs_disabled");
        }

        VerifyTelemetryTransformReference(
            scenePath,
            serializedSender,
            "leftControllerAnchor",
            "LeftControllerAnchor",
            failures);
        VerifyTelemetryTransformReference(
            scenePath,
            serializedSender,
            "rightControllerAnchor",
            "RightControllerAnchor",
            failures);
    }

    private static void VerifyTelemetryTransformReference(
        string scenePath,
        SerializedObject serializedSender,
        string propertyName,
        string expectedName,
        List<string> failures)
    {
        SerializedProperty property = serializedSender.FindProperty(propertyName);
        Transform transform = property != null ? property.objectReferenceValue as Transform : null;
        if (transform == null)
        {
            failures.Add($"{scenePath}: telemetry_missing_{propertyName}");
            return;
        }

        if (transform.name != expectedName)
        {
            failures.Add($"{scenePath}: telemetry_{propertyName}={PathOf(transform)} expected_name={expectedName}");
        }
    }

    private static void VerifyObjectReference(
        string scenePath,
        SerializedObject serializedObject,
        string propertyName,
        List<string> failures)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == null)
        {
            failures.Add($"{scenePath}: missing_recorder_{propertyName}");
        }
    }

    private static void VerifyBool(
        string scenePath,
        SerializedObject serializedObject,
        string propertyName,
        bool expected,
        List<string> failures)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.boolValue != expected)
        {
            string value = property != null ? property.boolValue.ToString() : "missing";
            failures.Add($"{scenePath}: recorder_{propertyName}={value}_expected_{expected}");
        }
    }

    private static void VerifyRayInteractable(
        string scenePath,
        string label,
        RayInteractable rayInteractable,
        List<string> failures,
        bool requireUmivrPlaneSurface,
        bool requireNullSelectSurface)
    {
        if (rayInteractable == null)
        {
            failures.Add($"{scenePath}: missing_{label}_RayInteractable");
            return;
        }

        if (!rayInteractable.enabled)
        {
            failures.Add($"{scenePath}: disabled_{label}_RayInteractable");
        }

        SerializedObject serializedRayInteractable = new SerializedObject(rayInteractable);
        SerializedProperty surface = serializedRayInteractable.FindProperty("_surface");
        SerializedProperty selectSurface = serializedRayInteractable.FindProperty("_selectSurface");
        UnityEngine.Object surfaceObject = surface != null ? surface.objectReferenceValue : null;
        UnityEngine.Object selectSurfaceObject = selectSurface != null ? selectSurface.objectReferenceValue : null;
        if (surfaceObject == null)
        {
            failures.Add($"{scenePath}: missing_{label}_RayInteractable_surface");
        }

        if (!requireNullSelectSurface && selectSurfaceObject == null)
        {
            failures.Add($"{scenePath}: missing_{label}_RayInteractable_selectSurface");
        }

        if (!requireNullSelectSurface && surfaceObject != null && selectSurfaceObject != null && surfaceObject != selectSurfaceObject)
        {
            failures.Add($"{scenePath}: mismatched_{label}_RayInteractable_selectSurface surface={surfaceObject.name} selectSurface={selectSurfaceObject.name}");
        }

        if (requireUmivrPlaneSurface && surfaceObject is not UnionClippedPlaneSurface)
        {
            string typeName = surfaceObject != null ? surfaceObject.GetType().Name : "null";
            failures.Add($"{scenePath}: {label}_RayInteractable_surface_not_UMIVR_UnionClippedPlaneSurface type={typeName}");
        }
    }

    private static void VerifyAllRayInteractablesHaveSelectSurface(string scenePath, List<string> failures)
    {
        RayInteractable[] rayInteractables = UnityEngine.Object.FindObjectsByType<RayInteractable>(
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
            UnityEngine.Object surfaceObject = surface != null ? surface.objectReferenceValue : null;
            UnityEngine.Object selectSurfaceObject = selectSurface != null ? selectSurface.objectReferenceValue : null;
            if (surfaceObject != null && selectSurfaceObject == null)
            {
                failures.Add($"{scenePath}: RayInteractable_missing_selectSurface path={PathOf(rayInteractable.transform)} surface={surfaceObject.name}");
            }
        }
    }

    private static bool SceneAssetExists(string scenePath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return File.Exists(Path.Combine(projectRoot, scenePath));
    }

    private static string PathOf(Transform transform)
    {
        if (transform == null)
        {
            return "null";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static bool IsUnderNamedAncestor(Transform transform, string ancestorName)
    {
        while (transform != null)
        {
            if (transform.name == ancestorName)
            {
                return true;
            }

            transform = transform.parent;
        }

        return false;
    }
}
