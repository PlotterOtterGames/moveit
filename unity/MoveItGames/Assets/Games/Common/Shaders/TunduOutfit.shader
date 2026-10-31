// Vishu!'s clothes (URP, unlit, alpha blended): the outfit atlas (tools/dress/outfits.py) coloured per piece.
// _MainTex holds fixed colours, or a grey where the piece takes the chosen colour (_MaskTex = 1 there):
// grey 0.8 = the colour itself, darker = its shade, lighter = toward white (sheen, highlights). The tint comes in
// the vertex colour (sRGB values; the maths runs in sRGB, so the art looks the same in a linear project). The atlas
// and the thumbnails are imported as data, not sRGB (DressArtImport), so the shader reads their sRGB values as they
// are; converting them back per pixel, and the result to linear with pow, cost the box's GPU most of this shader
// (every layer of clothing pays it again).
// uv1: x = finish (0 plain, 1 rainbow, 2 glitter, 3 gold, 4 galaxy), y = hidden where (0 nowhere, 1 the body,
// 2 the arms: _TunduBodyMask, R = body, G = arms), z = white flash (a new piece pops in), w = a per-piece seed.
// In the camera mirror the clothes take a little of the room's light (_TunduAmbient: the camera picture's average
// colour, a mip-mapped copy). _Shadow 1 = the same mesh as a soft dark halo under the clothes (blurred alpha, nudged
// down): they sit on the body instead of floating over the picture.
Shader "Tundu/Outfit"
{
    Properties
    {
        [MainTexture] _MainTex ("Colour (grey where tinted)", 2D) = "white" {}
        _MaskTex ("Tint mask", 2D) = "black" {}
        _Tint ("Tint (UI)", Color) = (1, 1, 1, 1)
        [Toggle] _UseExtra ("Read uv1 (off for the UI: Graphics.DrawTexture leaves it undefined)", Float) = 1
        [Toggle] _LinearOut ("Linear output (off for IMGUI, which draws in sRGB)", Float) = 1
        [Toggle] _Shadow ("Shadow pass", Float) = 0
        _ShadowOffset ("Shadow offset (world)", Vector) = (3, -6, 0, 0)
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.28
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _UseExtra;
                float _LinearOut;
                float _Shadow;
                float4 _ShadowOffset;
                float _ShadowStrength;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_TunduBodyMask);
            SAMPLER(sampler_TunduBodyMask);
            float4 _TunduBodyMaskRect;   // xy = world origin, zw = 1 / world size
            TEXTURE2D(_TunduAmbient);
            SAMPLER(sampler_TunduAmbient);
            float _TunduAmbientOn;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 extra : TEXCOORD1; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 extra : TEXCOORD1;
                float2 world : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz) + _ShadowOffset.xyz * _Shadow;
                o.positionCS = TransformWorldToHClip(world);
                o.world = world.xy;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.extra = v.extra * _UseExtra;
                o.color = v.color * _Tint;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 Hue(float h)
            {
                half3 k = saturate(abs(frac(h + half3(0.0, 0.6667, 0.3333)) * 6.0 - 3.0) - 1.0);
                return lerp(half3(1, 1, 1), k, 0.62);
            }

            // a tinted grey -> sRGB colour: the colour at 0.8, its shade below, toward white above
            half3 Shade(half3 tint, half g)
            {
                half3 base = tint * min(g / 0.8, 1.0);
                half hi = saturate((g - 0.8) / 0.2);
                return base + (1.0 - base) * hi * 0.75;
            }

            half Hidden(Varyings i)
            {
                if (i.extra.y <= 0.01) return 0.0;
                // behind the body (wings, capes, long hair: 0..1 = how much) or the arms (2: tops, skirts)
                half2 body = SAMPLE_TEXTURE2D(_TunduBodyMask, sampler_TunduBodyMask, (i.world - _TunduBodyMaskRect.xy) * _TunduBodyMaskRect.zw).rg;
                return i.extra.y > 1.5 ? body.g : body.r * saturate(i.extra.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Shadow > 0.5)
                {
                    half sa = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, i.uv, 2.5).a * i.color.a;
                    return half4(0, 0, 0, sa * _ShadowStrength * (1.0 - Hidden(i)));
                }
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);   // sRGB values (imported as data)
                half m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).r;
                half g = c.r;
                half3 tint = i.color.rgb;
                float t = _Time.y;
                int finish = (int)round(i.extra.x);
                half3 tinted;
                if (finish == 1)        // rainbow: bands of colour drifting across the piece
                {
                    tinted = Shade(Hue(i.world.x * 0.0016 + i.world.y * 0.0011 + t * 0.12 + i.extra.w), g);
                }
                else if (finish == 2)   // glitter: the colour, with tiny flakes that twinkle
                {
                    float2 cell = floor(i.world / 3.0);
                    float h = Hash(cell + i.extra.w * 17.0);
                    float tw = saturate(sin(t * (2.0 + h * 5.0) + h * 40.0) * 3.0 - 2.0) * step(0.86, h);
                    tinted = Shade(tint, g) + tw * 0.75 + (h - 0.5) * 0.08;
                }
                else if (finish == 3)   // gold: a metallic gradient and a highlight sweeping over it
                {
                    half3 dark = half3(0.62, 0.38, 0.08), light = half3(1.0, 0.86, 0.42);
                    half3 gold = lerp(dark, light, saturate(g * 1.15 - 0.05));
                    float sweep = saturate(1.0 - abs(frac((i.world.x + i.world.y) * 0.0018 - t * 0.35) - 0.5) * 9.0);
                    tinted = gold + sweep * 0.45 * saturate(g * 1.4 - 0.4);
                }
                else if (finish == 4)   // galaxy: deep blue and violet clouds with stars
                {
                    float n = sin(i.world.x * 0.012 + t * 0.3) * sin(i.world.y * 0.017 - t * 0.2) * 0.5 + 0.5;
                    half3 sky = lerp(half3(0.10, 0.08, 0.32), half3(0.45, 0.18, 0.62), n);
                    float h = Hash(floor(i.world / 4.0) + i.extra.w * 7.0);
                    float star = step(0.94, h) * (0.6 + 0.4 * sin(t * 3.0 + h * 50.0));
                    tinted = sky * min(g / 0.8, 1.15) + star;
                }
                else tinted = Shade(tint, g);

                half3 rgb = lerp(c.rgb, tinted, m);
                float ambientOn = _TunduAmbientOn * _UseExtra;   // not in the interface's thumbnails
                if (ambientOn > 0.001)
                {
                    // the room's light: a touch of its colour, darker in a dim room, never much
                    half3 amb = SAMPLE_TEXTURE2D_LOD(_TunduAmbient, sampler_TunduAmbient, float2(0.5, 0.5), 8).rgb;
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                        amb = sqrt(amb);   // close enough to sRGB for a faint tint, without pow
                    #endif
                    half l = dot(amb, half3(0.299, 0.587, 0.114));
                    half3 hue = clamp(amb / max(l, 0.04), 0.6, 1.5);
                    half3 light = lerp(half3(1, 1, 1), hue, 0.12) * lerp(1.0, clamp(l / 0.42, 0.72, 1.12), 0.4);
                    rgb *= lerp(half3(1, 1, 1), light, ambientOn);
                }
                rgb = lerp(rgb, half3(1, 1, 1), saturate(i.extra.z));
                half a = c.a * i.color.a * (1.0 - Hidden(i));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    if (_LinearOut > 0.5) rgb = FastSRGBToLinear(rgb);   // a cubic, no pow
                #endif
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
