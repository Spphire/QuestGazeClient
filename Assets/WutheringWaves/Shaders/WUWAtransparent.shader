Shader "WUWA/transparent"
{
    Properties
    {
        _BaseMap("Base Map (RGBA)", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "white" {}
        _MaskMap("Mask Map", 2D) = "white" {}
        _UseMaskTex ("Use Mask Texture", Float) = 1           // Toggle
        _MaskColor ("Mask Color", Color) = (1,1,1,1)           // 纯色 fallback
        
        _AlphaStrength("Alpha Strength", Range(0,2)) = 1.0
        
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width", Range(0, 0.1)) = 0.0003
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Transparent+30"  // 身体默认 +30，放在最后渲染
        }

        Pass
        {
            Name "BodyTransparent"
            Tags {"LightMode" = "TransparentForward1"}
            
            Stencil
            {
                Ref 8
                WriteMask 8  // 透明位
                Comp Always
                Pass Replace // 写入透明位
                Fail Keep
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "./include/WUWABody.hlsl"
            #include "./include/EnvironmentOcclusionURP.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
            float4x4 _LightOS2WS;
            float4 _LightWorldPos;
            float _UseMaskTex;
            float4 _MaskColor;
            float _AlphaStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionH : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : NORMAL;
                float3 tangentWS : TANGENT;
                float3 bitangentWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionH = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                float tangentSign = input.tangentOS.w;

                float3 bitangentWS = cross(normalWS, tangentWS) * tangentSign;

                output.normalWS = normalize(normalWS);
                output.tangentWS = normalize(tangentWS);
                output.bitangentWS = normalize(bitangentWS);

                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.color = input.color;
                return output;
            }

            float4 ColorRamp_Ease(float t)
            {
                t = saturate(t);

                const float positions[2] = {0.0, 0.077};
                const float4 colors[2] = {
                    float4(1.0, 1.0, 1.0, 1.0),
                    float4(0.0, 0.0, 0.0, 1.0),
                };

                if (t <= positions[0])
                    return colors[0];
                if (t >= positions[1])
                    return colors[1];

                for (int i = 0; i < 1; i++)
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

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float4 baseTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float4 normalTexture = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                float3 maskTexture = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv).rgb;
                maskTexture = lerp(_MaskColor.rgb, maskTexture, _UseMaskTex);

                float3 value;
                float3 influence;

                float alpha;
                if (_AlphaStrength>1)
                {
                    alpha = lerp(baseTexture.a, 1.0,_AlphaStrength-1);
                }else
                {
                    alpha = lerp(0.0, baseTexture.a,_AlphaStrength);
                }

                WUWABodyShader(
                    baseTexture,
                    normalTexture,
                    maskTexture,
                    input.tangentWS,
                    input.bitangentWS,
                    input.normalWS,
                    float3x3(
                        _LightOS2WS[0].xyz,
                        _LightOS2WS[1].xyz,
                        _LightOS2WS[2].xyz
                    ),
                    _LightWorldPos.xyz,
                    input.positionWS,
                    false,
                    value,
                    influence);
                float4 finalColor = float4(value,alpha);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.positionWS, finalColor, 0.0);
                return finalColor;
            }
            ENDHLSL
        }

        // ------------ Outline Pass（描边） ------------
        Pass
        {
            Name "BodyOutline"
            Tags { "LightMode" = "TransparentOutline" }
            
            Stencil
            {
                Ref 8
                ReadMask 8    // 透明位
                Comp NotEqual // 不透明部分
                Pass Keep
                Fail Keep
            }
            
            Cull Front
            ZTest LEqual
            ZWrite Off

            Blend 0 One Zero, Zero Zero
            Blend 1 Zero Zero

            ColorMask RGBA 0
            ColorMask 0 1

            HLSLPROGRAM
            #pragma vertex outline_vert
            #pragma fragment outline_frag
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "./include/EnvironmentOcclusionURP.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _OutlineWidth;
            float4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionH : SV_POSITION;
                float3 positionWS: TEXCOORD0;
                float3 normalWS  : TEXCOORD1;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings outline_vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float4 positionVS = mul(UNITY_MATRIX_V, float4(positionWS, 1.0));
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);

                positionVS.xyz += normalize(normalVS) * _OutlineWidth;

                output.positionH = mul(UNITY_MATRIX_P, positionVS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                return output;
            }

            float4 outline_frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.positionWS, _OutlineColor, 0.0);
                return _OutlineColor;
            }
            ENDHLSL
        }
    
    }
    FallBack "Universal Render Pipeline/Lit"
}
