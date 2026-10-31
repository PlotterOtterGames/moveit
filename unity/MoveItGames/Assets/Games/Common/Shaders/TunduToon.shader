// Tundu's cartoon look (URP): soft two-tone shading with tinted shadows, an optional rim light, fog, real-time
// shadows, and the "curved world" of famous runners: the ground bends away over the horizon so far things rise over
// it instead of popping in. The bend is one global value the game sets (_TunduCurve, metres of drop per metre² of
// distance ahead of the camera); 0 = flat (every scene that doesn't set it).
// Options for characters built from merged parts (all off by default, so older materials look the same): vertex
// colours (rgb = colour, a = how glossy), a soft specular highlight, emission (glows through the bloom) and a flash.
Shader "Tundu/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (palette)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Tint", Color) = (0.66, 0.68, 0.88, 1)
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = 0.0
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.6)) = 0.12
        _ShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 0.75
        _Ambient ("Ambient Fill", Range(0, 1)) = 0.25
        _RimColor ("Rim Light (alpha = strength)", Color) = (1, 1, 1, 0)
        _RimPower ("Rim Power", Range(1, 8)) = 3
        _Curve ("Bends With The World (0/1)", Float) = 1
        _UseVertexColor ("Vertex Colours (0/1; alpha = gloss)", Float) = 0
        _Gloss ("Gloss", Range(0, 2)) = 0
        _GlossPower ("Gloss Tightness", Range(4, 128)) = 32
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _FlashColor ("Flash (alpha = amount)", Color) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _ShadowStrength;
            half _Ambient;
            half4 _RimColor;
            half _RimPower;
            half _Curve;
            half _UseVertexColor;
            half _Gloss;
            half _GlossPower;
            half4 _EmissionColor;
            half4 _FlashColor;
        CBUFFER_END

        float _TunduCurve;

        float3 CurveWorld(float3 positionWS)
        {
            float d = max(0.0, positionWS.z - _WorldSpaceCameraPos.z);
            positionWS.y -= _TunduCurve * _Curve * d * d;
            return positionWS;
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
            #pragma multi_compile_instancing
            #pragma shader_feature_local _TOON_SIMPLE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;   // unbent: lighting and shadow maps live in the flat world
                float fogFactor : TEXCOORD3;
                half4 color : COLOR;
                #if defined(_TOON_SIMPLE)
                    half3 ambient : TEXCOORD4;   // sky light worked out per vertex
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(CurveWorld(ws));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                // meshes without colours read white here only when the option is off
                o.color = lerp(half4(1.0h, 1.0h, 1.0h, 1.0h), v.color, _UseVertexColor);
                #if defined(_TOON_SIMPLE)
                    o.ambient = SampleSH(o.normalWS);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb * i.color.rgb;
                albedo = lerp(albedo, _FlashColor.rgb, _FlashColor.a);
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, light.direction);
                half lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);
                lit *= lerp(1.0h, light.shadowAttenuation, _ShadowStrength);
                half3 color = lerp(albedo * _ShadeColor.rgb, albedo * light.color, lit);
            #if defined(_TOON_SIMPLE)
                // big flat surfaces (Sporte!'s venues): the sky light from the vertices, no rim, no gloss. Over a pitch
                // and the stands that's a lot less to work out per pixel on the TV box's GPU.
                color += albedo * i.ambient * _Ambient;
            #else
                color += albedo * SampleSH(n) * _Ambient;
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, view)), _RimPower) * _RimColor.a;
                color += _RimColor.rgb * rim;
                // a soft highlight where the sun glints (vertex alpha says how glossy the part is)
                half3 h = normalize(light.direction + view);
                color += light.color * pow(saturate(dot(n, h)), _GlossPower) * (_Gloss * i.color.a * lit);
            #endif
                color += _EmissionColor.rgb;
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
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vertShadow(ShadowAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
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

            half4 fragShadow() : SV_Target { return 0; }
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
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vertDepth(DepthAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformWorldToHClip(CurveWorld(TransformObjectToWorld(v.positionOS.xyz)));
            }

            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
