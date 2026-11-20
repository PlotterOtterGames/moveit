// Zana!'s fairy wings (URP): see-through and glowing. A panel (FairyArt.WingPanel) has uv.y 0 at its hinge .. 1 at its
// rim and uv.x round its outline. Its colour runs from _RootColor to _EdgeColor (their alpha too), a bright rim
// (_RimColor) glows along the edge, faint veins fan out from the hinge, and glitter twinkles (_Glitter). _Rainbow (0/1):
// the colour runs round the wing through the rainbow instead. Two-sided, drawn after the scene, no depth written.
Shader "Tundu/Wing"
{
    Properties
    {
        _RootColor ("Root", Color) = (1, 0.9, 0.96, 0.55)
        _EdgeColor ("Edge", Color) = (1, 0.55, 0.8, 0.8)
        _RimColor ("Rim", Color) = (1, 1, 1, 1)
        _Glitter ("Glitter", Range(0, 1)) = 0.5
        _Rainbow ("Rainbow (0/1)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RootColor;
                half4 _EdgeColor;
                half4 _RimColor;
                half _Glitter;
                half _Rainbow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 Hue(half h)
            {
                half3 k = saturate(abs(frac(h + half3(0.0h, 0.666h, 0.333h)) * 6.0h - 3.0h) - 1.0h);
                return lerp(half3(1, 1, 1), k, 0.6h);
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half out_ = smoothstep(0.05h, 1.0h, i.uv.y);
                half4 c = lerp(_RootColor, _EdgeColor, out_);
                if (_Rainbow > 0.5) c.rgb = lerp(half3(1, 1, 1), Hue(i.uv.x * 0.9h + _Time.y * 0.05h), 0.35h + 0.65h * out_);
                // veins: thin lines fanning out from the hinge, fading toward the rim
                half veins = pow(abs(sin(i.uv.x * 34.0h)), 26.0h) * (1.0h - out_) * 0.35h;
                c.rgb += veins;
                // a band of light just inside the rim, the rim itself bright and solid
                half band = smoothstep(0.7h, 0.86h, i.uv.y) * (1.0h - smoothstep(0.86h, 0.93h, i.uv.y)) * 0.25h;
                half rim = smoothstep(0.9h, 0.99h, i.uv.y);
                c.rgb = lerp(c.rgb + band, _RimColor.rgb, rim);
                c.a = saturate(c.a + rim * 0.6h + band);
                // glitter: little cells that light up now and then
                float2 cell = floor(i.uv * float2(46.0, 18.0));
                float twinkle = Hash(cell + floor(_Time.y * 6.0) * 0.37);
                half spark = step(1.0 - 0.05 * _Glitter, twinkle) * _Glitter;
                c.rgb += spark;
                c.a = saturate(c.a + spark * 0.5h);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }
    }
}
