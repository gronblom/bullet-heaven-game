// Unlit additive glow (blasts, lightning). Colour = _Color (per material or per instance) x vertex
// colour (for lines, _UseVertexColor = 1); alpha scales the brightness.
Shader "BulletHeaven/Additive"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _UseVertexColor ("Use vertex colour (lines)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _UseVertexColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(AdditiveProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(AdditiveProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = lerp(float4(1, 1, 1, 1), input.color, _UseVertexColor) * UNITY_ACCESS_INSTANCED_PROP(AdditiveProps, _Color);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.color.rgb * input.color.a, 1.0);
            }
            ENDHLSL
        }
    }
}
