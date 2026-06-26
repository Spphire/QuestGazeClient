// File: ShaderToRGB.hlsl
// 模拟 Blender 中 Shader to RGB 效果（基于白色 Diffuse BSDF）
// 此版本使用 Oren-Nayar 漫反射模型（支持粗糙度）
// ✅ 支持 Unity URP 多光源（主光源 + 附加光源）

#ifndef BLENDER_NODES_INCLUDED
#define BLENDER_NODES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float3 OrenNayarBRDF(float3 N, float3 V, float3 L, float roughness)
{
    float NL = saturate(dot(N, L));
    float NV = saturate(dot(N, V));
    float LV = saturate(dot(L, V));

    float sigma2 = roughness * roughness;
    float A = 1.0 - 0.5 * (sigma2 / (sigma2 + 0.33));
    float B = 0.45 * (sigma2 / (sigma2 + 0.09));

    float theta_r = acos(NV);
    float theta_i = acos(NL);
    float alpha = max(theta_r, theta_i);
    float beta = min(theta_r, theta_i);

    float gamma = LV - NL * NV;
    float t = (sin(alpha) * sin(beta)) > 0 ? gamma / (sin(alpha) * sin(beta)) : 0.0;

    return NL * (A + B * max(0.0, t));
}

float3 ShaderToRGB(float3 positionWS, float3 normalWS, float3 viewDirWS, float roughness)
{
    float3 N = normalize(normalWS);
    float3 V = normalize(viewDirWS);

    float3 finalColor = float3(0.0, 0.0, 0.0);

    // 主光源
    Light mainLight = GetMainLight();
    float3 L_main = normalize(mainLight.direction);
    float brdf_main = OrenNayarBRDF(N, V, L_main, roughness);
    finalColor += brdf_main * mainLight.color;

    // 附加光源
    uint additionalLightCount = GetAdditionalLightsCount();
    for (uint i = 0; i < additionalLightCount; ++i)
    {
        Light light = GetAdditionalLight(i, positionWS);
        float3 L = normalize(light.direction);
        float brdf = OrenNayarBRDF(N, V, L, roughness);
        finalColor += brdf * light.color;
    }

    return finalColor;
}

float3 Overlay(float3 baseColor, float3 blendColor)
{
    float3 isLow = step(baseColor, 0.5);
    float3 lowPart = 2.0 * baseColor * blendColor;
    float3 highPart = 1.0 - 2.0 * (1.0 - baseColor) * (1.0 - blendColor);
    return lerp(highPart, lowPart, isLow);
}

float3 Mix(float3 A, float3 B, float factor, bool clampResult, bool clampFactor)
{
    if(clampFactor)
    {
        factor = saturate(factor);
    }
    if(clampResult)
    {
        return saturate(lerp(A, B, factor));
    }
    return lerp(A, B, factor);
}

float3 OverlayMix(float3 A, float3 B, float factor)
{
    return Mix(A,Overlay(A,B),factor, false,false);
}

float3 MultiplyMix(float3 A, float3 B, float factor, bool clampResult, bool clampFactor)
{
    if(clampFactor)
    {
        factor = saturate(factor);
    }
    float3 blended = A * B;
    if(clampResult)
    {
        return saturate(lerp(A, blended, factor));
    }
    return lerp(A, blended, factor);
}

float3 RGB2HSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 HSV2RGB( in float3 c )
{
    float3 rgb = clamp( abs(fmod(c.x*6.0+float3(0.0,4.0,2.0),6.0)-3.0)-1.0, 0.0, 1.0 );

    return c.z * lerp( float3(1.0,1.0,1.0), rgb, c.y);
}

float3 HSVmodify(float3 RGBColor, float Hue, float Saturation, float Value, float Factor)
{
    // Step 1: RGB to HSV
    float3 HSVColor = RGB2HSV(RGBColor);
    
    // Step 2: Apply modifications
    HSVColor.x = frac(HSVColor.x + Hue - 0.5);                // hue rotation, wrap around
    HSVColor.y = saturate(HSVColor.y * Saturation);     // adjust saturation, clamp to [0, 1]
    HSVColor.z = HSVColor.z * Value;                    // adjust value (brightness)

    // Step 3: Convert back to RGB
    float3 ModifiedRGB = HSV2RGB(HSVColor);
    
    // Step 4: Blend original and modified based on Factor (0 = original, 1 = full effect)
    return lerp(RGBColor, ModifiedRGB, Factor);
}

// Layer Weight Node
// Inputs:
//   viewDir - 世界空间下的观察方向（通常为 normalize(_WorldSpaceCameraPos - worldPos)）
//   normal  - 世界空间的法线方向
//   blend   - Blend 参数，控制 fresnel 偏移程度
//
// Outputs:
//   facingWeight - 用于线性混合的权重（视角混合）
//   fresnelWeight - 用于塑料、高光的Fresnel反射率

void LayerWeight(
    float3 viewDir,    // View direction (world space, normalized)
    float3 normal,     // Normal direction (world space, normalized)
    float blend,       // Blend factor (0 to 1)
    out float facingWeight,
    out float fresnelWeight
)
{
    // 确保输入归一化
    viewDir = normalize(viewDir);
    normal = normalize(normal);

    // Facing: 越贴近视线方向越接近 1，越偏离越接近 0
    facingWeight = 1.0 - saturate(dot(viewDir, normal));  // ∈ [0, 1]

    // Fresnel: 电介质 Fresnel 效果，基于 Facing 权重再加非线性增强
    float f = facingWeight;
    fresnelWeight = pow(f, (1.0 - blend) * 5.0 + 1.0); // Blender-like remap
}

// Fresnel Node Implementation
// Inputs:
//   viewDir - world space view direction
//   normal  - world space surface normal
//   IOR     - index of refraction of surface layer
//
// Output:
//   fresnel - float ∈ [0,1], reflectivity factor (0: front, 1: grazing)

float ComputeFresnel(float3 viewDir, float3 normal, float IOR)
{
    viewDir = normalize(viewDir);
    normal = normalize(normal);

    // Cosine of angle between viewDir and surface normal
    float cosTheta = saturate(dot(viewDir, normal));

    // Compute F0 (base reflectance at normal incidence)
    float f0 = pow((1.0 - IOR) / (1.0 + IOR), 2.0);  // Common value: ~0.04 if IOR = 1.5

    // Schlick approximation
    float fresnel = f0 + (1.0 - f0) * pow(1.0 - cosTheta, 5.0);

    return fresnel;
}

// Invert Color Node
// Inputs:
//   color - 输入颜色 (RGBA)
//   factor - 反转因子（0~1）
//   invertRGB - 是否反转 RGB 通道（bool）
//   invertA - 是否反转 Alpha 通道（bool）
// Output:
//   返回反转后的颜色

float4 InvertColor(float4 color, float factor, bool invertRGB, bool invertA)
{
    float3 invertedRGB = 1.0 - color.rgb;
    float invertedAlpha = 1.0 - color.a;

    float3 rgbOut = lerp(color.rgb, invertedRGB, invertRGB ? factor : 0.0);
    float alphaOut = lerp(color.a, invertedAlpha, invertA ? factor : 0.0);

    return float4(rgbOut, alphaOut);
}

float4 ColorRamp_Ease1(float t)
{
    t = saturate(t);

    const float positions[4] = {0.173, 0.286, 0.314, 0.459};
    const float4 colors[4] = {
        float4(0.0, 0.0, 0.0, 1.0),          // 黑色
        float4(0.0, 0.0, 0.0, 1.0),          // 黑色
        float4(0.482, 0.482, 0.482, 1.0),    // #7B7B7B
        float4(1.0, 1.0, 1.0, 1.0)           // 白色
    };

    if (t <= positions[0])
        return colors[0];
    if (t >= positions[3])
        return colors[3];

    for (int i = 0; i < 3; i++)
    {
        if (t >= positions[i] && t <= positions[i + 1])
        {
            float segmentStart = positions[i];
            float segmentEnd = positions[i + 1];

            // 计算归一化局部t
            float localT = (t - segmentStart) / (segmentEnd - segmentStart);

            // smoothstep实现ease曲线插值
            float easedT = smoothstep(0.0, 1.0, localT);

            return lerp(colors[i], colors[i + 1], easedT);
        }
    }

    return colors[0];
}

float4 ColorRamp_Ease2(float t)
{
    t = saturate(t);

    const float positions[3] = {0.0, 0.607, 0.718};
    const float4 colors[3] = {
        float4(0.0, 0.0, 0.0, 1.0),
        float4(0.0, 0.0, 0.0, 1.0),
        float4(0.217, 0.217, 0.217, 1.0),
    };

    if (t <= positions[0])
        return colors[0];
    if (t >= positions[2])
        return colors[2];

    for (int i = 0; i < 2; i++)
    {
        if (t >= positions[i] && t <= positions[i + 1])
        {
            float segmentStart = positions[i];
            float segmentEnd = positions[i + 1];

            // 计算归一化局部t
            float localT = (t - segmentStart) / (segmentEnd - segmentStart);

            // smoothstep实现ease曲线插值
            float easedT = smoothstep(0.0, 1.0, localT);

            return lerp(colors[i], colors[i + 1], easedT);
        }
    }

    return colors[0];
}

float4 ColorRamp_Ease3(float t)
{
    t = saturate(t);

    const float positions[3] = {0.0, 0.059, 0.418};
    const float4 colors[3] = {
        float4(1.0, 1.0, 1.0, 1.0),
        float4(0.124, 0.124, 0.124, 1.0),
        float4(0.007, 0.007, 0.007, 1.0),
    };

    if (t <= positions[0])
        return colors[0];
    if (t >= positions[2])
        return colors[2];

    for (int i = 0; i < 2; i++)
    {
        if (t >= positions[i] && t <= positions[i + 1])
        {
            float segmentStart = positions[i];
            float segmentEnd = positions[i + 1];

            // 计算归一化局部t
            float localT = (t - segmentStart) / (segmentEnd - segmentStart);

            // smoothstep实现ease曲线插值
            float easedT = smoothstep(0.0, 1.0, localT);

            return lerp(colors[i], colors[i + 1], easedT);
        }
    }

    return colors[0];
}

float4 NodeGroup2(float3 Color, float3 B, float3 Position,float3 Normal, float3 Color2, float3 ViewDir)
{
    float3 OverlayA = HSVmodify(Color, 0.5, 0.8, 0.48, 1.0);
    float3 Mix1_A = OverlayMix(OverlayA, B, 0.358);
    float CR1output = ColorRamp_Ease1(ShaderToRGB(Position, Normal, ViewDir, 1.0)).x;
    float3 Mix1_B = HSVmodify(Color, 0.5, 1.1, 0.87, 1.0);
    float3 Mix1_output = Mix(Mix1_A,Mix1_B,CR1output, false,false);

    float Mix2_factor = ColorRamp_Ease2(ComputeFresnel(ViewDir, Normal, 1.45)).x;
    float3 Mix2_output = Mix(Mix1_output, Color, Mix2_factor, false,false);

    float MultiplyMix_factor = InvertColor(float4(Color2,1.0),1.0,true,false).x;
    float fresnel;
    float facing;
    LayerWeight(ViewDir,Normal,0.3, facing, fresnel);
    float3 MultiplyMix_A = ColorRamp_Ease3(fresnel);

    float MixFinal_factor = MultiplyMix(MultiplyMix_A, float3(0.0,0.0,0.0), MultiplyMix_factor, false,false);
    return float4(Mix(Mix2_output, float3(1.0,1.0,1.0), MixFinal_factor, false,false), 1.0);
}

//================

// Compute Fresnel term using complex IOR (n,k)
float3 FresnelConductor(float3 baseColor, float3 edgeTint, float3 n, float3 k, float cosTheta)
{
    float3 cosTheta2 = cosTheta * cosTheta;
    float3 sinTheta2 = 1.0 - cosTheta2;
    float3 n2 = n * n;
    float3 k2 = k * k;

    float3 t0 = n2 - k2 - sinTheta2;
    float3 a2plusb2 = sqrt(t0 * t0 + 4.0 * n2 * k2);
    float3 t1 = a2plusb2 + cosTheta2;
    float3 t2 = 2.0 * cosTheta * sqrt(a2plusb2);
    float3 Rs = (t1 - t2) / (t1 + t2);

    float3 t3 = cosTheta2 * a2plusb2 + sinTheta2 * sinTheta2;
    float3 t4 = t2 * sinTheta2;
    float3 Rp = Rs * (t3 - t4) / (t3 + t4);

    float3 F = 0.5 * (Rs + Rp);

    // Tint from F82 approximation
    float3 fresnelTint = baseColor + (edgeTint - baseColor) * pow(1.0 - cosTheta, 5.0);
    return fresnelTint * F;
}

float3 MultiscatterGGXMetallicBRDF_AllLights(
    float3 N, float3 V,
    float roughness,
    float3 baseColor, float3 edgeTint,
    float3 iorN, float3 iorK,
    float3 positionWS)
{
    float3 finalColor = 0;

    // 主光源
    Light mainLight = GetMainLight();
    float3 L_main = normalize(mainLight.direction);
    float3 H_main = normalize(V + L_main);
    float NoV_main = saturate(dot(N, V));
    float NoL_main = saturate(dot(N, L_main));
    float NoH_main = saturate(dot(N, H_main));
    float VoH_main = saturate(dot(V, H_main));

    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denom_main = NoH_main * NoH_main * (alpha2 - 1.0) + 1.0;
    float D_main = alpha2 / (PI * denom_main * denom_main + 1e-5);

    float k = alpha / 2.0;
    float G_V_main = NoV_main / (NoV_main * (1.0 - k) + k);
    float G_L_main = NoL_main / (NoL_main * (1.0 - k) + k);
    float G_main = G_V_main * G_L_main;

    float3 F_main = FresnelConductor(baseColor, edgeTint, iorN, iorK, VoH_main);
    float3 specular_main = D_main * G_main * F_main / (4.0 * NoV_main * NoL_main + 1e-5);
    finalColor += specular_main * NoL_main * mainLight.color;

    // 附加光源
    uint lightCount = GetAdditionalLightsCount();
    for (uint i = 0; i < lightCount; ++i)
    {
        Light light = GetAdditionalLight(i, positionWS);
        float3 L = normalize(light.direction);
        float3 H = normalize(V + L);

        float NoV = saturate(dot(N, V));
        float NoL = saturate(dot(N, L));
        float NoH = saturate(dot(N, H));
        float VoH = saturate(dot(V, H));

        float denom = NoH * NoH * (alpha2 - 1.0) + 1.0;
        float D = alpha2 / (PI * denom * denom + 1e-5);

        float G_V = NoV / (NoV * (1.0 - k) + k);
        float G_L = NoL / (NoL * (1.0 - k) + k);
        float G = G_V * G_L;

        float3 F = FresnelConductor(baseColor, edgeTint, iorN, iorK, VoH);
        float3 specular = D * G * F / (4.0 * NoV * NoL + 1e-5);

        finalColor += specular * NoL * light.color;
    }

    return finalColor;
}


#endif // BLENDER_NODES_INCLUDED
