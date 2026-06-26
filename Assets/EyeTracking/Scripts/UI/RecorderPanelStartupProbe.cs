using System;
using System.Collections;
using System.IO;
using System.Text;
using EyeTracking.Recording;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EyeTracking.UI
{
    public static class RecorderPanelStartupProbe
    {
        private const string FileName = "recorder_panel_startup_probe.json";
        private const string EarlyFileName = "recorder_panel_early_probe.json";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void CaptureAfterAssembliesLoaded()
        {
            CaptureEarly("AfterAssembliesLoaded");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureBeforeSceneLoad()
        {
            CaptureEarly("BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CaptureAfterSceneLoad()
        {
            CaptureEarly("AfterSceneLoad");
            Capture("AfterSceneLoad");
            EnsureRunner();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Capture("sceneLoaded:" + scene.name);
            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (UnityEngine.Object.FindFirstObjectByType<ProbeRunner>() != null)
            {
                return;
            }

            GameObject runner = new GameObject("RecorderPanelStartupProbeRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<ProbeRunner>();
        }

        private static void Capture(string source)
        {
            try
            {
                GameObject panel = GameObject.Find("QuestCameraRecorderPanel") ??
                                   GameObject.Find("RecorderUIEmptyPanel");
                QuestCameraRecorder recorder = UnityEngine.Object.FindFirstObjectByType<QuestCameraRecorder>();
                Transform physical = panel != null ? panel.transform.Find("RecorderPhysicalPanel") : null;
                Transform visibleCanvas = physical != null ? physical.Find("RecorderVisibleCanvas") : null;
                Transform meshVisuals = physical != null ? physical.Find("RecorderMeshVisuals") : null;
                Transform bar = physical != null ? physical.Find("RecorderHandler") : null;
                Collider barCollider = bar != null ? bar.GetComponent<Collider>() : null;
                PhysicalRecorderPanelController panelController = physical != null
                    ? physical.GetComponent<PhysicalRecorderPanelController>()
                    : null;
                StartupSnapshot snapshot = new StartupSnapshot
                {
                    source = source,
                    realtimeSinceStartup = Time.realtimeSinceStartupAsDouble,
                    activeScene = SceneManager.GetActiveScene().name,
                    panelFound = panel != null,
                    panelActiveInHierarchy = panel != null && panel.activeInHierarchy,
                    directChildren = panel != null ? ChildNames(panel.transform) : Array.Empty<string>(),
                    physicalPanelFound = physical != null,
                    physicalDirectChildren = physical != null ? ChildNames(physical) : Array.Empty<string>(),
                    visibleCanvasFound = visibleCanvas != null,
                    visibleCanvasActive = visibleCanvas != null && visibleCanvas.gameObject.activeInHierarchy,
                    visibleCanvasGraphicCount = visibleCanvas != null ? visibleCanvas.GetComponentsInChildren<Graphic>(true).Length : 0,
                    meshVisualsFound = meshVisuals != null,
                    meshVisualRendererCount = meshVisuals != null ? meshVisuals.GetComponentsInChildren<Renderer>(true).Length : 0,
                    meshVisualRenderers = meshVisuals != null ? RendererInfos(meshVisuals) : Array.Empty<RendererInfo>(),
                    panelActiveRendererCount = panel != null ? CountActiveRenderers(panel.transform) : 0,
                    panelErrorShaderRendererCount = panel != null ? CountErrorShaderRenderers(panel.transform) : 0,
                    panelActiveGraphicCount = panel != null ? CountActiveGraphics(panel.transform) : 0,
                    barFound = bar != null,
                    barColliderFound = barCollider != null,
                    barColliderType = barCollider != null ? barCollider.GetType().Name : string.Empty,
                    barColliderIsTrigger = barCollider != null && barCollider.isTrigger,
                    barColliderBounds = barCollider != null ? barCollider.bounds.ToString("F4") : string.Empty,
                    barRayInteractable = HasComponentNamed(bar, "RayInteractable"),
                    physicalRayInteractable = HasComponentNamed(physical, "RayInteractable"),
                    panelRootRayInteractable = HasComponentNamed(panel != null ? panel.transform : null, "RayInteractable"),
                    physicalGrabbable = HasComponentNamed(physical, "Grabbable"),
                    panelRootGrabbable = HasComponentNamed(panel != null ? panel.transform : null, "Grabbable"),
                    controllerFound = panelController != null,
                    controllerRayStatus = panelController != null ? panelController.LastRayStatus : string.Empty,
                    controllerSelectSource = panelController != null ? panelController.LastSelectSource : string.Empty,
                    controllerRightPoseValid = panelController != null && panelController.LastRightControllerPoseValid,
                    controllerRightRayToBarDistance = panelController != null ? panelController.LastRightRayToBarDistance : float.PositiveInfinity,
                    controllerOvrFallbackEnabled = panelController != null && panelController.EnableOvrControllerRayFallback,
                    recorderFound = recorder != null,
                    recorderReady = recorder != null && recorder.IsReady,
                    recordRightCamera = recorder != null && recorder.RecordRightCamera,
                    recordSynchronizedTrajectory = recorder != null && recorder.RecordSynchronizedTrajectory,
                    applicationIsFocused = Application.isFocused,
                    applicationIsPlaying = Application.isPlaying
                };

                string path = Path.Combine(Application.persistentDataPath, FileName);
                File.WriteAllText(path, JsonUtility.ToJson(snapshot, true), new UTF8Encoding(false));
                Debug.Log($"[RecorderPanelStartupProbe] wrote {path} panelFound={snapshot.panelFound} scene={snapshot.activeScene}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RecorderPanelStartupProbe] Capture failed: " + exception.Message);
            }
        }

        private static void CaptureEarly(string source)
        {
            try
            {
                EarlySnapshot snapshot = new EarlySnapshot
                {
                    source = source,
                    realtimeSinceStartup = Time.realtimeSinceStartupAsDouble,
                    activeScene = SceneManager.GetActiveScene().name,
                    persistentDataPath = Application.persistentDataPath,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    packageName = Application.identifier,
                    isPlaying = Application.isPlaying,
                    isFocused = Application.isFocused
                };

                string path = Path.Combine(Application.persistentDataPath, EarlyFileName);
                File.WriteAllText(path, JsonUtility.ToJson(snapshot, true), new UTF8Encoding(false));
                Debug.Log($"[RecorderPanelStartupProbe] early wrote {path} source={source}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RecorderPanelStartupProbe] Early capture failed: " + exception.Message);
            }
        }

        private static string[] ChildNames(Transform transform)
        {
            string[] names = new string[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                names[i] = child != null ? child.name : string.Empty;
            }

            return names;
        }

        private static RendererInfo[] RendererInfos(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            RendererInfo[] infos = new RendererInfo[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = renderer != null ? renderer.sharedMaterial : null;
                infos[i] = new RendererInfo
                {
                    path = renderer != null ? PathOf(renderer.transform) : string.Empty,
                    active = renderer != null && renderer.gameObject.activeInHierarchy,
                    enabled = renderer != null && renderer.enabled,
                    material = material != null ? material.name : string.Empty,
                    shader = material != null && material.shader != null ? material.shader.name : string.Empty,
                    shaderSupported = material != null && material.shader != null && material.shader.isSupported,
                    shaderPassCount = material != null && material.shader != null ? material.shader.passCount : -1
                };
            }

            return infos;
        }

        private static int CountActiveRenderers(Transform root)
        {
            int count = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountErrorShaderRenderers(Transform root)
        {
            int count = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
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
                    string shaderName = material != null && material.shader != null ? material.shader.name : string.Empty;
                    if (material == null || material.shader == null || shaderName == "Hidden/InternalErrorShader")
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static int CountActiveGraphics(Transform root)
        {
            int count = 0;
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic != null && graphic.enabled && graphic.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasComponentNamed(Transform transform, string typeName)
        {
            if (transform == null || string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            Component[] components = transform.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (type.Name == typeName || type.FullName != null && type.FullName.EndsWith("." + typeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string PathOf(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class StartupSnapshot
        {
            public string source;
            public double realtimeSinceStartup;
            public string activeScene;
            public bool panelFound;
            public bool panelActiveInHierarchy;
            public string[] directChildren;
            public bool physicalPanelFound;
            public string[] physicalDirectChildren;
            public bool visibleCanvasFound;
            public bool visibleCanvasActive;
            public int visibleCanvasGraphicCount;
            public bool meshVisualsFound;
            public int meshVisualRendererCount;
            public RendererInfo[] meshVisualRenderers;
            public int panelActiveRendererCount;
            public int panelErrorShaderRendererCount;
            public int panelActiveGraphicCount;
            public bool barFound;
            public bool barColliderFound;
            public string barColliderType;
            public bool barColliderIsTrigger;
            public string barColliderBounds;
            public bool barRayInteractable;
            public bool physicalRayInteractable;
            public bool panelRootRayInteractable;
            public bool physicalGrabbable;
            public bool panelRootGrabbable;
            public bool controllerFound;
            public string controllerRayStatus;
            public string controllerSelectSource;
            public bool controllerRightPoseValid;
            public float controllerRightRayToBarDistance;
            public bool controllerOvrFallbackEnabled;
            public bool recorderFound;
            public bool recorderReady;
            public bool recordRightCamera;
            public bool recordSynchronizedTrajectory;
            public bool applicationIsFocused;
            public bool applicationIsPlaying;
        }

        [Serializable]
        private sealed class RendererInfo
        {
            public string path;
            public bool active;
            public bool enabled;
            public string material;
            public string shader;
            public bool shaderSupported;
            public int shaderPassCount;
        }

        [Serializable]
        private sealed class EarlySnapshot
        {
            public string source;
            public double realtimeSinceStartup;
            public string activeScene;
            public string persistentDataPath;
            public string unityVersion;
            public string platform;
            public string packageName;
            public bool isPlaying;
            public bool isFocused;
        }

        private sealed class ProbeRunner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return new WaitForSecondsRealtime(1f);
                Capture("delayed:1s");
                yield return new WaitForSecondsRealtime(2f);
                Capture("delayed:3s");
                yield return new WaitForSecondsRealtime(5f);
                Capture("delayed:8s");
            }
        }
    }
}
