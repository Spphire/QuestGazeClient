// File: ShaderToRGB.hlsl
// 模拟 Blender 中 Shader to RGB 效果（基于白色 Diffuse BSDF）
// 此版本使用 Oren-Nayar 漫反射模型（支持粗糙度）
// ✅ 支持 Unity URP 多光源（主光源 + 附加光源）

#ifndef GLOSSY_BSDF_INCLUDED
#define GLOSSY_BSDF_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Glossy BSDF implementation in HLSL
// Distribution: Multiscatter GGX (default)

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float3 FresnelSchlick(float cosTheta, float3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float DistributionGGX(float3 N, float3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = saturate(dot(N, H));
    float NdotH2 = NdotH * NdotH;

    float denom = NdotH2 * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
{
    float NdotV = saturate(dot(N, V));
    float NdotL = saturate(dot(N, L));
    float ggx1 = GeometrySchlickGGX(NdotV, roughness);
    float ggx2 = GeometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
}

float3 AnisotropicTangentRotate(float3 tangent, float3 bitangent, float rotation)
{
    float angle = rotation * TWO_PI; // rotation in [0,1] mapped to [0,2pi]
    float cosR = cos(angle);
    float sinR = sin(angle);
    return normalize(tangent * cosR + bitangent * sinR);
}

float3 GlossyBSDF(
    float3 N, float3 V, float3 L,
    float3 baseColor, float roughness,
    float anisotropy, float rotation,
    float3 tangentWS)
{
    float3 H = normalize(V + L);

    // Anisotropic rotation
    float3 T = tangentWS;
    float3 B = normalize(cross(N, T));
    float3 rotatedT = AnisotropicTangentRotate(T, B, rotation);

    // Apply anisotropy by scaling roughness differently in tangent/bitangent direction
    float at = max(0.001, 1.0 - anisotropy);
    float ab = max(0.001, 1.0 + anisotropy);
    float3x3 M = float3x3(rotatedT * at, B * ab, N);
    float3 localH = mul(M, H);
    float3 warpedH = normalize(mul(transpose(M), localH));

    // GGX / Multiscatter Distribution
    float D = DistributionGGX(N, warpedH, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    float3 F0 = baseColor;
    float NdotL = saturate(dot(N, L));
    float NdotV = saturate(dot(N, V));
    float3 F = FresnelSchlick(saturate(dot(H, V)), F0);

    float3 specular = D * G * F / max(4.0 * NdotV * NdotL, 0.001);
    return specular;
}

float3 EasyGlossyBSDF(
    float3 N, float3 V, float3 L,
    float3 baseColor, float roughness)
{
    float3 H = normalize(V + L);

    float3 localH = mul(M, H);
    float3 warpedH = normalize(mul(transpose(M), localH));

    // GGX / Multiscatter Distribution
    float D = DistributionGGX(N, warpedH, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    float3 F0 = baseColor;
    float NdotL = saturate(dot(N, L));
    float NdotV = saturate(dot(N, V));
    float3 F = FresnelSchlick(saturate(dot(H, V)), F0);

    float3 specular = D * G * F / max(4.0 * NdotV * NdotL, 0.001);
    return specular;
}


#endif // GLOSSY_BSDF_INCLUDED
