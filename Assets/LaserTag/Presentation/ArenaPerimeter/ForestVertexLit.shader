Shader "LaserTag/Forest Vertex Lit"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float3 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; half3 light:TEXCOORD0; half4 color:COLOR; float fog:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS=TransformObjectToHClip(input.positionOS);
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
                Light sun=GetMainLight();
                output.light=max(SampleSH(normal)+sun.color*saturate(dot(normal,sun.direction)),half3(.09,.12,.16));
                output.color=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input):SV_Target { return half4(MixFog(input.color.rgb*input.light,input.fog),1); }
            ENDHLSL
        }
    }
}
