using EyeTracking.Recording;
using EyeTracking.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.Editor
{
    public static class RecorderUIControlPanelSceneMigrator
    {
        [MenuItem("EyeTracking/UI/Customize Scene RecorderUI Panel")]
        public static void CustomizeOpenScenePanel()
        {
            GameObject panel = GameObject.Find("RecorderUIEmptyPanel");
            if (panel == null)
            {
                Debug.LogError("[RecorderUIControlPanelSceneMigrator] RecorderUIEmptyPanel was not found in the open scene.");
                return;
            }

            CustomizePanel(panel);
        }

        [MenuItem("EyeTracking/UI/Fix Scene RecorderUI Rendering")]
        public static void FixOpenScenePanelRendering()
        {
            QuestCameraRecordingSceneSetup.SetupScene();
            Debug.Log("[RecorderUIControlPanelSceneMigrator] Delegated rendering repair to the safe Quest camera recording scene setup.");
        }

        private static void CustomizePanel(GameObject panel)
        {
            PrefabInstanceStatus prefabStatus = PrefabUtility.GetPrefabInstanceStatus(panel);
            if (prefabStatus == PrefabInstanceStatus.Connected)
            {
                PrefabUtility.UnpackPrefabInstance(panel, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            panel.name = "QuestCameraRecorderPanel";

            RectTransform content = panel.transform.Find("FlatUnityCanvas/Unity Canvas/Menu/Content") as RectTransform;
            if (content == null)
            {
                Debug.LogError("[RecorderUIControlPanelSceneMigrator] Could not find FlatUnityCanvas/Unity Canvas/Menu/Content.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(panel, "Customize RecorderUI Panel");
            ClearChildren(content);
            BuildRecordingControls(panel, content);
            ApplyRenderFix(panel);

            EditorSceneManager.MarkSceneDirty(panel.scene);
            EditorSceneManager.SaveScene(panel.scene);
            Debug.Log("[RecorderUIControlPanelSceneMigrator] RecorderUI panel unpacked completely and customized for QuestCameraRecorder.");
        }

        private static void ApplyRenderFix(GameObject panel)
        {
            Image[] images = panel.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                images[i].material = null;
                EditorUtility.SetDirty(images[i]);
            }

            Graphic[] graphics = panel.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic is TMP_Text)
                {
                    continue;
                }

                graphic.material = null;
                EditorUtility.SetDirty(graphic);
            }

            RemoveRoundedBoxComponents(panel);
            SetActiveIfFound(panel.transform, "FlatUnityCanvas", true);
            SetActiveIfFound(panel.transform, "HandleCanvas2", true);

            Renderer[] renderers = panel.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
                EditorUtility.SetDirty(renderers[i]);
            }

            SetLayerRecursively(panel, LayerMask.NameToLayer("UI"));

            EnsureHandleCollider(panel);
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

        private static void EnsureHandleCollider(GameObject panel)
        {
            RectTransform handler = panel.transform.Find("HandleCanvas2/Canvas/Menu/RecorderHandler") as RectTransform;
            if (handler == null)
            {
                Debug.LogWarning("[RecorderUIControlPanelSceneMigrator] Could not find RecorderUI handle canvas for drag fallback.");
                return;
            }

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

        private static void BuildRecordingControls(GameObject panel, RectTransform content)
        {
            VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
            if (contentLayout == null)
            {
                contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentLayout.spacing = 12f;
            contentLayout.childAlignment = TextAnchor.MiddleCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            TextMeshProUGUI status = CreateText(content, "StatusText", "Waiting for camera", 24f);
            status.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

            RectTransform row = CreateRect(content, "RecordButtons");
            HorizontalLayoutGroup rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            LayoutElement rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 58f;
            rowElement.flexibleHeight = 0f;

            Button startButton = CreateButton(row, "StartButton", "Start Recording", new Color(0.12f, 0.42f, 0.28f, 1f));
            Button stopButton = CreateButton(row, "StopButton", "Stop Recording", new Color(0.48f, 0.12f, 0.15f, 1f));

            TextMeshProUGUI hint = CreateText(content, "OutputHint", "Output: Application.persistentDataPath/record", 15f);
            hint.color = new Color(1f, 1f, 1f, 0.72f);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            RecorderControlPanel control = panel.GetComponent<RecorderControlPanel>();
            if (control == null)
            {
                control = panel.AddComponent<RecorderControlPanel>();
            }

            control.Recorder = Object.FindFirstObjectByType<QuestCameraRecorder>();
            control.StatusText = status;
            control.StartButton = startButton;
            control.StopButton = stopButton;

            startButton.onClick.RemoveAllListeners();
            stopButton.onClick.RemoveAllListeners();
            UnityEventTools.AddPersistentListener(startButton.onClick, control.StartRecording);
            UnityEventTools.AddPersistentListener(stopButton.onClick, control.StopRecording);
            control.Refresh();
        }

        private static Button CreateButton(RectTransform parent, string name, string label, Color color)
        {
            Image background = CreateImage(parent, name, color);
            LayoutElement layout = background.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = 58f;

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            TextMeshProUGUI text = CreateText(background.rectTransform, "Text (TMP)", label, 18f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            return button;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, float fontSize)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            RectTransform rect = rectObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }
    }
}
