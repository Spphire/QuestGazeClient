using UnityEditor;
using UnityEngine;

namespace EyeTracking.Editor
{
    public static class RecorderUIPanelRepairTools
    {
        [MenuItem("EyeTracking/UI/Repair RecorderUI Panel In Open Scene")]
        public static void RepairOpenScenePanel()
        {
            GameObject panel = GameObject.Find("QuestCameraRecorderPanel") ??
                               GameObject.Find("RecorderUIEmptyPanel") ??
                               Selection.activeGameObject;

            if (panel == null)
            {
                Debug.LogWarning("[RecorderUIPanelRepairTools] No recorder panel found in the open scene.");
                return;
            }

            RepairPanel(panel);
            EditorUtility.SetDirty(panel);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.scene);
        }

        [MenuItem("EyeTracking/UI/Repair RecorderUI Panel Scene And Prefab")]
        public static void RepairSceneAndPrefab()
        {
            RepairOpenScenePanel();
            RecorderUIPanelPrefabBuilder.RebuildPrefab();
            Debug.Log("[RecorderUIPanelRepairTools] Rebuilt recorder panel prefab as physical-only.");
        }

        [MenuItem("EyeTracking/UI/Repair Selected RecorderUI Panel")]
        public static void RepairSelectedPanel()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("[RecorderUIPanelRepairTools] Select a recorder panel root first.");
                return;
            }

            RepairPanel(Selection.activeGameObject);
            EditorUtility.SetDirty(Selection.activeGameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
        }

        private static void RepairPanel(GameObject panel)
        {
            Undo.RegisterFullObjectHierarchyUndo(panel, "Repair RecorderUI Panel");

            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = panel.transform.GetChild(i);
                if (child == null || child.name == "RecorderPhysicalPanel")
                {
                    continue;
                }

                Object.DestroyImmediate(child.gameObject);
            }

            if (panel.transform.Find("RecorderPhysicalPanel") == null)
            {
                GameObject replacement = RecorderUIPanelPrefabBuilder.CreatePanel();
                Transform physical = replacement.transform.Find("RecorderPhysicalPanel");
                if (physical != null)
                {
                    physical.SetParent(panel.transform, false);
                }

                Object.DestroyImmediate(replacement);
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = uiLayer;
                    EditorUtility.SetDirty(child.gameObject);
                }
            }

            Debug.Log($"[RecorderUIPanelRepairTools] Repaired recorder panel '{panel.name}' as physical-only.");
        }
    }
}
