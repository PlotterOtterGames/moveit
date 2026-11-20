// Sporte!'s athletes (art/sports/athlete.py): one skinned mesh per character, coloured by REGION. Every vertex carries
// its region in uv1.x (skin, shirt, shorts, hair, eyes ...) and its baked ambient occlusion in uv1.y; the character's
// colours are a table (_Regions, rgb = linear colour, a = gloss) set per renderer with a property block, so one model
// dresses every team. Soft toon light: a wrapped, gently stepped sun with tinted shade, sky ambient, a rim light and a
// small highlight on glossy regions (hair, shoes). Clothes are open shells (a hem has an inside): drawn two-sided, the
// inside darker.
Shader "Tundu/Athlete"
{
    Properties
    {
        _ShadeColor ("Shade Tint", Color) = (0.62, 0.64, 0.86, 1)
        _ShadeSoftness ("Shade Softness", Range(0.02, 0.8)) = 0.35
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _ShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 0.7
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.55
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.85
        _RimColor ("Rim Light (alpha = strength)", Color) = (1, 0.97, 0.9, 0.35)
        _RimPower ("Rim Power", Range(1, 8)) = 3.5
        _GlossPower ("Gloss Tightness", Range(4, 128)) = 28
        _FlashColor ("Flash (alpha = amount)", Color) = (1, 1, 1, 0)
        _Fade ("Fade (dither out)", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Athlete culls the closed pieces' back faces)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ShadeColor;
            half _ShadeSoftness;
            half _Wrap;
            half _ShadowStrength;
            half _Ambient;
            half _AOStrength;
            half4 _RimColor;
            half _RimPower;
            half _GlossPower;
            half4 _FlashColor;
            half _Fade;
        CBUFFER_END

        // the character's colours (set with a property block); 24 regions
        float4 _Regions[24];

        // a 4x4 ordered dither: fading characters out without sorting
        float Dither(float2 screen)
        {
            const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = (uint2)screen % 4;
            return (m[p.y * 4 + p.x] + 0.5) / 16.0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _FADE
            // _VERTEX_LIT (2026-11-13): the light worked out per vertex, the pixels only interpolate it: characters small on
            // the screen (the two-player split screens on the TV box, where everything is drawn twice). Front faces' light
            // on both sides (the inside of a hem darker as before); no cast shadows (the games that use it draw their own).
            #pragma multi_compile_local _ _VERTEX_LIT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 region : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            #if defined(_VERTEX_LIT)
                half3 color : TEXCOORD0;
            #else
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 region : TEXCOORD2;
            #endif
                float fogFactor : TEXCOORD3;
            };

            // the soft toon light on a region's colour (n facing the viewer's side of the surface)
            half3 Shade(float3 n, float3 positionWS, float2 region, Light light)
            {
                int r = (int)round(region.x);
                float4 entry = _Regions[clamp(r, 0, 23)];
                half3 albedo = entry.rgb;
                half gloss = entry.a;
                albedo = lerp(albedo, _FlashColor.rgb, _FlashColor.a);
                half ao = lerp(1.0h, (half)region.y, _AOStrength);
                half ndl = dot(n, light.direction);
                half wrapped = (ndl + _Wrap) / (1.0h + _Wrap);
                half lit = smoothstep(0.5h - _ShadeSoftness * 0.5h, 0.5h + _ShadeSoftness * 0.5h, wrapped);
                lit *= lerp(1.0h, light.shadowAttenuation, _ShadowStrength);
                half3 color = lerp(albedo * _ShadeColor.rgb, albedo * light.color, lit) * ao;
                color += albedo * SampleSH(n) * _Ambient * ao;
                float3 view = normalize(GetWorldSpaceViewDir(positionWS));
                half rim = pow(1.0h - saturate(dot(n, view)), _RimPower) * _RimColor.a * (0.4h + 0.6h * lit);
                color += _RimColor.rgb * rim * ao;
                half3 h = normalize(light.direction + view);
                color += light.color * pow(saturate(dot(n, h)), _GlossPower) * gloss * lit;
                return color;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if defined(_VERTEX_LIT)
                o.color = Shade(normalize(normalWS), positionWS, v.region, GetMainLight());
            #else
                o.positionWS = positionWS;
                o.normalWS = normalWS;
                o.region = v.region;
            #endif
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                // The dithered fade is a variant of its own (Athlete swaps to a material with _FADE while fading): a
                // discard anywhere in the shader costs Mali the early depth test, and every hidden layer gets shaded
                #if defined(_FADE)
                    clip(_Fade - Dither(i.positionCS.xy));
                #endif
            #if defined(_VERTEX_LIT)
                half3 color = i.color;
            #else
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                half3 color = Shade(n, i.positionWS, i.region, GetMainLight(TransformWorldToShadowCoord(i.positionWS)));
            #endif
                if (!front) color *= 0.55h;   // the inside of a sleeve or a hem
                return half4(MixFog(color, i.fogFactor), 1.0h);
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
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _FADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 vertShadow(ShadowAttributes v) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - ws);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }

            half4 fragShadow(float4 positionCS : SV_POSITION) : SV_Target
            {
                #if defined(_FADE)
                    clip(_Fade - Dither(positionCS.xy));
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            float4 vertDepth(DepthAttributes v) : SV_POSITION
            {
                return TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
            }

            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
