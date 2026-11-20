// Superboks!'s light in the air (URP): the beams from the gym's lamps down to the ring, halos round bulbs and orbs
// (art/boxing/arena.py's *_Beams). Added on top of what's there (additive), no depth written, the near side only; faded
// where the surface turns away from the eye (a cone of light is brightest seen through its middle) and close to the eye (a
// camera in a cone, or right beside it, looked through several at once: a golden wash in the gym's side shots), its
// colour (the kit's vertex colour: brighter at the lamp) times _Strength. Everything at the vertices (the cones come in
// rings for it): the pixels only add the colour. Faded per pixel and drawn on both sides, the gym's eight cones cost the
// box's GPU ~35 ms of its frame (2026-11-19).
Shader "Tundu/ArenaGlow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 2)) = 0.6
        _EdgePower ("Edge Fade Power", Range(0.5, 6)) = 1.6
        _NearFade ("Near Fade: gone at x m, whole from x + y m", Vector) = (0.5, 2.5, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
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
                half _Strength;
                half _EdgePower;
                float4 _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 colour : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float3 toEye = GetWorldSpaceViewDir(ws);
                float d = length(toEye);
                half facing = (half)saturate(dot(n, toEye / max(d, 1e-4)));
                half near = (half)saturate((d - _NearFade.x) / _NearFade.y);
                o.colour = v.color.rgb * (_Strength * pow(facing, _EdgePower) * near);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.colour, 0.0h);
            }
            ENDHLSL
        }
    }
}
