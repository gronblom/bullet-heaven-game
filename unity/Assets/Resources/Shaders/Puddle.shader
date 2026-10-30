// Spilled liquid lying flat on a surface (URP), drawn instanced on a unit quad facing up.
// Per instance: _Color = liquid colour, _Params = (shape seed 0..1, opacity, thickness 0 water .. 1 honey, -).
// The outline is a wobbly blob (a few sine lobes) plus a couple of droplets, cut out with clip.
// Thin liquids ripple and mirror the room; thick ones bulge at the edge (surface tension).
Shader "BulletHeaven/Puddle"
{
    Properties
    {
        _RippleStrength ("Ripple strength", Float) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RippleStrength;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PuddleProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)
            UNITY_INSTANCING_BUFFER_END(PuddleProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(PuddleProps, _Color);
                float4 params = UNITY_ACCESS_INSTANCED_PROP(PuddleProps, _Params);

                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float seed = params.x * 17.0;
                float edge = 0.78
                    + 0.09 * sin(a * 3.0 + Hash(seed) * 6.28)
                    + 0.06 * sin(a * 5.0 + Hash(seed + 1.0) * 6.28)
                    + 0.04 * sin(a * 9.0 + Hash(seed + 2.0) * 6.28);
                float2 d1 = float2(cos(Hash(seed + 3.0) * 6.28), sin(Hash(seed + 3.0) * 6.28)) * 0.9;
                float2 d2 = float2(cos(Hash(seed + 4.0) * 6.28), sin(Hash(seed + 4.0) * 6.28)) * 0.86;
                float inside = max(step(r, edge), max(step(length(p - d1), 0.07), step(length(p - d2), 0.05)));
                clip(inside - 0.5);
                clip(params.y - 0.02);

                float thick = params.z;
                float rim = smoothstep(edge - 0.18, edge, r) * thick;
                float t = _Time.y * lerp(1.0, 0.15, thick);
                float2 wave = float2(sin(p.x * 23.0 + t * 2.1 + seed), sin(p.y * 19.0 - t * 1.7 + seed * 0.5))
                    + 0.5 * float2(sin((p.x + p.y) * 41.0 + t * 3.3), sin((p.x - p.y) * 37.0 - t * 2.9));
                float2 slope = wave * _RippleStrength * (1.0 - thick) * 0.15 + p * rim * 0.6;
                float3 normalWS = normalize(float3(slope.x, 1.0, slope.y));

                float depth = 1.0 - smoothstep(0.0, edge, r);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = tint.rgb * lerp(1.1, 0.6, depth * (0.4 + 0.6 * thick));
                surface.alpha = lerp(0.55, 0.95, thick) * params.y;
                surface.metallic = 0.0;
                surface.smoothness = 1.0 - lerp(0.04, 0.12, thick);
                surface.occlusion = 1.0;

                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.positionCS = input.positionCS;
                data.normalWS = normalWS;
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                data.fogCoord = input.fogFactor;
                data.bakedGI = SampleSH(normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb = MixFog(color.rgb, data.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
