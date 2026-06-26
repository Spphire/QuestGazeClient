using Anaglyph.XRTemplate;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace EyeTracking
{
    public sealed class BoxVolumeAuthoringTool : MonoBehaviour
    {
        private enum Step
        {
            Idle,
            Line,
            Face,
            Height
        }

        [Header("Ray")]
        [SerializeField] private RayInteractor preferredRay;
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private LayerMask placementMask = ~0;
        [SerializeField] private bool useEnvironmentMapper = true;
        [SerializeField] private float maxRayDistance = 3f;
        [SerializeField] private float fallbackDistance = 0.1f;
        [SerializeField, Range(0f, 1f)] private float controllerTriggerThreshold = 0.72f;
        [SerializeField, Range(0f, 1f)] private float handPinchThreshold = 0.72f;
        [SerializeField] private bool requireRightAAndBWithTrigger = true;

        [Header("Box")]
        [SerializeField] private Transform volumeParent;
        [SerializeField] private bool finalColliderIsTrigger;
        [SerializeField] private float minSize = 0.05f;
        [SerializeField] private float previewThickness = 0.015f;
        [SerializeField] private float wireWidth = 0.008f;
        [SerializeField] private Color previewColor = new Color(0.2f, 0.85f, 1f, 0.22f);
        [SerializeField] private Color wireColor = new Color(0.05f, 0.95f, 1f, 0.95f);

        [Header("Construction Visuals")]
        [SerializeField] private float pointSize = 0.045f;
        [SerializeField] private float constructionLineWidth = 0.012f;
        [SerializeField] private float faceThickness = 0.006f;
        [SerializeField] private Color pointColor = new Color(1f, 0.88f, 0.12f, 1f);
        [SerializeField] private Color lineColor = new Color(1f, 0.95f, 0.2f, 1f);
        [SerializeField] private Color faceColor = new Color(0.2f, 0.85f, 1f, 0.16f);

        [Header("Diagnostics")]
        [SerializeField] private bool logAuthoringDiagnostics = true;
        [SerializeField] private float diagnosticLogInterval = 0.25f;

        public UnityEvent<GameObject> WhenVolumeCompleted = new UnityEvent<GameObject>();

        private Step step;
        private bool confirmHeld;
        private bool cancelHeld;

        private Vector3 point0;
        private Vector3 point1;
        private Vector3 axisX = Vector3.forward;
        private Vector3 axisZ = Vector3.right;
        private float length;
        private float signedWidth;
        private float height;

        private GameObject activeBox;
        private BoxCollider activeCollider;
        private Renderer activeRenderer;
        private LineRenderer[] edges;
        private Material previewMaterial;
        private Material wireMaterial;
        private Material pointMaterial;
        private Material lineMaterial;
        private Material faceMaterial;
        private GameObject pointMarker;
        private LineRenderer constructionLine;
        private GameObject facePreview;
        private RayInteractor[] cachedRays;
        private ControllerRef[] cachedControllers;
        private OVRPlugin.HandState leftHandState;
        private OVRPlugin.HandState rightHandState;
        private int completedCount;
        private bool lastDiagnosticHeld;
        private bool lastDiagnosticHasRay;
        private Step lastDiagnosticStep = (Step)(-1);
        private float nextDiagnosticLogTime;
        private string lastPointerSource = "None";
        private string lastConfirmSource = "None";

        private static readonly int[][] EdgeIndices =
        {
            new[] { 0, 1 }, new[] { 1, 3 }, new[] { 3, 2 }, new[] { 2, 0 },
            new[] { 4, 5 }, new[] { 5, 7 }, new[] { 7, 6 }, new[] { 6, 4 },
            new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
        };

        private void Awake()
        {
            AutoWireSceneReferences();
            previewMaterial = CreateMaterial(previewColor);
            wireMaterial = CreateMaterial(wireColor);
            pointMaterial = CreateMaterial(pointColor);
            lineMaterial = CreateMaterial(lineColor);
            faceMaterial = CreateMaterial(faceColor);
        }

        private void LateUpdate()
        {
            bool held = ConfirmHeld();
            bool cancel = CancelHeld();
            bool cancelPressed = cancel && !cancelHeld;
            cancelHeld = cancel;

            if (cancelPressed)
            {
                LogDiagnostic("cancel pressed");
                CancelActiveVolume();
                return;
            }

            if (!TryPointerRay(out Ray ray))
            {
                LogDiagnosticFrame(held, held && !confirmHeld, false, default);
                if (!held)
                {
                    confirmHeld = false;
                }

                return;
            }

            bool pressed = held && !confirmHeld;
            LogDiagnosticFrame(held, pressed, true, ray);
            confirmHeld = held;

            switch (step)
            {
                case Step.Idle:
                    if (pressed)
                    {
                        BeginLine(ray);
                    }
                    break;
                case Step.Line:
                    UpdateLine(ray);
                    if (pressed)
                    {
                        EndLine();
                    }
                    break;
                case Step.Face:
                    UpdateFace(ray);
                    if (pressed)
                    {
                        EndFace();
                    }
                    break;
                case Step.Height:
                    UpdateHeight(ray);
                    if (pressed)
                    {
                        EndHeight();
                    }
                    break;
            }
        }

        private void BeginLine(Ray ray)
        {
            LogDiagnostic($"BeginLine raySource={lastPointerSource}");
            if (!TrySurfacePoint(ray, out point0))
            {
                LogDiagnostic("BeginLine failed: TrySurfacePoint returned false");
                return;
            }

            EnsureActiveBox();
            EnsureConstructionVisuals();
            point1 = point0;
            length = minSize;
            signedWidth = 0f;
            height = previewThickness;
            step = Step.Line;
            UpdatePointVisual();
            UpdateLineVisual(point0, point0);
            UpdateFaceVisual(false);
            ApplyBox(axisX, length, 0f, previewThickness);
            LogDiagnostic($"Line started point0={point0:F3}");
        }

        private void UpdateLine(Ray ray)
        {
            Vector3 point = PointOnHorizontalPlane(ray, point0.y);
            Vector3 delta = Vector3.ProjectOnPlane(point - point0, Vector3.up);
            if (delta.sqrMagnitude >= 0.0001f)
            {
                axisX = delta.normalized;
                axisZ = Vector3.Cross(Vector3.up, axisX).normalized;
                length = Mathf.Max(delta.magnitude, minSize);
                point1 = point0 + axisX * length;
            }

            UpdateLineVisual(point0, point1);
            UpdateFaceVisual(false);
            ApplyBox(axisX, length, 0f, previewThickness);
        }

        private void EndLine()
        {
            if (length < minSize * 1.1f)
            {
                LogDiagnostic($"EndLine canceled: length={length:F3}");
                CancelActiveVolume();
                return;
            }

            step = Step.Face;
            LogDiagnostic("Line accepted; entering Face step");
        }

        private void UpdateFace(Ray ray)
        {
            Vector3 point = PointOnHorizontalPlane(ray, point0.y);
            signedWidth = Vector3.Dot(point - point0, axisZ);
            UpdateLineVisual(point0, point1);
            UpdateFaceVisual(Mathf.Abs(signedWidth) >= previewThickness);
            ApplyBox(axisX, length, signedWidth, previewThickness);
        }

        private void EndFace()
        {
            if (Mathf.Abs(signedWidth) < minSize)
            {
                signedWidth = 0f;
                UpdateFaceVisual(false);
                ApplyBox(axisX, length, signedWidth, previewThickness);
                LogDiagnostic($"Face width too small: width={signedWidth:F3}");
                return;
            }

            UpdateFaceVisual(true);
            step = Step.Height;
            LogDiagnostic("Face accepted; entering Height step");
        }

        private void UpdateHeight(Ray ray)
        {
            Vector3 baseCenter = point0 + axisX * (length * 0.5f) + axisZ * (signedWidth * 0.5f);
            Vector3 normal = Vector3.zero;

            if (viewCamera != null)
            {
                normal = Vector3.ProjectOnPlane(viewCamera.transform.position - baseCenter, Vector3.up);
            }

            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = Vector3.ProjectOnPlane(-ray.direction, Vector3.up);
            }

            normal = normal.sqrMagnitude < 0.0001f ? axisZ : normal.normalized;
            Plane plane = new Plane(normal, baseCenter);

            if (plane.Raycast(ray, out float enter) && enter > 0f)
            {
                height = Mathf.Max(ray.GetPoint(enter).y - point0.y, minSize);
            }
            else
            {
                height = Mathf.Max(height, minSize);
            }

            UpdateLineVisual(point0, point1);
            UpdateFaceVisual(true);
            ApplyBox(axisX, length, signedWidth, height);
        }

        private void EndHeight()
        {
            if (height < minSize)
            {
                return;
            }

            CompleteActiveVolume();
        }

        private void EnsureActiveBox()
        {
            if (activeBox != null)
            {
                return;
            }

            activeBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            activeBox.name = "Authored_BoxVolume_Preview";
            activeBox.transform.SetParent(volumeParent, true);

            activeCollider = activeBox.GetComponent<BoxCollider>();
            activeCollider.enabled = false;

            activeRenderer = activeBox.GetComponent<Renderer>();
            activeRenderer.sharedMaterial = previewMaterial;
            activeBox.SetActive(false);

            edges = new LineRenderer[EdgeIndices.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                GameObject edge = new GameObject("Edge");
                edge.transform.SetParent(activeBox.transform, false);
                LineRenderer line = edge.AddComponent<LineRenderer>();
                line.sharedMaterial = wireMaterial;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = wireWidth;
                line.endWidth = wireWidth;
                edges[i] = line;
            }
        }

        private void EnsureConstructionVisuals()
        {
            bool created = false;

            if (pointMarker == null)
            {
                pointMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pointMarker.name = "Point Preview";
                pointMarker.transform.SetParent(volumeParent, true);
                pointMarker.GetComponent<Renderer>().sharedMaterial = pointMaterial;
                Collider collider = pointMarker.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }

                created = true;
            }

            if (constructionLine == null)
            {
                GameObject lineObject = new GameObject("Line Preview");
                lineObject.transform.SetParent(volumeParent, true);
                constructionLine = lineObject.AddComponent<LineRenderer>();
                constructionLine.sharedMaterial = lineMaterial;
                constructionLine.useWorldSpace = true;
                constructionLine.positionCount = 2;
                constructionLine.startWidth = constructionLineWidth;
                constructionLine.endWidth = constructionLineWidth;
                created = true;
            }

            if (facePreview == null)
            {
                facePreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                facePreview.name = "Face Preview";
                facePreview.transform.SetParent(volumeParent, true);
                facePreview.GetComponent<Renderer>().sharedMaterial = faceMaterial;
                Collider collider = facePreview.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }

                created = true;
            }

            if (created)
            {
                LogDiagnostic("Construction visuals created");
            }
        }

        private void ApplyBox(Vector3 xAxis, float xSize, float zSignedSize, float ySize)
        {
            if (activeBox == null)
            {
                return;
            }

            float safeX = Mathf.Max(Mathf.Abs(xSize), previewThickness);
            float safeZ = Mathf.Max(Mathf.Abs(zSignedSize), previewThickness);
            float safeY = Mathf.Max(ySize, previewThickness);
            Vector3 zAxis = Vector3.Cross(Vector3.up, xAxis).normalized;
            Vector3 center = point0 + xAxis * (safeX * 0.5f) + zAxis * (zSignedSize * 0.5f) + Vector3.up * (safeY * 0.5f);

            activeBox.SetActive(step == Step.Height);
            activeBox.transform.SetPositionAndRotation(center, Quaternion.LookRotation(zAxis, Vector3.up));
            activeBox.transform.localScale = new Vector3(safeX, safeY, safeZ);
            UpdateWireframe();
        }

        private void CompleteActiveVolume()
        {
            if (activeBox == null)
            {
                LogDiagnostic("CompleteActiveVolume called without activeBox");
                ResetState();
                return;
            }

            activeBox.name = $"Authored_BoxVolume_{++completedCount:00}";
            activeBox.SetActive(true);
            activeCollider.enabled = true;
            activeCollider.isTrigger = finalColliderIsTrigger;
            WhenVolumeCompleted?.Invoke(activeBox);

            ClearConstructionVisuals();
            activeBox = null;
            activeCollider = null;
            activeRenderer = null;
            edges = null;
            ResetState();
            LogDiagnostic("Volume completed");
        }

        private void CancelActiveVolume()
        {
            LogDiagnostic("Volume canceled");
            if (activeBox != null)
            {
                Destroy(activeBox);
            }

            ClearConstructionVisuals();
            activeBox = null;
            activeCollider = null;
            activeRenderer = null;
            edges = null;
            ResetState();
        }

        private void ResetState()
        {
            step = Step.Idle;
            length = 0f;
            signedWidth = 0f;
            height = 0f;
        }

        private void LogDiagnosticFrame(bool held, bool pressed, bool hasRay, Ray ray)
        {
            if (!logAuthoringDiagnostics)
            {
                return;
            }

            bool shouldLog = pressed ||
                             held != lastDiagnosticHeld ||
                             hasRay != lastDiagnosticHasRay ||
                             step != lastDiagnosticStep ||
                             (held && Time.unscaledTime >= nextDiagnosticLogTime);
            if (!shouldLog)
            {
                return;
            }

            string rayText = hasRay
                ? $"rayOrigin={ray.origin:F3} rayDir={ray.direction:F3}"
                : "ray=missing";
            LogDiagnostic($"step={step} held={held} pressed={pressed} input={lastConfirmSource} raySource={lastPointerSource} {rayText}");

            lastDiagnosticHeld = held;
            lastDiagnosticHasRay = hasRay;
            lastDiagnosticStep = step;
            nextDiagnosticLogTime = Time.unscaledTime + Mathf.Max(0.05f, diagnosticLogInterval);
        }

        private void LogDiagnostic(string message)
        {
            if (!logAuthoringDiagnostics)
            {
                return;
            }

            Debug.Log($"[BoxVolumeAuthoring] {message}", this);
        }

        private void UpdatePointVisual()
        {
            if (pointMarker == null)
            {
                return;
            }

            pointMarker.SetActive(true);
            pointMarker.transform.position = point0;
            pointMarker.transform.localScale = Vector3.one * pointSize;
        }

        private void UpdateLineVisual(Vector3 start, Vector3 end)
        {
            if (constructionLine == null)
            {
                return;
            }

            constructionLine.gameObject.SetActive(true);
            constructionLine.startWidth = constructionLineWidth;
            constructionLine.endWidth = constructionLineWidth;
            constructionLine.SetPosition(0, start);
            constructionLine.SetPosition(1, end);
        }

        private void UpdateFaceVisual(bool visible)
        {
            if (facePreview == null)
            {
                return;
            }

            facePreview.SetActive(visible);
            if (!visible)
            {
                return;
            }

            float safeX = Mathf.Max(length, previewThickness);
            float safeZ = Mathf.Max(Mathf.Abs(signedWidth), previewThickness);
            Vector3 center = point0 + axisX * (safeX * 0.5f) + axisZ * (signedWidth * 0.5f) + Vector3.up * (faceThickness * 0.5f);
            facePreview.transform.SetPositionAndRotation(center, Quaternion.LookRotation(axisZ, Vector3.up));
            facePreview.transform.localScale = new Vector3(safeX, faceThickness, safeZ);
        }

        private void ClearConstructionVisuals()
        {
            if (pointMarker != null)
            {
                Destroy(pointMarker);
            }

            if (constructionLine != null)
            {
                Destroy(constructionLine.gameObject);
            }

            if (facePreview != null)
            {
                Destroy(facePreview);
            }

            pointMarker = null;
            constructionLine = null;
            facePreview = null;
        }

        private bool TrySurfacePoint(Ray ray, out Vector3 point)
        {
            if (useEnvironmentMapper &&
                EnvironmentMapper.Raycast(ray, maxRayDistance, out EnvironmentMapper.RayResult envHit, EnvironmentMapper.RaycastMode.Negative))
            {
                point = envHit.point;
                return true;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, placementMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }

            point = ray.GetPoint(fallbackDistance);
            return true;
        }

        private Vector3 PointOnHorizontalPlane(Ray ray, float y)
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            if (plane.Raycast(ray, out float enter) && enter > 0f)
            {
                return ray.GetPoint(enter);
            }

            Vector3 point = ray.GetPoint(fallbackDistance);
            point.y = y;
            return point;
        }

        private bool TryPointerRay(out Ray ray)
        {
            if (requireRightAAndBWithTrigger)
            {
                if (RightControllerConfirmComboHeld() && TryTriggeredMetaControllerPointerRay(out ray))
                {
                    return true;
                }

                if (RightControllerConfirmComboHeld() && TryOvrControllerRay(OVRInput.Controller.RTouch, out ray))
                {
                    lastPointerSource = "OVRInput:RTouch:A+B+Trigger";
                    return true;
                }

                ray = default;
                lastPointerSource = "None";
                return false;
            }

            if (preferredRay != null && preferredRay.isActiveAndEnabled && preferredRay.Ray.direction.sqrMagnitude > 0.01f)
            {
                ray = preferredRay.Ray;
                lastPointerSource = $"PreferredRay:{preferredRay.name}";
                return true;
            }

            if (RightControllerConfirmComboHeld() && TryMetaControllerPointerRay(Handedness.Right, out ray))
            {
                return true;
            }

            if (cachedRays == null || cachedRays.Length == 0)
            {
                cachedRays = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None);
            }

            RayInteractor rightRay = FindNamedRay("right");
            if (rightRay != null)
            {
                ray = rightRay.Ray;
                lastPointerSource = $"RayInteractor:{rightRay.name}";
                return true;
            }

            RayInteractor leftRay = FindNamedRay("left");
            if (leftRay != null)
            {
                ray = leftRay.Ray;
                lastPointerSource = $"RayInteractor:{leftRay.name}";
                return true;
            }

            for (int i = 0; i < cachedRays.Length; i++)
            {
                RayInteractor interactor = cachedRays[i];
                if (interactor != null && interactor.isActiveAndEnabled && interactor.Ray.direction.sqrMagnitude > 0.01f)
                {
                    ray = interactor.Ray;
                    lastPointerSource = $"RayInteractor:{interactor.name}";
                    return true;
                }
            }

            if (TryMetaControllerPointerRay(out ray))
            {
                return true;
            }

            if (TryOvrControllerRay(OVRInput.Controller.RTouch, out ray))
            {
                lastPointerSource = "OVRInput:RTouch";
                return true;
            }

            if (TryOvrControllerRay(OVRInput.Controller.LTouch, out ray))
            {
                lastPointerSource = "OVRInput:LTouch";
                return true;
            }

            if (TryOvrHandPointerRay(OVRPlugin.Hand.HandRight, ref rightHandState, out ray))
            {
                lastPointerSource = "OVRPlugin:RightHandPointer";
                return true;
            }

            if (TryOvrHandPointerRay(OVRPlugin.Hand.HandLeft, ref leftHandState, out ray))
            {
                lastPointerSource = "OVRPlugin:LeftHandPointer";
                return true;
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera != null)
            {
                ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
                lastPointerSource = $"Camera:{viewCamera.name}";
                return true;
            }

            ray = default;
            lastPointerSource = "None";
            return false;
        }

        private RayInteractor FindNamedRay(string nameFragment)
        {
            for (int i = 0; i < cachedRays.Length; i++)
            {
                RayInteractor interactor = cachedRays[i];
                if (interactor == null ||
                    !interactor.isActiveAndEnabled ||
                    interactor.Ray.direction.sqrMagnitude <= 0.01f)
                {
                    continue;
                }

                if (interactor.name.ToLowerInvariant().Contains(nameFragment) ||
                    interactor.transform.root.name.ToLowerInvariant().Contains(nameFragment))
                {
                    return interactor;
                }
            }

            return null;
        }

        private bool TryMetaControllerPointerRay(out Ray ray)
        {
            RefreshMetaControllerCache();

            if (requireRightAAndBWithTrigger)
            {
                return TryTriggeredMetaControllerPointerRay(out ray);
            }

            if (TryMetaControllerPointerRay(Handedness.Right, out ray) ||
                TryMetaControllerPointerRay(Handedness.Left, out ray))
            {
                return true;
            }

            ray = default;
            return false;
        }

        private bool TryTriggeredMetaControllerPointerRay(out Ray ray)
        {
            RefreshMetaControllerCache();

            for (int i = 0; i < cachedControllers.Length; i++)
            {
                ControllerRef controller = cachedControllers[i];
                if (!IsUsableMetaController(controller) ||
                    controller.Handedness != Handedness.Right ||
                    !RightControllerConfirmComboHeld() ||
                    !controller.TryGetPointerPose(out Pose pose))
                {
                    continue;
                }

                ray = new Ray(pose.position, pose.forward);
                lastPointerSource = $"MetaController:{controller.Handedness}:A+B+Trigger";
                return true;
            }

            ray = default;
            return false;
        }

        private bool TryMetaControllerPointerRay(Handedness handedness, out Ray ray)
        {
            for (int i = 0; i < cachedControllers.Length; i++)
            {
                ControllerRef controller = cachedControllers[i];
                if (!IsUsableMetaController(controller) ||
                    controller.Handedness != handedness ||
                    !controller.TryGetPointerPose(out Pose pose))
                {
                    continue;
                }

                ray = new Ray(pose.position, pose.forward);
                lastPointerSource = $"MetaController:{handedness}";
                return true;
            }

            ray = default;
            return false;
        }

        private bool TryOvrControllerRay(OVRInput.Controller controller, out Ray ray)
        {
            if ((OVRInput.GetConnectedControllers() & controller) == 0)
            {
                ray = default;
                return false;
            }

            Transform space = TrackingSpace;
            Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
            Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);
            Vector3 position = space != null ? space.TransformPoint(localPosition) : localPosition;
            Quaternion rotation = space != null ? space.rotation * localRotation : localRotation;

            ray = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        private bool TryOvrHandPointerRay(OVRPlugin.Hand hand, ref OVRPlugin.HandState state, out Ray ray)
        {
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state) || !IsHandUsable(state))
            {
                ray = default;
                return false;
            }

            Vector3 localPosition = new Vector3(
                state.PointerPose.Position.x,
                state.PointerPose.Position.y,
                -state.PointerPose.Position.z);
            Quaternion localRotation = new Quaternion(
                -state.PointerPose.Orientation.x,
                -state.PointerPose.Orientation.y,
                state.PointerPose.Orientation.z,
                state.PointerPose.Orientation.w);

            Transform space = TrackingSpace;
            Vector3 position = space != null ? space.TransformPoint(localPosition) : localPosition;
            Quaternion rotation = space != null ? space.rotation * localRotation : localRotation;

            ray = new Ray(position, rotation * Vector3.forward);
            return true;
        }

        private Transform TrackingSpace
        {
            get
            {
                if (trackingSpace == null)
                {
                    OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                    if (rig != null)
                    {
                        trackingSpace = rig.trackingSpace;
                    }
                }

                return trackingSpace;
            }
        }

        private void AutoWireSceneReferences()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            _ = TrackingSpace;
        }

        private bool ConfirmHeld()
        {
            if (requireRightAAndBWithTrigger)
            {
                if (RightControllerConfirmComboHeld())
                {
                    lastConfirmSource = "OVRInput:RightA+B+Trigger";
                    return true;
                }

                lastConfirmSource = "None";
                return false;
            }

            if (MouseConfirmHeld())
            {
                return true;
            }

            if (SpaceConfirmHeld())
            {
                return true;
            }

            if (RightControllerConfirmComboHeld())
            {
                lastConfirmSource = requireRightAAndBWithTrigger
                    ? "OVRInput:RightA+B+Trigger"
                    : "OVRInput:RightTrigger";
                return true;
            }

            if (MetaControllerTriggerHeld())
            {
                lastConfirmSource = "MetaController:Trigger";
                return true;
            }

            if (HandPinchHeld(OVRPlugin.Hand.HandRight, ref rightHandState))
            {
                lastConfirmSource = "OVRPlugin:RightHandPinch";
                return true;
            }

            if (HandPinchHeld(OVRPlugin.Hand.HandLeft, ref leftHandState))
            {
                lastConfirmSource = "OVRPlugin:LeftHandPinch";
                return true;
            }

            lastConfirmSource = "None";
            return false;
        }

        private bool MouseConfirmHeld()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                lastConfirmSource = "Mouse";
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(0))
            {
                lastConfirmSource = "Mouse";
                return true;
            }
#endif

            return false;
        }

        private bool SpaceConfirmHeld()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.isPressed)
            {
                lastConfirmSource = "Keyboard:Space";
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.Space))
            {
                lastConfirmSource = "Keyboard:Space";
                return true;
            }
#endif

            return false;
        }

        private bool MetaControllerTriggerHeld()
        {
            RefreshMetaControllerCache();

            for (int i = 0; i < cachedControllers.Length; i++)
            {
                ControllerRef controller = cachedControllers[i];
                if (IsUsableMetaController(controller) && IsMetaControllerTriggerHeld(controller))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshMetaControllerCache()
        {
            if (cachedControllers == null || cachedControllers.Length == 0)
            {
                cachedControllers = FindObjectsByType<ControllerRef>(FindObjectsSortMode.None);
            }
        }

        private bool IsMetaControllerTriggerHeld(ControllerRef controller)
        {
            ControllerInput input = controller.ControllerInput;
            return controller.IsButtonUsageAnyActive(ControllerButtonUsage.TriggerButton) ||
                   input.Trigger > controllerTriggerThreshold;
        }

        private static bool IsUsableMetaController(ControllerRef controller)
        {
            return controller != null &&
                   controller.isActiveAndEnabled &&
                   controller.IsConnected;
        }

        private bool RightControllerConfirmComboHeld()
        {
            if (!RightControllerTriggerHeld())
            {
                return false;
            }

            if (!requireRightAAndBWithTrigger)
            {
                return true;
            }

            return RightAButtonHeld() && RightBButtonHeld();
        }

        private bool RightControllerTriggerHeld()
        {
            return OVRInput.Get(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger, OVRInput.Controller.RTouch) > controllerTriggerThreshold ||
                   OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) > controllerTriggerThreshold;
        }

        private static bool RightAButtonHeld()
        {
            return OVRInput.Get(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        }

        private static bool RightBButtonHeld()
        {
            return OVRInput.Get(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                   OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        }

        private bool HandPinchHeld(OVRPlugin.Hand hand, ref OVRPlugin.HandState state)
        {
            return OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state) &&
                   IsHandUsable(state) &&
                   IndexPinch(state) >= handPinchThreshold;
        }

        private static bool IsHandUsable(OVRPlugin.HandState state)
        {
            OVRPlugin.HandStatus status = state.Status;
            return (status & OVRPlugin.HandStatus.HandTracked) != 0 &&
                   (status & OVRPlugin.HandStatus.InputStateValid) != 0 &&
                   (status & OVRPlugin.HandStatus.SystemGestureInProgress) == 0;
        }

        private static float IndexPinch(OVRPlugin.HandState state)
        {
            int index = (int)OVRPlugin.HandFinger.Index;
            if (state.PinchStrength != null && state.PinchStrength.Length > index)
            {
                return state.PinchStrength[index];
            }

            return (state.Pinches & OVRPlugin.HandFingerPinch.Index) == OVRPlugin.HandFingerPinch.Index ? 1f : 0f;
        }

        private static bool CancelHeld()
        {
            return KeyboardEscapeHeld() ||
                   OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch);
        }

        private static bool KeyboardEscapeHeld()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.isPressed)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Escape);
#else
            return false;
#endif
        }

        private void UpdateWireframe()
        {
            if (edges == null)
            {
                return;
            }

            Transform box = activeBox.transform;
            Vector3[] corners =
            {
                box.TransformPoint(new Vector3(-0.5f, -0.5f, -0.5f)),
                box.TransformPoint(new Vector3( 0.5f, -0.5f, -0.5f)),
                box.TransformPoint(new Vector3(-0.5f, -0.5f,  0.5f)),
                box.TransformPoint(new Vector3( 0.5f, -0.5f,  0.5f)),
                box.TransformPoint(new Vector3(-0.5f,  0.5f, -0.5f)),
                box.TransformPoint(new Vector3( 0.5f,  0.5f, -0.5f)),
                box.TransformPoint(new Vector3(-0.5f,  0.5f,  0.5f)),
                box.TransformPoint(new Vector3( 0.5f,  0.5f,  0.5f))
            };

            for (int i = 0; i < edges.Length; i++)
            {
                int[] edge = EdgeIndices[i];
                edges[i].SetPosition(0, corners[edge[0]]);
                edges[i].SetPosition(1, corners[edge[1]]);
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            Material material = new Material(shader);
            material.color = color;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }
    }
}
