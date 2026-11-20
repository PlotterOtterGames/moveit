// Vishu!'s camera picture as a flattering mirror (URP, unlit, opaque): the player as the camera sees them, brightened
// by the room's own light level (the evening living rooms the box sees are dim: the picture was grey and dark), a little
// more colourful and warmer; the room round them softly blurred, dimmed and tinted toward the dressing room's violet,
// like a portrait photo's background, so the player and the clothes stand out and the room's noise disappears.
// _BaseMap: the denoised picture (MirrorLook); _PersonMap: the room's blurred colour (rgb) and where the player is (a:
// the body mask widened and softened), one small map in the screen's space through _PersonRect (one read for both);
// _TunduAmbient: the picture's average colour (its last mip, DressGame). uv1: 0..1 across the quad, for the vignette.
Shader "Tundu/Mirror"
{
    Properties
    {
        [MainTexture] _BaseMap ("Camera picture", 2D) = "white" {}
        _PersonMap ("Room blurred (rgb), where the player is (a)", 2D) = "white" {}
        _PersonRect ("Screen -> person map (xy origin, zw 1/size)", Vector) = (0, 0, 0.001, 0.001)
        _PersonOn ("A player in the picture (0..1)", Float) = 0
        _Live ("A live camera picture (0/1)", Float) = 1
        _Target ("Brightness to aim for (linear)", Float) = 0.20
        _MaxGain ("Most it brightens", Float) = 2.2
        _Saturation ("Saturation", Float) = 1.22
        _Warm ("Warmth", Float) = 0.05
        _PersonLift ("The player: a little brighter than the room", Float) = 1.22
        _RoomDim ("Room: darker", Range(0, 1)) = 0.30
        _RoomBlur ("Room: blurred", Range(0, 1)) = 0.85
        _RoomTint ("Room: tint (a = how much)", Color) = (0.30, 0.20, 0.48, 0.22)
        _VignetteColor ("Vignette colour (alpha = strength)", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _PersonRect;
                half _PersonOn, _Live, _Target, _MaxGain, _Saturation, _Warm, _RoomDim, _RoomBlur, _PersonLift;
                half4 _RoomTint;
                half4 _VignetteColor;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PersonMap);
            SAMPLER(sampler_PersonMap);
            TEXTURE2D(_TunduAmbient);
            SAMPLER(sampler_TunduAmbient);
            float _TunduAmbientOn;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 quad : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 quad : TEXCOORD1;      // -1..1 across the quad
                float2 world : TEXCOORD2;
                half gain : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.world = TransformObjectToWorld(v.positionOS.xyz).xy;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.quad = v.quad * 2.0 - 1.0;
                // how much to brighten: the picture's average brightness against the target (per vertex: one fetch)
                half3 amb = SAMPLE_TEXTURE2D_LOD(_TunduAmbient, sampler_TunduAmbient, float2(0.5, 0.5), 8).rgb;
                half l = dot(amb, half3(0.299, 0.587, 0.114));
                o.gain = _TunduAmbientOn > 0.5 ? clamp(_Target / max(l, 0.01), 1.0, _MaxGain) : 1.0;
                return o;
            }

            half3 Grade(half3 c, half gain)
            {
                c *= gain;
                c = c / (1.0 + c * 0.25) * 1.25;                    // a soft shoulder: brighter without burning out
                half l = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(l.xxx, c, _Saturation);
                c += half3(_Warm, _Warm * 0.35, -_Warm * 0.6) * l;
                return max(c, 0.0);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                if (_Live > 0.5)
                {
                    c = Grade(c, i.gain);
                    half4 around4 = SAMPLE_TEXTURE2D(_PersonMap, sampler_PersonMap, (i.world - _PersonRect.xy) * _PersonRect.zw);
                    half person = lerp(1.0, around4.a, _PersonOn);
                    half3 lifted = c * lerp(1.0, _PersonLift, _PersonOn);
                    // the room's look only where the room shows (on the player, or with nobody there, it isn't seen: the
                    // box's GPU skips its maths there; the result is the same)
                    if (person < 0.999)
                    {
                        half3 room = Grade(around4.rgb, i.gain);
                        half3 around = lerp(c, room, _RoomBlur);
                        around = lerp(around, _RoomTint.rgb * dot(around, half3(0.33, 0.33, 0.34)) * 2.2, _RoomTint.a) * (1.0 - _RoomDim);
                        c = lerp(around, lifted, person);
                    }
                    else c = lifted;
                }
                // the mirror's darker edges: a rounded-rectangle falloff (none in the middle: the roots only nearer the edges)
                float2 a = abs(i.quad);
                a *= a;
                a *= a;
                float edge = a.x + a.y;
                if (edge > 0.1478 && _VignetteColor.a > 0.0)   // 0.62^4: smoothstep(0.62, ...) is 0 below it
                {
                    half k = smoothstep(0.62, 1.18, sqrt(sqrt(edge))) * 0.62 * _VignetteColor.a;
                    c = lerp(c, _VignetteColor.rgb, k);
                }
                return half4(saturate(c), 1.0);
            }
            ENDHLSL
        }
    }
}
