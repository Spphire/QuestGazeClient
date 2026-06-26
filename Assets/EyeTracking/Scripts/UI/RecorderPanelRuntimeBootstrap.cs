using EyeTracking.Recording;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EyeTracking.UI
{
    public static class RecorderPanelRuntimeBootstrap
    {
        private const int RecorderPanelLayer = 5;
        private const string PanelRootName = "QuestCameraRecorderPanel";
        private const string FallbackPanelRootName = "RecorderUIEmptyPanel";
        private const string RuntimePanelName = "RecorderFallbackPanel";
        private const string PhysicalPanelName = "RecorderPhysicalPanel";
        private const string MeshVisualRootName = "RecorderMeshVisuals";
        private const string PhysicalSurfaceName = "PhysicalInteractionSurface";
        private const int RepairFrameCount = 300;

        private static RecorderPanelRuntimeRepairLoop repairLoop;
        private static bool isBootstrapping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBootstrapState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            repairLoop = null;
            isBootstrapping = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DeregisterSceneLoadedBeforeBootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapRecorderPanel()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            BootstrapCurrentScene();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BootstrapCurrentScene();
        }

        private static void BootstrapCurrentScene()
        {
            if (isBootstrapping)
            {
                return;
            }

            isBootstrapping = true;
            try
            {
                int removed = DestroyRecorderPanelRoots();
                if (removed > 0)
                {
                    Debug.Log($"[RecorderPanelRuntimeBootstrap] Removed {removed} recorder panel root(s); recorder UI panel is disabled.");
                }
            }
            finally
            {
                isBootstrapping = false;
            }
        }

        private static int DestroyRecorderPanelRoots()
        {
            int removed = 0;
            List<Transform> roots = FindRecorderPanelRoots();
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                Transform root = roots[i];
                if (root == null)
                {
                    continue;
                }

                Object.Destroy(root.gameObject);
                removed++;
            }

            return removed;
        }

        private static void BootstrapPanelRoot(Transform panelRoot)
        {
            if (panelRoot == null)
            {
                return;
            }

            GameObject panel = panelRoot.gameObject;
            Debug.Log($"[RecorderPanelRuntimeBootstrap] Found panel root '{panel.name}'.", panel);
            PlacePanelInFrontOfViewIfNeeded(panelRoot);

            RecorderPanelLegacyVisualKiller visualKiller = panel.GetComponent<RecorderPanelLegacyVisualKiller>();
            if (visualKiller == null)
            {
                visualKiller = panel.AddComponent<RecorderPanelLegacyVisualKiller>();
            }

            visualKiller.enabled = true;
            visualKiller.DisableLegacyVisuals();
            DestroyLegacyVisualRoots(panelRoot);
            DestroyNonPhysicalPanelChildren(panelRoot);

            RecorderControlPanel control = panel.GetComponent<RecorderControlPanel>();
            if (control == null)
            {
                control = panel.AddComponent<RecorderControlPanel>();
            }

            if (control.Recorder == null)
            {
                control.Recorder = Object.FindFirstObjectByType<QuestCameraRecorder>();
            }

            DestroyFallbackPanel(panelRoot);
            BuildPhysicalPanel(panelRoot, control);
            visualKiller.DisableLegacyVisuals();
            DestroyLegacyVisualRoots(panelRoot);
            DestroyNonPhysicalPanelChildren(panelRoot);

            RecorderPanelDiagnostics diagnostics = panel.GetComponent<RecorderPanelDiagnostics>();
            if (diagnostics == null)
            {
                diagnostics = panel.AddComponent<RecorderPanelDiagnostics>();
            }

            diagnostics.enabled = true;

            Debug.Log("[RecorderPanelRuntimeBootstrap] Recorder panel runtime wiring requested.", panel);
        }

        private static List<Transform> FindRecorderPanelRoots()
        {
            List<Transform> roots = new List<Transform>();
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null ||
                    candidate.name != PanelRootName &&
                    candidate.name != FallbackPanelRootName &&
                    candidate.name != "RecorderUI" &&
                    candidate.name != PhysicalPanelName)
                {
                    continue;
                }

                Transform root = candidate.name == PhysicalPanelName && candidate.parent != null
                    ? candidate.parent
                    : candidate;
                if (!roots.Contains(root))
                {
                    roots.Add(root);
                }
            }

            return roots;
        }

        private static void EnsureRepairLoop(List<Transform> panelRoots)
        {
            if (repairLoop == null)
            {
                GameObject loopObject = new GameObject("RecorderPanelRuntimeRepairLoop");
                Object.DontDestroyOnLoad(loopObject);
                repairLoop = loopObject.AddComponent<RecorderPanelRuntimeRepairLoop>();
            }

            repairLoop.Begin(panelRoots, RepairFrameCount);
        }

        private static void EnsurePointableEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<PointableCanvasModule>() == null)
            {
                eventSystem.gameObject.AddComponent<PointableCanvasModule>();
            }
        }

        private static void PlacePanelInFrontOfViewIfNeeded(Transform panel)
        {
            if (panel == null || panel.childCount > 0 && panel.position.sqrMagnitude > 0.04f)
            {
                return;
            }

            Transform view = Camera.main != null ? Camera.main.transform : null;
            if (view == null)
            {
                OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>();
                view = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : null;
            }

            if (view == null)
            {
                panel.SetPositionAndRotation(new Vector3(0f, 1.35f, 0.85f), Quaternion.Euler(0f, 180f, 0f));
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 position = view.position + forward * 0.75f;
            position.y = Mathf.Max(view.position.y - 0.12f, 1.0f);
            panel.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        internal static void DisableGlobalLegacyRecorderVisuals()
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || IsRuntimeFallbackVisual(candidate))
                {
                    continue;
                }

                if (candidate.name != "FlatUnityCanvas" &&
                    candidate.name != "HandleCanvas2" &&
                    candidate.name != "RecorderSafeUnityPanel" &&
                    (candidate.name != "Surface" || !IsLegacyRecorderVisual(candidate)))
                {
                    continue;
                }

                candidate.gameObject.SetActive(false);
            }
        }

        private static bool IsLegacyRecorderVisual(Transform candidate)
        {
            Transform current = candidate;
            while (current != null)
            {
                string name = current.name;
                if (name == "FlatUnityCanvas" ||
                    name == "HandleCanvas2" ||
                    name == "RecorderSafeUnityPanel" ||
                    name == "RecorderUIEmptyPanel" ||
                    name == "QuestCameraRecorderPanel" ||
                    name.Contains("RoundedBox"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsRuntimeFallbackVisual(Transform candidate)
        {
            Transform current = candidate;
            while (current != null)
            {
                if (current.name == RuntimePanelName ||
                    current.name == PhysicalPanelName)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        internal static void BuildFallbackPanel(Transform root, RecorderControlPanel control)
        {
            if (control == null && root != null)
            {
                control = root.GetComponent<RecorderControlPanel>();
                if (control == null)
                {
                    control = root.gameObject.AddComponent<RecorderControlPanel>();
                }
            }

            if (root.Find(RuntimePanelName) != null)
            {
                WireExistingFallbackPanel(root, control);
                EnsureMaterialSanitizer(root.Find(RuntimePanelName));
                ForceSafeFallbackGraphicMaterials(root.Find(RuntimePanelName));
                HideFallbackCanvasVisuals(root.Find(RuntimePanelName));
                return;
            }

            GameObject panel = new GameObject(RuntimePanelName, typeof(RectTransform));
            panel.layer = RecorderPanelLayer;
            panel.transform.SetParent(root, false);

            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.localPosition = Vector3.zero;
            panelRect.localRotation = Quaternion.identity;
            panelRect.localScale = Vector3.one;
            panelRect.sizeDelta = new Vector2(360f, 220f);

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.layer = panel.layer;
            canvasObject.transform.SetParent(panel.transform, false);

            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            canvasRect.sizeDelta = panelRect.sizeDelta;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                              AdditionalCanvasShaderChannels.Normal |
                                              AdditionalCanvasShaderChannels.Tangent;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;
            AddInteractionSdkCanvasRayTarget(canvasObject, canvas, panelRect.sizeDelta);

            RectTransform background = CreateImage(canvasRect, "Background", new Color(0.08f, 0.1f, 0.11f, 0.96f), new Vector2(360f, 220f));
            background.anchoredPosition = Vector2.zero;

            RectTransform bar = CreateImage(canvasRect, "RecorderHandler", new Color(0.16f, 0.22f, 0.25f, 1f), new Vector2(360f, 48f));
            bar.anchorMin = new Vector2(0.5f, 1f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            ISurface handleSurface = EnsureUnionClippedPlaneSurface(bar, bar.sizeDelta);
            EnsureGrabbablePanelRoot(panel.transform, handleSurface);

            Text title = CreateText(bar, "Title", "Quest Camera Recorder", 22, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(14f, 0f);
            title.rectTransform.offsetMax = new Vector2(-14f, 0f);

            Text status = CreateText(canvasRect, "StatusText", "Waiting for camera", 22, TextAnchor.MiddleCenter);
            status.rectTransform.sizeDelta = new Vector2(320f, 42f);
            status.rectTransform.anchoredPosition = new Vector2(0f, 32f);

            Button startButton = CreateButton(canvasRect, "StartButton", "Start", new Vector2(-78f, -62f), new Color(0.1f, 0.5f, 0.28f, 1f));
            Button stopButton = CreateButton(canvasRect, "StopButton", "Stop", new Vector2(78f, -62f), new Color(0.58f, 0.16f, 0.16f, 1f));

            if (control != null)
            {
                startButton.onClick.AddListener(control.StartRecording);
                stopButton.onClick.AddListener(control.StopRecording);

                control.StatusTextLegacy = status;
                control.StartButton = startButton;
                control.StopButton = stopButton;
                control.Refresh();
            }

            BaseLinkDragController dragController = bar.gameObject.AddComponent<BaseLinkDragController>();
            dragController.Configure(panel.transform, bar);
            RecorderPanelPointerDragController pointerDragController = bar.gameObject.AddComponent<RecorderPanelPointerDragController>();
            pointerDragController.Configure(panel.transform);
            EnsureMaterialSanitizer(panel.transform);
            ForceSafeFallbackGraphicMaterials(panel.transform);
            HideFallbackCanvasVisuals(panel.transform);

            Debug.Log("[RecorderPanelRuntimeBootstrap] Standard Unity fallback recorder panel created.", root);
        }

        internal static void BuildPhysicalPanel(Transform root, RecorderControlPanel control)
        {
            if (root == null)
            {
                return;
            }

            if (control == null)
            {
                control = root.GetComponent<RecorderControlPanel>();
            }

            Transform existing = root.Find(PhysicalPanelName);
            if (existing != null)
            {
                WirePhysicalPanel(existing, control);
                EnsureOnlyPhysicalPanelChildren(root);
                return;
            }

            GameObject panel = new GameObject(PhysicalPanelName);
            panel.layer = RecorderPanelLayer;
            panel.transform.SetParent(root, false);
            panel.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one;

            CreatePhysicalPlate(panel.transform, "Background", new Vector3(0f, -0.024f, 0.004f), new Vector3(0.36f, 0.172f, 0.006f), new Color(0.08f, 0.1f, 0.11f, 1f));
            Collider bar = CreatePhysicalPlate(panel.transform, "RecorderHandler", new Vector3(0f, 0.086f, -0.002f), new Vector3(0.36f, 0.048f, 0.01f), new Color(0.16f, 0.22f, 0.25f, 1f));
            Collider start = CreatePhysicalPlate(panel.transform, "StartButton", new Vector3(-0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.1f, 0.5f, 0.28f, 1f));
            Collider stop = CreatePhysicalPlate(panel.transform, "StopButton", new Vector3(0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.58f, 0.16f, 0.16f, 1f));
            EnsurePhysicalPanelMeshVisuals(panel.transform);

            PhysicalRecorderPanelController controller = panel.AddComponent<PhysicalRecorderPanelController>();
            controller.Configure(root, control != null ? control.Recorder : Object.FindFirstObjectByType<QuestCameraRecorder>(), bar, start, stop);
            EnsurePhysicalPanelGrabInteraction(panel.transform, bar);
            EnsureOnlyPhysicalPanelChildren(root);
            Debug.Log("[RecorderPanelRuntimeBootstrap] Physical recorder panel created.", root);
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
                renderer.enabled = false;
                Object.Destroy(renderer);
            }

            MeshFilter meshFilter = plate.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                Object.Destroy(meshFilter);
            }

            Collider collider = plate.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
                InflatePhysicalCollider(collider, name);
            }

            return collider;
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

        private static void WirePhysicalPanel(Transform panel, RecorderControlPanel control)
        {
            Collider bar = panel.Find("RecorderHandler")?.GetComponent<Collider>();
            Collider start = panel.Find("StartButton")?.GetComponent<Collider>();
            Collider stop = panel.Find("StopButton")?.GetComponent<Collider>();
            PhysicalRecorderPanelController controller = panel.GetComponent<PhysicalRecorderPanelController>();
            if (controller == null)
            {
                controller = panel.gameObject.AddComponent<PhysicalRecorderPanelController>();
            }

            Transform targetRoot = panel.parent != null ? panel.parent : panel;
            controller.Configure(targetRoot, control != null ? control.Recorder : Object.FindFirstObjectByType<QuestCameraRecorder>(), bar, start, stop);
            ConfigurePhysicalPlateCollider(bar, "RecorderHandler");
            ConfigurePhysicalPlateCollider(start, "StartButton");
            ConfigurePhysicalPlateCollider(stop, "StopButton");
            EnsurePhysicalPanelMeshVisuals(panel);
            EnsurePhysicalPanelGrabInteraction(panel, bar);
            EnsureOnlyPhysicalPanelChildren(targetRoot);
        }

        private static void ConfigurePhysicalPlateCollider(Collider collider, string plateName)
        {
            if (collider == null)
            {
                return;
            }

            collider.isTrigger = true;
            InflatePhysicalCollider(collider, plateName);
        }

        private static void EnsureOnlyPhysicalPanelChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.name == PhysicalPanelName)
                {
                    continue;
                }

                Object.Destroy(child.gameObject);
            }
        }

        private static void EnsurePhysicalPlateVisual(Transform plate, Color color)
        {
            if (plate == null)
            {
                return;
            }

            MeshFilter meshFilter = plate.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = plate.gameObject.AddComponent<MeshFilter>();
            }

            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            MeshRenderer renderer = plate.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = plate.gameObject.AddComponent<MeshRenderer>();
            }

            Material material = CreateRuntimePhysicalMaterial(plate.name, color);
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            renderer.enabled = material != null;
        }

        private static void EnsurePhysicalPlateColliderOnly(Transform plate)
        {
            if (plate == null)
            {
                return;
            }

            Renderer renderer = plate.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
                Object.Destroy(renderer);
            }

            MeshFilter meshFilter = plate.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                Object.Destroy(meshFilter);
            }
        }

        private static Material CreateRuntimePhysicalMaterial(string name, Color color)
        {
            Material material = RecorderPanelMaterialSanitizer.SafeRendererMaterial(color);
            if (material == null || RendererMaterialNeedsRepair(material))
            {
                return null;
            }

            Material instance = new Material(material)
            {
                name = "RecorderPhysicalPanel_" + name,
                hideFlags = HideFlags.DontSave
            };

            ConfigureMaterialColor(instance, color);

            return instance;
        }

        private static bool RendererMaterialNeedsRepair(Material material)
        {
            return material == null ||
                   material.shader == null ||
                   material.shader.name == "Hidden/InternalErrorShader";
        }

        private static void ConfigureMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
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

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }
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
                Object.Destroy(existing.gameObject);
            }
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
            RemovePhysicalVisibleCanvas(physicalRoot);
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
                Object.Destroy(existing.gameObject);
            }
        }

        private static void EnsurePhysicalMeshVisuals(Transform physicalRoot)
        {
            if (physicalRoot == null)
            {
                return;
            }

            Transform existing = physicalRoot.Find(MeshVisualRootName);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
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
                Object.Destroy(collider);
            }

            Renderer renderer = plate.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = RecorderPanelMaterialSanitizer.SafeRendererMaterial(color);
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.enabled = true;
                }
                else
                {
                    renderer.enabled = false;
                }
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
                Object.Destroy(existing.gameObject);
            }

            GameObject canvasObject = new GameObject("RecorderVisibleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = physicalRoot.gameObject.layer;
            canvasObject.transform.SetParent(physicalRoot, false);

            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.localPosition = new Vector3(0f, -0.005f, -0.011f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasRect.sizeDelta = new Vector2(360f, 220f);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;

            CreateVisualImage(canvasRect, "Background", new Vector2(0f, -24f), new Vector2(360f, 172f), new Color(0.075f, 0.095f, 0.105f, 0.96f));
            CreateVisualImage(canvasRect, "RecorderHandlerVisual", new Vector2(0f, 86f), new Vector2(360f, 48f), new Color(0.16f, 0.22f, 0.25f, 1f));
            CreateVisualImage(canvasRect, "StartButtonVisual", new Vector2(-78f, -65f), new Vector2(124f, 46f), new Color(0.10f, 0.50f, 0.28f, 1f));
            CreateVisualImage(canvasRect, "StopButtonVisual", new Vector2(78f, -65f), new Vector2(124f, 46f), new Color(0.58f, 0.16f, 0.16f, 1f));

            Text title = CreateText(canvasRect, "Title", "Quest Camera Recorder", 20, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            title.rectTransform.sizeDelta = new Vector2(330f, 48f);

            Text start = CreateText(canvasRect, "StartText", "Start", 22, TextAnchor.MiddleCenter);
            start.rectTransform.anchoredPosition = new Vector2(-78f, -65f);
            start.rectTransform.sizeDelta = new Vector2(124f, 46f);

            Text stop = CreateText(canvasRect, "StopText", "Stop", 22, TextAnchor.MiddleCenter);
            stop.rectTransform.anchoredPosition = new Vector2(78f, -65f);
            stop.rectTransform.sizeDelta = new Vector2(124f, 46f);
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
                }
            }
        }

        private static void StripPhysicalPanelVisualComponents(Transform physicalRoot)
        {
            if (physicalRoot == null)
            {
                return;
            }

            Renderer[] renderers = physicalRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null &&
                    !IsUnderChild(renderer.transform, MeshVisualRootName))
                {
                    renderer.enabled = false;
                    Object.Destroy(renderer);
                }
            }

            MeshFilter[] meshFilters = physicalRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter != null &&
                    !IsUnderChild(meshFilter.transform, MeshVisualRootName))
                {
                    Object.Destroy(meshFilter);
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
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)gameObject.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = gameObject.GetComponent<Image>();
            image.material = null;
            image.color = color;
            image.raycastTarget = false;

            return rect;
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
            ConfigureBarRayInteractableOnly(dragBar.transform, handleSurface, targetRoot);
            Debug.Log(
                $"[RecorderPanelRuntimeBootstrap] Physical grab aligned root={targetRoot.name} bar={dragBar.name} surface={SurfaceDebugName(handleSurface)}.",
                physicalRoot);
        }

        private static void ConfigureBarRayInteractableOnly(Transform bar, ISurface handleSurface, Transform targetRoot)
        {
            if (bar == null || handleSurface == null || targetRoot == null)
            {
                return;
            }

            Grabbable grabbable = targetRoot.GetComponent<Grabbable>();
            MoveRelativeToTargetProvider movementProvider = targetRoot.GetComponent<MoveRelativeToTargetProvider>();
            RayInteractable barRayInteractable = bar.GetComponent<RayInteractable>();
            if (barRayInteractable == null)
            {
                barRayInteractable = bar.gameObject.AddComponent<RayInteractable>();
            }

            barRayInteractable.InjectSurface(handleSurface);
            barRayInteractable.InjectOptionalSelectSurface(handleSurface);
            if (grabbable != null)
            {
                barRayInteractable.InjectOptionalPointableElement(grabbable);
            }

            if (movementProvider != null)
            {
                barRayInteractable.InjectOptionalMovementProvider(movementProvider);
            }

            ForceUnlimitedRayInteractors(barRayInteractable);

            BaseLinkDragController dragController = bar.GetComponent<BaseLinkDragController>();
            if (dragController == null)
            {
                dragController = bar.gameObject.AddComponent<BaseLinkDragController>();
            }

            Collider handleCollider = bar.GetComponent<Collider>();
            if (handleCollider != null)
            {
                dragController.Configure(targetRoot, handleCollider);
            }
            else
            {
                dragController.Configure(targetRoot, bar as RectTransform);
            }
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
                    Object.Destroy(existingSurface);
                }
            }

            GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject(PhysicalSurfaceName);
            surfaceObject.layer = parent.gameObject.layer;
            if (surfaceObject.transform.parent != parent)
            {
                surfaceObject.transform.SetParent(parent, false);
            }

            ConfigurePhysicalSurface(surfaceObject.transform, barCollider);

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
        }

        private static Bounds LocalColliderBoundsInRoot(Collider collider, Transform root)
        {
            Bounds worldBounds = collider.bounds;
            Vector3 min = root.InverseTransformPoint(worldBounds.min);
            Vector3 max = root.InverseTransformPoint(worldBounds.max);
            return new Bounds((min + max) * 0.5f, new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z)));
        }

        private static string SurfaceDebugName(ISurface surface)
        {
            return surface is Component component
                ? component.transform.name
                : surface != null ? surface.GetType().Name : "null";
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

        private static void EnsureMaterialSanitizer(Transform root)
        {
            if (root == null)
            {
                return;
            }

            RecorderPanelMaterialSanitizer sanitizer = root.GetComponent<RecorderPanelMaterialSanitizer>();
            if (sanitizer == null)
            {
                sanitizer = root.gameObject.AddComponent<RecorderPanelMaterialSanitizer>();
            }

            sanitizer.enabled = true;
            sanitizer.SanitizeNow();
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
                if (canvases[i] != null)
                {
                    canvases[i].enabled = false;
                }
            }

            Graphic[] graphics = fallback.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].enabled = false;
                    graphics[i].raycastTarget = false;
                }
            }

            GraphicRaycaster[] raycasters = fallback.GetComponentsInChildren<GraphicRaycaster>(true);
            for (int i = 0; i < raycasters.Length; i++)
            {
                if (raycasters[i] != null)
                {
                    raycasters[i].enabled = false;
                }
            }
        }

        private static void ForceSafeFallbackGraphicMaterials(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Material safeMaterial = RecorderPanelMaterialSanitizer.SafeGraphicMaterial();
            if (safeMaterial == null)
            {
                Debug.LogWarning("[RecorderPanelRuntimeBootstrap] Could not resolve a safe material for recorder fallback graphics.", root);
                return;
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || IsTextMeshProGraphic(graphic))
                {
                    continue;
                }

                graphic.material = safeMaterial;
            }
        }

        private static void WireExistingFallbackPanel(Transform root, RecorderControlPanel control)
        {
            Transform panel = root.Find(RuntimePanelName);
            if (panel == null)
            {
                return;
            }

            RectTransform bar = panel.Find("Canvas/RecorderHandler") as RectTransform;
            if (bar != null)
            {
                BaseLinkDragController dragController = bar.GetComponent<BaseLinkDragController>();
                if (dragController == null)
                {
                    dragController = bar.gameObject.AddComponent<BaseLinkDragController>();
                }

                dragController.Configure(panel, bar);

                RecorderPanelPointerDragController pointerDragController = bar.GetComponent<RecorderPanelPointerDragController>();
                if (pointerDragController == null)
                {
                    pointerDragController = bar.gameObject.AddComponent<RecorderPanelPointerDragController>();
                }

                pointerDragController.Configure(panel);
            }

            if (bar == null)
            {
                Debug.LogWarning("[RecorderPanelRuntimeBootstrap] Existing fallback panel is missing Canvas/RecorderHandler; rebuilding fallback panel.", root);
                Object.Destroy(panel.gameObject);
                BuildFallbackPanel(root, control);
                return;
            }

            ISurface handleSurface = bar.GetComponentInChildren<UnionClippedPlaneSurface>(true);
            if (handleSurface == null)
            {
                handleSurface = EnsureUnionClippedPlaneSurface(bar, bar.sizeDelta);
            }

            EnsureGrabbablePanelRoot(panel, handleSurface);

            Text status = panel.Find("Canvas/StatusText")?.GetComponent<Text>();
            Button startButton = panel.Find("Canvas/StartButton")?.GetComponent<Button>();
            Button stopButton = panel.Find("Canvas/StopButton")?.GetComponent<Button>();
            if (control != null && status != null && startButton != null && stopButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                stopButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(control.StartRecording);
                stopButton.onClick.AddListener(control.StopRecording);
                control.StatusTextLegacy = status;
                control.StartButton = startButton;
                control.StopButton = stopButton;
                control.Refresh();
            }

            ForceSafeFallbackGraphicMaterials(panel);
        }

        private static void DestroyLegacyVisualRoots(Transform root)
        {
            if (root == null)
            {
                return;
            }

            DestroyChildIfFound(root, "FlatUnityCanvas");
            DestroyChildIfFound(root, "HandleCanvas2");
            DestroyChildIfFound(root, "RecorderSafeUnityPanel");
            DestroyFallbackPanel(root);
        }

        private static void DestroyNonPhysicalPanelChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.name == PhysicalPanelName)
                {
                    continue;
                }

                Object.Destroy(child.gameObject);
            }
        }

        private static void DestroyFallbackPanel(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = children.Length - 1; i >= 0; i--)
            {
                Transform child = children[i];
                if (child != null && child != root && child.name == RuntimePanelName)
                {
                    Object.Destroy(child.gameObject);
                }
            }
        }

        private static void DestroyChildIfFound(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            if (child == null || IsRuntimeFallbackVisual(child))
            {
                return;
            }

            Object.Destroy(child.gameObject);
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
        }

        private static ISurface EnsureUnionClippedPlaneSurface(Transform parent, Vector2 canvasSize)
        {
            Transform existing = parent.Find("InteractionSurface");
            if (existing != null)
            {
                UnionClippedPlaneSurface existingUnion = existing.GetComponent<UnionClippedPlaneSurface>();
                if (existingUnion != null)
                {
                    ConfigureCanvasSurface(existing, canvasSize);
                    return existingUnion;
                }

                ClippedPlaneSurface existingSurface = existing.GetComponent<ClippedPlaneSurface>();
                if (existingSurface != null)
                {
                    Object.Destroy(existingSurface);
                }
            }

            GameObject surfaceObject = existing != null ? existing.gameObject : new GameObject("InteractionSurface");
            surfaceObject.layer = parent.gameObject.layer;
            if (surfaceObject.transform.parent != parent)
            {
                surfaceObject.transform.SetParent(parent, false);
            }

            ConfigureCanvasSurface(surfaceObject.transform, canvasSize);

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
            return clippedSurface;
        }

        private static void ConfigureCanvasSurface(Transform surface, Vector2 canvasSize)
        {
            surface.localPosition = Vector3.zero;
            surface.localRotation = Quaternion.identity;
            surface.localScale = new Vector3(canvasSize.x * 0.001f, canvasSize.y * 0.001f, 0.01f);
        }

        private static RectTransform CreateImage(RectTransform parent, string name, Color color, Vector2 size)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)gameObject.transform;
            rect.sizeDelta = size;

            Image image = gameObject.GetComponent<Image>();
            image.material = null;
            image.color = color;

            return rect;
        }

        private static Text CreateText(RectTransform parent, string name, string text, int fontSize, TextAnchor alignment)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);

            Text label = gameObject.GetComponent<Text>();
            label.text = text;
            label.font = BuiltInUiFont();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.material = null;

            return label;
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

        private static Button CreateButton(RectTransform parent, string name, string text, Vector2 position, Color color)
        {
            RectTransform rect = CreateImage(parent, name, color, new Vector2(124f, 46f));
            rect.anchoredPosition = position;

            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.65f);
            button.colors = colors;

            Text label = CreateText(rect, "Text", text, 22, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            return button;
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
            ForceUnlimitedGrabPoints(grabbable);

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

            Debug.Log(
                $"[RecorderPanelRuntimeBootstrap] Physical grab aligned root={root.name} " +
                $"surface={SurfaceDebugName(handleSurface)} maxGrabPoints=-1.",
                root);
        }

        private static void EnsureAllRayInteractablesHaveSelectSurface()
        {
            RayInteractable[] rayInteractables = Object.FindObjectsByType<RayInteractable>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (rayInteractables == null || rayInteractables.Length == 0)
            {
                return;
            }

            FieldInfo surfaceField = typeof(RayInteractable).GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo selectSurfaceField = typeof(RayInteractable).GetField("_selectSurface", BindingFlags.Instance | BindingFlags.NonPublic);
            int repairedCount = 0;

            for (int i = 0; i < rayInteractables.Length; i++)
            {
                RayInteractable rayInteractable = rayInteractables[i];
                if (rayInteractable == null)
                {
                    continue;
                }

                bool hasSelectSurface = false;
                if (selectSurfaceField != null)
                {
                    hasSelectSurface = selectSurfaceField.GetValue(rayInteractable) != null;
                }

                if (hasSelectSurface)
                {
                    continue;
                }

                ISurface surface = rayInteractable.Surface;
                if (surface == null && surfaceField != null)
                {
                    surface = surfaceField.GetValue(rayInteractable) as ISurface;
                }

                if (surface == null)
                {
                    continue;
                }

                rayInteractable.InjectOptionalSelectSurface(surface);
                repairedCount++;
            }

            if (repairedCount > 0)
            {
                Debug.LogWarning(
                    $"[RecorderPanelRuntimeBootstrap] Repaired {repairedCount} RayInteractable select surface(s) at runtime.",
                    rayInteractables[0]);
            }
        }

        private static void ForceUnlimitedGrabPoints(Grabbable grabbable)
        {
            if (grabbable == null)
            {
                return;
            }

            FieldInfo field = typeof(Grabbable).GetField("_maxGrabPoints", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int))
            {
                return;
            }

            field.SetValue(grabbable, -1);
        }

        private static void ForceUnlimitedRayInteractors(RayInteractable rayInteractable)
        {
            if (rayInteractable == null)
            {
                return;
            }

            SetPrivateInt(rayInteractable, "_maxInteractors", -1);
            SetPrivateInt(rayInteractable, "_maxSelectingInteractors", -1);
        }

        private static void SetPrivateInt(object target, string fieldName, int value)
        {
            if (target == null)
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(int))
            {
                field.SetValue(target, value);
            }
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

        private sealed class RecorderPanelRuntimeRepairLoop : MonoBehaviour
        {
            private readonly List<Transform> panelRoots = new List<Transform>();
            private int framesRemaining;

            public void Begin(List<Transform> roots, int frameCount)
            {
                panelRoots.Clear();
                if (roots != null)
                {
                    for (int i = 0; i < roots.Count; i++)
                    {
                        if (roots[i] != null && !panelRoots.Contains(roots[i]))
                        {
                            panelRoots.Add(roots[i]);
                        }
                    }
                }

                framesRemaining = Mathf.Max(framesRemaining, frameCount);
            }

            private void LateUpdate()
            {
                if (framesRemaining <= 0)
                {
                    return;
                }

                framesRemaining--;
                AddNewRecorderPanelRoots();
                DisableGlobalLegacyRecorderVisuals();
                EnsureAllRayInteractablesHaveSelectSurface();

                for (int i = panelRoots.Count - 1; i >= 0; i--)
                {
                    Transform panelRoot = panelRoots[i];
                    if (panelRoot == null)
                    {
                        panelRoots.RemoveAt(i);
                        continue;
                    }

                    DestroyLegacyVisualRoots(panelRoot);

                    RecorderPanelLegacyVisualKiller visualKiller = panelRoot.GetComponent<RecorderPanelLegacyVisualKiller>();
                    if (visualKiller != null)
                    {
                        visualKiller.DisableLegacyVisuals();
                    }

                    RecorderControlPanel control = panelRoot.GetComponent<RecorderControlPanel>();
                    DestroyFallbackPanel(panelRoot);
                    BuildPhysicalPanel(panelRoot, control);
                    EnsureMaterialSanitizer(panelRoot);
                }
            }

            private void AddNewRecorderPanelRoots()
            {
                List<Transform> roots = FindRecorderPanelRoots();
                for (int i = 0; i < roots.Count; i++)
                {
                    if (roots[i] != null && !panelRoots.Contains(roots[i]))
                    {
                        panelRoots.Add(roots[i]);
                    }
                }
            }
        }
    }
}
