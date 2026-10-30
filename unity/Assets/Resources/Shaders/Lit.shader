// Lit surface for everything solid in Bullet Heaven (URP).
// - Textures follow glTF (what the Blender generator bakes): albedo, roughness in G + metallic in B
//   of _OrmMap, a tangent-space normal map. Primitives just use the colour and the scalars.
// - _Fade < 1 dissolves the surface with an ordered dither (like Godot's alpha hash): it still
//   writes depth, so it never sorts wrongly or vanishes up close. Shadows stay solid.
// - _BUGWALK: walk cycle for the generated bug models, drawn instanced. Vertex colour RGB holds the
//   part code as bits (0 body, 1-3 one side's legs front to back, 4-6 the other side's, 7 antennae),
//   alpha how far along the limb. Per instance _Anim = (walk phase, stride 0..1, hit flash 0..1, -).
//   Mesh space: forward +Z, up +Y.
Shader "BulletHeaven/Lit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _OrmMap ("Roughness (G) Metallic (B)", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _HasNormalMap ("Has normal map", Float) = 0
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission colour", Color) = (0, 0, 0, 0)
        _Roughness ("Roughness", Range(0, 1)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Matte ("Matte (pushes roughness to 1)", Range(0, 1)) = 0
        _Fade ("Fade (1 = opaque)", Range(0, 1)) = 1
        [Toggle(_BUGWALK)] _BugWalk ("Bug walk cycle", Float) = 0
        _StrideLength ("Stride length", Float) = 0.16
        _StepHeight ("Step height", Float) = 0.12
        _AntennaSway ("Antenna sway", Float) = 0.07
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half _HasNormalMap;
            half _Roughness;
            half _Metallic;
            half _Matte;
            half _Fade;
            float _StrideLength;
            float _StepHeight;
            float _AntennaSway;
        CBUFFER_END

        TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
        TEXTURE2D(_OrmMap);      SAMPLER(sampler_OrmMap);
        TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        UNITY_INSTANCING_BUFFER_START(BugProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Anim)
        UNITY_INSTANCING_BUFFER_END(BugProps)

        float4 AnimData()
        {
        #if defined(_BUGWALK)
            return UNITY_ACCESS_INSTANCED_PROP(BugProps, _Anim);
        #else
            return float4(0, 0, 0, 0);
        #endif
        }

        float3 BugWalk(float3 p, float4 color)
        {
        #if defined(_BUGWALK)
            float4 anim = AnimData();
            float code = step(0.5, color.r) + 2.0 * step(0.5, color.g) + 4.0 * step(0.5, color.b);
            float along = color.a;
            if (code > 0.5 && code < 6.5)
            {
                float side = code > 3.5 ? 1.0 : 0.0;
                float index = code - 1.0 - side * 3.0;
                // Tripod gait: front and back legs of one side step with the other side's middle leg.
                float legPhase = anim.x + fmod(index + side, 2.0) * PI;
                p.z += sin(legPhase) * _StrideLength * along * anim.y;
                p.y += max(0.0, cos(legPhase)) * _StepHeight * along * anim.y;
            }
            else if (code > 6.5)
            {
                // Antennae feel around constantly.
                p.x += sin(_Time.y * 6.0 + anim.x * 0.25) * _AntennaSway * along;
                p.y += cos(_Time.y * 4.3 + anim.x * 0.2) * _AntennaSway * 0.6 * along;
            }
        #endif
            return p;
        }

        static const float kBayer[16] =
        {
             0.0,  8.0,  2.0, 10.0,
            12.0,  4.0, 14.0,  6.0,
             3.0, 11.0,  1.0,  9.0,
            15.0,  7.0, 13.0,  5.0
        };

        void DitherClip(float4 positionCS)
        {
            if (_Fade >= 0.999)
                return;
            uint2 p = uint2(positionCS.xy) % 4;
            clip(_Fade - (kBayer[p.y * 4 + p.x] + 0.5) / 16.0);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _BUGWALK
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float flash : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionOS = BugWalk(input.positionOS.xyz, input.color);
                VertexPositionInputs positions = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.tangentWS = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.flash = AnimData().z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DitherClip(input.positionCS);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half4 orm = SAMPLE_TEXTURE2D(_OrmMap, sampler_OrmMap, input.uv);
                half roughness = lerp(orm.g * _Roughness, 1.0, _Matte);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(albedo.rgb, half3(1.0, 0.95, 0.85), input.flash);
                surface.alpha = 1.0;
                surface.metallic = orm.b * _Metallic * (1.0 - _Matte * 0.8);
                surface.smoothness = 1.0 - roughness;
                surface.occlusion = 1.0;
                surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb
                    + half3(1.0, 0.6, 0.3) * input.flash * 1.5;

                // Normal map only when the mesh has usable tangents.
                float3 normalWS = normalize(input.normalWS);
                if (_HasNormalMap > 0.5 && dot(input.tangentWS.xyz, input.tangentWS.xyz) > 0.01)
                {
                    half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                    float3 tangent = normalize(input.tangentWS.xyz);
                    float3 bitangent = input.tangentWS.w * cross(normalWS, tangent);
                    normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(tangent, bitangent, normalWS)));
                }

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
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _BUGWALK
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(BugWalk(input.positionOS.xyz, input.color));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _BUGWALK

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(BugWalk(input.positionOS.xyz, input.color));
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DitherClip(input.positionCS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _BUGWALK

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(BugWalk(input.positionOS.xyz, input.color));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DitherClip(input.positionCS);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
