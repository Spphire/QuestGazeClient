using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public class CustomRenderFeature : ScriptableRendererFeature
{
    [NonSerialized] private DrawObjectsPass mDrawOpaqueForward1Pass;

    [NonSerialized] private DrawObjectsPass mDrawTransparentForward1Pass;
    [NonSerialized] private DrawObjectsPass mDrawTransparentOutlinePass;

    public override void Create()
    {
            Debug.Log("WUWARenderFeature created!");
            mDrawOpaqueForward1Pass = new DrawObjectsPass
            (
                profilerTag: "CustomOpaque (1)",
                shaderTagIds: new ShaderTagId[]{
                    new ShaderTagId("OpaqueForward1")
                },
                opaque   : true,
                evt      : RenderPassEvent.AfterRenderingOpaques,
                renderQueueRange :RenderQueueRange.all, 
                layerMask  : -1,
                stencilState : new StencilState() { enabled = false }, 
                stencilReference:-1
            );

            mDrawTransparentForward1Pass = new DrawObjectsPass
            (
                profilerTag: "CustomTransparent (1)",
                shaderTagIds: new ShaderTagId[]{
                    new ShaderTagId("TransparentForward1")
                },
                opaque   : false,
                evt      : RenderPassEvent.AfterRenderingTransparents,
                renderQueueRange :RenderQueueRange.all, 
                layerMask  : -1,
                stencilState : new StencilState() { enabled = false }, 
                stencilReference:-1
            );

            mDrawTransparentOutlinePass = new DrawObjectsPass
            (
                profilerTag: "CustomTransparent (Outline)",
                shaderTagIds: new ShaderTagId[]{
                    new ShaderTagId("TransparentOutline")
                },
                opaque   : false,
                evt      : RenderPassEvent.AfterRenderingTransparents,
                renderQueueRange :RenderQueueRange.all, 
                layerMask  : -1,
                stencilState : new StencilState() { enabled = false }, 
                stencilReference:-1
            );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(mDrawOpaqueForward1Pass);

        renderer.EnqueuePass(mDrawTransparentForward1Pass);
        renderer.EnqueuePass(mDrawTransparentOutlinePass);
    }

}