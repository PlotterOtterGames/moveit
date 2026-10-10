// Cartoon sky for Tundu (URP skybox): a soft gradient from the horizon to the top, a pale band under the horizon,
// and a warm sun glow. Use as RenderSettings.skybox; set the fog colour to _HorizonColor so the world melts into it.
Shader "Tundu/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.36, 0.72, 0.95, 1)
        _HorizonColor ("Horizon", Color) = (0.82, 0.93, 0.96, 1)
        _BottomColor ("Below Horizon", Color) = (0.74, 0.88, 0.86, 1)
        _SunColor ("Sun Glow", Color) = (1, 0.93, 0.7, 1)
        _SunDirection ("Sun Direction", Vector) = (-0.3, 0.35, 1, 0)
        _SunSize ("Sun Glow Size", Range(1, 64)) = 10
        _Exponent ("Gradient Curve", Range(0.1, 2)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half4 _SunColor;
                float4 _SunDirection;
                half _SunSize;
                half _Exponent;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half up = saturate(d.y);
                half3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(up, _Exponent));
                sky = d.y < 0 ? lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-d.y * 4)) : sky;
                half sun = pow(saturate(dot(d, normalize(_SunDirection.xyz))), _SunSize);
                sky += _SunColor.rgb * sun * 0.6;
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
