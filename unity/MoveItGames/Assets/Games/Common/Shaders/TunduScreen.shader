// LED screens (Sporte!'s ad boards and big screens): a texture that glows a little through the bloom, scrolling along
// uv.x (_Scroll = texture widths a second), with a fine LED grid (_Led) so it reads as a screen up close. Unlit, fogged.
Shader "Tundu/Screen"
{
    Properties
    {
        [MainTexture] _BaseMap ("Picture", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (1.15, 1.15, 1.15, 1)
        _Scroll ("Scroll (widths a second)", Float) = 0
        _Led ("LED Grid", Range(0, 1)) = 0.25
        _LedDensity ("LED Density (dots per uv)", Vector) = (400, 40, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                float _Scroll;
                half _Led;
                float4 _LedDensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 raw : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap) + float2(_Time.y * _Scroll, 0);
                o.raw = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Color.rgb;
                float2 cell = frac(i.raw * _LedDensity.xy) - 0.5;
                half led = saturate(1.0 - dot(cell, cell) * 3.0);
                c *= lerp(1.0h, 0.55h + 0.65h * led, _Led);
                return half4(MixFog(c, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }
    }
}
