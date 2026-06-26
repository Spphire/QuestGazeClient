using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EyeTracking
{
    public sealed class PaperTrackerOscDebugRenderFeature : ScriptableRendererFeature
    {
        private const string ShaderName = "Hidden/EyeTracking/PaperTrackerOscDebugDot";

        [SerializeField] private Shader debugDotShader;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        private DebugDotPass pass;
        private Material material;
        private bool loggedMissingShader;

        public override void Create()
        {
            if (debugDotShader == null)
            {
                debugDotShader = Shader.Find(ShaderName);
            }

            CoreUtils.Destroy(material);
            material = debugDotShader != null ? CoreUtils.CreateEngineMaterial(debugDotShader) : null;

            pass = new DebugDotPass("PaperTracker OSC Debug Dot");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
            {
                return;
            }

            if (material == null || pass == null)
            {
                if (!loggedMissingShader)
                {
                    Debug.LogWarning("PaperTracker OSC debug render feature is missing shader " + ShaderName + ".");
                    loggedMissingShader = true;
                }

                return;
            }

            if (!PaperTrackerOscReceiver.TryGetScreenDebugDot(renderingData.cameraData.camera, out PaperTrackerOscReceiver.ScreenDebugDot dot))
            {
                return;
            }

            pass.renderPassEvent = injectionPoint;
            pass.ConfigureInput(ScriptableRenderPassInput.None);
            pass.Setup(material, dot);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class DebugDotPass : ScriptableRenderPass
        {
            private static readonly int DotUvRadiusId = Shader.PropertyToID("_PaperTrackerDotUvRadius");
            private static readonly int DotColorId = Shader.PropertyToID("_PaperTrackerDotColor");
            private static readonly int TargetSizeId = Shader.PropertyToID("_PaperTrackerTargetSize");
            private static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();

            private Material material;
            private PaperTrackerOscReceiver.ScreenDebugDot dot;

            public DebugDotPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
            }

            public void Setup(Material passMaterial, PaperTrackerOscReceiver.ScreenDebugDot screenDot)
            {
                material = passMaterial;
                dot = screenDot;
            }

            [System.Obsolete("This path is used when URP Render Graph compatibility mode is enabled.", false)]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ResetTarget();
            }

            [System.Obsolete("This path is used when URP Render Graph compatibility mode is enabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (material == null)
                {
                    return;
                }

                Camera camera = renderingData.cameraData.camera;
                int width = renderingData.cameraData.cameraTargetDescriptor.width;
                int height = renderingData.cameraData.cameraTargetDescriptor.height;
                if (width <= 0 || height <= 0)
                {
                    width = camera != null ? camera.pixelWidth : 1;
                    height = camera != null ? camera.pixelHeight : 1;
                }

                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                    RasterCommandBuffer rasterCommandBuffer = CommandBufferHelpers.GetRasterCommandBuffer(cmd);
                    ExecuteDraw(rasterCommandBuffer, CreatePassData(width, height));
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null)
                {
                    return;
                }

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid())
                {
                    return;
                }

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                int width = cameraData.cameraTargetDescriptor.width;
                int height = cameraData.cameraTargetDescriptor.height;
                if (width <= 0 || height <= 0)
                {
                    width = cameraData.camera != null ? cameraData.camera.pixelWidth : 1;
                    height = cameraData.camera != null ? cameraData.camera.pixelHeight : 1;
                }

                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("PaperTracker OSC Debug Dot", out PassData passData, profilingSampler))
                {
                    FillPassData(passData, width, height);

                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecuteDraw(context.cmd, data));
                }
            }

            private PassData CreatePassData(int width, int height)
            {
                PassData passData = new PassData();
                FillPassData(passData, width, height);
                return passData;
            }

            private void FillPassData(PassData passData, int width, int height)
            {
                passData.Material = material;
                passData.DotUvRadius = new Vector4(
                    dot.ViewportPosition.x,
                    dot.ViewportPosition.y,
                    dot.DiameterPixels * 0.5f,
                    0f
                );
                passData.DotColor = dot.Color;
                passData.TargetSize = new Vector4(width, height, 1f / width, 1f / height);
            }

            private static void ExecuteDraw(RasterCommandBuffer cmd, PassData data)
            {
                SharedPropertyBlock.Clear();
                SharedPropertyBlock.SetVector(DotUvRadiusId, data.DotUvRadius);
                SharedPropertyBlock.SetColor(DotColorId, data.DotColor);
                SharedPropertyBlock.SetVector(TargetSizeId, data.TargetSize);

                cmd.DrawProcedural(
                    Matrix4x4.identity,
                    data.Material,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1,
                    SharedPropertyBlock
                );
            }

            private sealed class PassData
            {
                public Material Material;
                public Vector4 DotUvRadius;
                public Color DotColor;
                public Vector4 TargetSize;
            }
        }
    }
}
