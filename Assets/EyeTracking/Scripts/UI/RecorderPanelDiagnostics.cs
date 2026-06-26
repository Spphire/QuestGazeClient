using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EyeTracking.Recording;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.UI
{
    public sealed class RecorderPanelDiagnostics : MonoBehaviour
    {
        [SerializeField] private float logIntervalSeconds = 2f;
        [SerializeField] private bool logActiveObjectDetails = true;
        [SerializeField] private int maxDetailEntries = 12;
        [SerializeField] private int maxGlobalRenderEntries = 160;
        [SerializeField] private bool writeDiagnosticsFile = true;
        [SerializeField] private string diagnosticsFileName = "recorder_panel_diagnostics_latest.json";

        private float nextLogTime;

        private void OnEnable()
        {
            CaptureNow("OnEnable");
        }

        private void Start()
        {
            CaptureNow("Start");
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextLogTime)
            {
                return;
            }

            nextLogTime = Time.unscaledTime + Mathf.Max(0.25f, logIntervalSeconds);
            CaptureNow("LateUpdate");
        }

        private void CaptureNow(string source)
        {
            Transform bar = transform.Find("RecorderPhysicalPanel/RecorderHandler") ??
                            transform.Find("HandleCanvas2/Canvas/Menu/RecorderHandler") ??
                            transform.Find("RecorderSafeUnityPanel/Canvas/Panel/SafeDragBar") ??
                            transform.Find("RecorderFallbackPanel/Canvas/RecorderHandler");
            Transform meshVisuals = transform.Find("RecorderPhysicalPanel/RecorderMeshVisuals");
            int activeGeneratedRenderers = CountActiveGeneratedRenderers();
            int activeGraphics = CountActiveGraphics();
            int activeLegacyVisuals = CountActiveLegacyVisuals();
            bool fallbackActive = IsActive("RecorderFallbackPanel");
            bool physicalActive = IsActive("RecorderPhysicalPanel");
            bool flatActive = IsActive("FlatUnityCanvas");
            bool handleActive = IsActive("HandleCanvas2");
            Collider barCollider = bar != null ? bar.GetComponent<Collider>() : null;
            Transform visibleCanvas = transform.Find("RecorderPhysicalPanel/RecorderVisibleCanvas");

            Debug.Log(
                $"[RecorderPanelDiagnostics] source={source} bar={(bar != null ? bar.name : "null")} " +
                $"barCollider={(barCollider != null ? barCollider.GetType().Name : "none")} " +
                RayInteractableSummary(bar) +
                $"fallbackActive={fallbackActive} physicalActive={physicalActive} flatActive={flatActive} handleActive={handleActive} " +
                $"activeLegacyVisuals={activeLegacyVisuals} activeGeneratedRenderers={activeGeneratedRenderers} activeGraphics={activeGraphics}",
                this);

            if (logActiveObjectDetails)
            {
                LogErrorShaderObjects();
                LogGlobalErrorShaderObjects();
                LogPhysicalPanelRendererSummary();
                LogActiveObjectDetails();
            }

            if (writeDiagnosticsFile)
            {
                WriteDiagnosticsFile(bar, barCollider, visibleCanvas, meshVisuals);
            }
        }

        private bool IsActive(string childPath)
        {
            Transform child = transform.Find(childPath);
            return child != null && child.gameObject.activeInHierarchy;
        }

        private int CountActiveGeneratedRenderers()
        {
            int count = 0;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null &&
                    renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    IsGeneratedPanelVisual(renderer.transform))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActiveLegacyVisuals()
        {
            int count = 0;
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (candidate.name == "FlatUnityCanvas" ||
                    candidate.name == "HandleCanvas2" ||
                    candidate.name == "RecorderSafeUnityPanel" ||
                    IsGeneratedPanelVisual(candidate))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActiveGraphics()
        {
            int count = 0;
            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic != null &&
                    graphic.enabled &&
                    graphic.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private void LogActiveObjectDetails()
        {
            int remaining = Mathf.Max(0, maxDetailEntries);
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length && remaining > 0; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material material = renderer.sharedMaterial;
                string materialName = material != null ? material.name : "null";
                string shaderName = material != null && material.shader != null ? material.shader.name : "null";
                Debug.Log(
                    $"[RecorderPanelDiagnostics] activeRenderer path={PathOf(renderer.transform)} material={materialName} shader={shaderName}",
                    renderer);
                remaining--;
            }

            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length && remaining > 0; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!IsRecorderVisibleCanvasGraphic(graphic.transform) && remaining <= maxDetailEntries / 2)
                {
                    continue;
                }

                Material material = graphic.material;
                Material renderMaterial = SafeGraphicMaterialForRendering(graphic);
                string materialName = material != null ? material.name : "null";
                string shaderName = material != null && material.shader != null ? material.shader.name : "null";
                string renderMaterialName = renderMaterial != null ? renderMaterial.name : "null";
                string renderShaderName = renderMaterial != null && renderMaterial.shader != null ? renderMaterial.shader.name : "null";
                Debug.Log(
                    $"[RecorderPanelDiagnostics] activeGraphic path={PathOf(graphic.transform)} type={graphic.GetType().Name} material={materialName} shader={shaderName} renderMaterial={renderMaterialName} renderShader={renderShaderName}",
                    graphic);
                remaining--;
            }
        }

        private void LogErrorShaderObjects()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    string shaderName = material != null && material.shader != null ? material.shader.name : "null";
                    if (material == null || material.shader == null || shaderName == "Hidden/InternalErrorShader")
                    {
                        Debug.LogError(
                            $"[RecorderPanelDiagnostics] ERROR_SHADER renderer path={PathOf(renderer.transform)} materialIndex={j} material={(material != null ? material.name : "null")} shader={shaderName}",
                            renderer);
                    }
                }
            }

            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material renderMaterial = SafeGraphicMaterialForRendering(graphic);
                string renderShaderName = renderMaterial != null && renderMaterial.shader != null ? renderMaterial.shader.name : "null";
                if (renderMaterial == null || renderMaterial.shader == null || renderShaderName == "Hidden/InternalErrorShader")
                {
                    Debug.LogError(
                        $"[RecorderPanelDiagnostics] ERROR_SHADER graphic path={PathOf(graphic.transform)} type={graphic.GetType().Name} renderMaterial={(renderMaterial != null ? renderMaterial.name : "null")} renderShader={renderShaderName}",
                        graphic);
                }
            }
        }

        private void LogGlobalErrorShaderObjects()
        {
            int remaining = Mathf.Max(1, maxDetailEntries);
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length && remaining > 0; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int j = 0; j < materials.Length && remaining > 0; j++)
                {
                    Material material = materials[j];
                    string shaderName = material != null && material.shader != null ? material.shader.name : "null";
                    if (material == null || material.shader == null || shaderName == "Hidden/InternalErrorShader")
                    {
                        Debug.LogError(
                            $"[RecorderPanelDiagnostics] GLOBAL_ERROR_SHADER renderer path={PathOf(renderer.transform)} materialIndex={j} material={(material != null ? material.name : "null")} shader={shaderName}",
                            renderer);
                        remaining--;
                    }
                }
            }
        }

        private void LogPhysicalPanelRendererSummary()
        {
            Transform physical = transform.Find("RecorderPhysicalPanel");
            if (physical == null)
            {
                return;
            }

            Renderer[] renderers = physical.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material material = renderer.sharedMaterial;
                string materialName = material != null ? material.name : "null";
                string shaderName = material != null && material.shader != null ? material.shader.name : "null";
                Debug.Log(
                    $"[RecorderPanelDiagnostics] physicalRenderer path={PathOf(renderer.transform)} active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} material={materialName} shader={shaderName}",
                    renderer);
            }
        }

        private void WriteDiagnosticsFile(Transform bar, Collider barCollider, Transform visibleCanvas, Transform meshVisuals)
        {
            try
            {
                DiagnosticsSnapshot snapshot = BuildSnapshot(bar, barCollider, visibleCanvas, meshVisuals);
                string directory = Path.Combine(Application.persistentDataPath, "record");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, diagnosticsFileName);
                File.WriteAllText(path, JsonUtility.ToJson(snapshot, true), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RecorderPanelDiagnostics] Failed to write diagnostics file: " + exception.Message, this);
            }
        }

        private DiagnosticsSnapshot BuildSnapshot(Transform bar, Collider barCollider, Transform visibleCanvas, Transform meshVisuals)
        {
            QuestCameraRecorder recorder = FindFirstObjectByType<QuestCameraRecorder>();
            PhysicalRecorderPanelController controller = GetComponentInChildren<PhysicalRecorderPanelController>(true);
            DiagnosticsSnapshot snapshot = new DiagnosticsSnapshot
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                realtimeSinceStartup = Time.realtimeSinceStartupAsDouble,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                rootPath = PathOf(transform),
                directChildren = DirectChildNames(transform),
                fallbackActive = IsActive("RecorderFallbackPanel"),
                physicalActive = IsActive("RecorderPhysicalPanel"),
                flatActive = IsActive("FlatUnityCanvas"),
                handleActive = IsActive("HandleCanvas2"),
                activeLegacyVisuals = CountActiveLegacyVisuals(),
                activeGeneratedRenderers = CountActiveGeneratedRenderers(),
                activeGraphics = CountActiveGraphics(),
                barPath = PathOf(bar),
                barColliderType = barCollider != null ? barCollider.GetType().Name : "none",
                barColliderIsTrigger = barCollider != null && barCollider.isTrigger,
                barColliderBoundsCenter = barCollider != null ? Vector3ToArray(barCollider.bounds.center) : null,
                barColliderBoundsSize = barCollider != null ? Vector3ToArray(barCollider.bounds.size) : null,
                visibleCanvas = BuildVisibleCanvasSnapshot(visibleCanvas),
                meshVisuals = BuildMeshVisualSnapshot(meshVisuals),
                rootRayInteractable = BuildRayInteractableSnapshot(transform),
                barRayInteractable = BuildRayInteractableSnapshot(bar),
                grabbable = BuildGrabbableSnapshot(transform),
                movementProvider = BuildMovementProviderSnapshot(transform),
                controller = BuildControllerSnapshot(controller),
                recorder = BuildRecorderSnapshot(recorder),
                physicalRenderers = PhysicalRendererInfos(),
                globalRecorderRenderers = GlobalRecorderRendererInfos(),
                globalRecorderGraphics = GlobalRecorderGraphicInfos(),
                globalErrorShaderRenderers = GlobalErrorShaderInfos(),
                globalPurpleOrErrorRenderers = GlobalPurpleOrErrorRendererInfos(maxGlobalRenderEntries),
                globalPurpleOrErrorGraphics = GlobalPurpleOrErrorGraphicInfos(maxGlobalRenderEntries),
                allActiveRenderers = AllActiveRendererInfos(maxGlobalRenderEntries),
                allActiveGraphics = AllActiveGraphicInfos(maxGlobalRenderEntries)
            };

            return snapshot;
        }

        private static GrabbableSnapshot BuildGrabbableSnapshot(Transform target)
        {
            Grabbable grabbable = target != null ? target.GetComponent<Grabbable>() : null;
            if (grabbable == null)
            {
                return null;
            }

            return new GrabbableSnapshot
            {
                path = PathOf(grabbable.transform),
                enabled = grabbable.enabled,
                activeInHierarchy = grabbable.gameObject.activeInHierarchy,
                maxGrabPoints = ReadPrivateInt(grabbable, "_maxGrabPoints", int.MinValue)
            };
        }

        private static MovementProviderSnapshot BuildMovementProviderSnapshot(Transform target)
        {
            MoveRelativeToTargetProvider movementProvider = target != null ? target.GetComponent<MoveRelativeToTargetProvider>() : null;
            if (movementProvider == null)
            {
                return null;
            }

            return new MovementProviderSnapshot
            {
                path = PathOf(movementProvider.transform),
                enabled = movementProvider.enabled,
                activeInHierarchy = movementProvider.gameObject.activeInHierarchy
            };
        }

        private static RayInteractableSnapshot BuildRayInteractableSnapshot(Transform target)
        {
            RayInteractable rayInteractable = target != null ? target.GetComponent<RayInteractable>() : null;
            if (rayInteractable == null)
            {
                return null;
            }

            return new RayInteractableSnapshot
            {
                path = PathOf(rayInteractable.transform),
                enabled = rayInteractable.enabled,
                activeInHierarchy = rayInteractable.gameObject.activeInHierarchy,
                hasSurface = rayInteractable.Surface != null,
                surfaceType = rayInteractable.Surface != null ? rayInteractable.Surface.GetType().Name : "null",
                surfacePath = SurfacePath(rayInteractable.Surface),
                maxInteractors = ReadPrivateInt(rayInteractable, "_maxInteractors", int.MinValue),
                maxSelectingInteractors = ReadPrivateInt(rayInteractable, "_maxSelectingInteractors", int.MinValue),
                serializedSelectSurfacePath = SerializedSurfacePath(rayInteractable, "_selectSurface"),
                surfaceLocalPosition = SurfaceLocalPosition(rayInteractable.Surface),
                surfaceLocalScale = SurfaceLocalScale(rayInteractable.Surface),
                surfaceWorldPosition = SurfaceWorldPosition(rayInteractable.Surface),
                surfaceLossyScale = SurfaceLossyScale(rayInteractable.Surface)
            };
        }

        private static VisibleCanvasSnapshot BuildVisibleCanvasSnapshot(Transform visibleCanvas)
        {
            if (visibleCanvas == null)
            {
                return null;
            }

            Canvas canvas = visibleCanvas.GetComponent<Canvas>();
            Graphic[] graphics = visibleCanvas.GetComponentsInChildren<Graphic>(true);
            int enabledGraphics = 0;
            int likelyPurpleGraphics = 0;
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                enabledGraphics++;
                if (GraphicInfoFrom(graphic).likelyPurple)
                {
                    likelyPurpleGraphics++;
                }
            }

            return new VisibleCanvasSnapshot
            {
                path = PathOf(visibleCanvas),
                activeInHierarchy = visibleCanvas.gameObject.activeInHierarchy,
                canvasEnabled = canvas != null && canvas.enabled,
                graphicCount = graphics.Length,
                enabledGraphicCount = enabledGraphics,
                likelyPurpleGraphicCount = likelyPurpleGraphics
            };
        }

        private static MeshVisualSnapshot BuildMeshVisualSnapshot(Transform meshVisuals)
        {
            if (meshVisuals == null)
            {
                return null;
            }

            Renderer[] renderers = meshVisuals.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            int likelyPurpleRenderers = 0;
            List<RendererInfo> rendererInfos = new List<RendererInfo>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                enabledRenderers++;
                RendererInfo info = RendererInfoFrom(renderer);
                rendererInfos.Add(info);
                if (info.likelyPurple)
                {
                    likelyPurpleRenderers++;
                }
            }

            return new MeshVisualSnapshot
            {
                path = PathOf(meshVisuals),
                activeInHierarchy = meshVisuals.gameObject.activeInHierarchy,
                rendererCount = renderers.Length,
                enabledRendererCount = enabledRenderers,
                likelyPurpleRendererCount = likelyPurpleRenderers,
                renderers = rendererInfos
            };
        }

        private static ControllerSnapshot BuildControllerSnapshot(PhysicalRecorderPanelController controller)
        {
            if (controller == null)
            {
                return null;
            }

            Collider dragBar = controller.DragBarCollider;
            return new ControllerSnapshot
            {
                path = PathOf(controller.transform),
                enabled = controller.enabled,
                activeInHierarchy = controller.gameObject.activeInHierarchy,
                isDragging = controller.IsDragging,
                lastRayStatus = controller.LastRayStatus,
                lastSelectSource = controller.LastSelectSource,
                lastRayCandidateCount = controller.LastRayCandidateCount,
                lastBestNearMissDistance = FloatOrMinusOne(controller.LastBestNearMissDistance),
                lastBestFallbackDistance = FloatOrMinusOne(controller.LastBestFallbackDistance),
                lastRightControllerPoseValid = controller.LastRightControllerPoseValid,
                lastRightRayToBarDistance = FloatOrMinusOne(controller.LastRightRayToBarDistance),
                lastDragStartTime = controller.LastDragStartTime,
                rayHitPadding = controller.RayHitPadding,
                dragBarNearRayTolerance = controller.DragBarNearRayTolerance,
                panelDragFallbackTolerance = controller.PanelDragFallbackTolerance,
                directHandDragStartTolerance = controller.DirectHandDragStartTolerance,
                enableOvrControllerRayFallback = controller.EnableOvrControllerRayFallback,
                dragBarPath = dragBar != null ? PathOf(dragBar.transform) : "null",
                dragBarBoundsCenter = dragBar != null ? Vector3ToArray(dragBar.bounds.center) : null,
                dragBarBoundsSize = dragBar != null ? Vector3ToArray(dragBar.bounds.size) : null
            };
        }

        private static RecorderSnapshot BuildRecorderSnapshot(QuestCameraRecorder recorder)
        {
            if (recorder == null)
            {
                return null;
            }

            return new RecorderSnapshot
            {
                path = PathOf(recorder.transform),
                enabled = recorder.enabled,
                activeInHierarchy = recorder.gameObject.activeInHierarchy,
                isReady = recorder.IsReady,
                isRecording = recorder.IsRecording,
                outputDirectory = recorder.OutputDirectory,
                recordRightCamera = recorder.RecordRightCamera,
                recordSynchronizedTrajectory = recorder.RecordSynchronizedTrajectory,
                hasLeftCameraAccess = recorder.HasLeftCameraAccess,
                hasRightCameraAccess = recorder.HasRightCameraAccess,
                hasGazeReceiver = recorder.HasGazeReceiver,
                gazeReceiverConnected = recorder.GazeReceiverConnected,
                lastInitStatus = recorder.LastInitStatus,
                lastInitError = recorder.LastInitError,
                leftCameraIsPlaying = recorder.LeftCameraIsPlaying,
                rightCameraIsPlaying = recorder.RightCameraIsPlaying,
                leftTextureType = recorder.LeftTextureType,
                rightTextureType = recorder.RightTextureType,
                leftTextureWidth = recorder.LeftTextureWidth,
                leftTextureHeight = recorder.LeftTextureHeight,
                rightTextureWidth = recorder.RightTextureWidth,
                rightTextureHeight = recorder.RightTextureHeight
            };
        }

        private List<RendererInfo> PhysicalRendererInfos()
        {
            List<RendererInfo> infos = new List<RendererInfo>();
            Transform physical = transform.Find("RecorderPhysicalPanel");
            if (physical == null)
            {
                return infos;
            }

            Renderer[] renderers = physical.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                infos.Add(RendererInfoFrom(renderers[i]));
            }

            return infos;
        }

        private static List<RendererInfo> GlobalErrorShaderInfos()
        {
            List<RendererInfo> infos = new List<RendererInfo>();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int j = 0; j < materials.Length; j++)
                {
                    string shaderName = ShaderName(materials[j]);
                    if (materials[j] == null || materials[j].shader == null || shaderName == "Hidden/InternalErrorShader")
                    {
                        RendererInfo info = RendererInfoFrom(renderer);
                        info.errorMaterialIndex = j;
                        infos.Add(info);
                        break;
                    }
                }
            }

            return infos;
        }

        private static List<RendererInfo> GlobalRecorderRendererInfos()
        {
            List<RendererInfo> infos = new List<RendererInfo>();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    !IsRecorderRelated(renderer.transform))
                {
                    continue;
                }

                infos.Add(RendererInfoFrom(renderer));
            }

            return infos;
        }

        private static List<GraphicInfo> GlobalRecorderGraphicInfos()
        {
            List<GraphicInfo> infos = new List<GraphicInfo>();
            Graphic[] graphics = FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null ||
                    !graphic.enabled ||
                    !graphic.gameObject.activeInHierarchy ||
                    !IsRecorderRelated(graphic.transform))
                {
                    continue;
                }

                Material material = graphic.material;
                Material renderMaterial = SafeGraphicMaterialForRendering(graphic);
                infos.Add(new GraphicInfo
                {
                    path = PathOf(graphic.transform),
                    type = graphic.GetType().Name,
                    material = material != null ? material.name : "null",
                    shader = ShaderName(material),
                    renderMaterial = renderMaterial != null ? renderMaterial.name : "null",
                    renderShader = ShaderName(renderMaterial)
                });
            }

            return infos;
        }

        private static List<RendererInfo> GlobalPurpleOrErrorRendererInfos(int maxEntries)
        {
            List<RendererInfo> infos = new List<RendererInfo>();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int limit = Mathf.Max(0, maxEntries);
            for (int i = 0; i < renderers.Length && infos.Count < limit; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                RendererInfo info = RendererInfoFrom(renderer);
                if (info != null && (info.likelyPurple || info.shader == "Hidden/InternalErrorShader" || !info.shaderSupported))
                {
                    infos.Add(info);
                }
            }

            return infos;
        }

        private static List<GraphicInfo> GlobalPurpleOrErrorGraphicInfos(int maxEntries)
        {
            List<GraphicInfo> infos = new List<GraphicInfo>();
            Graphic[] graphics = FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int limit = Mathf.Max(0, maxEntries);
            for (int i = 0; i < graphics.Length && infos.Count < limit; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GraphicInfo info = GraphicInfoFrom(graphic);
                if (info != null && (info.likelyPurple || info.renderShader == "Hidden/InternalErrorShader"))
                {
                    infos.Add(info);
                }
            }

            return infos;
        }

        private static List<RendererInfo> AllActiveRendererInfos(int maxEntries)
        {
            List<RendererInfo> infos = new List<RendererInfo>();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int limit = Mathf.Max(0, maxEntries);
            for (int i = 0; i < renderers.Length && infos.Count < limit; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                infos.Add(RendererInfoFrom(renderer));
            }

            return infos;
        }

        private static List<GraphicInfo> AllActiveGraphicInfos(int maxEntries)
        {
            List<GraphicInfo> infos = new List<GraphicInfo>();
            Graphic[] graphics = FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int limit = Mathf.Max(0, maxEntries);
            for (int i = 0; i < graphics.Length && infos.Count < limit; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                {
                    continue;
                }

                infos.Add(GraphicInfoFrom(graphic));
            }

            return infos;
        }

        private static RendererInfo RendererInfoFrom(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            Material material = renderer.sharedMaterial;
            Shader shader = material != null ? material.shader : null;
            return new RendererInfo
            {
                path = PathOf(renderer.transform),
                activeInHierarchy = renderer.gameObject.activeInHierarchy,
                enabled = renderer.enabled,
                material = material != null ? material.name : "null",
                shader = ShaderName(material),
                shaderSupported = shader != null && shader.isSupported,
                shaderPassCount = shader != null ? shader.passCount : -1,
                color = MaterialColor(material),
                likelyPurple = IsLikelyPurple(material),
                boundsCenter = Vector3ToArray(renderer.bounds.center),
                boundsSize = Vector3ToArray(renderer.bounds.size)
            };
        }

        private static GraphicInfo GraphicInfoFrom(Graphic graphic)
        {
            Material material = graphic.material;
            Material renderMaterial = SafeGraphicMaterialForRendering(graphic);
            Color color = graphic.color;
            return new GraphicInfo
            {
                path = PathOf(graphic.transform),
                type = graphic.GetType().Name,
                material = material != null ? material.name : "null",
                shader = ShaderName(material),
                renderMaterial = renderMaterial != null ? renderMaterial.name : "null",
                renderShader = ShaderName(renderMaterial),
                color = ColorToArray(color),
                likelyPurple = IsLikelyPurple(color) ||
                               ShaderName(material) == "Hidden/InternalErrorShader" ||
                               ShaderName(renderMaterial) == "Hidden/InternalErrorShader"
            };
        }

        private static string[] DirectChildNames(Transform root)
        {
            if (root == null)
            {
                return Array.Empty<string>();
            }

            string[] names = new string[root.childCount];
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                names[i] = child != null ? child.name : "null";
            }

            return names;
        }

        private static Material SafeGraphicMaterialForRendering(Graphic graphic)
        {
            if (graphic == null)
            {
                return null;
            }

            try
            {
                return graphic.materialForRendering;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RecorderPanelDiagnostics] Could not inspect graphic material path={PathOf(graphic.transform)} type={graphic.GetType().Name}: {exception.Message}", graphic);
                return null;
            }
        }

        private static string ShaderName(Material material)
        {
            return material != null && material.shader != null ? material.shader.name : "null";
        }

        private static float[] MaterialColor(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty("_BaseColor"))
            {
                return ColorToArray(material.GetColor("_BaseColor"));
            }

            if (material.HasProperty("_Color"))
            {
                return ColorToArray(material.GetColor("_Color"));
            }

            return null;
        }

        private static bool IsLikelyPurple(Material material)
        {
            string shaderName = ShaderName(material);
            if (shaderName == "Hidden/InternalErrorShader")
            {
                return true;
            }

            float[] color = MaterialColor(material);
            return color != null && IsLikelyPurple(color[0], color[1], color[2], color[3]);
        }

        private static bool IsLikelyPurple(Color color)
        {
            return IsLikelyPurple(color.r, color.g, color.b, color.a);
        }

        private static bool IsLikelyPurple(float r, float g, float b, float a)
        {
            return a > 0.05f && r > 0.45f && b > 0.45f && g < 0.28f;
        }

        private static string SurfacePath(Oculus.Interaction.Surfaces.ISurface surface)
        {
            if (surface is Component component)
            {
                return PathOf(component.transform);
            }

            return surface != null ? surface.GetType().Name : "null";
        }

        private static string SerializedSurfacePath(RayInteractable rayInteractable, string fieldName)
        {
            if (rayInteractable == null)
            {
                return "null";
            }

            FieldInfo field = typeof(RayInteractable).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return "field_not_found";
            }

            object value = field.GetValue(rayInteractable);
            if (value is Component component)
            {
                return PathOf(component.transform);
            }

            return value != null ? value.GetType().Name : "null";
        }

        private static float[] SurfaceLocalPosition(Oculus.Interaction.Surfaces.ISurface surface)
        {
            return surface is Component component ? Vector3ToArray(component.transform.localPosition) : null;
        }

        private static float[] SurfaceLocalScale(Oculus.Interaction.Surfaces.ISurface surface)
        {
            return surface is Component component ? Vector3ToArray(component.transform.localScale) : null;
        }

        private static float[] SurfaceWorldPosition(Oculus.Interaction.Surfaces.ISurface surface)
        {
            return surface is Component component ? Vector3ToArray(component.transform.position) : null;
        }

        private static float[] SurfaceLossyScale(Oculus.Interaction.Surfaces.ISurface surface)
        {
            return surface is Component component ? Vector3ToArray(component.transform.lossyScale) : null;
        }

        private static string RayInteractableSummary(Transform target)
        {
            RayInteractable rayInteractable = target != null ? target.GetComponent<RayInteractable>() : null;
            if (rayInteractable == null)
            {
                return "rayInteractable=none ";
            }

            return $"rayInteractable={rayInteractable.enabled} surface={SurfacePath(rayInteractable.Surface)} ";
        }

        private static float FloatOrMinusOne(float value)
        {
            return float.IsFinite(value) ? value : -1f;
        }

        private static int ReadPrivateInt(object target, string fieldName, int fallback)
        {
            if (target == null)
            {
                return fallback;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int))
            {
                return fallback;
            }

            return (int)field.GetValue(target);
        }

        private static float[] Vector3ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        private static float[] ColorToArray(Color value)
        {
            return new[] { value.r, value.g, value.b, value.a };
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

        private static bool IsGeneratedPanelVisual(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            string name = candidate.name;
            return name == "Surface" ||
                   name == "GradientEffect" ||
                   name == "Backplate" ||
                   name.Contains("RoundedBox") ||
                   name.Contains("Generated");
        }

        private static bool IsRecorderVisibleCanvasGraphic(Transform candidate)
        {
            while (candidate != null)
            {
                if (candidate.name == "RecorderVisibleCanvas")
                {
                    return true;
                }

                candidate = candidate.parent;
            }

            return false;
        }

        private static bool IsRecorderRelated(Transform candidate)
        {
            while (candidate != null)
            {
                string name = candidate.name;
                if (name == "QuestCameraRecorderPanel" ||
                    name == "RecorderUIEmptyPanel" ||
                    name == "RecorderUI" ||
                    name == "RecorderPhysicalPanel" ||
                    name == "RecorderFallbackPanel" ||
                    name == "RecorderVisibleCanvas" ||
                    name == "FlatUnityCanvas" ||
                    name == "HandleCanvas2" ||
                    name == "RecorderSafeUnityPanel" ||
                    name == "RecorderHandler" ||
                    name.Contains("Recorder") ||
                    name.Contains("RoundedBox") ||
                    name.Contains("QDS"))
                {
                    return true;
                }

                candidate = candidate.parent;
            }

            return false;
        }

        [Serializable]
        private sealed class DiagnosticsSnapshot
        {
            public string timestampUtc;
            public double realtimeSinceStartup;
            public string scene;
            public string rootPath;
            public string[] directChildren;
            public bool fallbackActive;
            public bool physicalActive;
            public bool flatActive;
            public bool handleActive;
            public int activeLegacyVisuals;
            public int activeGeneratedRenderers;
            public int activeGraphics;
            public string barPath;
            public string barColliderType;
            public bool barColliderIsTrigger;
            public float[] barColliderBoundsCenter;
            public float[] barColliderBoundsSize;
            public VisibleCanvasSnapshot visibleCanvas;
            public MeshVisualSnapshot meshVisuals;
            public RayInteractableSnapshot rootRayInteractable;
            public RayInteractableSnapshot barRayInteractable;
            public GrabbableSnapshot grabbable;
            public MovementProviderSnapshot movementProvider;
            public ControllerSnapshot controller;
            public RecorderSnapshot recorder;
            public List<RendererInfo> physicalRenderers;
            public List<RendererInfo> globalRecorderRenderers;
            public List<GraphicInfo> globalRecorderGraphics;
            public List<RendererInfo> globalErrorShaderRenderers;
            public List<RendererInfo> globalPurpleOrErrorRenderers;
            public List<GraphicInfo> globalPurpleOrErrorGraphics;
            public List<RendererInfo> allActiveRenderers;
            public List<GraphicInfo> allActiveGraphics;
        }

        [Serializable]
        private sealed class ControllerSnapshot
        {
            public string path;
            public bool enabled;
            public bool activeInHierarchy;
            public bool isDragging;
            public string lastRayStatus;
            public string lastSelectSource;
            public int lastRayCandidateCount;
            public float lastBestNearMissDistance;
            public float lastBestFallbackDistance;
            public bool lastRightControllerPoseValid;
            public float lastRightRayToBarDistance;
            public float lastDragStartTime;
            public float rayHitPadding;
            public float dragBarNearRayTolerance;
            public float panelDragFallbackTolerance;
            public float directHandDragStartTolerance;
            public bool enableOvrControllerRayFallback;
            public string dragBarPath;
            public float[] dragBarBoundsCenter;
            public float[] dragBarBoundsSize;
        }

        [Serializable]
        private sealed class VisibleCanvasSnapshot
        {
            public string path;
            public bool activeInHierarchy;
            public bool canvasEnabled;
            public int graphicCount;
            public int enabledGraphicCount;
            public int likelyPurpleGraphicCount;
        }

        [Serializable]
        private sealed class MeshVisualSnapshot
        {
            public string path;
            public bool activeInHierarchy;
            public int rendererCount;
            public int enabledRendererCount;
            public int likelyPurpleRendererCount;
            public List<RendererInfo> renderers;
        }

        [Serializable]
        private sealed class RayInteractableSnapshot
        {
            public string path;
            public bool enabled;
            public bool activeInHierarchy;
            public bool hasSurface;
            public string surfaceType;
            public string surfacePath;
            public int maxInteractors;
            public int maxSelectingInteractors;
            public string serializedSelectSurfacePath;
            public float[] surfaceLocalPosition;
            public float[] surfaceLocalScale;
            public float[] surfaceWorldPosition;
            public float[] surfaceLossyScale;
        }

        [Serializable]
        private sealed class GrabbableSnapshot
        {
            public string path;
            public bool enabled;
            public bool activeInHierarchy;
            public int maxGrabPoints;
        }

        [Serializable]
        private sealed class MovementProviderSnapshot
        {
            public string path;
            public bool enabled;
            public bool activeInHierarchy;
        }

        [Serializable]
        private sealed class RecorderSnapshot
        {
            public string path;
            public bool enabled;
            public bool activeInHierarchy;
            public bool isReady;
            public bool isRecording;
            public string outputDirectory;
            public bool recordRightCamera;
            public bool recordSynchronizedTrajectory;
            public bool hasLeftCameraAccess;
            public bool hasRightCameraAccess;
            public bool hasGazeReceiver;
            public bool gazeReceiverConnected;
            public string lastInitStatus;
            public string lastInitError;
            public bool leftCameraIsPlaying;
            public bool rightCameraIsPlaying;
            public string leftTextureType;
            public string rightTextureType;
            public int leftTextureWidth;
            public int leftTextureHeight;
            public int rightTextureWidth;
            public int rightTextureHeight;
        }

        [Serializable]
        private sealed class RendererInfo
        {
            public string path;
            public bool activeInHierarchy;
            public bool enabled;
            public string material;
            public string shader;
            public bool shaderSupported;
            public int shaderPassCount;
            public float[] color;
            public bool likelyPurple;
            public int errorMaterialIndex = -1;
            public float[] boundsCenter;
            public float[] boundsSize;
        }

        [Serializable]
        private sealed class GraphicInfo
        {
            public string path;
            public string type;
            public string material;
            public string shader;
            public string renderMaterial;
            public string renderShader;
            public float[] color;
            public bool likelyPurple;
        }
    }
}
