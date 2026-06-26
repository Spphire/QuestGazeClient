using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace EyeTracking.UI
{
    [ExecuteAlways]
    public class RecorderPanelVisibilityController : MonoBehaviour
    {
        [SerializeField] private Transform rootTransform;
        [SerializeField] private List<CanvasGroup> showHideList = new List<CanvasGroup>();
        [SerializeField] private bool show = true;
        [SerializeField] private bool keepMeshRenderersHidden = true;

        public Transform RootTransform
        {
            get => rootTransform;
            set => rootTransform = value;
        }

        public List<CanvasGroup> ShowHideList => showHideList;
        public bool IsShowing => show;

        private void Update()
        {
            if (showHideList.Count == 0 || showHideList[0] == null)
            {
                return;
            }

            if (show != showHideList[0].interactable)
            {
                show = !show;
                ShowHide();
            }
        }

        public void ShowHide()
        {
            SetVisible(!show);
        }

        public void SetVisible(bool visible)
        {
            show = visible;

            foreach (CanvasGroup canvasGroup in showHideList)
            {
                if (canvasGroup == null)
                {
                    continue;
                }

                canvasGroup.alpha = show ? 1f : 0f;
                canvasGroup.interactable = show;
                canvasGroup.blocksRaycasts = show;

                RayInteractable rayInteractable = canvasGroup.GetComponentInChildren<RayInteractable>(true);
                if (rayInteractable != null)
                {
                    rayInteractable.enabled = show;
                    if (rayInteractable.transform.childCount > 0)
                    {
                        rayInteractable.transform.GetChild(0).gameObject.SetActive(show);
                    }
                }

                MeshRenderer[] meshRenderers = canvasGroup.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    if (meshRenderers[i] != null)
                    {
                        meshRenderers[i].enabled = !keepMeshRenderersHidden && show;
                    }
                }
            }
        }
    }
}
