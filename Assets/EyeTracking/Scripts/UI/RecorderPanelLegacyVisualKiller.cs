using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.UI
{
    [DefaultExecutionOrder(-11000)]
    public sealed class RecorderPanelLegacyVisualKiller : MonoBehaviour
    {
        [SerializeField] private string safeRootName = "RecorderPhysicalPanel";
        [SerializeField] private int warmupFrames = 240;
        [SerializeField] private bool keepRunning = true;

        private int framesRemaining;

        private void Awake()
        {
            safeRootName = "RecorderPhysicalPanel";
            framesRemaining = warmupFrames;
            DisableLegacyVisuals();
        }

        private void OnEnable()
        {
            safeRootName = "RecorderPhysicalPanel";
            framesRemaining = warmupFrames;
            DisableLegacyVisuals();
        }

        private void LateUpdate()
        {
            if (!keepRunning && framesRemaining <= 0)
            {
                return;
            }

            framesRemaining--;
            DisableLegacyVisuals();
        }

        public void DisableLegacyVisuals()
        {
            DisableGlobalLegacyVisuals();
            SetActiveIfFound("RecorderSafeUnityPanel", false);
            SetActiveIfFound("FlatUnityCanvas", false);
            SetActiveIfFound("HandleCanvas2", false);
            SetActiveIfFound("FlatUnityCanvas/Surface", false);
            SetActiveIfFound("HandleCanvas2/Surface", false);
            DisableFallbackCanvasVisuals();

            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas != null &&
                    !IsSafeChild(canvas.transform) &&
                    !IsKnownSafeRuntimePanel(canvas.transform))
                {
                    canvas.enabled = false;
                }
            }

            GraphicRaycaster[] raycasters = GetComponentsInChildren<GraphicRaycaster>(true);
            for (int i = 0; i < raycasters.Length; i++)
            {
                GraphicRaycaster raycaster = raycasters[i];
                if (raycaster != null &&
                    !IsSafeChild(raycaster.transform) &&
                    !IsKnownSafeRuntimePanel(raycaster.transform))
                {
                    raycaster.enabled = false;
                }
            }

            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                if (!IsSafeChild(graphic.transform) &&
                    !IsKnownSafeRuntimePanel(graphic.transform))
                {
                    graphic.enabled = false;
                    continue;
                }

                RecorderPanelMaterialSanitizer.SanitizeGraphics(graphic.transform);
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer targetRenderer = renderers[i];
                if (targetRenderer == null)
                {
                    continue;
                }

                if (!IsSafeChild(targetRenderer.transform) &&
                    !IsKnownSafeRuntimePanel(targetRenderer.transform))
                {
                    targetRenderer.enabled = false;
                    continue;
                }

                RecorderPanelMaterialSanitizer.SanitizeRenderers(targetRenderer.transform);
            }

            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter != null &&
                    !IsSafeChild(meshFilter.transform) &&
                    !IsKnownSafeRuntimePanel(meshFilter.transform))
                {
                    meshFilter.gameObject.SetActive(false);
                }
            }

            Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null ||
                    IsSafeChild(behaviour.transform) ||
                    IsKnownSafeRuntimePanel(behaviour.transform))
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (IsGeneratedPanelVisual(behaviour.transform) ||
                    typeName.Contains("RoundedBox") ||
                    typeName.Contains("Overlay") ||
                    typeName.Contains("Sprite") ||
                    typeName.Contains("PlaneSurface") ||
                    typeName.Contains("ClippedPlaneSurface") ||
                    typeName.Contains("BoundsClipper"))
                {
                    behaviour.enabled = false;
                }
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

        private static bool IsGeneratedPanelVisual(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            string name = candidate.name;
            return name == "Surface" ||
                   name == "BoardClipper" ||
                   name == "GradientEffect" ||
                   name == "Backplate" ||
                   name.Contains("RoundedBox") ||
                   name.Contains("QDS") ||
                   name.Contains("Generated");
        }

        private bool IsSafeChild(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            Transform safeRoot = transform.Find(safeRootName);
            if (safeRoot != null && (candidate == safeRoot || candidate.IsChildOf(safeRoot)))
            {
                return true;
            }

            Transform physicalRoot = transform.Find("RecorderPhysicalPanel");
            if (physicalRoot != null && (candidate == physicalRoot || candidate.IsChildOf(physicalRoot)))
            {
                return true;
            }

            return false;
        }

        private bool IsKnownSafeRuntimePanel(Transform candidate)
        {
            return candidate != null &&
                   (candidate.name == "RecorderPhysicalPanel" ||
                    candidate.name == "Canvas" && IsSafeChild(candidate.parent));
        }

        private void SetActiveIfFound(string childName, bool active)
        {
            Transform child = transform.Find(childName);
            if (child != null && child.gameObject.activeSelf != active)
            {
                child.gameObject.SetActive(active);
            }
        }

        private void DisableFallbackCanvasVisuals()
        {
            Transform fallbackRoot = transform.Find("RecorderFallbackPanel");
            if (fallbackRoot == null)
            {
                return;
            }

            Canvas[] canvases = fallbackRoot.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null)
                {
                    canvases[i].enabled = false;
                }
            }

            GraphicRaycaster[] raycasters = fallbackRoot.GetComponentsInChildren<GraphicRaycaster>(true);
            for (int i = 0; i < raycasters.Length; i++)
            {
                if (raycasters[i] != null)
                {
                    raycasters[i].enabled = false;
                }
            }

            Graphic[] graphics = fallbackRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].enabled = false;
                    graphics[i].raycastTarget = false;
                }
            }
        }

        private void DisableGlobalLegacyVisuals()
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || IsSafeChild(candidate))
                {
                    continue;
                }

                if (candidate.name == "FlatUnityCanvas" ||
                    candidate.name == "HandleCanvas2" ||
                    candidate.name == "RecorderSafeUnityPanel" ||
                    candidate.name == "GradientEffect" ||
                    candidate.name == "Backplate" ||
                    candidate.name == "BoardClipper" ||
                    candidate.name.Contains("RoundedBox") ||
                    candidate.name.Contains("QDS") ||
                    candidate.name == "Surface" && IsLegacyRecorderVisual(candidate))
                {
                    candidate.gameObject.SetActive(false);
                }
            }
        }

        private static void RendererSafeMaterial(Renderer targetRenderer)
        {
            if (targetRenderer == null)
            {
                return;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials == null)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                {
                    materials[i] = null;
                    changed = true;
                }
            }

            if (changed)
            {
                targetRenderer.sharedMaterials = materials;
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
                    name == "RecorderUI" ||
                    name.Contains("RoundedBox") ||
                    name.Contains("QDS") ||
                    name.Contains("Generated"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
