// Zana!'s garden (URP), the look's overhaul of 2026-11-17: a storybook garden in the late-afternoon sun. Everything is lit
// at the vertices (on the box the lands' per-pixel light was most of a hot box's frame) and the pixel only adds the
// ground's one detail read; no textures on the props.
//
// The props (art/fairy/props.py) carry their own colours: vertex colour rgb = the colour (linear), a = baked ambient
// occlusion; uv0.x = how much it sways in the wind, uv0.y = how much it glows (lit windows, lanterns, glowing caps).
// The light: a soft toon terminator (wrapped), the shade a cool violet-blue (hue-shifted, never grey), the sky's light
// from above and the meadow's green bounce from below, a rim of light on the edges (warm on the sunny side, lilac in
// the shade), the baked occlusion. Then the garden's haze: the further and the lower, the more it melts into the
// horizon's colour (aerial perspective; glowing things shine through it). Globals set by GardenWorld: _FairySky,
// _FairyBounce, _FairyHaze (near), _FairyHazeFar, _FairyFog (start, end, most, height falloff), _FairyWind (x, z, metres,
// speed), _FairyGloom (0 .. 1: the tree's hurt greys the garden a little).
//
//   _GROUND   the meadow: vertex colour = the grass's colour, uv0.x = the path (0 grass .. 1 path); the light is worked
//             out at the vertices for white and the pixel colours it: the grass (its dabs) or the path (cobbles in
//             _PathColor, the gaps _PathShade), a crisp ragged edge between them from one read of the detail map
//             (R grass dabs, G cobbles, B the edge's wobble) at world xz
//   _SKY      the sky dome: the vertex colour as it is, at the far plane (drawn after the garden, only where nothing is)
//   _LITE     the small and far things (grass, flowers, stones, far trees, mountains): no rim, one wave of wind
//   _Cull     0 for the grass (single blades; its normals point up so both sides light alike)
// The cost on the box (2026-11-17, its GPU at 200 MHz does ~16 operations a clock): every vertex runs all of the light
// (~250k a frame), so the props' pixel is just their colour (the haze mixed in at the vertices too) and the meadow's
// pixel a handful of operations (its texture holds what it needs: tools/fairy/ground.py).
//   _FogScale how much of the haze it takes (the clouds less: they are the sky's)
Shader "Tundu/Realm"
{
    Properties
    {
        _ShadeColor ("Shade (hue-shifted)", Color) = (0.56, 0.6, 0.95, 1)
        _Softness ("Terminator Softness", Range(0.02, 0.8)) = 0.22
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.42
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.75
        _RimColor ("Rim (alpha = strength)", Color) = (1, 0.93, 0.78, 0.35)
        _RimShade ("Rim in the shade", Color) = (0.72, 0.68, 1, 1)
        _Glow ("Glow Boost", Range(0, 4)) = 1.6
        _Sway ("Wind Sway (0 = none)", Range(0, 2)) = 1
        _FogScale ("Haze taken", Range(0, 1)) = 1
        _DetailMap ("Ground Detail (R grass, G path, B edge)", 2D) = "grey" {}
        _DetailScale ("Detail Tiles a Metre", Float) = 0.18
        _DetailStrength ("Grass Detail", Range(0, 1)) = 0.55
        _PathColor ("Path Colour", Color) = (0.93, 0.8, 0.62, 1)
        _PathShade ("Path Gaps", Color) = (0.62, 0.48, 0.5, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _GROUND _SKY
            #pragma multi_compile_local _ _LITE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadeColor;
                half _Softness;
                half _Wrap;
                half _Ambient;
                half _AOStrength;
                half4 _RimColor;
                half4 _RimShade;
                half _Glow;
                half _Sway;
                half _FogScale;
                float4 _DetailMap_ST;
                float _DetailScale;
                half _DetailStrength;
                half4 _PathColor;
                half4 _PathShade;
                half _Cull;
            CBUFFER_END
            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);

            float4 _FairySky, _FairyBounce, _FairyHaze, _FairyHazeFar, _FairyFog, _FairyWind;
            float _FairyGloom;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 colour : TEXCOORD0;       // the props: the finished colour; the meadow: the light on white, hazed
            #if defined(_GROUND)
                half4 haze : TEXCOORD1;         // rgb = the haze added, a = the path's edge offset
                float2 detailUV : TEXCOORD2;
                half3 grass : TEXCOORD3;        // the grass's colour, doubled (the texture holds half its multiplier)
            #endif
            };

            // the garden's light at a point (albedo linear, ao 0..1): returns the lit colour
            half3 Shade(half3 albedo, half ao, float3 n, float3 view, out half lit)
            {
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half wrapped = (ndl + _Wrap) / (1.0h + _Wrap);
                lit = smoothstep(0.5h - _Softness, 0.5h + _Softness, wrapped);
                half3 direct = lerp(_ShadeColor.rgb, light.color, lit);
                // the sky from above, the meadow's green from below
                half3 ambient = lerp(_FairyBounce.rgb, _FairySky.rgb, saturate(n.y * 0.5h + 0.5h)) * _Ambient;
                half occ = lerp(1.0h, ao, _AOStrength);
                half3 colour = albedo * (direct * lerp(1.0h, occ, 0.55h) + ambient * occ);
            #if !defined(_LITE)
                // a rim of light on the edges: warm where the sun is, lilac in the shade
                half f = 1.0h - saturate(dot(n, view));
                half rim = f * f * f * _RimColor.a * (0.4h + 0.6h * occ);
                colour += lerp(_RimShade.rgb * 0.6h, _RimColor.rgb, lit) * rim;
            #endif
                return colour;
            }

            // the gloom: grey-violet and dim (a touch: the garden stays vivid)
            half3 Gloom(half3 colour)
            {
                half luma = dot(colour, half3(0.3h, 0.55h, 0.15h));
                half3 grey = lerp(luma.xxx, colour, 0.5h) * half3(0.58h, 0.52h, 0.76h) * 1.15h;
                return lerp(colour, grey, _FairyGloom * 0.22h);
            }

            // the haze: the further away and the lower, the more of it; its colour from near (warm) to far (the horizon)
            half4 Haze(float3 ws, half glow)
            {
                float dist = distance(ws, _WorldSpaceCameraPos);
                half f = saturate((dist - _FairyFog.x) / max(1.0, _FairyFog.y - _FairyFog.x));
                f = f * (2.0h - f);
                half low = saturate(1.0h - max(0.0, ws.y) * _FairyFog.w);
                f *= lerp(0.55h, 1.0h, low) * _FairyFog.z * _FogScale * (1.0h - glow * 0.6h);
                half3 c = lerp(_FairyHaze.rgb, _FairyHazeFar.rgb, saturate((dist - _FairyFog.x) / max(1.0, _FairyFog.y * 1.6 - _FairyFog.x)));
                return half4(c, f);
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
            #if defined(_SKY)
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                // at the far plane: drawn after the garden, only where nothing else is
            #if UNITY_REVERSED_Z
                o.positionCS.z = o.positionCS.w * 1e-6;
            #else
                o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
            #endif
                o.colour = v.color.rgb;
                return o;
            #else
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
            #if defined(_GROUND)
                half sway = 0;
                half glow = 0;
            #else
                half sway = v.uv.x * _Sway;
                half glow = v.uv.y;
                // the wind: a slow gust rolling across the garden and a quicker flutter at the tips (bigger things sway more)
                if (sway > 0.001)
                {
                    float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float phase = dot(ws.xz, float2(0.11, 0.07)) + _Time.y * _FairyWind.w;
                #if defined(_LITE)
                    float gust = sin(phase + ws.x * 1.3) * 0.9;
                #else
                    float gust = sin(phase) * 0.65 + sin(phase * 2.3 + 1.7) * 0.35 + sin(_Time.y * 5.3 + ws.x * 2.1 + ws.z * 1.7) * 0.18;
                #endif
                    float3 push = float3(_FairyWind.x, 0, _FairyWind.y) * gust * _FairyWind.z * sway * scale;
                    ws += push - float3(0, dot(push, push) * 0.3 / max(0.3, scale), 0);
                }
            #endif
                o.positionCS = TransformWorldToHClip(ws);
                float3 view = normalize(GetWorldSpaceViewDir(ws));
                half lit;
                half4 fog = Haze(ws, glow);
            #if defined(_GROUND)
                // the pixel: colour * albedo + haze (the haze's share already taken off the light)
                o.colour = Shade(half3(1, 1, 1), v.color.a, n, view, lit) * (1.0h - fog.a);
                o.haze = half4(fog.rgb * fog.a, v.uv.x * 6.0h - 4.3h);
                o.grass = v.color.rgb * 2.0h;
                o.detailUV = ws.xz * _DetailScale;
            #else
                half3 albedo = v.color.rgb;
                half3 colour = Shade(albedo, v.color.a, n, view, lit);
                colour = lerp(colour, albedo * _Glow, glow);
                o.colour = lerp(Gloom(colour), fog.rgb, fog.a);
            #endif
                return o;
            #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
            #if defined(_SKY)
                return half4(i.colour, 1.0h);
            #else
            #if defined(_GROUND)
                half3 d = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.detailUV).rgb;
                // the path's ragged edge: its mask nudged by the wobble, made crisp; the grass's dabs; the cobbles
                half path = saturate(d.b * 3.6h + i.haze.a);
                half3 albedo = lerp(i.grass * d.r, lerp(_PathShade.rgb, _PathColor.rgb, d.g), path);
                half3 colour = i.colour * albedo + i.haze.rgb;
                if (_FairyGloom > 0.001) colour = Gloom(colour);
                return half4(colour, 1.0h);
            #else
                return half4(i.colour, 1.0h);
            #endif
            #endif
            }
            ENDHLSL
        }
    }
}
