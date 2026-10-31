// Tundu's effects (URP): unlit, soft alpha, tinted by the particle / vertex colour. Dust, sparkles, stars, speed
// lines and blob shadows all use it with different textures. Cheap on the box (no lighting, no fog).
// _VIGNETTE (Vishu!'s camera picture): darkens toward _VignetteColor near the edges, from uv1 (0..1 across the quad):
// the mirror's vignette, which had been a second full-screen layer blended over everything every frame.
Shader "Tundu/Fx"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test (Always = markers drawn on top)", Float) = 4
        _VignetteColor ("Vignette colour (alpha = strength, with _VIGNETTE)", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _VIGNETTE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _VignetteColor;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                #if defined(_VIGNETTE)
                    float2 quad : TEXCOORD1;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                #if defined(_VIGNETTE)
                    float2 quad : TEXCOORD1;   // -1..1 across the quad
                #endif
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color * _BaseColor;
                #if defined(_VIGNETTE)
                    o.quad = v.quad * 2.0 - 1.0;
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * i.color;
                #if defined(_VIGNETTE)
                    // a rounded-rectangle falloff (|u|^4 + |v|^4)^(1/4): clear in the middle, darker toward the edges
                    float2 a = abs(i.quad);
                    a *= a;
                    a *= a;
                    half k = smoothstep(0.62, 1.18, sqrt(sqrt(a.x + a.y))) * 0.62 * _VignetteColor.a;
                    c.rgb = lerp(c.rgb, _VignetteColor.rgb, k);
                #endif
                return c;
            }
            ENDHLSL
        }
    }
}
