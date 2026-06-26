using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
using Oculus.Interaction;

public class BaseLinkDragController : MonoBehaviour
{
    [SerializeField] private Transform targetRoot;
    [SerializeField] private Transform trackingSpace;
    [SerializeField] private RectTransform handleRect;
    [SerializeField] private Collider handleCollider;
    [FormerlySerializedAs("gripThreshold")]
    [SerializeField, Range(0f, 1f)] private float controllerGripThreshold = 0.55f;
    [FormerlySerializedAs("triggerThreshold")]
    [SerializeField, Range(0f, 1f)] private float controllerTriggerThreshold = 0.55f;
    [SerializeField, Range(0f, 1f)] private float handPinchStartThreshold = 0.7f;
    [SerializeField, Range(0f, 1f)] private float handPinchReleaseThreshold = 0.45f;
    [SerializeField] private float maxHitDistance = 4f;
    [SerializeField] private float minRayDistance = 0.2f;
    [SerializeField] private float handleHitPadding = 80f;
    [SerializeField] private float fallbackHandleWorldRadius = 0.42f;
    [SerializeField] private bool requireHandleHit = true;
    [SerializeField] private bool logDiagnostics = true;
    [SerializeField] private float diagnosticLogInterval = 1f;

    private enum Source { None, LeftController, RightController, LeftHand, RightHand }

    private Source activeSource;
    private float rayDistance;
    private float sourceYawOnGrab;
    private float targetYawOnGrab;
    private Vector3 grabPlaneNormal;
    private Vector3 grabPlaneOffset;
    private OVRPlugin.HandState leftHandState;
    private OVRPlugin.HandState rightHandState;
    private RayInteractor[] cachedRayInteractors;
    private float nextRayCacheRefreshTime;
    private float nextDiagnosticLogTime;

    public bool IsDragging => activeSource != Source.None;

    public void Configure(Transform newTargetRoot, RectTransform newHandleRect)
    {
        targetRoot = newTargetRoot;
        handleRect = newHandleRect;
        handleCollider = newHandleRect != null ? newHandleRect.GetComponent<Collider>() : handleCollider;
        EnsureHandleCollider();
    }

    public void Configure(Transform newTargetRoot, Collider newHandleCollider)
    {
        targetRoot = newTargetRoot;
        handleCollider = newHandleCollider;
        handleRect = newHandleCollider != null ? newHandleCollider.transform as RectTransform : null;
    }

    private void Awake()
    {
        if (targetRoot == null)
        {
            targetRoot = FindRecorderPanelRoot() ?? transform;
        }

        if (trackingSpace == null)
        {
            OVRCameraRig ovrRig = FindFirstObjectByType<OVRCameraRig>();
            trackingSpace = ovrRig != null && ovrRig.trackingSpace != null
                ? ovrRig.trackingSpace
                : GameObject.Find("XR Origin")?.transform;
        }

        AutoWireHandle();
        EnsureHandleCollider();
    }

    private void LateUpdate()
    {
        if (targetRoot == null)
        {
            return;
        }

        if (handleRect == null && handleCollider == null)
        {
            AutoWireHandle();
            EnsureHandleCollider();
        }

        if (activeSource == Source.None)
        {
            TryGrabAny();
            return;
        }

        if (TryPose(activeSource, true, out Pose pose))
        {
            MoveByRay(pose);
        }
        else
        {
            activeSource = Source.None;
        }
    }

    private bool TryGrabAny()
    {
        if (TryGrabFromAnyActiveRayInteractor())
        {
            return true;
        }

        return TryGrab(Source.RightController) ||
               TryGrab(Source.LeftController) ||
               TryGrab(Source.RightHand) ||
               TryGrab(Source.LeftHand);
    }

    private bool TryGrabFromAnyActiveRayInteractor()
    {
        if (!AnyControllerSelectPressed() ||
            !TryAnyRayInteractorPoseHittingHandle(out Pose pose))
        {
            return false;
        }

        activeSource = GuessControllerSourceFromRayPose(pose);
        rayDistance = Mathf.Max(minRayDistance, Vector3.Distance(targetRoot.position, pose.position));
        if (TryRaycastTargetPlane(new Ray(pose.position, pose.forward), out Vector3 planePoint))
        {
            rayDistance = Mathf.Max(minRayDistance, Vector3.Distance(planePoint, pose.position));
            grabPlaneOffset = targetRoot.position - planePoint;
        }
        else
        {
            grabPlaneOffset = Vector3.zero;
        }

        grabPlaneNormal = targetRoot.forward.sqrMagnitude > 0.0001f ? targetRoot.forward.normalized : -pose.forward;
        sourceYawOnGrab = Yaw(pose.rotation);
        targetYawOnGrab = Yaw(targetRoot.rotation);
        MoveByRay(pose);
        Debug.Log($"[BaseLinkDragController] Grabbed recorder panel with active RayInteractor fallback using handle '{HandleName()}'.", this);
        return true;
    }

    private bool TryGrab(Source source)
    {
        if (!TryPose(source, false, out Pose pose))
        {
            LogDiagnostic(source, "select/pose not ready");
            return false;
        }

        if (requireHandleHit && !RayHitsHandle(new Ray(pose.position, pose.forward)))
        {
            if (!TryAnyRayInteractorPoseHittingHandle(out pose))
            {
                LogDiagnostic(source, $"ray missed handle '{(handleRect != null ? handleRect.name : "null")}'");
                return false;
            }
        }

        activeSource = source;
        rayDistance = Mathf.Max(minRayDistance, Vector3.Distance(targetRoot.position, pose.position));
        if (TryRaycastTargetPlane(new Ray(pose.position, pose.forward), out Vector3 planePoint))
        {
            rayDistance = Mathf.Max(minRayDistance, Vector3.Distance(planePoint, pose.position));
            grabPlaneOffset = targetRoot.position - planePoint;
        }
        else
        {
            grabPlaneOffset = Vector3.zero;
        }

        grabPlaneNormal = targetRoot.forward.sqrMagnitude > 0.0001f ? targetRoot.forward.normalized : -pose.forward;
        sourceYawOnGrab = Yaw(pose.rotation);
        targetYawOnGrab = Yaw(targetRoot.rotation);
        MoveByRay(pose);
        Debug.Log($"[BaseLinkDragController] Grabbed recorder panel with {source} using handle '{HandleName()}'.", this);
        return true;
    }

    private void MoveByRay(Pose pose)
    {
        float yaw = targetYawOnGrab + Mathf.DeltaAngle(sourceYawOnGrab, Yaw(pose.rotation));
        Vector3 targetPosition;
        Ray ray = new Ray(pose.position, pose.forward);
        if (TryRaycastDragPlane(ray, out targetPosition))
        {
            targetPosition += grabPlaneOffset;
        }
        else
        {
            targetPosition = pose.position + pose.forward * rayDistance;
        }

        targetRoot.SetPositionAndRotation(targetPosition, Quaternion.AngleAxis(yaw, Vector3.up));
    }

    private bool TryPose(Source source, bool held, out Pose pose)
    {
        pose = default;

        switch (source)
        {
            case Source.LeftController:
                return TryControllerPose(XRNode.LeftHand, out pose);
            case Source.RightController:
                return TryControllerPose(XRNode.RightHand, out pose);
            case Source.LeftHand:
                return TryHandPose(OVRPlugin.Hand.HandLeft, held, ref leftHandState, out pose);
            case Source.RightHand:
                return TryHandPose(OVRPlugin.Hand.HandRight, held, ref rightHandState, out pose);
            default:
                return false;
        }
    }

    private bool TryControllerPose(XRNode node, out Pose pose)
    {
        pose = default;

        if (TryOvrControllerPose(node, out pose))
        {
            return true;
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !IsControllerSelectPressed(device))
        {
            return false;
        }

        if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
        {
            return false;
        }

        pose = ToWorldPose(position, rotation);
        return true;
    }

    private bool TryOvrControllerPose(XRNode node, out Pose pose)
    {
        OVRInput.Controller controller = node == XRNode.LeftHand
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        if ((OVRInput.GetConnectedControllers() & controller) == 0 ||
            !IsOvrControllerSelectPressed(controller))
        {
            pose = default;
            return false;
        }

        if (TryRayInteractorPose(node, out pose))
        {
            return true;
        }

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);
        pose = ToWorldPose(localPosition, localRotation);
        return true;
    }

    private bool IsControllerSelectPressed(InputDevice device)
    {
        if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool button) && button)
        {
            return true;
        }

        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out button) && button)
        {
            return true;
        }

        if (device.TryGetFeatureValue(CommonUsages.grip, out float grip) &&
            grip >= controllerGripThreshold)
        {
            return true;
        }

        return device.TryGetFeatureValue(CommonUsages.trigger, out float trigger) &&
               trigger >= controllerTriggerThreshold;
    }

    private bool IsOvrControllerSelectPressed(OVRInput.Controller controller)
    {
        return OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller) ||
               OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) >= controllerGripThreshold ||
               OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller) ||
               OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) >= controllerTriggerThreshold;
    }

    private bool AnyControllerSelectPressed()
    {
        return IsOvrControllerSelectPressed(OVRInput.Controller.RTouch) ||
               IsOvrControllerSelectPressed(OVRInput.Controller.LTouch) ||
               IsXrControllerSelectPressed(XRNode.RightHand) ||
               IsXrControllerSelectPressed(XRNode.LeftHand);
    }

    private bool IsXrControllerSelectPressed(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid && IsControllerSelectPressed(device);
    }

    private bool TryRayInteractorPose(XRNode node, out Pose pose)
    {
        if (cachedRayInteractors == null || cachedRayInteractors.Length == 0)
        {
            RefreshRayInteractorCache();
        }

        string handName = node == XRNode.LeftHand ? "left" : "right";
        for (int i = 0; i < cachedRayInteractors.Length; i++)
        {
            RayInteractor interactor = cachedRayInteractors[i];
            if (interactor == null || !interactor.isActiveAndEnabled)
            {
                continue;
            }

            string interactorName = interactor.name.ToLowerInvariant();
            string rootName = interactor.transform.root.name.ToLowerInvariant();
            if (!interactorName.Contains(handName) && !rootName.Contains(handName))
            {
                continue;
            }

            Ray ray = interactor.Ray;
            if (ray.direction.sqrMagnitude <= 0.01f)
            {
                continue;
            }

            pose = new Pose(ray.origin, Quaternion.LookRotation(ray.direction.normalized, Vector3.up));
            return true;
        }

        for (int i = 0; i < cachedRayInteractors.Length; i++)
        {
            RayInteractor interactor = cachedRayInteractors[i];
            if (TryPoseFromRayInteractor(interactor, out pose))
            {
                return true;
            }
        }

        if (Time.unscaledTime >= nextRayCacheRefreshTime)
        {
            RefreshRayInteractorCache();
        }

        pose = default;
        return false;
    }

    private void RefreshRayInteractorCache()
    {
        cachedRayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None);
        nextRayCacheRefreshTime = Time.unscaledTime + 1f;
    }

    private static bool TryPoseFromRayInteractor(RayInteractor interactor, out Pose pose)
    {
        if (interactor == null || !interactor.isActiveAndEnabled)
        {
            pose = default;
            return false;
        }

        Ray ray = interactor.Ray;
        if (ray.direction.sqrMagnitude <= 0.01f)
        {
            pose = default;
            return false;
        }

        pose = new Pose(ray.origin, Quaternion.LookRotation(ray.direction.normalized, Vector3.up));
        return true;
    }

    private bool TryAnyRayInteractorPoseHittingHandle(out Pose pose)
    {
        pose = default;
        if (handleRect == null && handleCollider == null)
        {
            return false;
        }

        if (cachedRayInteractors == null ||
            cachedRayInteractors.Length == 0 ||
            Time.unscaledTime >= nextRayCacheRefreshTime)
        {
            RefreshRayInteractorCache();
        }

        for (int i = 0; i < cachedRayInteractors.Length; i++)
        {
            if (!TryPoseFromRayInteractor(cachedRayInteractors[i], out Pose candidatePose))
            {
                continue;
            }

            if (RayHitsHandle(new Ray(candidatePose.position, candidatePose.forward)))
            {
                pose = candidatePose;
                return true;
            }
        }

        return false;
    }

    private Source GuessControllerSourceFromRayPose(Pose pose)
    {
        if (TryOvrControllerPoseWithoutSelect(XRNode.RightHand, out Pose rightPose) &&
            Vector3.SqrMagnitude(rightPose.position - pose.position) <= 0.09f)
        {
            return Source.RightController;
        }

        if (TryOvrControllerPoseWithoutSelect(XRNode.LeftHand, out Pose leftPose) &&
            Vector3.SqrMagnitude(leftPose.position - pose.position) <= 0.09f)
        {
            return Source.LeftController;
        }

        if (IsOvrControllerSelectPressed(OVRInput.Controller.RTouch) || IsXrControllerSelectPressed(XRNode.RightHand))
        {
            return Source.RightController;
        }

        if (IsOvrControllerSelectPressed(OVRInput.Controller.LTouch) || IsXrControllerSelectPressed(XRNode.LeftHand))
        {
            return Source.LeftController;
        }

        return Source.RightController;
    }

    private bool TryOvrControllerPoseWithoutSelect(XRNode node, out Pose pose)
    {
        OVRInput.Controller controller = node == XRNode.LeftHand
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        if ((OVRInput.GetConnectedControllers() & controller) == 0)
        {
            pose = default;
            return false;
        }

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);
        pose = ToWorldPose(localPosition, localRotation);
        return true;
    }

    private bool TryHandPose(OVRPlugin.Hand hand, bool held, ref OVRPlugin.HandState state, out Pose pose)
    {
        pose = default;

        if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state))
        {
            return false;
        }

        OVRPlugin.HandStatus status = state.Status;
        if ((status & OVRPlugin.HandStatus.HandTracked) == 0 ||
            (status & OVRPlugin.HandStatus.InputStateValid) == 0 ||
            (status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0)
        {
            return false;
        }

        if (IndexPinch(state) < (held ? handPinchReleaseThreshold : handPinchStartThreshold))
        {
            return false;
        }

        pose = ToWorldPose(
            new Vector3(state.PointerPose.Position.x, state.PointerPose.Position.y, -state.PointerPose.Position.z),
            new Quaternion(-state.PointerPose.Orientation.x, -state.PointerPose.Orientation.y, state.PointerPose.Orientation.z, state.PointerPose.Orientation.w));
        return true;
    }

    private float IndexPinch(OVRPlugin.HandState state)
    {
        int index = (int)OVRPlugin.HandFinger.Index;
        if (state.PinchStrength != null && state.PinchStrength.Length > index)
        {
            return state.PinchStrength[index];
        }

        return (state.Pinches & OVRPlugin.HandFingerPinch.Index) == OVRPlugin.HandFingerPinch.Index ? 1f : 0f;
    }

    private Pose ToWorldPose(Vector3 position, Quaternion rotation)
    {
        return trackingSpace == null
            ? new Pose(position, rotation)
            : new Pose(trackingSpace.TransformPoint(position), trackingSpace.rotation * rotation);
    }

    private void AutoWireHandle()
    {
        if (handleCollider != null && handleCollider.transform != null)
        {
            handleRect = handleCollider.transform as RectTransform;
            return;
        }

        Transform root = targetRoot != null ? targetRoot : transform;
        RectTransform selfRect = transform as RectTransform;
        if (selfRect != null && transform.name.Contains("RecorderHandler"))
        {
            handleRect = selfRect;
            return;
        }

        handleRect = root.Find("HandleCanvas2/Canvas/Menu/RecorderHandler") as RectTransform;
        if (handleRect == null)
        {
            handleRect = root.Find("RecorderSafeUnityPanel/Canvas/Panel/SafeDragBar") as RectTransform;
        }

        if (handleRect == null && selfRect != null)
        {
            handleRect = selfRect;
        }
    }

    private Transform FindRecorderPanelRoot()
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == "QuestCameraRecorderPanel" ||
                current.name == "RecorderUIEmptyPanel" ||
                current.name == "RecorderFallbackPanel")
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private bool RayHitsHandle(Ray ray)
    {
        if (handleRect == null && handleCollider == null)
        {
            return false;
        }

        if (RayHitsCollider(ray))
        {
            return true;
        }

        if (handleRect == null)
        {
            return RayNearHandle(ray);
        }

        if (!TryRaycastHandlePlane(ray, handleRect.forward, out float enter) &&
            !TryRaycastHandlePlane(ray, -handleRect.forward, out enter))
        {
            return RayNearHandle(ray);
        }

        Vector3 worldPoint = ray.GetPoint(enter);
        Vector3 localPoint = handleRect.InverseTransformPoint(worldPoint);
        Rect rect = handleRect.rect;
        rect.xMin -= handleHitPadding;
        rect.xMax += handleHitPadding;
        rect.yMin -= handleHitPadding;
        rect.yMax += handleHitPadding;

        return rect.Contains(new Vector2(localPoint.x, localPoint.y)) || RayNearHandle(ray);
    }

    private bool TryRaycastTargetPlane(Ray ray, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (targetRoot == null)
        {
            return false;
        }

        Vector3 normal = targetRoot.forward;
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = HandleForwardOrFallback(-ray.direction);
        }

        Plane plane = new Plane(normal.normalized, targetRoot.position);
        if (!plane.Raycast(ray, out float enter) || enter < minRayDistance || enter > maxHitDistance)
        {
            return false;
        }

        worldPoint = ray.GetPoint(enter);
        return true;
    }

    private bool TryRaycastDragPlane(Ray ray, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (targetRoot == null || grabPlaneNormal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Plane plane = new Plane(grabPlaneNormal, targetRoot.position);
        if (!plane.Raycast(ray, out float enter) || enter < minRayDistance || enter > maxHitDistance)
        {
            return false;
        }

        worldPoint = ray.GetPoint(enter);
        return true;
    }

    private bool RayHitsCollider(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxHitDistance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider != null && handleCollider != null &&
                (hitCollider == handleCollider || hitCollider.transform.IsChildOf(handleCollider.transform)))
            {
                return true;
            }

            if (hitCollider != null && handleRect != null &&
                (hitCollider.transform == handleRect || hitCollider.transform.IsChildOf(handleRect)))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRaycastHandlePlane(Ray ray, Vector3 normal, out float enter)
    {
        Vector3 position = handleRect != null
            ? handleRect.position
            : handleCollider != null ? handleCollider.bounds.center : transform.position;
        Plane plane = new Plane(normal, position);
        return plane.Raycast(ray, out enter) && enter >= 0f && enter <= maxHitDistance;
    }

    private bool RayNearHandle(Ray ray)
    {
        Vector3 direction = ray.direction.normalized;
        Vector3 handleCenter = handleRect != null
            ? handleRect.TransformPoint(handleRect.rect.center)
            : handleCollider != null ? handleCollider.bounds.center : transform.position;
        float distanceAlongRay = Mathf.Clamp(Vector3.Dot(handleCenter - ray.origin, direction), 0f, maxHitDistance);
        Vector3 closest = ray.origin + direction * distanceAlongRay;
        float radius = fallbackHandleWorldRadius;
        if (handleCollider != null)
        {
            radius = Mathf.Max(radius, handleCollider.bounds.extents.magnitude);
        }

        return Vector3.Distance(handleCenter, closest) <= radius;
    }

    private void EnsureHandleCollider()
    {
        if (handleRect == null)
        {
            return;
        }

        BoxCollider collider = handleRect.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = handleRect.gameObject.AddComponent<BoxCollider>();
        }

        Rect rect = handleRect.rect;
        collider.isTrigger = true;
        collider.center = new Vector3(rect.center.x, rect.center.y, 0f);
        collider.size = new Vector3(
            Mathf.Max(1f, rect.width + handleHitPadding * 2f),
            Mathf.Max(1f, rect.height + handleHitPadding * 2f),
            120f);
        handleCollider = collider;
    }

    private Vector3 HandleForwardOrFallback(Vector3 fallback)
    {
        if (handleRect != null && handleRect.forward.sqrMagnitude > 0.0001f)
        {
            return handleRect.forward;
        }

        if (handleCollider != null && handleCollider.transform.forward.sqrMagnitude > 0.0001f)
        {
            return handleCollider.transform.forward;
        }

        return fallback;
    }

    private void LogDiagnostic(Source source, string message)
    {
        if (!logDiagnostics || Time.unscaledTime < nextDiagnosticLogTime)
        {
            return;
        }

        nextDiagnosticLogTime = Time.unscaledTime + diagnosticLogInterval;
        Debug.Log($"[BaseLinkDragController] {source}: {message}. targetRoot={(targetRoot != null ? targetRoot.name : "null")} handle={HandleName()}", this);
    }

    private string HandleName()
    {
        if (handleRect != null)
        {
            return handleRect.name;
        }

        return handleCollider != null ? handleCollider.name : "null";
    }

    private static float Yaw(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 0.000001f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }
}
