# RecorderUI Empty Panel

This prefab is a decoupled, empty-content version of the `RecorderUI` node from:

`W:\FinalWork\UMIVR\Assets\Scenes\QuestUMIVR_v2_fileupload.unity`

It keeps the same high-level structure:

- `RecorderUIEmptyPanel`: root object with `Grabbable`, `RayInteractable`, and `MoveRelativeToTargetProvider`.
- `FlatUnityCanvas / Unity Canvas`: main world-space panel content. Put custom UI under `Menu/Content`.
- `HandleCanvas2 / Canvas / Menu / RecorderHandler`: bar UI.
- `CollapseButton`: calls `RecorderPanelVisibilityController.ShowHide()`.
- `ScaleHideButton`: also calls `ShowHide()` because the original RecorderUI right button has no serialized OnClick binding.

The visibility controller mirrors UMIVR's `UIHandler`: it toggles `CanvasGroup.alpha`, `interactable`, `blocksRaycasts`, and disables child `RayInteractable` / first surface child when hidden.

Use Unity menu:

`EyeTracking/UI/Rebuild RecorderUI Empty Panel Prefab`

or:

`EyeTracking/UI/Create RecorderUI Empty Panel In Scene`

The panel depends on the existing Meta XR Interaction SDK package in this project. It does not copy recorder, HTTP, IP-list, frame-pusher, or camera-recording logic.
