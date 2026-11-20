// Yjet!'s sky (URP skybox): tools/space/sky.py's painted panorama wrapped round the camera. Column = longitude,
// atan2(z, x) from -180 degrees at the left; row = latitude (the picture's top = straight up). Sampled at mip 0 (the
// picture has no mipmaps: the longitude wraps at the back, and its derivative jump would draw a seam). One texture read
// and a little arithmetic a pixel: the old gradient sky with procedural clouds cost about as much on the box.
Shader "Tundu/SkyPanorama"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Panorama (equirectangular)", 2D) = "black" {}
        _Exposure ("Brightness", Range(0, 2)) = 1
        _Turn ("Turn round the vertical (degrees)", Float) = 0
        _Hue ("Hue turn (0..1: every star system its own colours)", Float) = 0
        _Saturation ("Saturation", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                float _Turn;
                float _Hue;
                half _Saturation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
                half2 hue : TEXCOORD1;   // the hue turn's cosine and sine
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float s, c;
                sincos(radians(_Turn), s, c);
                o.dir = float3(c * v.positionOS.x - s * v.positionOS.z, v.positionOS.y, s * v.positionOS.x + c * v.positionOS.z);
                float hs, hc;
                sincos(_Hue * 6.2831853, hs, hc);
                o.hue = half2(hc, hs);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float2 uv = float2(atan2(d.z, d.x) * (0.5 / PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) * (1.0 / PI) + 0.5);
                half3 c = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0).rgb;
                // the hue turned round the grey axis (Rodrigues), the saturation scaled
                const half3 k = half3(0.57735h, 0.57735h, 0.57735h);
                half grey = dot(k, c);
                c = c * i.hue.x + cross(k, c) * i.hue.y + k * grey * (1.0h - i.hue.x);
                half luma = dot(c, half3(0.299h, 0.587h, 0.114h));
                c = max(0.0h, lerp(luma.xxx, c, _Saturation));
                return half4(c * _Exposure, 1.0h);
            }
            ENDHLSL
        }
    }
}
