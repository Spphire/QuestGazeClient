using UnityEngine;
using UnityEngine.EventSystems;

namespace EyeTracking.UI
{
    public sealed class RecorderPanelPointerDragController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Transform targetRoot;
        [SerializeField] private float maxDragDelta = 0.25f;

        private bool dragging;
        private Vector3 lastWorldPoint;

        public void Configure(Transform newTargetRoot)
        {
            targetRoot = newTargetRoot;
        }

        private void Awake()
        {
            if (targetRoot == null)
            {
                targetRoot = FindRecorderPanelRoot() ?? transform;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (targetRoot == null || !TryGetWorldPoint(eventData, out lastWorldPoint))
            {
                dragging = false;
                return;
            }

            dragging = true;
            Debug.Log("[RecorderPanelPointerDragController] Begin dragging recorder panel.", this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || targetRoot == null || !TryGetWorldPoint(eventData, out Vector3 currentWorldPoint))
            {
                return;
            }

            Vector3 delta = currentWorldPoint - lastWorldPoint;
            if (delta.sqrMagnitude > maxDragDelta * maxDragDelta)
            {
                delta = delta.normalized * maxDragDelta;
            }

            targetRoot.position += delta;
            lastWorldPoint = currentWorldPoint;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                Debug.Log("[RecorderPanelPointerDragController] End dragging recorder panel.", this);
            }

            dragging = false;
        }

        private bool TryGetWorldPoint(PointerEventData eventData, out Vector3 worldPoint)
        {
            RaycastResult raycast = eventData.pointerCurrentRaycast;
            if (raycast.isValid && raycast.worldPosition.sqrMagnitude > 0.000001f)
            {
                worldPoint = raycast.worldPosition;
                return true;
            }

            raycast = eventData.pointerPressRaycast;
            if (raycast.isValid && raycast.worldPosition.sqrMagnitude > 0.000001f)
            {
                worldPoint = raycast.worldPosition;
                return true;
            }

            worldPoint = default;
            return false;
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
    }
}
