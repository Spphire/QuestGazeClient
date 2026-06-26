#if UNITY_EDITOR
using EyeTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EyeTracking.Editor
{
    internal static class EyeTrackingBoxVolumeSetup
    {
        private const string ScenePath = "Assets/EyeTracking/EyeTrackingScene.unity";
        private const string ToolName = "Box Volume Authoring Tool";

        [InitializeOnLoadMethod]
        private static void AutoSetup()
        {
            EditorApplication.delayCall -= DelayedAutoSetup;
            EditorApplication.delayCall += DelayedAutoSetup;
        }

        [MenuItem("EyeTracking/Add Box Volume Authoring Tool")]
        private static void SetupFromMenu()
        {
            Setup(true);
        }

        private static void DelayedAutoSetup()
        {
            EditorApplication.delayCall -= DelayedAutoSetup;
            Setup(false);
        }

        private static void Setup(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != ScenePath)
            {
                return;
            }

            BoxVolumeAuthoringTool existing = Object.FindFirstObjectByType<BoxVolumeAuthoringTool>();
            if (existing != null && !force)
            {
                return;
            }

            if (existing == null)
            {
                GameObject tool = new GameObject(ToolName);
                Undo.RegisterCreatedObjectUndo(tool, "Add box volume authoring tool");
                tool.AddComponent<BoxVolumeAuthoringTool>();
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
                Debug.Log("EyeTracking box volume authoring tool added to the scene.");
            }
            else
            {
                Selection.activeObject = existing.gameObject;
            }
        }
    }
}
#endif
