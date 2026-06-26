// File: ShaderToRGB.hlsl
// 模拟 Blender 中 Shader to RGB 效果（基于白色 Diffuse BSDF）
// 此版本使用 Oren-Nayar 漫反射模型（支持粗糙度）
// ✅ 支持 Unity URP 多光源（主光源 + 附加光源）

#ifndef WUWA_HAIR_INCLUDED
#define WUWA_HAIR_INCLUDED

#include "./WUWAUtils.hlsl"


void WUWAFaceSDF(
    Texture2D sdf,
    SamplerState sdf_Sampler,

    float3 forward,
    float3 right,

    float2 uv,
    
    float bias,
    float smooth,

    out float3 result,
    out float biasValue,
    out float sdfAlpha
    )
{
    forward = normalize(forward);
    right = normalize(right);
    float3 headDir = cross(forward,right);
    float3 lightDir = normalize(GetMainLight().direction);
    float3 lightDirZ = normalize(lightDir-project(lightDir,headDir));
    
    uv = float2(sign(dot(lightDirZ,right)),1.0)*uv;
    uv = uv*float2(-1.0,1.0);
    
    sdfAlpha = lerp(sdf.Sample(sdf_Sampler,uv).r,sdf.Sample(sdf_Sampler,uv).a,0.5);

    float v1 = -(dot(lightDirZ,forward)*0.5+0.5)+bias;
    float v2 = 2.0/smooth;

    result = lerp(0.2,2.0,saturate( (sdfAlpha+0.04-(v1-v2))/2.0/v2));
    biasValue = dot(float3(uv,0.0),dot(v1-v2,v1+v2))*0.5+0.5;
}

float bezier2_y(float x, float2 p0, float2 p1, float2 p2)
{
    // 计算 t 的归一化（基于 x）
    float t;
    if (x <= p0.x) return p0.y;
    else if (x >= p2.x) return p2.y;
    else {
        // 将 x 映射为 t ∈ [0, 1]（假设 P0.x < P1.x < P2.x）
        t = (x - p0.x) / (p2.x - p0.x);
    }

    float u = 1.0 - t;
    return u * u * p0.y + 2.0 * u * t * p1.y + t * t * p2.y;
}

float3 applyCombinedBezier(float3 color, float2 p0, float2 p1, float2 p2, float factor, bool clampResult)
{
    float3 mapped;
    mapped.r = bezier2_y(color.r, p0, p1, p2);
    mapped.g = bezier2_y(color.g, p0, p1, p2);
    mapped.b = bezier2_y(color.b, p0, p1, p2);

    float3 result = lerp(color, mapped, factor);
    if (clampResult)
        result = saturate(result);
    return result;
}

float3 WUWAFaceShader(
    float4 baseColor,
    float4 ID,
    Texture2D sdf,
    SamplerState sdf_Sampler,
    float3 forward,
    float3 right,
    float2 uv,
    float bias
    )
{
    ID = 1.0-step(0.2,ID);
    
    float3 lightSide;
    float3 darkSide;
    SkinColorRefine(lightSide,darkSide);

    float3 faceSDFresult;
    float biasValue;
    float sdfAlpha;
    WUWAFaceSDF(sdf,sdf_Sampler,forward,right,uv,bias,37.0,faceSDFresult,biasValue,sdfAlpha);

    return Mix(
        MultiplyMix(
            baseColor,
            Mix(
                darkSide,
                lightSide,
                faceSDFresult,
                false,
                true
                ),
            1.0,
            false,
            true
            ),
        MixColorDodge(
            HSVmodify(baseColor,0.5,1.2,0.7,ID),
            applyCombinedBezier(
                baseColor,
                float2(0.1968,0.3419),
                float2(0.4404,0.6838),
                float2(0.7616,0.8272),
                1.0,
                false),
            faceSDFresult,
            false,
            true
            ),
        ID,
        false,
        true
    );
}

#endif // WUTHERING_WAVES_INCLUDED
