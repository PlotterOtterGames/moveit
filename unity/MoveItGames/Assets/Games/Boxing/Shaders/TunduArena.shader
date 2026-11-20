// Superboks!'s arenas (URP), the overhaul of 2026-11-19: art/boxing/arena.py's kit lit at the vertices like Vrapo!'s
// valley and Zana!'s garden (on the box at 200-300 MHz every vertex runs the light and the pixel is nearly free).
//
// The kit carries its own colours: vertex colour rgb = the colour (linear), a = baked ambient occlusion; uv0.y = how much
// it glows (lamps, windows, neon, the city's lights: their own colour whatever the light, brighter at night).
// The light: a soft toon terminator (wrapped), the shade a colour of its own (each stage's: a warm plum indoors, a violet
// at sunset, a cool blue on the snow), the sky's light from above and the floor's bounce from below, a rim of light on
// the edges, the baked occlusion; then the haze: the further and the lower, the more it melts into the stage's air.
// Globals set by BoxingWorld for each stage: _ArenaShade, _ArenaSky, _ArenaBounce, _ArenaRim (rgb, a = strength),
// _ArenaRimShade, _ArenaHaze (near), _ArenaHazeFar, _ArenaFog (start, end, most, height falloff), _ArenaNight (0 .. 1).
//
//   _SKY    a dome round the camera, its colours painted in by the kit (unlit), drawn at the far plane
//   _LITE   far things: no rim
Shader "Tundu/Arena"
{
    Properties
    {
        _Softness ("Terminator Softness", Range(0.02, 0.8)) = 0.22
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.55
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.85
        _Glow ("Glow Boost", Range(0, 4)) = 1.35
        _FogScale ("Haze taken", Range(0, 1)) = 1
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
            #pragma multi_compile_local _ _SKY
            #pragma multi_compile_local _ _LITE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Wrap;
                half _Ambient;
                half _AOStrength;
                half _Glow;
                half _FogScale;
                half _Cull;
            CBUFFER_END

            float4 _ArenaShade, _ArenaSky, _ArenaBounce, _ArenaRim, _ArenaRimShade, _ArenaHaze, _ArenaHazeFar, _ArenaFog;
            float _ArenaNight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 colour : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
            #if defined(_SKY)
                // the dome follows the camera (it's far): its colours as painted, at the far plane
                float3 ws = TransformObjectToWorld(v.positionOS.xyz) + _WorldSpaceCameraPos;
                o.positionCS = TransformWorldToHClip(ws);
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
                o.positionCS = TransformWorldToHClip(ws);
                float3 view = normalize(GetWorldSpaceViewDir(ws));
                half3 albedo = v.color.rgb;
                half glow = saturate(v.uv.y);

                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half wrapped = (ndl + _Wrap) / (1.0h + _Wrap);
                half lit = smoothstep(0.5h - _Softness, 0.5h + _Softness, wrapped);
                half3 direct = lerp(_ArenaShade.rgb, light.color, lit);
                half3 ambient = lerp(_ArenaBounce.rgb, _ArenaSky.rgb, saturate(n.y * 0.5h + 0.5h)) * _Ambient;
                half occ = lerp(1.0h, v.color.a, _AOStrength);
                half3 colour = albedo * (direct * lerp(1.0h, occ, 0.6h) + ambient * occ);
            #if !defined(_LITE)
                half f = 1.0h - saturate(dot(n, view));
                half rim = f * f * f * _ArenaRim.a * (0.35h + 0.65h * occ);
                colour += lerp(_ArenaRimShade.rgb * 0.6h, _ArenaRim.rgb, lit) * rim;
            #endif
                // glowing parts: their own colour, brighter at night
                colour = lerp(colour, albedo * _Glow * (1.0h + _ArenaNight * 0.5h), glow);

                // the haze: further away and lower, more of it (glowing things shine through)
                float dist = distance(ws, _WorldSpaceCameraPos);
                half k = saturate((dist - _ArenaFog.x) / max(1.0, _ArenaFog.y - _ArenaFog.x));
                k = k * (2.0h - k);
                half low = saturate(1.0h - max(0.0, ws.y) * _ArenaFog.w);
                k *= lerp(0.6h, 1.0h, low) * _ArenaFog.z * _FogScale * (1.0h - glow * 0.65h);
                half3 haze = lerp(_ArenaHaze.rgb, _ArenaHazeFar.rgb, saturate((dist - _ArenaFog.x) / max(1.0, _ArenaFog.y * 1.5 - _ArenaFog.x)));
                o.colour = lerp(colour, haze, k);
                return o;
            #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.colour, 1.0h);
            }
            ENDHLSL
        }
    }
}
