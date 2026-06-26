// File: ShaderToRGB.hlsl
// 模拟 Blender 中 Shader to RGB 效果（基于白色 Diffuse BSDF）
// 此版本使用 Oren-Nayar 漫反射模型（支持粗糙度）
// ✅ 支持 Unity URP 多光源（主光源 + 附加光源）

#ifndef WUWA_HAIR_INCLUDED
#define WUWA_HAIR_INCLUDED

#include "./WUWAUtils.hlsl"

float HairLightCompute(
    float3 color,
    float threshold,
    float transition,
    float3 normalWS
    )
{
    float v = dot(normalize(normalWS),normalize(GetMainLight().direction))*0.5+0.5;
    float v1 = 1-color.g+saturate(threshold-0.5);
    float v2 = transition*0.02;
    return lerp(0.15,1.0,saturate((v-(v1-v2))/2.0/v2))*color.r;
}

float3 HairColorRefine(float hairValue)
{
    float pos_day[MAX_POINTS] = {0.111, 0.2, 0.3};
    float4 colors_day[MAX_POINTS] = {
        float4(0.502,0.641,0.9,1), float4(1.148,1.033,1.033,1), float4(1.0,1.0,1.0,1)
    };
    float pos_night[MAX_POINTS] = {0.111, 0.2, 0.3};
    float4 colors_night[MAX_POINTS] = {
        float4(0.403,0.364,0.537,1), float4(0.675,0.584,0.61,1), float4(0.7,0.7,0.7,1)
    };
    
    float3 result_night = ColorRamp_BSpline_Dynamic(hairValue, 3, pos_night, colors_night).rgb;
    float3 result_day = ColorRamp_BSpline_Dynamic(hairValue, 3, pos_day, colors_day).rgb;
    return Mix(result_night,result_day,1.0,false,true);
}

float3 OS2VS(float3 vector_in_object_space)
{
    return mul(UNITY_MATRIX_V, mul(unity_ObjectToWorld, float4(vector_in_object_space, 0.0))).xyz;
}

float3 WS2VS(float3 vector_in_world_space)
{
    return mul(UNITY_MATRIX_V, float4(vector_in_world_space, 0.0)).xyz;
}

void Hightlight(
    float3 hightlight,
    float value,
    float3 normalWS,
    Texture2D spa_Mask,
    SamplerState spa_Mask_Sampler,
    out float result,
    out float hightlightValue
    )
{
    hightlightValue = step(0.5,hightlight.r);
    float2 uv = WS2VS(normalWS).xy*float2(0.41,0.5)+float2(0.5,0.5);
    float mask = spa_Mask.Sample(spa_Mask_Sampler, uv)*value;
    result = hightlightValue*mask;
}

float3 WUWAHairShader(
    float4 baseTexture,
    float4 normalValue,
    float4 hmTexture,
    float3 tangentWS,
    float3 bitangentWS,
    float3 normalWS,

    Texture2D spa_Mask,
    SamplerState spa_Mask_Sampler,
    
    float3 vertexPosWS,
    bool isPotLight)
{
    float3 color = HSVmodify(baseTexture.rgb, 0.5,1.2,1.1,1.0);
    color = MultiplyMix(color, step(0.1,hmTexture.g),0.1,false,true);
    
    //float3 normal = NormalMap(float3(normalTexture.rg,1.0),tangentWS,bitangentWS,normalWS,1.0);

    //color
    float hairValue = (step(0.1,hmTexture.g)+hmTexture.b-1.75)+hmTexture.g*0.27;
    
    hairValue = HairLightCompute(hairValue,0.63,1.0, normalWS);//mul(unity_WorldToObject, float4(normalWS, 0.0)).xyz);
    hairValue = HairColorRefine(hairValue);
    
    color = MultiplyMix(hairValue,color,1.0,false,true);
    
    float result;
    float hightlightValue;
    Hightlight(hmTexture,0.2,normalWS,spa_Mask,spa_Mask_Sampler,result,hightlightValue);
    
    return ColorMixAdd(color, result, hightlightValue,true,false);
}


#endif // WUTHERING_WAVES_INCLUDED
