Shader "LaserTag/Night Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Pass
        {
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.direction = input.positionOS;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float height = saturate(normalize(input.direction).y);
                return half4(lerp(half3(.008,.013,.026), half3(.001,.002,.007), sqrt(height)),1);
            }
            ENDHLSL
        }
    }
}
