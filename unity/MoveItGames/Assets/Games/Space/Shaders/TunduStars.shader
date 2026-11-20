// Yjet!'s star field (URP): every star a little quad turned to the camera in the vertex shader (uv0 = its corner -1..1,
// uv1.x = its size in metres, uv1.y = its twinkle's phase, the vertex colour its colour and brightness), a soft dot
// with a faint cross of light, twinkling, added to what's behind. The game keeps the mesh round the camera (stars are
// far): ~1500 stars are one draw call and a few small fragments each, far cheaper than stars worked out per sky pixel.
Shader "Tundu/Stars"
{
    Properties
    {
        _Twinkle ("Twinkle (0 = steady)", Range(0, 1)) = 0.45
        _Brightness ("Brightness", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Twinkle;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                float2 info : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 view = TransformWorldToView(TransformObjectToWorld(v.positionOS.xyz));
                view.xy += v.corner * v.info.x;
                o.positionCS = TransformWViewToHClip(view);
                o.corner = v.corner;
                half twinkle = 1.0h - _Twinkle * (0.5h + 0.5h * sin(_Time.y * (1.3h + frac(v.info.y * 7.1h) * 2.4h) + v.info.y * 6.2831h));
                o.color = v.color * twinkle * _Brightness;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.corner;
                half r2 = dot(c, c);
                half dot_ = exp(-r2 * 9.0h);
                half cross = exp(-abs(c.x) * 26.0h) * exp(-abs(c.y) * 3.2h) + exp(-abs(c.y) * 26.0h) * exp(-abs(c.x) * 3.2h);
                half k = saturate(dot_ + cross * 0.35h) * saturate(1.0h - r2);
                return half4(i.color.rgb * k * i.color.a, 0.0h);
            }
            ENDHLSL
        }
    }
}
