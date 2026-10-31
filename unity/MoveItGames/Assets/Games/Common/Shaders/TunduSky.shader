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
        _Clouds ("Clouds (0 = none)", Range(0, 1)) = 0
        _CloudColor ("Cloud Colour", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud Shade", Color) = (0.78, 0.84, 0.95, 1)
        _CloudScale ("Cloud Scale", Range(0.2, 6)) = 1.6
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
                half _Clouds;
                half4 _CloudColor;
                half4 _CloudShade;
                half _CloudScale;
            CBUFFER_END

            TEXTURE2D(_TunduNoise);  // 256x256 random values, global (SkyNoise)
            SAMPLER(sampler_TunduNoise);

            // Value noise: one bilinear sample of the table, placed so the blend between the four corners follows the
            // smoothstep curve. Four sin() hashes per octave made Sporte!'s clouds cost ~14 ms a frame on the X98K Pro.
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return SAMPLE_TEXTURE2D_LOD(_TunduNoise, sampler_TunduNoise, (i + f + 0.5) / 256.0, 0).r;
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += Noise(p) * a; p *= 2.03; a *= 0.5; }
                return v;
            }

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
                if (_Clouds > 0.001 && d.y > 0.0)
                {
                    // puffy cartoon clouds on a plane above: soft-edged blobs, lit on top, bluish underneath,
                    // thinning toward the horizon; drifting slowly
                    float2 p = d.xz / (d.y + 0.18) * _CloudScale + float2(_Time.y * 0.012, _Time.y * 0.004);
                    float n = Fbm(p);
                    float cover = smoothstep(1.0 - _Clouds * 0.62, 1.0 - _Clouds * 0.62 + 0.12, n);
                    float lightSide = saturate((Fbm(p + float2(0.06, 0.09)) - n) * 6.0 + 0.6);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lightSide);
                    half fade = smoothstep(0.02, 0.2, d.y);
                    sky = lerp(sky, cloud, cover * fade);
                }
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
