// Vrapo!'s valley (URP), the console polish of 2026-11-18: a prehistoric valley in a storybook light, everything lit at
// the vertices like Zana!'s garden (Tundu/Realm: on the box at 200 MHz every vertex runs the light and the pixel is
// nearly free), on the runner's curved world (the ground bends away over the horizon: far things rise over it instead of
// popping in; _TunduCurve, metres of drop per metre squared ahead of the camera), its colours from the time of day.
//
// The props (art/runner/valley.py) carry their own colours: vertex colour rgb = the colour (linear), a = baked ambient
// occlusion; uv0.x = how much it sways in the wind, uv0.y = how much it glows (lava, eggs, the sky's own light: the night
// makes glowing things shine brighter, _ValleyNight).
// The light: a soft toon terminator (wrapped), the shade a hue-shifted colour of the time of day (cool blue-violet by day,
// deep blue at night), the sky's light from above and the grass's green bounce from below, a rim of light on the edges,
// the baked occlusion. Then the haze: the further and the lower, the more it melts into the horizon's colour.
// Globals set by RunnerWorld every frame (the time of day blends them): _ValleyShade, _ValleySky, _ValleyBounce,
// _ValleyRim (rgb, a = strength), _ValleyRimShade, _ValleyHaze (near), _ValleyHazeFar, _ValleyFog (start, end, most, height
// falloff), _ValleyWind (x, z, metres, speed), _ValleyNight (0 day .. 1 night), _ValleySkyTop, _ValleySkyHorizon,
// _ValleySkyGlow (the sun's glow round its direction, a = size), _ValleySun (where the sun is, for the sky's glow).
//
//   _GROUND   the valley floor: vertex colour = the grass's colour; uv0.x = the path (0 grass .. 1 path), uv0.y = the worn
//             trails in it; uv1 = the tile's own metres (x, z) for one read of the detail map (R grass dabs, G the path's
//             pebbles, cracks and footprints, 0.5 = none, B the edge's wobble): a crisp ragged edge between path and grass.
//             The grass and the path's earth are lit and hazed at the vertices; the pixel only adds the detail (three
//             values passed down, the fade worked out from the pixel's own depth and uv: on the box the pixel's work
//             is mostly fetching what's passed down). The detail fades out toward DETAIL_FAR metres and toward the banks
//             (DETAIL_SIDE), where the floor is drawn with _GROUND _LITE instead: everything at the vertices, the
//             detail's average in its place (on the box the near floor's pixels were ~5.4 ms of a 42 ms frame at 300
//             MHz with five values passed down, 2.9 ms with four). The floor is drawn after the scenery (RunnerWorld
//             puts it in a later queue): its pixels under the plants are never worked out.
//   _SKY      the sky dome round the camera: its gradient worked out at the vertices from the time of day, the sun's glow
//             per pixel; drawn at the far plane (after everything, only where nothing is)
//   _LITE     the small and far things: no rim, one wave of wind
//   _Curve    how much of the world's bend it takes (the backdrop less: far mountains would sink out of sight)
//   _Cull     0 for single-sided leaves and grass (their normals point up-ish: both sides light alike)
Shader "Tundu/Valley"
{
    Properties
    {
        _Softness ("Terminator Softness", Range(0.02, 0.8)) = 0.2
        _Wrap ("Light Wrap", Range(0, 1)) = 0.42
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.5
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.8
        _Glow ("Glow Boost", Range(0, 4)) = 1.5
        _Sway ("Wind Sway (0 = none)", Range(0, 2)) = 1
        _FogScale ("Haze taken", Range(0, 1)) = 1
        _Curve ("Bends with the world", Range(0, 1)) = 1
        _DetailMap ("Ground Detail (R grass, G path, B edge)", 2D) = "grey" {}
        _DetailScale ("Detail Tiles a Metre (x, z)", Vector) = (0.2272727, 0.2, 0, 0)
        _PathColor ("Path", Color) = (0.93, 0.74, 0.5, 1)
        _TrailColor ("Worn Trails", Color) = (0.8, 0.58, 0.38, 1)
        _PathShade ("Path Cracks", Color) = (0.55, 0.36, 0.3, 1)
        _PathLight ("Path Pebbles", Color) = (1.0, 0.92, 0.78, 1)
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
            // every instance is scaled the same way in x, y and z: the normals turn with the object's own matrix, so no
            // inverse matrix is fetched for every vertex (on the box the vertices' loads were most of the scenery's cost)
            #pragma instancing_options assumeuniformscaling nolightprobe nolodfade
            #pragma multi_compile_local _ _GROUND _SKY
            #pragma multi_compile_local _ _LITE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Wrap;
                half _Ambient;
                half _AOStrength;
                half _Glow;
                half _Sway;
                half _FogScale;
                half _Curve;
                float4 _DetailMap_ST;
                float4 _DetailScale;
                half4 _PathColor;
                half4 _TrailColor;
                half4 _PathShade;
                half4 _PathLight;
                half _Cull;
            CBUFFER_END
            TEXTURE2D(_DetailMap);
            SAMPLER(sampler_DetailMap);

            float _TunduCurve;
            float4 _ValleyShade, _ValleySky, _ValleyBounce, _ValleyRim, _ValleyRimShade, _ValleyHaze, _ValleyHazeFar,
                   _ValleyFog, _ValleyWind, _ValleySkyTop, _ValleySkyHorizon, _ValleySkyGlow, _ValleySun;
            float _ValleyNight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            #if defined(_GROUND) && !defined(_LITE)
                float2 uv : TEXCOORD0;          // the detail map's uv (x = metres aside x _DetailScale.x)
                half4 grass : TEXCOORD1;        // rgb = the grass, lit and hazed (finished but for the detail); a = the path
                half4 earth : TEXCOORD2;        // rgb = the path's earth, lit and hazed; a = the light's brightness
            #else
                half3 colour : TEXCOORD0;       // the finished colour
            #endif
            #if defined(_SKY)
                float3 dir : TEXCOORD1;
            #endif
            };

            // the detail map's average (tools/runner/ground.py's ground_detail.png): where the detail fades out, this stands
            // in for it, so the far and the banks' floor (_LITE) meets the near one without a seam
            #define DETAIL_MEAN half3(0.509h, 0.491h, 0.489h)
            #define DETAIL_FAR 40.0
            #define DETAIL_FADE 14.0
            #define DETAIL_SIDE 6.5

            half GrassTone(half r) { return 0.62h + r * 0.76h; }

            half3 Earth(half trail, half g)
            {
                half3 earth = lerp(_PathColor.rgb, _TrailColor.rgb, trail);
                return g < 0.5h ? lerp(_PathShade.rgb, earth, g * 2.0h) : lerp(earth, _PathLight.rgb, g * 2.0h - 1.0h);
            }

            float3 Bend(float3 ws)
            {
                float d = max(0.0, ws.z - _WorldSpaceCameraPos.z);
                ws.y -= _TunduCurve * _Curve * d * d;
                return ws;
            }

            // the light at a point (albedo linear, ao 0..1): returns the lit colour
            half3 Shade(half3 albedo, half ao, float3 n, float3 view)
            {
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half wrapped = (ndl + _Wrap) / (1.0h + _Wrap);
                half lit = smoothstep(0.5h - _Softness, 0.5h + _Softness, wrapped);
                half3 direct = lerp(_ValleyShade.rgb, light.color, lit);
                half3 ambient = lerp(_ValleyBounce.rgb, _ValleySky.rgb, saturate(n.y * 0.5h + 0.5h)) * _Ambient;
                half occ = lerp(1.0h, ao, _AOStrength);
                half3 colour = albedo * (direct * lerp(1.0h, occ, 0.6h) + ambient * occ);
            #if !defined(_LITE) || defined(_GROUND)
                // (the floor keeps its rim in the far _LITE version too: toward the horizon it's most of its glow)
                half f = 1.0h - saturate(dot(n, view));
                half rim = f * f * f * _ValleyRim.a * (0.35h + 0.65h * occ);
                colour += lerp(_ValleyRimShade.rgb * 0.6h, _ValleyRim.rgb, lit) * rim;
            #endif
                return colour;
            }

            // the haze: the further away and the lower, the more of it; its colour from near to far
            half4 Haze(float3 ws, half glow)
            {
                float dist = distance(ws, _WorldSpaceCameraPos);
                half f = saturate((dist - _ValleyFog.x) / max(1.0, _ValleyFog.y - _ValleyFog.x));
                f = f * (2.0h - f);
                half low = saturate(1.0h - max(0.0, ws.y) * _ValleyFog.w);
                f *= lerp(0.55h, 1.0h, low) * _ValleyFog.z * _FogScale * (1.0h - glow * 0.7h);
                half3 c = lerp(_ValleyHaze.rgb, _ValleyHazeFar.rgb, saturate((dist - _ValleyFog.x) / max(1.0, _ValleyFog.y * 1.5 - _ValleyFog.x)));
                return half4(c, f);
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
            #if defined(_SKY)
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
            #if UNITY_REVERSED_Z
                o.positionCS.z = o.positionCS.w * 1e-6;
            #else
                o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
            #endif
                float3 dir = normalize(v.positionOS.xyz);
                o.dir = dir;
                // the gradient: the horizon's band, the top's deep colour above it
                half up = saturate(dir.y);
                half3 c = lerp(_ValleySkyHorizon.rgb, _ValleySkyTop.rgb, pow(up, 0.55h));
                // below the horizon (seen only past the bend): the haze
                c = lerp(c, _ValleyHazeFar.rgb, saturate(-dir.y * 6.0));
                o.colour = c;
                return o;
            #else
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
            #if defined(_GROUND)
                half glow = 0;
            #else
                half sway = v.uv.x * _Sway;
                half glow = v.uv.y;
                // the wind: a slow gust rolling across the valley and a quicker flutter at the tips (bigger things sway more)
                if (sway > 0.001)
                {
                    float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float phase = dot(ws.xz, float2(0.11, 0.07)) + _Time.y * _ValleyWind.w;
                #if defined(_LITE)
                    float gust = sin(phase + ws.x * 1.3) * 0.9;
                #else
                    float gust = sin(phase) * 0.65 + sin(phase * 2.3 + 1.7) * 0.35 + sin(_Time.y * 5.3 + ws.x * 2.1 + ws.z * 1.7) * 0.18;
                #endif
                    float3 push = float3(_ValleyWind.x, 0, _ValleyWind.y) * gust * _ValleyWind.z * sway * scale;
                    ws += push - float3(0, dot(push, push) * 0.3 / max(0.3, scale), 0);
                }
            #endif
                o.positionCS = TransformWorldToHClip(Bend(ws));
                float3 view = normalize(GetWorldSpaceViewDir(ws));
                half4 fog = Haze(ws, glow);
            #if defined(_GROUND)
                half3 light = Shade(half3(1, 1, 1), v.color.a, n, view) * (1.0h - fog.a);
            #if defined(_LITE)
                // far away and the banks: the floor's colour at the vertices, the detail's average in its place
                half3 albedo = lerp(v.color.rgb * GrassTone(DETAIL_MEAN.r), Earth(v.uv.y, DETAIL_MEAN.g), v.uv.x);
                o.colour = light * albedo + fog.rgb * fog.a;
            #else
                half3 haze = fog.rgb * fog.a;
                o.uv = v.uv1 * _DetailScale.xy;
                o.grass = half4(light * v.color.rgb + haze, v.uv.x);
                o.earth = half4(light * lerp(_PathColor.rgb, _TrailColor.rgb, v.uv.y) + haze, dot(light, half3(0.2126h, 0.7152h, 0.0722h)));
            #endif
            #else
                half3 albedo = v.color.rgb;
                half3 colour = Shade(albedo, v.color.a, n, view);
                // glowing parts: their own colour, brighter at night
                colour = lerp(colour, albedo * _Glow * (1.0h + _ValleyNight * 0.8h), glow);
                o.colour = lerp(colour, fog.rgb, fog.a);
            #endif
                return o;
            #endif
            }

            half4 frag(Varyings i) : SV_Target
            {
            #if defined(_SKY)
                // the sun's soft glow round where it is (per pixel: the dome's vertices are too far apart for a disc)
                half k = saturate(dot(normalize(i.dir), _ValleySun.xyz));
                half glow = pow(k, _ValleySkyGlow.a) * 0.75h + pow(k, _ValleySkyGlow.a * 0.12h) * 0.25h;
                return half4(i.colour + _ValleySkyGlow.rgb * glow, 1.0h);
            #elif defined(_GROUND) && !defined(_LITE)
                // how much detail: all of it near the path, its average far away (the pixel's depth) and toward the banks
                float depth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half detail = saturate((DETAIL_FAR - depth) / DETAIL_FADE)
                            * saturate((DETAIL_SIDE - abs(i.uv.x) / _DetailScale.x) / 1.5);
                half3 d = lerp(DETAIL_MEAN, SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv).rgb, detail);
                // the path's ragged edge: its amount nudged by the wobble, made crisp (soft where the detail has faded:
                // there it meets the _LITE floor's edge, interpolated between the vertices)
                half path = saturate((i.grass.a - 0.571h + (d.b - 0.5h) * 0.55h) * lerp(1.5h, 7.0h, detail) + 0.5h);
                // the path's cracks darken its earth, its pebbles turn it pale (toward _PathLight, by the light's brightness)
                half k = d.g - 0.5h;
                half3 earth = k < 0.0h ? i.earth.rgb * (1.0h + 1.4h * k)
                                       : i.earth.rgb + (_PathLight.rgb - _PathColor.rgb) * (k * 2.0h * i.earth.a);
                return half4(lerp(i.grass.rgb * GrassTone(d.r), earth, path), 1.0h);
            #else
                return half4(i.colour, 1.0h);
            #endif
            }
            ENDHLSL
        }
    }
}
