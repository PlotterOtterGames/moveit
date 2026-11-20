// Zana!'s soap bubble (URP): the gloom bubble a pixie is caught in. Clear in the middle, a rainbow-tinted rim (the
// colours of a soap film drifting round it), a soft highlight; _Fade lets it burst away. Transparent, no depth written.
Shader "Tundu/Bubble"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.75, 0.55, 1, 0.25)
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, view)), 2.2h);
                half hue = frac(dot(n, float3(0.4, 0.7, 0.2)) * 1.5h + _Time.y * 0.08h);
                half3 film = saturate(abs(frac(hue + half3(0.0h, 0.666h, 0.333h)) * 6.0h - 3.0h) - 1.0h);
                half3 c = lerp(_Tint.rgb, film, 0.55h) + rim * 0.5h;
                // a soft window highlight up and to the left
                half spot = pow(saturate(dot(n, normalize(float3(-0.4, 0.7, -0.3)))), 24.0h);
                c += spot;
                half a = saturate(_Tint.a + rim * 0.8h + spot) * _Fade;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
