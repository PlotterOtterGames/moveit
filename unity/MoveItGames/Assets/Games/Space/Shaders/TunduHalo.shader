// Yjet!'s glows round a planet or a star (URP, 2026-11-14): a quad at the body's middle turned to the camera in the
// vertex shader (no script turns it), drawing the light just outside its edge: a planet's air lit on its sunny side (all
// round when the sun is behind it), a star's corona (_Corona 1: bright, on every side, with soft rays). Additive, depth
// tested (whatever is in front hides it), no depth written. The quad's uvs run -1..1 across, in the body's radii
// times _Size.
Shader "Tundu/Halo"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.6, 0.8, 1, 1)
        _Size ("Quad half size (radii)", Float) = 1.7
        _Falloff ("Falloff", Float) = 5
        _Corona ("Star corona (0 = planet air)", Float) = 0
        _Radius ("Body radius (m)", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _Color;
                float _Size;
                float _Falloff;
                float _Corona;
                float _Radius;
            CBUFFER_END
            float4 _TunduStar;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 offset : TEXCOORD0;     // radii from the middle, in the quad's plane
                half2 sun : TEXCOORD1;         // the sun's way across the screen
                half back : TEXCOORD2;         // the sun behind the body (lights the air all round)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 centreVS = TransformWorldToView(centreWS);
                float2 off = (v.uv * 2.0 - 1.0) * _Size;
                float3 posVS = centreVS + float3(off * _Radius, 0.0);
                // drawn a little in front of the middle (never behind it for the depth test), pulled toward the camera
                posVS.z += _Radius * 0.02;
                o.positionCS = TransformWViewToHClip(posVS);
                o.offset = off;
                float3 toSun = TransformWorldToViewDir(_TunduStar.xyz - centreWS);
                o.sun = (half2)normalize(toSun.xy + 1e-4);
                o.back = (half)saturate(-normalize(toSun).z * 0.5 + 0.5);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.offset);
                half2 dir = (half2)(i.offset / max(r, 1e-4));
                half outside = exp(-max(r - 1.0, 0.0) * _Falloff) * smoothstep(0.96, 1.01, r);
                half fade = saturate((_Size - r) / (_Size * 0.25));
                if (_Corona > 0.5)
                {
                    float a = atan2(i.offset.y, i.offset.x);
                    half rays = 0.75h + 0.25h * sin(a * 7.0 + _Time.y * 0.3) * sin(a * 3.0 - _Time.y * 0.2);
                    half core = exp(-max(r - 1.0, 0.0) * _Falloff) * smoothstep(0.9, 1.0, r);
                    half wide = exp(-max(r - 1.0, 0.0) * _Falloff * 0.3) * 0.35h * rays;
                    return half4(_Color.rgb * (core + wide) * fade, 1.0h);
                }
                half lit = saturate(dot(dir, i.sun) * 0.6h + 0.45h);
                lit = max(lit, i.back * 0.9h);
                return half4(_Color.rgb * (outside * lit * fade * _Color.a), 1.0h);
            }
            ENDHLSL
        }
    }
}
