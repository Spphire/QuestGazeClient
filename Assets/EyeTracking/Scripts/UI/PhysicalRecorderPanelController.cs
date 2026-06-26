using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Oculus.Interaction;
using Oculus.Interaction.Input;

namespace EyeTracking.UI
{
    [DisallowMultipleComponent]
    public sealed class PhysicalRecorderPanelController : MonoBehaviour
    {
        [SerializeField] private Transform targetRoot;
        [SerializeField] private EyeTracking.Recording.QuestCameraRecorder recorder;
        [SerializeField] private Collider dragBar;
        [SerializeField] private Collider startButton;
        [SerializeField] private Collider stopButton;
        [SerializeField] private float maxRayDistance = 4f;
        [SerializeField] private float minDragDistance = 0.2f;
        [SerializeField] private float controllerTriggerThreshold = 0.55f;
        [SerializeField] private float rayHitPadding = 0.28f;
        [SerializeField] private float dragBarNearRayTolerance = 0.55f;
        [SerializeField] private float panelDragFallbackTolerance = 1.25f;
        [SerializeField] private float directHandDragStartTolerance = 1.25f;
        [SerializeField] private bool allowPanelWideFallbackDrag = true;
        [SerializeField] private bool enableOvrControllerRayFallback = true;
        [SerializeField] private bool ignoreInputWhileSystemMenuHeld = true;
        [SerializeField] private bool logRayDiagnostics = true;

        private Transform trackingSpace;
        private RayInteractor[] rayInteractors;
        private ControllerRef[] controllerRefs;
        private float nextRayRefreshTime;
        private float nextDiagnosticTime;
        private bool dragging;
        private bool directHandDragging;
        private bool ovrRayFallbackDragging;
        private bool suppressSelectUntilReleased;
        private bool wasSelectHeld;
        private float dragDistance;
        private Vector3 dragOffset;
        private Vector3 dragPlaneNormal;
        private Ray activeDragRay;
        private Vector3 directHandStartPosition;
        private Vector3 directPanelStartPosition;
        private int lastRayCandidateCount;
        private float lastBestNearMissDistance = float.PositiveInfinity;
        private float lastBestFallbackDistance = float.PositiveInfinity;
        private string lastRayStatus = "idle";
        private float lastDragStartTime = -1f;
        private string lastSelectSource = "none";
        private bool lastRightControllerPoseValid;
        private float lastRightRayToBarDistance = float.PositiveInfinity;

        public bool IsDragging => dragging;
        public int LastRayCandidateCount => lastRayCandidateCount;
        public float LastBestNearMissDistance => lastBestNearMissDistance;
        public float LastBestFallbackDistance => lastBestFallbackDistance;
        public string LastRayStatus => lastRayStatus;
        public string LastSelectSource => lastSelectSource;
        public float LastDragStartTime => lastDragStartTime;
        public bool LastRightControllerPoseValid => lastRightControllerPoseValid;
        public float LastRightRayToBarDistance => lastRightRayToBarDistance;
        public float RayHitPadding => rayHitPadding;
        public float DragBarNearRayTolerance => dragBarNearRayTolerance;
        public float PanelDragFallbackTolerance => panelDragFallbackTolerance;
        public float DirectHandDragStartTolerance => directHandDragStartTolerance;
        public bool EnableOvrControllerRayFallback => enableOvrControllerRayFallback;
        public Collider DragBarCollider => dragBar;

        public void Configure(
            Transform newTargetRoot,
            EyeTracking.Recording.QuestCameraRecorder newRecorder,
            Collider newDragBar,
            Collider newStartButton,
            Collider newStopButton)
        {
            targetRoot = newTargetRoot;
            recorder = newRecorder;
            dragBar = newDragBar;
            startButton = newStartButton;
            stopButton = newStopButton;
        }

        private void Awake()
        {
            if (targetRoot == null)
            {
                targetRoot = transform.parent != null ? transform.parent : transform;
            }

            if (recorder == null)
            {
                recorder = FindFirstObjectByType<EyeTracking.Recording.QuestCameraRecorder>();
            }

            AutoWirePhysicalColliders();

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            trackingSpace = rig != null && rig.trackingSpace != null
                ? rig.trackingSpace
                : GameObject.Find("XR Origin")?.transform;
        }

        private void Update()
        {
            if (targetRoot == null)
            {
                return;
            }

            if (dragBar == null || startButton == null || stopButton == null)
            {
                AutoWirePhysicalColliders();
            }

            if (ignoreInputWhileSystemMenuHeld && EyeTracking.Recording.QuestRecordingInputGuard.ShouldIgnoreRecordingInput())
            {
                suppressSelectUntilReleased = true;
                ClearSelectionState();
                lastSelectSource = "system_menu_guard";
                lastRayStatus = "system_menu_guard";
                return;
            }

            bool selectHeld = SelectHeld();
            if (suppressSelectUntilReleased)
            {
                if (!selectHeld)
                {
                    suppressSelectUntilReleased = false;
                }

                ClearSelectionState();
                return;
            }

            bool selectDown = SelectDown(selectHeld);
            if (!selectHeld)
            {
                ClearSelectionState();
                return;
            }

            if (selectDown && TryRayForCollider(startButton, out Ray startRay))
            {
                recorder ??= FindFirstObjectByType<EyeTracking.Recording.QuestCameraRecorder>();
                recorder?.StartRecording();
                Debug.Log("[PhysicalRecorderPanelController] Start button selected.", this);
                return;
            }

            if (selectDown && TryRayForCollider(stopButton, out Ray stopRay))
            {
                recorder ??= FindFirstObjectByType<EyeTracking.Recording.QuestCameraRecorder>();
                recorder?.StopRecording();
                Debug.Log("[PhysicalRecorderPanelController] Stop button selected.", this);
                return;
            }

            if (ovrRayFallbackDragging)
            {
                MoveByOvrControllerRayFallback();
                return;
            }

            if (directHandDragging)
            {
                MoveByDirectHand();
                return;
            }

            if (!dragging)
            {
                if (!TryRightControllerRayForCollider(dragBar, out Ray barRay, allowNearMiss: true) &&
                    !TryRayForCollider(dragBar, out barRay, allowNearMiss: true))
                {
                    if (!TryPanelFallbackRay(out barRay))
                    {
                        if (TryBeginOvrControllerRayFallbackDrag())
                        {
                            return;
                        }

                        if (TryBeginDirectHandDrag())
                        {
                            return;
                        }

                        LogRayMissIfNeeded();
                        return;
                    }
                }

                BeginDrag(barRay);
            }

            if (TryRayNearActiveDrag(out Ray dragRay))
            {
                activeDragRay = dragRay;
            }

            MoveByRay(activeDragRay);
        }

        private void ClearSelectionState()
        {
            dragging = false;
            directHandDragging = false;
            ovrRayFallbackDragging = false;
            wasSelectHeld = false;
        }

        private void AutoWirePhysicalColliders()
        {
            if (dragBar == null)
            {
                dragBar = transform.Find("RecorderHandler")?.GetComponent<Collider>();
            }

            if (startButton == null)
            {
                startButton = transform.Find("StartButton")?.GetComponent<Collider>();
            }

            if (stopButton == null)
            {
                stopButton = transform.Find("StopButton")?.GetComponent<Collider>();
            }

            ConfigurePanelCollider(dragBar, "RecorderHandler");
            ConfigurePanelCollider(startButton, "StartButton");
            ConfigurePanelCollider(stopButton, "StopButton");
        }

        private static void ConfigurePanelCollider(Collider collider, string plateName)
        {
            if (collider == null)
            {
                return;
            }

            collider.isTrigger = true;
            if (collider is not BoxCollider boxCollider)
            {
                return;
            }

            if (plateName == "RecorderHandler")
            {
                boxCollider.size = new Vector3(1.8f, 3.2f, 18f);
                return;
            }

            if (plateName == "StartButton" || plateName == "StopButton")
            {
                boxCollider.size = new Vector3(1.8f, 2.2f, 14f);
            }
        }

        private bool TryBeginOvrControllerRayFallbackDrag()
        {
            if (!enableOvrControllerRayFallback)
            {
                return false;
            }

            Bounds bounds = dragBar != null ? dragBar.bounds : new Bounds(targetRoot.position, Vector3.one * 0.2f);
            if (allowPanelWideFallbackDrag)
            {
                EncapsulateColliderBounds(ref bounds, startButton);
                EncapsulateColliderBounds(ref bounds, stopButton);
            }

            if (!TryBestRightControllerRayForBounds(bounds, out Ray ray, out float bestDistance, out float projectedDistance, out bool sawPose))
            {
                lastBestFallbackDistance = bestDistance;
                lastRayStatus = sawPose ? "ovr_ray_no_valid_candidate" : "ovr_ray_no_pose";
                return false;
            }

            if (projectedDistance < minDragDistance || projectedDistance > maxRayDistance)
            {
                lastBestFallbackDistance = projectedDistance;
                lastRayStatus = "ovr_ray_bad_depth";
                return false;
            }

            if (bestDistance > panelDragFallbackTolerance)
            {
                lastBestFallbackDistance = bestDistance;
                lastRayStatus = "ovr_ray_too_far";
                return false;
            }

            BeginDrag(ray);
            ovrRayFallbackDragging = true;
            lastBestFallbackDistance = bestDistance;
            lastRayStatus = "ovr_ray_fallback_dragging";
            return true;
        }

        private bool TryRightControllerRayForCollider(Collider target, out Ray ray, bool allowNearMiss)
        {
            lastRightControllerPoseValid = false;
            lastRightRayToBarDistance = float.PositiveInfinity;

            if (target == null)
            {
                ray = default;
                return false;
            }

            lastRightControllerPoseValid = true;
            float bestNearMissDistance = float.PositiveInfinity;
            foreach (Ray candidate in RightControllerRays())
            {
                if (TryHitCollider(candidate, target, allowNearMiss, ref bestNearMissDistance))
                {
                    lastBestNearMissDistance = bestNearMissDistance;
                    lastRightRayToBarDistance = FloatOrInfinity(bestNearMissDistance);
                    lastRayStatus = allowNearMiss ? "right_controller_hit_or_near_hit" : "right_controller_hit";
                    ray = candidate;
                    return true;
                }
            }

            lastRightControllerPoseValid = bestNearMissDistance < float.PositiveInfinity ||
                                           TryRightHandPose(out _, out _);
            lastRightRayToBarDistance = FloatOrInfinity(bestNearMissDistance);
            ray = default;
            return false;
        }

        private void MoveByOvrControllerRayFallback()
        {
            Bounds bounds = dragBar != null ? dragBar.bounds : new Bounds(targetRoot.position, Vector3.one * 0.2f);
            if (allowPanelWideFallbackDrag)
            {
                EncapsulateColliderBounds(ref bounds, startButton);
                EncapsulateColliderBounds(ref bounds, stopButton);
            }

            if (TryBestRightControllerRayForBounds(bounds, out Ray ray, out float bestDistance, out _, out _))
            {
                activeDragRay = ray;
                MoveByRay(activeDragRay);
                lastBestFallbackDistance = bestDistance;
                lastRayStatus = "ovr_ray_fallback_dragging";
                return;
            }

            lastRayStatus = "ovr_ray_fallback_lost_pose";
        }

        private bool TryBeginDirectHandDrag()
        {
            if (!TryRightHandPose(out Vector3 handPosition, out _))
            {
                lastRayStatus = "direct_hand_no_pose";
                return false;
            }

            Bounds bounds = dragBar != null ? dragBar.bounds : new Bounds(targetRoot.position, Vector3.one * 0.2f);
            if (allowPanelWideFallbackDrag)
            {
                EncapsulateColliderBounds(ref bounds, startButton);
                EncapsulateColliderBounds(ref bounds, stopButton);
            }

            float distance = Mathf.Sqrt(bounds.SqrDistance(handPosition));
            if (distance > directHandDragStartTolerance)
            {
                lastBestFallbackDistance = distance;
                lastRayStatus = "direct_hand_too_far";
                if (logRayDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
                {
                    nextDiagnosticTime = Time.unscaledTime + 1f;
                    Debug.Log($"[PhysicalRecorderPanelController] Direct hand drag did not start. handDistance={distance:0.000}m tolerance={directHandDragStartTolerance:0.000}m panelBounds={bounds}", this);
                }

                return false;
            }

            dragging = false;
            directHandDragging = true;
            directHandStartPosition = handPosition;
            directPanelStartPosition = targetRoot.position;
            lastRayStatus = "direct_hand_dragging";
            lastDragStartTime = Time.unscaledTime;
            Debug.Log($"[PhysicalRecorderPanelController] Begin direct hand fallback drag. handDistance={distance:0.000}m", this);
            return true;
        }

        private void MoveByDirectHand()
        {
            if (!TryRightHandPose(out Vector3 handPosition, out _))
            {
                lastRayStatus = "direct_hand_lost_pose";
                return;
            }

            Vector3 position = directPanelStartPosition + (handPosition - directHandStartPosition);
            Vector3 forward = Camera.main != null ? position - Camera.main.transform.position : targetRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = targetRoot.forward;
            }

            targetRoot.SetPositionAndRotation(position, Quaternion.LookRotation(forward.normalized, Vector3.up));
            lastRayStatus = "direct_hand_dragging";
        }

        private void BeginDrag(Ray ray)
        {
            activeDragRay = ray;
            dragging = true;
            lastRayStatus = "dragging";
            lastDragStartTime = Time.unscaledTime;
            dragPlaneNormal = targetRoot.forward.sqrMagnitude > 0.0001f ? targetRoot.forward.normalized : -ray.direction;
            if (TryRaycastDragPlane(ray, out Vector3 planePoint))
            {
                dragDistance = Mathf.Max(minDragDistance, Vector3.Distance(ray.origin, planePoint));
                dragOffset = targetRoot.position - planePoint;
            }
            else
            {
                dragDistance = Mathf.Max(minDragDistance, Vector3.Distance(ray.origin, targetRoot.position));
                dragOffset = Vector3.zero;
            }

            Debug.Log("[PhysicalRecorderPanelController] Begin dragging physical recorder panel.", this);
        }

        private void MoveByRay(Ray ray)
        {
            Vector3 position;
            if (TryRaycastDragPlane(ray, out Vector3 planePoint))
            {
                position = planePoint + dragOffset;
            }
            else
            {
                position = ray.origin + ray.direction * dragDistance;
            }

            Vector3 forward = Camera.main != null ? position - Camera.main.transform.position : targetRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = targetRoot.forward;
            }

            targetRoot.SetPositionAndRotation(position, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        private bool TryRaycastDragPlane(Ray ray, out Vector3 worldPoint)
        {
            Plane plane = new Plane(dragPlaneNormal, targetRoot.position);
            if (plane.Raycast(ray, out float enter) && enter >= minDragDistance && enter <= maxRayDistance)
            {
                worldPoint = ray.GetPoint(enter);
                return true;
            }

            worldPoint = default;
            return false;
        }

        private bool TryRayForCollider(Collider target, out Ray ray, bool allowNearMiss = false)
        {
            int candidateCount = 0;
            float bestNearMissDistance = float.PositiveInfinity;
            foreach (Ray candidate in CandidateRays())
            {
                candidateCount++;
                if (TryHitCollider(candidate, target, allowNearMiss, ref bestNearMissDistance))
                {
                    lastRayCandidateCount = candidateCount;
                    lastBestNearMissDistance = bestNearMissDistance;
                    lastRayStatus = allowNearMiss ? "hit_or_near_hit" : "hit";
                    ray = candidate;
                    return true;
                }
            }

            lastRayCandidateCount = candidateCount;
            lastBestNearMissDistance = bestNearMissDistance;
            lastRayStatus = target != null ? "miss_" + target.name : "miss_null_target";

            if (target != null && logRayDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
            {
                nextDiagnosticTime = Time.unscaledTime + 1f;
                string nearMiss = float.IsFinite(bestNearMissDistance)
                    ? $" bestNearMiss={bestNearMissDistance:0.000}m tolerance={dragBarNearRayTolerance:0.000}m"
                    : string.Empty;
                Debug.Log($"[PhysicalRecorderPanelController] No ray hit for {target.name}. candidates={candidateCount} targetBounds={target.bounds}.{nearMiss} status={lastRayStatus}", this);
            }

            ray = default;
            return false;
        }

        private bool TryPanelFallbackRay(out Ray ray)
        {
            if (!SelectHeld())
            {
                lastRayStatus = "fallback_not_select_held";
                ray = default;
                return false;
            }

            Bounds panelBounds = dragBar != null ? dragBar.bounds : new Bounds(targetRoot.position, Vector3.one * 0.2f);
            if (allowPanelWideFallbackDrag)
            {
                EncapsulateColliderBounds(ref panelBounds, startButton);
                EncapsulateColliderBounds(ref panelBounds, stopButton);
            }

            Vector3 panelCenter = panelBounds.center;
            Ray bestRay = default;
            float bestDistance = float.PositiveInfinity;
            int candidateCount = 0;
            foreach (Ray candidate in CandidateRays())
            {
                candidateCount++;
                Vector3 direction = candidate.direction.sqrMagnitude > 0.0001f ? candidate.direction.normalized : Vector3.forward;
                float projectedDistance = Vector3.Dot(panelCenter - candidate.origin, direction);
                if (projectedDistance < minDragDistance || projectedDistance > maxRayDistance)
                {
                    continue;
                }

                Vector3 closestPoint = candidate.origin + direction * projectedDistance;
                float distance = allowPanelWideFallbackDrag
                    ? Mathf.Sqrt(panelBounds.SqrDistance(closestPoint))
                    : Vector3.Distance(closestPoint, panelCenter);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRay = candidate;
                }
            }

            if (bestDistance > panelDragFallbackTolerance)
            {
                lastRayCandidateCount = candidateCount;
                lastBestFallbackDistance = bestDistance;
                lastRayStatus = "fallback_too_far";
                if (logRayDiagnostics && Time.unscaledTime >= nextDiagnosticTime)
                {
                    nextDiagnosticTime = Time.unscaledTime + 1f;
                    string best = float.IsFinite(bestDistance) ? $"{bestDistance:0.000}m" : "none";
                    Debug.Log($"[PhysicalRecorderPanelController] Fallback rays are too far from panel. candidates={candidateCount} bestDistance={best} tolerance={panelDragFallbackTolerance:0.000}m panelBounds={panelBounds}", this);
                }

                ray = default;
                return false;
            }

            ray = bestRay;
            lastRayCandidateCount = candidateCount;
            lastBestFallbackDistance = bestDistance;
            lastRayStatus = "fallback_drag";
            Debug.Log($"[PhysicalRecorderPanelController] Using fallback drag ray. distance={bestDistance:0.000}m candidates={candidateCount}", this);
            return true;
        }

        private bool TryBestRightControllerRayForBounds(Bounds bounds, out Ray ray, out float bestDistance, out float bestProjectedDistance, out bool sawPose)
        {
            ray = default;
            bestDistance = float.PositiveInfinity;
            bestProjectedDistance = float.PositiveInfinity;
            sawPose = false;

            foreach (Ray candidate in RightControllerRays())
            {
                sawPose = true;
                Vector3 direction = candidate.direction.sqrMagnitude > 0.0001f ? candidate.direction.normalized : Vector3.forward;
                float projectedDistance = Vector3.Dot(bounds.center - candidate.origin, direction);
                if (projectedDistance < minDragDistance || projectedDistance > maxRayDistance)
                {
                    float depthMiss = Mathf.Abs(projectedDistance);
                    if (depthMiss < Mathf.Abs(bestProjectedDistance))
                    {
                        bestProjectedDistance = projectedDistance;
                    }

                    continue;
                }

                Vector3 closestPoint = candidate.origin + direction * projectedDistance;
                float distance = Mathf.Sqrt(bounds.SqrDistance(closestPoint));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestProjectedDistance = projectedDistance;
                    ray = candidate;
                }
            }

            return sawPose && bestDistance < float.PositiveInfinity;
        }

        private static void EncapsulateColliderBounds(ref Bounds bounds, Collider collider)
        {
            if (collider == null)
            {
                return;
            }

            bounds.Encapsulate(collider.bounds);
        }

        private bool TryRayNearActiveDrag(out Ray ray)
        {
            foreach (Ray candidate in CandidateRays())
            {
                float ignored = float.PositiveInfinity;
                if (TryHitCollider(candidate, dragBar, allowNearMiss: true, ref ignored) ||
                    Vector3.Dot(candidate.direction, activeDragRay.direction) > 0.96f)
                {
                    ray = candidate;
                    return true;
                }
            }

            ray = default;
            return false;
        }

        private bool TryHitCollider(Ray ray, Collider target, bool allowNearMiss, ref float bestNearMissDistance)
        {
            if (target == null)
            {
                return false;
            }

            if (target.Raycast(ray, out RaycastHit _, maxRayDistance))
            {
                return true;
            }

            Bounds paddedBounds = target.bounds;
            paddedBounds.Expand(rayHitPadding * 2f);
            if (paddedBounds.IntersectRay(ray, out float distance) &&
                   distance >= 0f &&
                   distance <= maxRayDistance)
            {
                return true;
            }

            if (!allowNearMiss)
            {
                return false;
            }

            Vector3 direction = ray.direction.sqrMagnitude > 0.0001f ? ray.direction.normalized : Vector3.forward;
            float projectedDistance = Vector3.Dot(paddedBounds.center - ray.origin, direction);
            if (projectedDistance < minDragDistance || projectedDistance > maxRayDistance)
            {
                return false;
            }

            Vector3 closestPointOnRay = ray.origin + direction * projectedDistance;
            float distanceToBounds = Mathf.Sqrt(paddedBounds.SqrDistance(closestPointOnRay));
            if (distanceToBounds < bestNearMissDistance)
            {
                bestNearMissDistance = distanceToBounds;
            }

            return distanceToBounds <= dragBarNearRayTolerance;
        }

        private void LogRayMissIfNeeded()
        {
            if (!logRayDiagnostics || Time.unscaledTime < nextDiagnosticTime)
            {
                return;
            }

            nextDiagnosticTime = Time.unscaledTime + 1f;
            lastRayStatus = "select_held_no_candidate_ray";
            Debug.Log("[PhysicalRecorderPanelController] Select is held but no candidate ray is hitting the recorder bar.", this);
        }

        private IEnumerable<Ray> CandidateRays()
        {
            if (rayInteractors == null || rayInteractors.Length == 0 || Time.unscaledTime >= nextRayRefreshTime)
            {
                rayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None);
                controllerRefs = FindObjectsByType<ControllerRef>(FindObjectsSortMode.None);
                nextRayRefreshTime = Time.unscaledTime + 1f;
            }

            if (controllerRefs != null)
            {
                for (int i = 0; i < controllerRefs.Length; i++)
                {
                    ControllerRef controller = controllerRefs[i];
                    if (!IsUsableMetaController(controller) ||
                        controller.Handedness != Handedness.Right ||
                        !controller.TryGetPointerPose(out Pose pose))
                    {
                        continue;
                    }

                    yield return new Ray(pose.position, pose.forward);
                }
            }

            if (rayInteractors != null)
            {
                for (int i = 0; i < rayInteractors.Length; i++)
                {
                    RayInteractor interactor = rayInteractors[i];
                    if (interactor == null || !interactor.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (!LooksLikeRightHandRay(interactor))
                    {
                        continue;
                    }

                    Ray candidate = interactor.Ray;
                    if (candidate.direction.sqrMagnitude > 0.01f)
                    {
                        yield return candidate;
                    }
                }
            }

            if (TryXrNodeRay(XRNode.RightHand, out Ray xrRightRay))
            {
                yield return xrRightRay;
            }

            if (TryRightHandToPanelRay(out Ray rightHandToPanelRay))
            {
                yield return rightHandToPanelRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, out Ray rightRay))
            {
                yield return rightRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.forward, out Ray rightBackRay))
            {
                yield return rightBackRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, Vector3.up, out Ray rightUpRay))
            {
                yield return rightUpRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.up, out Ray rightDownRay))
            {
                yield return rightDownRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, Vector3.right, out Ray rightLocalRightRay))
            {
                yield return rightLocalRightRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.right, out Ray rightLocalLeftRay))
            {
                yield return rightLocalLeftRay;
            }

            if (Camera.main != null)
            {
                Transform cameraTransform = Camera.main.transform;
                yield return new Ray(cameraTransform.position, cameraTransform.forward);
            }
        }

        private IEnumerable<Ray> RightControllerRays()
        {
            if (TryXrNodeRay(XRNode.RightHand, out Ray xrRightRay))
            {
                yield return xrRightRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, out Ray rightRay))
            {
                yield return rightRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.forward, out Ray rightBackRay))
            {
                yield return rightBackRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, Vector3.up, out Ray rightUpRay))
            {
                yield return rightUpRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.up, out Ray rightDownRay))
            {
                yield return rightDownRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, Vector3.right, out Ray rightLocalRightRay))
            {
                yield return rightLocalRightRay;
            }

            if (TryOvrRay(OVRInput.Controller.RTouch, -Vector3.right, out Ray rightLocalLeftRay))
            {
                yield return rightLocalLeftRay;
            }

            if (TryRightHandToPanelRay(out Ray rightHandToPanelRay))
            {
                yield return rightHandToPanelRay;
            }
        }

        private bool TryRightHandToPanelRay(out Ray ray)
        {
            Vector3 handPosition;
            if (TryRightHandPose(out handPosition, out _))
            {
                return TryBuildRayFromPointToPanel(handPosition, out ray);
            }

            ray = default;
            return false;
        }

        private bool TryBuildRayFromPointToPanel(Vector3 origin, out Ray ray)
        {
            Bounds bounds = dragBar != null ? dragBar.bounds : new Bounds(targetRoot != null ? targetRoot.position : origin + Vector3.forward, Vector3.one * 0.2f);
            EncapsulateColliderBounds(ref bounds, startButton);
            EncapsulateColliderBounds(ref bounds, stopButton);
            Vector3 direction = bounds.center - origin;
            if (direction.sqrMagnitude < 0.0001f)
            {
                ray = default;
                return false;
            }

            ray = new Ray(origin, direction.normalized);
            return true;
        }

        private static bool LooksLikeRightHandRay(RayInteractor interactor)
        {
            if (interactor == null)
            {
                return false;
            }

            Transform current = interactor.transform;
            while (current != null)
            {
                string name = current.name;
                if (name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LTouch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LeftHand", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                if (name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("RTouch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("RightHand", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return true;
        }

        private bool TryOvrRay(OVRInput.Controller controller, out Ray ray)
        {
            return TryOvrRay(controller, Vector3.forward, out ray);
        }

        private bool TryOvrRay(OVRInput.Controller controller, Vector3 localDirection, out Ray ray)
        {
            if ((OVRInput.GetConnectedControllers() & controller) == 0)
            {
                ray = default;
                return false;
            }

            Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
            Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);
            Vector3 position = trackingSpace != null ? trackingSpace.TransformPoint(localPosition) : localPosition;
            Quaternion rotation = trackingSpace != null ? trackingSpace.rotation * localRotation : localRotation;
            ray = new Ray(position, rotation * localDirection.normalized);
            return true;
        }

        private bool TryXrNodeRay(XRNode node, out Ray ray)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                ray = default;
                return false;
            }

            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                ray = default;
                return false;
            }

            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
                rotation = trackingSpace.rotation * rotation;
            }

            ray = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        private bool TryRightHandPose(out Vector3 position, out Quaternion rotation)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out position) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
            {
                if (trackingSpace != null)
                {
                    position = trackingSpace.TransformPoint(position);
                    rotation = trackingSpace.rotation * rotation;
                }

                return true;
            }

            if ((OVRInput.GetConnectedControllers() & OVRInput.Controller.RTouch) != 0)
            {
                Vector3 localPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
                Quaternion localRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
                position = trackingSpace != null ? trackingSpace.TransformPoint(localPosition) : localPosition;
                rotation = trackingSpace != null ? trackingSpace.rotation * localRotation : localRotation;
                return true;
            }

            position = default;
            rotation = default;
            return false;
        }

        private bool TryXrNodePosition(XRNode node, out Vector3 position)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                position = default;
                return false;
            }

            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out position))
            {
                return false;
            }

            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
            }

            return true;
        }

        private bool SelectHeld()
        {
            if (ControllerSelectHeld(OVRInput.Controller.RTouch))
            {
                lastSelectSource = "OVRInput:RTouchTrigger";
                return true;
            }

            if (MetaControllerSelectHeld())
            {
                lastSelectSource = "MetaController:RightTrigger";
                return true;
            }

            if (XrControllerSelectHeld(XRNode.RightHand))
            {
                lastSelectSource = "XRNode:RightTrigger";
                return true;
            }

            lastSelectSource = "none";
            return false;
        }

        private bool SelectDown(bool selectHeld)
        {
            bool down = ControllerSelectDown(OVRInput.Controller.RTouch) ||
                        selectHeld && !wasSelectHeld;
            wasSelectHeld = selectHeld;
            return down;
        }

        private bool ControllerSelectHeld(OVRInput.Controller controller)
        {
            bool rawRight = controller == OVRInput.Controller.RTouch &&
                            (OVRInput.Get(OVRInput.RawButton.RIndexTrigger) ||
                             OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger) > controllerTriggerThreshold);
            return rawRight ||
                   OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller) ||
                   OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > controllerTriggerThreshold;
        }

        private static bool ControllerSelectDown(OVRInput.Controller controller)
        {
            bool rawRight = controller == OVRInput.Controller.RTouch &&
                            OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger);
            return rawRight ||
                   OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
        }

        private bool XrControllerSelectHeld(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                return false;
            }

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton) && triggerButton)
            {
                return true;
            }

            if (device.TryGetFeatureValue(CommonUsages.trigger, out float trigger) && trigger > controllerTriggerThreshold)
            {
                return true;
            }

            return false;
        }

        private bool MetaControllerSelectHeld()
        {
            if (controllerRefs == null || controllerRefs.Length == 0 || Time.unscaledTime >= nextRayRefreshTime)
            {
                controllerRefs = FindObjectsByType<ControllerRef>(FindObjectsSortMode.None);
            }

            if (controllerRefs == null)
            {
                return false;
            }

            for (int i = 0; i < controllerRefs.Length; i++)
            {
                ControllerRef controller = controllerRefs[i];
                if (!IsUsableMetaController(controller) ||
                    controller.Handedness != Handedness.Right)
                {
                    continue;
                }

                ControllerInput input = controller.ControllerInput;
                if (controller.IsButtonUsageAnyActive(ControllerButtonUsage.TriggerButton) ||
                    input.Trigger > controllerTriggerThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableMetaController(ControllerRef controller)
        {
            return controller != null &&
                   controller.isActiveAndEnabled &&
                   controller.IsConnected;
        }

        private static float FloatOrInfinity(float value)
        {
            return float.IsFinite(value) ? value : float.PositiveInfinity;
        }

    }
}
