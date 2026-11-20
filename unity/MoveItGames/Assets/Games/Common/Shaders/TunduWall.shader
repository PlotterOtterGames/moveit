// Lunapark!'s funhouse wall (Muri!): the board picture with a hole in a person's shape cut out of it. The hole comes as a
// distance field (_HoleTex, R8: 0.5 on the edge, more inside the shape), so its edge stays crisp at any size of the wall
// (it grows as it comes closer), with a glowing rim just outside the shape and a soft shadow beyond (the board's
// thickness). Unlit, alpha blended, cheap on the box: two texture reads.
Shader "Tundu/Wall"
{
    Properties
    {
        [MainTexture] _BaseMap ("Board", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint (alpha = the wall's)", Color) = (1, 1, 1, 1)
        _HoleTex ("Hole distance field (R)", 2D) = "black" {}
        _RimColor ("Rim (alpha = strength)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _RimColor;
            CBUFFER_END
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HoleTex);
            SAMPLER(sampler_HoleTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 board : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.board = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = SAMPLE_TEXTURE2D(_HoleTex, sampler_HoleTex, i.uv).r;
                half aa = max(fwidth(d), 0.002h);
                half inside = smoothstep(0.5h - aa, 0.5h + aa, d);
                half rim = smoothstep(0.37h, 0.47h, d) * (1.0h - inside);
                half shadow = smoothstep(0.2h, 0.4h, d) * (1.0h - inside);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.board) * _BaseColor;
                c.rgb *= 1.0h - 0.45h * shadow;
                c.rgb = lerp(c.rgb, _RimColor.rgb, rim * _RimColor.a);
                c.a *= 1.0h - inside;
                return c;
            }
            ENDHLSL
        }
    }
}
