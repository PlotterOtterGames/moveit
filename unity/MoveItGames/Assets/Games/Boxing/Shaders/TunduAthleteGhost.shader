// Superboks!'s see-through hero (URP): the player's chibi seen from behind and above, like the see-through Mii in Wii Sports
// Boxing (and Little Mac in Punch-Out!! Wii), so the opponent in front is never hidden (2026-11-19, the user: "more from
// behind, see-through, the camera a bit at the top"). The chibi's own colours (its region table, _Regions, from the same
// property block as Tundu/Athlete), see-through in the middle and glowing at the edges (fresnel), all worked out at the
// vertices (it covers a big part of the screen: on the box its pixels must cost nothing but the blend).
//
// Drawn in two materials of this shader, like Tundu/Ghost: "depth" (ColorMask 0, ZWrite On, queue 2999) lays down the
// nearest surface of the whole figure first, then "colour" (ZWrite Off, ZTest LEqual, queue 3000) tints only that surface,
// once: one even, glassy figure without darker patches where its parts overlap. _FlashColor adds a colour for a moment.
Shader "Tundu/AthleteGhost"
{
    Properties
    {
        _Alpha ("See-through (alpha of the body)", Range(0, 1)) = 0.2
        _Wash ("Toward the edge glow's colour (light and airy)", Range(0, 1)) = 0.55
        _RimColor ("Edge Glow (alpha = strength)", Color) = (0.86, 0.95, 1, 0.9)
        _RimPower ("Edge Glow Width", Range(0.5, 8)) = 2.4
        _FlashColor ("Flash (alpha = amount)", Color) = (1, 1, 1, 0)
        [Enum(UnityEngine.Rendering.ColorWriteMask)] _ColorMask ("Colour Mask (0 = depth material)", Float) = 15
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write (On = depth material)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Alpha;
                half _Wash;
                half4 _RimColor;
                half _RimPower;
                half4 _FlashColor;
                half _ColorMask;
                half _ZWrite;
            CBUFFER_END

            // the character's colours (Chibi's property block); 24 regions
            float4 _Regions[24];

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 region : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 colour : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float3 view = normalize(GetWorldSpaceViewDir(ws));
                Light light = GetMainLight();
                half3 albedo = _Regions[clamp((int)round(v.region.x), 0, 23)].rgb;
                half lit = smoothstep(-0.25h, 0.4h, dot(n, light.direction));
                half3 colour = albedo * lerp(0.75h, 1.2h, lit) * lerp(0.78h, 1.0h, (half)v.region.y);
                // washed toward the glow's colour: a light figure (dark hair or a dark suit would hide what's behind it)
                colour = lerp(colour, _RimColor.rgb, _Wash);
                half rim = pow(1.0h - saturate(dot(n, view)), _RimPower) * _RimColor.a;
                colour = lerp(colour, _RimColor.rgb, saturate(rim) * 0.65h);
                half alpha = saturate(_Alpha + rim * 0.6h);
                colour = lerp(colour, _FlashColor.rgb, _FlashColor.a);
                alpha = saturate(alpha + _FlashColor.a * 0.35h);
                o.colour = half4(colour, alpha);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return i.colour;
            }
            ENDHLSL
        }
    }
}
