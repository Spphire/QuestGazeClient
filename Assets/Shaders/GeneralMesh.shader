Shader "Unlit/GeneralMesh"
{
    Properties
    {
        _ShadowIntensity ("Shadow Intensity", Range (0, 1)) = 0.8
        _HighLightAttenuation ("Highlight Attenuation", Range (0, 1)) = 0.8
        _DepthBias ("Depth Occulusion Bias", Float) = 0.0
    }
    SubShader
    {
        /*PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }*/
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+30" //"Geometry-3"//
        }
        Pass
        {

            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            
            Cull Back
            Blend One OneMinusSrcAlpha
            //Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ShadowReceiverVertex
            #pragma fragment ShadowReceiverFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/WutheringWaves/Shaders/include/EnvironmentOcclusionURP.hlsl"
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

            float _HighLightAttenuation;
            float _ShadowIntensity;
            float _DepthBias;

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 normalWS : NORMAL;
                float3 positionWS : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowReceiverVertex(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                const VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalize(mul(unity_ObjectToWorld, float4(input.normal, 0.0)).xyz);
                return output;
            }

            float4 ShadowReceiverFragment(const Varyings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 color = half3(0, 0, 0);
                half mainLightShadowAttenuation;

                // Main light shadows.
                VertexPositionInputs vertexInput = (VertexPositionInputs)0;
                vertexInput.positionWS = input.positionWS;
                const float4 shadowCoord = GetShadowCoord(vertexInput);
                mainLightShadowAttenuation = MainLightRealtimeShadow(shadowCoord);
                half alpha = (1 - mainLightShadowAttenuation) * _ShadowIntensity;

                //Additional lights highlights.
                for (int i = 0; i < GetAdditionalLightsCount(); i++) {
                    Light light = GetAdditionalLight(i, input.positionWS, float4(0, 0, 0, 0));
                    float ndtol = saturate(dot(light.direction, input.normalWS));
                    float lightAlpha = light.distanceAttenuation * ndtol * _HighLightAttenuation * light.shadowAttenuation;
                    alpha = max(lightAlpha, alpha);
                    color += light.color * lightAlpha;
                }
                float4 colorTarget = float4(color, alpha);
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.positionWS, colorTarget, _DepthBias);
                //colorTarget.a=0;
                return colorTarget;
            }
            ENDHLSL
        }
            /*
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            // Required to compile gles 2.0 with standard srp library
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            // -------------------------------------
            // Material Keywords
            //#pragma shader_feature _ALPHATEST_ON

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            //#pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }*/
    }
}
