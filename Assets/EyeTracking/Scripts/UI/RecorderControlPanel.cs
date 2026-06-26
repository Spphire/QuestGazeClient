using EyeTracking.Recording;
using UnityEngine;
using UnityEngine.UI;

namespace EyeTracking.UI
{
    public sealed class RecorderControlPanel : MonoBehaviour
    {
        [SerializeField] private QuestCameraRecorder recorder;
        [SerializeField] private Behaviour statusText;
        [SerializeField] private Text statusTextLegacy;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;

        public QuestCameraRecorder Recorder
        {
            get => recorder;
            set => recorder = value;
        }

        public Behaviour StatusText
        {
            get => statusText;
            set => statusText = value;
        }

        public Text StatusTextLegacy
        {
            get => statusTextLegacy;
            set => statusTextLegacy = value;
        }

        public Button StartButton
        {
            get => startButton;
            set => startButton = value;
        }

        public Button StopButton
        {
            get => stopButton;
            set => stopButton = value;
        }

        private void Reset()
        {
            recorder = FindFirstObjectByType<QuestCameraRecorder>();
        }

        private void Update()
        {
            Refresh();
        }

        public void StartRecording()
        {
            if (recorder == null)
            {
                Debug.LogWarning("[RecorderControlPanel] QuestCameraRecorder is not assigned.");
                return;
            }

            recorder.StartRecording();
            Refresh();
        }

        public void StopRecording()
        {
            if (recorder == null)
            {
                Debug.LogWarning("[RecorderControlPanel] QuestCameraRecorder is not assigned.");
                return;
            }

            recorder.StopRecording();
            Refresh();
        }

        public void Refresh()
        {
            bool hasRecorder = recorder != null;
            bool ready = hasRecorder && recorder.IsReady;
            bool recording = hasRecorder && recorder.IsRecording;

            if (startButton != null)
            {
                startButton.interactable = ready && !recording;
            }

            if (stopButton != null)
            {
                stopButton.interactable = recording;
            }

            string status;

            if (!hasRecorder)
            {
                status = "Recorder: missing";
            }
            else if (recording)
            {
                status = "Recording";
            }
            else if (ready)
            {
                status = "Ready";
            }
            else
            {
                status = "Waiting for camera";
            }

            if (statusText != null)
            {
                TrySetText(statusText, status);
            }

            if (statusTextLegacy != null)
            {
                statusTextLegacy.text = status;
            }
        }

        private static void TrySetText(Behaviour textBehaviour, string value)
        {
            System.Type type = textBehaviour.GetType();
            System.Reflection.PropertyInfo textProperty = type.GetProperty("text");
            if (textProperty != null && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
            {
                textProperty.SetValue(textBehaviour, value);
            }
        }
    }
}
