// Yjet!'s suns (URP, 2026-11-14): a star's surface boiling (two of the planets' height maps sliding over each other,
// tools/space/heights.py's swirls and cells), hot white in the middle, its own colour toward the edge, brighter granules.
// Unlit: it is the light. Its corona is a Tundu/Halo quad.
Shader "Tundu/Star"
{
    Properties
    {
        _Heights ("Height maps (array)", 2DArray) = "" {}
        [HDR] _Color ("Colour (the edge)", Color) = (1, 0.7, 0.3, 1)
        [HDR] _Core ("Colour (the middle)", Color) = (1, 0.97, 0.85, 1)
        _Boil ("Boiling speed", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Core;
                float _Boil;
            CBUFFER_END
            TEXTURE2D_ARRAY(_Heights);
            SAMPLER(sampler_Heights);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Boil;
                half a = SAMPLE_TEXTURE2D_ARRAY(_Heights, sampler_Heights, i.uv * float2(2.0, 2.0) + float2(t * 0.004, t * 0.0013), 4).r;
                half b = SAMPLE_TEXTURE2D_ARRAY(_Heights, sampler_Heights, i.uv * float2(3.0, 3.0) - float2(t * 0.0031, 0.0), 3).r;
                half boil = a * 0.6h + b * 0.4h;
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half facing = saturate(dot(n, v));
                half3 colour = lerp(_Color.rgb, _Core.rgb, pow(facing, 0.7h));
                colour *= 0.78h + 0.45h * boil;
                colour += _Color.rgb * pow(1.0h - facing, 3.0h) * 0.6h;
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
}
