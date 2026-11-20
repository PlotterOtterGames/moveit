// Zana!'s creatures (URP): one mesh a species, put together from the gloom kit (GloomViews.Combine). Every vertex carries
// its region in uv1.x (body, belly, accent, eye white, pupil, mouth, wing, cheek) and its baked shade in uv1.y; the
// species' bright colours are a table (_Colors, linear, set per creature). While it's gloomy (_Purify 0) the colours are
// greyed toward a dark violet, the pupils glow, a violet aura rims it; purified (1) its own bright colours, a warm rim.
// Its faces come twice: vertex colour b 0.5 = grumpy (shown while gloomy), 1 = happy (shown purified), 0 = always; a
// hidden one collapses onto its middle (uv2). Wings flap in the vertex shader round their hinge (uv2), as far out as
// vertex colour r says, the side in g. _Flash whitens it (a bubble popped), _Fade shrinks it away.
// _VERTEX_LIT (2026-11-16; Zana!'s runtime copy turns it on): the colour, the light, the rim and the shine at the vertices,
// the pixel only fogs it (a busy wave's ~50 creatures lit per pixel were ~40 % of a hot box's frame).
// 2026-11-17 (the garden's new look): the garden's light (Tundu/Realm: a wrapped toon terminator, a hue-shifted shade, the
// sky above and the meadow's bounce below) and its haze (_FairySky, _FairyBounce, _FairyHaze, _FairyHazeFar, _FairyFog);
// gloomy, a deep plum with a magenta-violet rim and glowing eyes, so they read against the green.
Shader "Tundu/Gloom"
{
    Properties
    {
        _ShadeColor ("Shade Tint", Color) = (0.56, 0.58, 0.95, 1)
        _ShadeSoftness ("Shade Softness", Range(0.02, 0.8)) = 0.22
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.5
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.8
        _RimGloom ("Gloomy Aura (alpha = strength)", Color) = (0.95, 0.35, 1, 1.1)
        _RimBright ("Purified Rim (alpha = strength)", Color) = (1, 0.93, 0.78, 0.45)
        _EyeGlow ("Gloomy Eyes' Glow", Color) = (1, 0.4, 0.95, 1)
        _GloomTint ("Gloom Tint", Color) = (0.36, 0.26, 0.52, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _VERTEX_LIT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadeColor;
                half _ShadeSoftness;
                half _Ambient;
                half _AOStrength;
                half4 _RimGloom;
                half4 _RimBright;
                half4 _EyeGlow;
                half4 _GloomTint;
            CBUFFER_END

            float4 _Colors[8];
            float _Purify, _Flash, _Phase, _FlapSpeed, _Fade;
            float4 _FairySky, _FairyBounce, _FairyHaze, _FairyHazeFar, _FairyFog;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 region : TEXCOORD1;
                float2 hinge : TEXCOORD2;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            #if defined(_VERTEX_LIT)
                half3 colour : TEXCOORD0;
            #else
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 region : TEXCOORD2;
            #endif
                half4 fog : TEXCOORD3;
            };

            // the garden's haze (Tundu/Realm's): the further and the lower, the more
            half4 Haze(float3 ws)
            {
                float dist = distance(ws, _WorldSpaceCameraPos);
                half f = saturate((dist - _FairyFog.x) / max(1.0, _FairyFog.y - _FairyFog.x));
                f = f * (2.0h - f);
                half low = saturate(1.0h - max(0.0, ws.y) * _FairyFog.w);
                f *= lerp(0.55h, 1.0h, low) * _FairyFog.z;
                return half4(lerp(_FairyHaze.rgb, _FairyHazeFar.rgb, saturate((dist - _FairyFog.x) / max(1.0, _FairyFog.y * 1.6 - _FairyFog.x))), f);
            }

            /// The creature's colour at a point: its region's colour (greyed while gloomy), the toon light, the sky, the
            /// rim (a gloomy aura, a warm edge purified), the glow of gloomy eyes, a little shine.
            half3 Shade(float2 region, float3 n, float3 positionWS)
            {
                int r = clamp((int)round(region.x), 0, 7);
                half3 bright = _Colors[r].rgb;
                half luma = dot(bright, half3(0.3h, 0.55h, 0.15h));
                half3 gloomy = lerp(luma.xxx, bright, 0.35h) * _GloomTint.rgb * 0.72h;
                half pupil = r == 4 ? 1.0h : 0.0h;
                half eyeWhite = r == 3 ? 1.0h : 0.0h;
                // gloomy eyes: dark whites, glowing pupils
                gloomy = lerp(gloomy, half3(0.16h, 0.12h, 0.24h), eyeWhite);
                half3 albedo = lerp(gloomy, bright, _Purify);
                float3 view = normalize(GetWorldSpaceViewDir(positionWS));
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half wrapped = (ndl + 0.45h) / 1.45h;
                half lit = smoothstep(0.5h - _ShadeSoftness, 0.5h + _ShadeSoftness, wrapped);
                half ao = lerp(1.0h, (half)region.y, _AOStrength);
                half3 colour = lerp(albedo * _ShadeColor.rgb, albedo * light.color, lit) * ao;
                half3 sky = lerp(_FairyBounce.rgb, _FairySky.rgb, saturate(n.y * 0.5h + 0.5h));
                colour += albedo * sky * _Ambient * ao * (0.5h + 0.5h * _Purify);
                half4 rimColour = lerp(_RimGloom, _RimBright, _Purify);
                half rim = pow(1.0h - saturate(dot(n, view)), lerp(4.5h, 2.6h, _Purify)) * rimColour.a;   // gloomy: a thin glowing edge
                colour += rimColour.rgb * rim;
                // the glow of gloomy eyes, a soft shine on happy ones
                colour += _EyeGlow.rgb * pupil * (1.0h - _Purify) * 1.2h;
                half3 h = normalize(light.direction + view);
                colour += light.color * pow(saturate(dot(n, h)), 40.0h) * 0.35h * (0.4h + 0.6h * _Purify) * lit;
                return colour;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float3 n = v.normalOS;
                // the face sets: a hidden one collapses onto its middle
                float face = v.color.b;
                float show = face < 0.25 ? 1.0 : (face < 0.75 ? 1.0 - _Purify : _Purify);
                show = saturate(show * 1.6 - 0.3);
                if (face > 0.25) p.xy = lerp(v.hinge, p.xy, show);
                // the wings beat round their hinge (the creature's forward axis)
                float flap = v.color.r;
                if (flap > 0.001)
                {
                    float side = v.color.g * 2.0 - 1.0;
                    float a = sin(_Time.y * _FlapSpeed + _Phase) * 0.75 * flap * side;
                    float s = sin(a), c = cos(a);
                    float2 d = p.xy - v.hinge;
                    p.xy = v.hinge + float2(d.x * c - d.y * s, d.x * s + d.y * c);
                    n.xy = float2(n.x * c - n.y * s, n.x * s + n.y * c);
                }
                p *= saturate(_Fade);
                float3 ws = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(ws);
            #if defined(_VERTEX_LIT)
                o.colour = Shade(v.region, normalize(TransformObjectToWorldNormal(n)), ws);
            #else
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(n);
                o.region = v.region;
            #endif
                o.fog = Haze(ws);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
            #if defined(_VERTEX_LIT)
                half3 colour = i.colour;
            #else
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                half3 colour = Shade(i.region, n, i.positionWS);
            #endif
                colour = lerp(colour, half3(1, 1, 1), saturate(_Flash) * 0.75h);
                return half4(lerp(colour, i.fog.rgb, i.fog.a), 1.0h);
            }
            ENDHLSL
        }
    }
}
