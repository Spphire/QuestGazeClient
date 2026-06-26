using EyeTracking.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EyeTracking.Editor
{
    public static class RecorderUIPanelPrefabBuilder
    {
        private const string PrefabPath = "Assets/EyeTracking/Prefabs/RecorderUIEmptyPanel.prefab";
        private const string PhysicalPanelName = "RecorderPhysicalPanel";
        private const string PhysicalSurfaceName = "PhysicalInteractionSurface";

        [MenuItem("EyeTracking/UI/Rebuild RecorderUI Empty Panel Prefab")]
        public static void RebuildPrefab()
        {
            GameObject root = CreatePanel();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"RecorderUI physical-only panel prefab rebuilt at {PrefabPath}");
        }

        [MenuItem("EyeTracking/UI/Create RecorderUI Empty Panel In Scene")]
        public static void CreateInScene()
        {
            GameObject panel = CreatePanel();
            Undo.RegisterCreatedObjectUndo(panel, "Create RecorderUI Empty Panel");
            Selection.activeGameObject = panel;
        }

        internal static GameObject CreatePanel()
        {
            GameObject root = new GameObject("RecorderUIEmptyPanel");
            root.transform.localPosition = new Vector3(0f, -0.1f, 0.5f);
            root.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
            root.transform.localScale = Vector3.one;

            root.AddComponent<RecorderPanelMaterialSanitizer>();
            root.AddComponent<RecorderPanelLegacyVisualKiller>();
            RecorderControlPanel control = root.AddComponent<RecorderControlPanel>();

            MoveRelativeToTargetProvider movementProvider = root.AddComponent<MoveRelativeToTargetProvider>();
            Grabbable grabbable = root.AddComponent<Grabbable>();
            grabbable.InjectOptionalTargetTransform(root.transform);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            SerializedObject serializedGrabbable = new SerializedObject(grabbable);
            SerializedProperty maxGrabPoints = serializedGrabbable.FindProperty("_maxGrabPoints");
            if (maxGrabPoints != null)
            {
                maxGrabPoints.intValue = -1;
                serializedGrabbable.ApplyModifiedPropertiesWithoutUndo();
            }

            GameObject physical = new GameObject(PhysicalPanelName);
            physical.transform.SetParent(root.transform, false);
            physical.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            physical.transform.localRotation = Quaternion.identity;
            physical.transform.localScale = Vector3.one;

            CreatePhysicalPlate(physical.transform, "Background", new Vector3(0f, -0.024f, 0.004f), new Vector3(0.36f, 0.172f, 0.006f), new Color(0.08f, 0.1f, 0.11f, 1f));
            Collider bar = CreatePhysicalPlate(physical.transform, "RecorderHandler", new Vector3(0f, 0.086f, -0.002f), new Vector3(0.36f, 0.048f, 0.01f), new Color(0.16f, 0.22f, 0.25f, 1f));
            Collider start = CreatePhysicalPlate(physical.transform, "StartButton", new Vector3(-0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.1f, 0.5f, 0.28f, 1f));
            Collider stop = CreatePhysicalPlate(physical.transform, "StopButton", new Vector3(0.078f, -0.065f, -0.004f), new Vector3(0.124f, 0.046f, 0.012f), new Color(0.58f, 0.16f, 0.16f, 1f));
            StripPhysicalPanelVisualComponents(physical.transform);
            EnsurePhysicalMeshVisuals(physical.transform);

            PhysicalRecorderPanelController controller = physical.AddComponent<PhysicalRecorderPanelController>();
            controller.Configure(root.transform, null, bar, start, stop);

            DisablePhysicalColliderSurface(bar);
            ISurface handleSurface = EnsurePhysicalUnionSurface(bar);
            RayInteractable rootRayInteractable = root.AddComponent<RayInteractable>();
            rootRayInteractable.InjectOptionalPointableElement(grabbable);
            rootRayInteractable.InjectSurface(handleSurface);
            rootRayInteractable.InjectOptionalSelectSurface(handleSurface);
            rootRayInteractable.InjectOptionalMovementProvider(movementProvider);
            RayInteractable barRayInteractable = bar.gameObject.AddComponent<RayInteractable>();
            barRayInteractable.InjectOptionalPointableElement(grabbable);
            barRayInteractable.InjectSurface(handleSurface);
            barRayInteractable.InjectOptionalSelectSurface(handleSurface);
            barRayInteractable.InjectOptionalMovementProvider(movementProvider);

            EnsurePointableCanvasEventSystem();
            SetLayerRecursively(root, LayerMask.NameToLayer("UI"));
            EditorUtility.SetDirty(control);
            return root;
        }

        private static Collider CreatePhysicalPlate(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
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

        private static void StripPhysicalPanelVisualComponents(Transform physicalRoot)
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
                    Object.DestroyImmediate(renderers[i]);
                }
            }

            MeshFilter[] meshFilters = physicalRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i] != null)
                {
                    Object.DestroyImmediate(meshFilters[i]);
                }
            }
        }

        private static void EnsurePhysicalMeshVisuals(Transform physicalRoot)
        {
            if (physicalRoot == null)
            {
                return;
            }

            GameObject root = new GameObject("RecorderMeshVisuals");
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
                Material material = CreatePrefabMeshMaterial(name, color);
                renderer.sharedMaterial = material;
                renderer.enabled = material != null;
            }
        }

        private static Material CreatePrefabMeshMaterial(string name, Color color)
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
                name = "RecorderPanelPrefabMesh_" + name,
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
                            AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader");
            if (shader == null)
            {
                return null;
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = shader;
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static ISurface EnsurePhysicalUnionSurface(Collider barCollider)
        {
            Transform parent = barCollider.transform;
            GameObject surfaceObject = new GameObject(PhysicalSurfaceName);
            surfaceObject.transform.SetParent(parent, false);
            ConfigurePhysicalSurface(surfaceObject.transform, barCollider);

            PlaneSurface planeSurface = surfaceObject.AddComponent<PlaneSurface>();
            planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, true);
            BoundsClipper boundsClipper = surfaceObject.AddComponent<BoundsClipper>();
            boundsClipper.Size = Vector3.one;
            UnionClippedPlaneSurface clippedSurface = surfaceObject.AddComponent<UnionClippedPlaneSurface>();
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
            Bounds localBounds = LocalColliderBounds(barCollider);
            surface.localPosition = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z - 0.002f);
            surface.localRotation = Quaternion.identity;
            surface.localScale = new Vector3(
                Mathf.Max(0.001f, localBounds.size.x),
                Mathf.Max(0.001f, localBounds.size.y),
                0.01f);
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
            }
        }

        private static void EnsurePointableCanvasEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                if (eventSystem.GetComponent<PointableCanvasModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<PointableCanvasModule>();
                }

                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<PointableCanvasModule>();
        }
    }
}
