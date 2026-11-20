// Vishu!'s mirror, the passes before it is drawn (a quad drawn into render textures by MirrorLook's command buffer):
// 0 Denoise: the new camera frame averaged with the last result where nothing moved (a dim room's picture is noisy;
//   static parts get steady over a few frames, moving parts take the new frame at once: no ghosts).
// 1 Down: a quarter-size copy (four bilinear taps), 2 Blur: a soft blur of it (the room behind the player),
// 3 Person: the body mask (_TunduBodyMask, drawn from the skeleton by OutfitRenderer) widened and softened: where the
//   player is, generously, so hair and loose sleeves aren't blurred (alpha); and the room's blurred colour at the same
//   place on the screen (rgb: _MainTex = the blurred picture, through _PictureST, the mirror's own mapping), so the
//   mirror reads both from one small map (2026-11-12: one texture read less for every pixel of the screen).
// _Region (xy origin, zw size, in the texture's 0..1): the quad drawn over that part of the target only, with the same
// texture coordinates (Denoise and Down only work where the mirror shows the picture: MirrorLook).
Shader "Hidden/Tundu/MirrorPrep"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _PrevTex ("Last denoised frame", 2D) = "black" {}
        _Keep ("Denoise: how much of the last frame a still pixel keeps", Range(0, 0.95)) = 0.62
        _Still ("Denoise: changes below this are noise (sqrt space)", Float) = 0.035
        _Moving ("Denoise: changes above this are movement", Float) = 0.10
        _Radius ("Person: widening (uv)", Float) = 0.03
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_PrevTex);
    SAMPLER(sampler_PrevTex);
    TEXTURE2D(_TunduBodyMask);
    SAMPLER(sampler_TunduBodyMask);
    float4 _MainTex_TexelSize;
    float4 _Region;
    float4 _PictureST;   // Person: the screen's 0..1 -> the camera picture's texture coordinates (xy scale, zw offset)
    float _Keep, _Still, _Moving, _Radius;

    struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

    Varyings vert(Attributes v)
    {
        Varyings o;
        float4 region = any(_Region.zw) ? _Region : float4(0, 0, 1, 1);
        o.positionCS = TransformObjectToHClip(float3(region.xy + v.positionOS.xy * region.zw, v.positionOS.z));
        o.uv = region.xy + v.uv * region.zw;
        return o;
    }

    half4 denoise(Varyings i) : SV_Target
    {
        half3 cur = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
        half3 prev = SAMPLE_TEXTURE2D(_PrevTex, sampler_PrevTex, i.uv).rgb;
        half3 d = abs(sqrt(cur) - sqrt(prev));
        half diff = max(d.r, max(d.g, d.b));
        half keep = _Keep * (1.0 - smoothstep(_Still, _Moving, diff));
        return half4(lerp(cur, prev, keep), 1.0);
    }

    half4 down(Varyings i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy;
        half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + t * float2(-1, -1)).rgb
                + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + t * float2(1, -1)).rgb
                + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + t * float2(-1, 1)).rgb
                + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + t * float2(1, 1)).rgb;
        return half4(c * 0.25, 1.0);
    }

    half4 blur(Varyings i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy * 1.5;
        half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * 0.25;
        c += (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(t.x, 0)).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(t.x, 0)).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0, t.y)).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0, t.y)).rgb) * 0.125;
        c += (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + t).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - t).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(t.x, -t.y)).rgb
            + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(-t.x, t.y)).rgb) * 0.0625;
        return half4(c, 1.0);
    }

    half4 person(Varyings i) : SV_Target
    {
        half m = SAMPLE_TEXTURE2D(_TunduBodyMask, sampler_TunduBodyMask, i.uv).r;
        float2 r = float2(_Radius * 0.5625, _Radius);
        [unroll] for (int k = 0; k < 8; k++)
        {
            float a = k * 0.785398;
            m = max(m, SAMPLE_TEXTURE2D(_TunduBodyMask, sampler_TunduBodyMask, i.uv + float2(cos(a), sin(a)) * r).r * 0.9);
            m = max(m, SAMPLE_TEXTURE2D(_TunduBodyMask, sampler_TunduBodyMask, i.uv + float2(cos(a), sin(a)) * r * 2.0).r * 0.55);
        }
        half3 room = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * _PictureST.xy + _PictureST.zw).rgb;
        return half4(room, smoothstep(0.05, 0.75, m));
    }
    ENDHLSL

    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment denoise
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment down
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment blur
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment person
            ENDHLSL }
    }
}
