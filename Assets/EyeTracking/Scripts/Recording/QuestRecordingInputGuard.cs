using UnityEngine.XR;

namespace EyeTracking.Recording
{
    public static class QuestRecordingInputGuard
    {
        public static bool ShouldIgnoreRecordingInput()
        {
            return AppInputFocusLost() || IsSystemMenuButtonHeld();
        }

        public static bool IsSystemMenuButtonHeld()
        {
            return OVRInput.Get(OVRInput.RawButton.Start, OVRInput.Controller.All) ||
                   OVRInput.Get(OVRInput.RawButton.Back, OVRInput.Controller.All) ||
                   OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.All) ||
                   OVRInput.Get(OVRInput.Button.Back, OVRInput.Controller.All) ||
                   XrMenuButtonHeld(XRNode.LeftHand) ||
                   XrMenuButtonHeld(XRNode.RightHand);
        }

        private static bool XrMenuButtonHeld(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid &&
                   device.TryGetFeatureValue(CommonUsages.menuButton, out bool held) &&
                   held;
        }

        private static bool AppInputFocusLost()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return !OVRManager.hasInputFocus;
#else
            return false;
#endif
        }
    }
}
