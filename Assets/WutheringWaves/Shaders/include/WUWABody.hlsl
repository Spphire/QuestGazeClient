// File: ShaderToRGB.hlsl
// 模拟 Blender 中 Shader to RGB 效果（基于白色 Diffuse BSDF）
// 此版本使用 Oren-Nayar 漫反射模型（支持粗糙度）
// ✅ 支持 Unity URP 多光源（主光源 + 附加光源）

#ifndef WUWA_BODY_INCLUDED
#define WUWA_BODY_INCLUDED

#include "./WUWAUtils.hlsl"
#include "./GlossyBSDF.hlsl"

// direction to light
float3 dgCompute(
    float3x3 lightOS2WS,
    float3 lightPosWS,
    float3 vertexPosWS,
    bool isPotLight
    )
{
    if (isPotLight)
    {
        return normalize(lightPosWS-vertexPosWS);
    }
    else
    {
        return normalize(GetMainLight().direction);
        return normalize(mul(lightOS2WS, float3(0.0,0.0,-1.0)));
    }
}

float3 lightCreate(
    float3 inputVector,
    float3x3 lightOS2WS,
    float3 lightPosWS,
    float3 vertexPosWS,
    bool isPotLight)
{
    float3 dg = dgCompute(lightOS2WS,lightPosWS,vertexPosWS,isPotLight);
    float res = dot(dg,normalize(inputVector));
    return res;
}

void WUWABodyShader(
    float4 baseTexture,
    float4 normalTexture,
    float3 maskId,
    float3 tangentWS,
    float3 bitangentWS,
    float3 normalWS,

    float3x3 lightOS2WS,
    float3 lightPosWS,
    float3 vertexPosWS,
    bool isPotLight,
    
    out float3 value,
    out float3 influence)
{
    float3 skin;
    float3 cloth;
    float metal;
    float3 result;
    NormalTextureTransfer(normalTexture,
        tangentWS,
        bitangentWS,
        normalWS,
        skin,
        cloth,
        metal,
        result);
    float3 dg = dgCompute(lightOS2WS,lightPosWS,vertexPosWS,isPotLight);

    //Skin Grey
    float3 skinValue = ColorMixAdd(
        baseTexture.aaa,
        dot(dg,normalize(skin)),
        step(0.1,baseTexture.a),
        true,
        false);
    skinValue = ColorRamp_BSpline_Skin1(skinValue.x);

    //cloth
    float3 clothValue = MultiplyMix(
        step(0.1,baseTexture.a),
        dot(dg,normalize(cloth)),
        1.0,
        false,
        false);

    //skin-cloth color
    float3 skinMask = step(0.84,maskId.x);
    float3 clothMask = 1-skinMask;

    float3 skinColor = HSVmodify(baseTexture,0.5,1.0,1.0,skinMask);
    float3 lightSide;
    float3 darkSide;
    SkinColorRefine(lightSide,darkSide);
    skinColor = MultiplyMix(
        Mix(
            darkSide,
            lightSide,
            skinValue,
            true,
            true
            ),
        skinColor,
        1.0,
        false,
        false);
    float3 clothColor = HSVmodify(baseTexture,0.5,1.1,1.2,clothMask);
    clothColor = MultiplyMix(clothColor,ClothColorRefine(clothValue),clothMask, false,true);

    float3 skinClothValue = Mix(skinColor,clothColor,clothMask,false,true);

    //metal
    float3 viewDirWS = normalize(_WorldSpaceCameraPos - vertexPosWS);
    float fac = EasyGlossyBSDF(cloth,viewDirWS,-normalize(GetMainLight().direction),float3(0.8,0.8,0.8),normalTexture.a).x;
    float output = lerp(0.124, 1.0, saturate((fac - 0.057) / (0.531 - 0.057)));

    value = skinClothValue;
    influence = float3(1.0,1.0,1.0);
}


#endif // WUTHERING_WAVES_INCLUDED
