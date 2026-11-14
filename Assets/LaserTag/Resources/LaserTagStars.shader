Shader "LaserTag/Stars"
{
    SubShader
    {
        Tags { "Queue"="Transparent-1" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 directionVS = mul((float3x3)UNITY_MATRIX_V, input.positionOS);
                output.positionCS = mul(UNITY_MATRIX_P, float4(directionVS, 1));
                #if UNITY_REVERSED_Z
                output.positionCS.z = 0;
                #else
                output.positionCS.z = output.positionCS.w * UNITY_RAW_FAR_CLIP_VALUE;
                #endif
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target { return half4(input.color.rgb, 1); }
            ENDHLSL
        }
    }
}
