// Yjet!'s space dust (URP, 2026-11-14): specks round the camera that make the speed felt (space has nothing else close
// by). The mesh holds each speck's place in a unit cube (positionOS), its corner (uv -1..1) and brightness (colour); the
// vertex shader wraps every speck into a cube of _Box metres round the camera (no script moves them), stretches it
// along the flight (_DustVelocity, set by SpaceWorld: a streak when fast) and fades it close to the camera and far
// away. One draw call, added on.
Shader "Tundu/Dust"
{
    Properties
    {
        _Color ("Colour", Color) = (0.75, 0.85, 1, 1)
        _Size ("Speck size (m)", Float) = 0.09
        _Box ("Cube round the camera (m)", Float) = 60
        _Stretch ("Streak length per m/s (s)", Float) = 0.035
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
                float _Box;
                float _Stretch;
            CBUFFER_END
            float4 _DustVelocity;   // the flight's velocity (world, m/s)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 cam = _WorldSpaceCameraPos;
                float3 p = cam + (frac(v.positionOS.xyz - cam / _Box) - 0.5) * _Box;
                float3 vel = _DustVelocity.xyz;
                float speed = length(vel);
                float3 dir = speed > 0.05 ? vel / speed : float3(0, 0, 1);
                float3 toCam = normalize(cam - p);
                float3 side = cross(dir, toCam);
                side = dot(side, side) > 1e-6 ? normalize(side) : float3(1, 0, 0);
                float len = _Size + speed * _Stretch;
                // the streak trails behind the speck (it was there a moment ago)
                float3 world = p + side * (v.uv.x * _Size) - dir * ((v.uv.y * 0.5 + 0.5) * len);
                o.positionCS = TransformWorldToHClip(world);
                float d = distance(p, cam);
                half fade = smoothstep(1.5, 6.0, d) * smoothstep(_Box * 0.5, _Box * 0.3, d);
                // a long streak spreads its light
                o.color = v.color * _Color * fade * saturate(_Size * 3.0 / len + 0.15);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = saturate(1.0h - i.uv.x * i.uv.x) * saturate(1.0h - i.uv.y * i.uv.y * 0.7h);
                return half4(i.color.rgb * a * i.color.a, 1.0h);
            }
            ENDHLSL
        }
    }
}
