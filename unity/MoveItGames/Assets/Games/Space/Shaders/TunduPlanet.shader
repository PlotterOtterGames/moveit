// Yjet!'s planets (URP): an equirectangular map on a sphere (or any mesh with such uvs: the donuts), lit like a toy
// (2026-11-14, the "toy galaxy" look): a soft two-tone sun with the night side tinted violet, a warm glow along the
// day/night edge, a glossy highlight, a wide rim of air toward the edge that shows on the night side too (the nebulae's
// light), and the map's alpha glowing (lava, the star planet). No fog, no shadows: space is clear, and on the TV box
// every planet pixel costs one texture read and a little arithmetic.
Shader "Tundu/Planet"
{
    Properties
    {
        [MainTexture] _BaseMap ("Map (equirectangular; alpha = glow)", 2D) = "white" {}
        _ShadeColor ("Night side tint", Color) = (0.32, 0.3, 0.6, 1)
        _ShadeSoftness ("Day/night edge softness", Range(0.01, 0.6)) = 0.22
        _Ambient ("Night side brightness", Range(0, 1)) = 0.3
        _AtmoColor ("Atmosphere", Color) = (0.6, 0.85, 1, 1)
        _AtmoStrength ("Atmosphere strength", Range(0, 2)) = 0.8
        _Gloss ("Toy gloss", Range(0, 1.5)) = 0.35
        _EdgeColor ("Day/night edge glow (alpha = strength)", Color) = (1, 0.62, 0.5, 0.35)
        [HDR] _GlowColor ("Glow (times the map's alpha)", Color) = (0, 0, 0, 0)
        _Spin ("Turning (map widths a second)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _ShadeColor;
                half _ShadeSoftness;
                half _Ambient;
                half4 _AtmoColor;
                half _AtmoStrength;
                half _Gloss;
                half4 _EdgeColor;
                half4 _GlowColor;
                float _Spin;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap) + float2(_Time.y * _Spin, 0.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 map = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half lit = smoothstep(-_ShadeSoftness, _ShadeSoftness, ndl);
                half3 day = map.rgb * light.color * (0.78h + 0.3h * saturate(ndl));
                half3 night = map.rgb * _ShadeColor.rgb * _Ambient;
                half3 color = lerp(night, day, lit);
                // a warm glow along the edge of the night, like a toy lit by a lamp
                color += _EdgeColor.rgb * (_EdgeColor.a * 4.0h * lit * (1.0h - lit)) * map.rgb;
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 h = normalize(light.direction + view);
                color += light.color * (pow(saturate(dot(n, h)), 40.0h) * _Gloss * lit);
                half rim = pow(1.0h - saturate(dot(n, view)), 2.2h);
                color += _AtmoColor.rgb * rim * _AtmoStrength * (0.45h + 0.55h * lit);
                color += map.a * _GlowColor.rgb;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
