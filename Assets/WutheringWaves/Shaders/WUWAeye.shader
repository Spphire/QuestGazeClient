Shader "WUWA/Eye"
{
    Properties
    {
        _BaseMap("Base Map (RGBA)", 2D) = "white" {}
        _HighlightMap("ID Map", 2D) = "white" {}
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
            #include "./include/WUWAface.hlsl"
            #include "./include/EnvironmentOcclusionURP.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HighlightMap);
            SAMPLER(sampler_HighlightMap);
            
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
                float4 highlightTexture = SAMPLE_TEXTURE2D(_HighlightMap, sampler_HighlightMap, input.uv);
                //return baseTexture;

                float3 color = ColorMixAdd(baseTexture.rgb,highlightTexture.r,highlightTexture.b,true,false);

                float4 finalColor = float4(baseTexture.xyz,1.0);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.positionWS, finalColor, 0.0);
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
