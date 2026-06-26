using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Oculus.Interaction;
using Oculus.Interaction.Input;

namespace EyeTracking.UI
{
    [DisallowMultipleComponent]
    public sealed class RecorderPanelMaterialSanitizer : MonoBehaviour
    {
        [SerializeField] private int warmupFrames = 300;
        [SerializeField] private bool keepRunning = true;
        [SerializeField] private bool logRepairs = true;

        private int framesRemaining;
        private static Material safeGraphicMaterial;
        private static Material safeRendererMaterial;
        private static readonly Dictionary<string, Material> safeRendererMaterialsByColor = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticMaterialCache()
        {
            safeGraphicMaterial = null;
            safeRendererMaterial = null;
            safeRendererMaterialsByColor.Clear();
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            framesRemaining = warmupFrames;
            SanitizeNow();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            framesRemaining = warmupFrames;
            SanitizeNow();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!keepRunning && framesRemaining <= 0)
            {
                return;
            }

            framesRemaining--;
            SanitizeNow();
        }

        public void SanitizeNow()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SanitizeGraphics(transform, logRepairs);
            SanitizeRenderers(transform, logRepairs);
            SanitizeGlobalRecorderPanelVisuals(logRepairs);
            SanitizeGlobalErrorShaderRenderers(logRepairs);
            SanitizeGlobalRecorderGraphics(logRepairs);
        }

        public static void SanitizeGraphics(Transform root)
        {
            SanitizeGraphics(root, false);
        }

        public static void SanitizeGraphics(Transform root, bool logRepairs)
        {
            if (root == null)
            {
                return;
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                Material renderMaterial = null;
                try
                {
                    renderMaterial = graphic.materialForRendering;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[RecorderPanelMaterialSanitizer] Could not inspect graphic material path={PathOf(graphic.transform)} type={graphic.GetType().Name}: {exception.Message}", graphic);
                }

                if (!NeedsReplacement(renderMaterial) && !IsFallbackRecorderPanelGraphic(graphic.transform))
                {
                    continue;
                }

                Material safeMaterial = SafeGraphicMaterial();
                if (safeMaterial == null || NeedsReplacement(safeMaterial))
                {
                    graphic.enabled = false;
                    if (logRepairs)
                    {
                        Debug.LogError($"[RecorderPanelMaterialSanitizer] Disabled graphic with unsafe shader path={PathOf(graphic.transform)} type={graphic.GetType().Name}.", graphic);
                    }

                    continue;
                }

                if (IsTextMeshProGraphic(graphic))
                {
                    // TMP font materials can throw during build/import when the font asset is not initialized.
                    // Disable only the bad TMP visual instead of touching TMP_Text.fontMaterial.
                    if (NeedsReplacement(renderMaterial))
                    {
                        graphic.enabled = false;
                        if (logRepairs)
                        {
                            Debug.LogError($"[RecorderPanelMaterialSanitizer] Disabled TMP graphic with unsafe shader path={PathOf(graphic.transform)} type={graphic.GetType().Name}.", graphic);
                        }
                    }

                    continue;
                }

                try
                {
                    graphic.material = safeMaterial;
                    if (logRepairs)
                    {
                        string shaderName = renderMaterial != null && renderMaterial.shader != null ? renderMaterial.shader.name : "null";
                        Debug.Log($"[RecorderPanelMaterialSanitizer] Repaired graphic path={PathOf(graphic.transform)} oldShader={shaderName} newShader={safeMaterial.shader.name}.", graphic);
                    }
                }
                catch (System.Exception exception)
                {
                    graphic.enabled = false;
                    Debug.LogError($"[RecorderPanelMaterialSanitizer] Failed to repair graphic path={PathOf(graphic.transform)} type={graphic.GetType().Name}; disabled it. {exception.Message}", graphic);
                }
            }
        }

        public static void SanitizeRenderers(Transform root)
        {
            SanitizeRenderers(root, false);
        }

        public static void SanitizeRenderers(Transform root, bool logRepairs)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                bool changed = false;
                for (int j = 0; j < materials.Length; j++)
                {
                    if (NeedsReplacement(materials[j]))
                    {
                        Material safeMaterial = SafeRendererMaterial();
                        if (safeMaterial == null || NeedsReplacement(safeMaterial))
                        {
                            renderer.enabled = false;
                            if (logRepairs)
                            {
                                Debug.LogError($"[RecorderPanelMaterialSanitizer] Disabled renderer with unsafe shader path={PathOf(renderer.transform)} materialIndex={j}.", renderer);
                            }

                            changed = false;
                            break;
                        }

                        if (logRepairs)
                        {
                            string oldName = materials[j] != null && materials[j].shader != null ? materials[j].shader.name : "null";
                            Debug.Log($"[RecorderPanelMaterialSanitizer] Repaired renderer path={PathOf(renderer.transform)} materialIndex={j} oldShader={oldName} newShader={safeMaterial.shader.name}.", renderer);
                        }

                        materials[j] = safeMaterial;
                        changed = renderer.enabled;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        public static void SanitizeGlobalRecorderPanelVisuals(bool logRepairs)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !IsRecorderPanelRelated(renderer.transform))
                {
                    continue;
                }

                if (IsRecorderMeshVisual(renderer.transform))
                {
                    ForceRendererMaterial(renderer, PanelColorFor(renderer.transform), logRepairs);
                    renderer.enabled = renderer.sharedMaterial != null && !NeedsReplacement(renderer.sharedMaterial);
                    renderer.gameObject.SetActive(true);
                    continue;
                }

                renderer.enabled = false;
                renderer.gameObject.SetActive(false);
                if (logRepairs)
                {
                    Debug.LogWarning($"[RecorderPanelMaterialSanitizer] Disabled non-whitelisted recorder renderer path={PathOf(renderer.transform)}.", renderer);
                }
            }

            Graphic[] graphics = Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !IsRecorderPanelRelated(graphic.transform))
                {
                    continue;
                }

                graphic.enabled = false;
                graphic.raycastTarget = false;
                graphic.gameObject.SetActive(false);
            }
        }

        public static void SanitizeGlobalErrorShaderRenderers(bool logRepairs)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                bool changed = false;
                for (int j = 0; j < materials.Length; j++)
                {
                    if (!NeedsReplacement(materials[j]))
                    {
                        continue;
                    }

                    Color color = IsRecorderPanelRelated(renderer.transform)
                        ? PanelColorFor(renderer.transform)
                        : new Color(0.18f, 0.18f, 0.18f, 1f);
                    Material safeMaterial = SafeRendererMaterial(color);
                    if (safeMaterial == null || NeedsReplacement(safeMaterial))
                    {
                        renderer.enabled = false;
                        if (logRepairs)
                        {
                            Debug.LogError($"[RecorderPanelMaterialSanitizer] Disabled global error-shader renderer path={PathOf(renderer.transform)} materialIndex={j}.", renderer);
                        }

                        changed = false;
                        break;
                    }

                    materials[j] = safeMaterial;
                    changed = true;
                    if (logRepairs)
                    {
                        Debug.LogWarning($"[RecorderPanelMaterialSanitizer] Replaced global error-shader renderer material path={PathOf(renderer.transform)} materialIndex={j} newShader={safeMaterial.shader.name}.", renderer);
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        public static void SanitizeGlobalRecorderGraphics(bool logRepairs)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Graphic[] graphics = Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !IsRecorderPanelRelated(graphic.transform))
                {
                    continue;
                }

                if (!IsSafeRecorderGraphic(graphic.transform))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                    graphic.gameObject.SetActive(false);
                    continue;
                }

                graphic.enabled = false;
                graphic.raycastTarget = false;
                graphic.gameObject.SetActive(false);
                if (logRepairs)
                {
                    Debug.LogWarning($"[RecorderPanelMaterialSanitizer] Disabled non-visible recorder graphic path={PathOf(graphic.transform)} type={graphic.GetType().Name}.", graphic);
                }
            }
        }

        private static void ForceRendererMaterial(Renderer renderer, Color color, bool logRepairs)
        {
            if (renderer == null)
            {
                return;
            }

            Material safeMaterial = SafeRendererMaterial(color);
            if (safeMaterial == null || NeedsReplacement(safeMaterial))
            {
                renderer.enabled = false;
                if (logRepairs)
                {
                    Debug.LogError($"[RecorderPanelMaterialSanitizer] Could not create physical renderer material path={PathOf(renderer.transform)}; disabled renderer.", renderer);
                }

                return;
            }

            Material current = renderer.sharedMaterial;
            bool replace = current == null ||
                           current.shader == null ||
                           current.shader.name != safeMaterial.shader.name ||
                           NeedsReplacement(current);
            if (!replace)
            {
                return;
            }

            string oldShader = current != null && current.shader != null ? current.shader.name : "null";
            renderer.sharedMaterial = safeMaterial;
            if (logRepairs)
            {
                Debug.Log($"[RecorderPanelMaterialSanitizer] Forced physical renderer material path={PathOf(renderer.transform)} oldShader={oldShader} newShader={safeMaterial.shader.name}.", renderer);
            }
        }

        private static void RepairPhysicalRendererIfNeeded(Renderer renderer, Color color, bool logRepairs)
        {
            if (renderer == null)
            {
                return;
            }

            Material current = renderer.sharedMaterial;
            if (current != null && current.shader != null && !NeedsReplacement(current))
            {
                return;
            }

            ForceRendererMaterial(renderer, color, logRepairs);
        }

        private static bool RendererNeedsReplacement(Renderer renderer)
        {
            if (renderer == null)
            {
                return true;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (NeedsReplacement(materials[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NeedsReplacement(Material material)
        {
            return material == null ||
                   material.shader == null ||
                   material.shader.name == "Hidden/InternalErrorShader" ||
                   material.shader.name.StartsWith("EyeTracking/RecorderPanel", System.StringComparison.Ordinal) ||
                   !material.shader.isSupported;
        }

        public static Material SafeRendererMaterial()
        {
            return SafeRendererMaterial(Color.white);
        }

        public static Material SafeRendererMaterial(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            if (safeRendererMaterialsByColor.TryGetValue(key, out Material cachedMaterial) &&
                cachedMaterial != null &&
                cachedMaterial.shader != null &&
                !NeedsReplacement(cachedMaterial))
            {
                return cachedMaterial;
            }

            Shader shader = FirstSupportedShader(
                Shader.Find("EnvironmentDepth/OcclusionLit"),
                Shader.Find("Sprites/Default"),
                Shader.Find("UI/Default"),
                Shader.Find("Oculus/Unlit"),
                Shader.Find("Oculus/Unlit Transparent Color"),
                Shader.Find("Universal Render Pipeline/Unlit"),
                Shader.Find("Universal Render Pipeline/Simple Lit"),
                Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("Unlit/Color"),
                Shader.Find("Standard"));
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "RecorderPanelSafeRendererMaterial_" + key,
                hideFlags = HideFlags.DontSave
            };
            ConfigureUnlitMaterial(material, color);
            safeRendererMaterialsByColor[key] = material;
            if (ColorUtility.ToHtmlStringRGBA(color) == "FFFFFFFF")
            {
                safeRendererMaterial = material;
            }

            return material;
        }

        public static Material SafeGraphicMaterial()
        {
            if (safeGraphicMaterial != null && safeGraphicMaterial.shader != null && !NeedsReplacement(safeGraphicMaterial))
            {
                return safeGraphicMaterial;
            }

            Shader shader = FirstSupportedShader(
                Shader.Find("UI/Default"),
                Shader.Find("Sprites/Default"),
                Shader.Find("Oculus/Unlit"),
                Shader.Find("Oculus/Unlit Transparent Color"),
                Shader.Find("Universal Render Pipeline/Unlit"),
                Shader.Find("Unlit/Color"),
                Shader.Find("UI/Default Correct"),
                Shader.Find("UI/Default (Overlay)"));
            if (shader == null)
            {
                return null;
            }

            safeGraphicMaterial = new Material(shader)
            {
                name = "RecorderPanelSafeUIMaterial",
                hideFlags = HideFlags.DontSave
            };
            ConfigureUnlitMaterial(safeGraphicMaterial, Color.white);

            if (safeGraphicMaterial.HasProperty("_MainTex"))
            {
                safeGraphicMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            return safeGraphicMaterial;
        }

        private static Shader FirstSupportedShader(params Shader[] candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                Shader shader = candidates[i];
                if (shader != null &&
                    shader.name != "Hidden/InternalErrorShader" &&
                    shader.isSupported)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void ConfigureUnlitMaterial(Material material, Color color)
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

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
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

            material.enableInstancing = false;
            material.renderQueue = -1;
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

        private static bool IsFallbackRecorderPanelGraphic(Transform transform)
        {
            while (transform != null)
            {
                if (transform.name == "RecorderFallbackPanel")
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
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

        private static bool IsRecorderPanelRelated(Transform transform)
        {
            while (transform != null)
            {
                string name = transform.name;
                if (name == "QuestCameraRecorderPanel" ||
                    name == "RecorderUIEmptyPanel" ||
                    name == "RecorderPhysicalPanel" ||
                    name == "RecorderMeshVisuals" ||
                    name == "RecorderFallbackPanel" ||
                    name == "RecorderVisibleCanvas" ||
                    name == "RecorderUI" ||
                    name == "FlatUnityCanvas" ||
                    name == "HandleCanvas2" ||
                    name == "RecorderSafeUnityPanel" ||
                    name == "Surface" ||
                    name == "BoardClipper" ||
                    name == "GradientEffect" ||
                    name == "Backplate" ||
                    name == "BG" ||
                    name.Contains("RecorderHandler") ||
                    name.Contains("QDS") ||
                    name.Contains("RecorderPanel") ||
                    name.Contains("RoundedBox") ||
                    name.Contains("Generated"))
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static bool IsSafeRecorderGraphic(Transform transform)
        {
            return false;
        }

        private static bool IsRecorderVisibleCanvas(Transform transform)
        {
            while (transform != null)
            {
                if (transform.name == "RecorderVisibleCanvas")
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static bool IsRecorderMeshVisual(Transform transform)
        {
            while (transform != null)
            {
                if (transform.name == "RecorderMeshVisuals")
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static Color PanelColorFor(Transform transform)
        {
            string name = transform != null ? transform.name : string.Empty;
            if (name == "RecorderHandler" || name == "RecorderHandlerMesh")
            {
                return new Color(0.16f, 0.22f, 0.25f, 1f);
            }

            if (name == "StartButton" || name == "StartButtonMesh")
            {
                return new Color(0.1f, 0.5f, 0.28f, 1f);
            }

            if (name == "StopButton" || name == "StopButtonMesh")
            {
                return new Color(0.58f, 0.16f, 0.16f, 1f);
            }

            if (name == "BuildMarkerMesh_v43")
            {
                return new Color(1f, 0.78f, 0f, 1f);
            }

            return new Color(0.08f, 0.1f, 0.11f, 1f);
        }
    }
}
