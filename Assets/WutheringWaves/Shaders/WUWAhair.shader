Shader "WUWA/Hair"
{
    Properties
    {
        _BaseMap("Base Map (RGBA)", 2D) = "white" {}
        _HMMap("HM Map", 2D) = "white" {}
        _spaMask("spa Mask", 2D) = "white" {}
        _Normal("Normal", Color) = (0,0,0,1)
        
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width", Range(0, 0.1)) = 0.0003
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry+30"  // 身体默认 +30，放在最后渲染
        }

        Pass
        {
            Name "BodyOpaque"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "./include/WUWAhair.hlsl"
            #include "./include/EnvironmentOcclusionURP.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HMMap);
            SAMPLER(sampler_HMMap);
            TEXTURE2D(_spaMask);
            SAMPLER(sampler_spaMask);

            float4 _Normal;
            
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
            

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float4 baseTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float4 hmTexture = SAMPLE_TEXTURE2D(_HMMap, sampler_HMMap, input.uv);
                

                float3 res = WUWAHairShader(
                    baseTexture,
                    _Normal,
                    hmTexture,
                    input.tangentWS,
                    input.bitangentWS,
                    input.normalWS,
                    _spaMask,
                    sampler_spaMask,

                    input.positionWS,
                    false);
                float4 finalColor = float4(res,1.0);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.positionWS, finalColor, 0.0);
                return finalColor;
            }
            ENDHLSL
        }

        // ------------ Outline Pass（描边） ------------
        Pass
        {
            Name "BodyOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZTest LEqual
            ZWrite Off
/*
            Blend 0 One Zero, Zero Zero
            Blend 1 Zero Zero*/

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
