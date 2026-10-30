// Additive glow for effects (lasers, bullet streaks, sparks, pulse-ring fronts, flashes), built as
// camera-facing quads by Scripts/Effects.cs. Brightness = vertex colour rgb x alpha x _Intensity
// (above 1 feeds the bloom), shaped by the UVs:
//   _Shape 0: soft round dot over the quad (sparks, flashes, streaks when the quad is stretched)
//   _Shape 1: soft band across V (laser beams, ring fronts): bright along the middle, fading at the edges
Shader "BulletHeaven/Glow"
{
    Properties
    {
        _Shape ("Shape (0 dot, 1 band)", Float) = 0
        _Intensity ("Intensity", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float falloff;
                if (_Shape < 0.5)
                {
                    float2 p = input.uv * 2.0 - 1.0;
                    falloff = saturate(1.0 - dot(p, p));
                }
                else
                {
                    falloff = saturate(1.0 - abs(input.uv.y * 2.0 - 1.0));
                }
                falloff *= falloff;
                return half4(input.color.rgb * input.color.a * falloff * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
