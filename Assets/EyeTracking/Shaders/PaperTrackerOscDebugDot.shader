Shader "Hidden/EyeTracking/PaperTrackerOscDebugDot"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "PaperTrackerOscDebugDot"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _PaperTrackerDotUvRadius;
            float4 _PaperTrackerDotColor;
            float4 _PaperTrackerTargetSize;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 pixelPosition = input.texcoord * _PaperTrackerTargetSize.xy;
                float2 center = _PaperTrackerDotUvRadius.xy * _PaperTrackerTargetSize.xy;
                float radius = max(0.5, _PaperTrackerDotUvRadius.z);
                float feather = max(1.0, radius * 0.2);
                float distanceToCenter = distance(pixelPosition, center);
                float alpha = 1.0 - smoothstep(radius - feather, radius, distanceToCenter);

                return half4(_PaperTrackerDotColor.rgb, _PaperTrackerDotColor.a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
