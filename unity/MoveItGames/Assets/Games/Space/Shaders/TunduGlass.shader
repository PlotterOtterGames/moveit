// Yjet!'s helmet (URP): a clear bubble in front of the astronaut's face (faint in the middle so the face shows, brighter
// toward its edge (fresnel), a sharp glint where the sun shines and a soft window-shaped reflection up on the left) and a
// solid shell behind (the suit's colour with a rim in its accent), like a cartoon astronaut's helmet: seen from behind,
// where the chase camera sees it most, a bubble showed only dark hair (a black ball, 2026-11-14). _Front is the face's
// direction in the dome's own space (the astronaut faces +Z at its bind pose, and the dome turns with the head's bone).
// Two passes: the shell's inside first (seen round the head through the glass), then the outside. Drawn after the head
// (transparent queue).
Shader "Tundu/Glass"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (0.7, 0.9, 1, 1)
        _Edge ("Edge brightness", Range(0, 2)) = 0.85
        _Middle ("Middle opacity", Range(0, 1)) = 0.06
        _Gloss ("Sun glint", Range(0, 4)) = 1.4
        _ShellColor ("Shell", Color) = (0.95, 0.95, 0.98, 1)
        _RimColor ("Shell's rim", Color) = (1, 0.35, 0.3, 1)
        _Front ("Face direction (object space)", Vector) = (0, 0, 1, 0)
        _Cut ("Shell from (cosine to the face)", Range(-1, 1)) = -0.06
        _RimWidth ("Rim width (cosine)", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Edge;
            half _Middle;
            half _Gloss;
            half4 _ShellColor;
            half4 _RimColor;
            float4 _Front;
            half _Cut;
            half _RimWidth;
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
            float3 normalVS : TEXCOORD2;
            float3 normalOS : TEXCOORD3;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.normalVS = TransformWorldToViewDir(o.normalWS);
            o.normalOS = v.normalOS;
            return o;
        }

        // 0 on the glass, 1 on the shell (the rim included), and 0..1 for the rim band inside it (anti-aliased)
        void Shell(float3 normalOS, out half back, out half rim)
        {
            float f = dot(normalize(normalOS), normalize(_Front.xyz));
            float aa = max(fwidth(f), 1e-4);
            back = (half)smoothstep(_Cut + aa, _Cut - aa, f);
            half inner = (half)smoothstep(_Cut - _RimWidth + aa, _Cut - _RimWidth - aa, f);
            rim = back * (1.0h - inner);
        }
        ENDHLSL

        // the shell's inside, round the head (in its own shade)
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragInside

            half4 fragInside(Varyings i) : SV_Target
            {
                half back, rim;
                Shell(i.normalOS, back, rim);
                half3 color = lerp(_ShellColor.rgb * 0.55h, _RimColor.rgb * 0.6h, rim);
                return half4(color, back);
            }
            ENDHLSL
        }

        // the outside: the glass in front, the lit shell behind
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half facing = saturate(dot(n, view));
                half edge = pow(1.0h - facing, 2.2h) * _Edge;
                Light light = GetMainLight();
                half3 h = normalize(light.direction + view);
                half spec = pow(saturate(dot(n, h)), 90.0h);
                half glint = spec * _Gloss;
                // a soft window of reflected light up on the left of the bubble (as seen)
                float3 nv = normalize(i.normalVS);
                half window = smoothstep(0.75h, 0.95h, dot(nv.xy, normalize(float2(-0.55, 0.65)))) * smoothstep(0.1h, 0.4h, nv.z);
                half3 glass = _BaseColor.rgb * (edge + 0.15h) + glint + window * 0.55h;
                half glassAlpha = saturate(_Middle + edge * 0.75h + glint + window * 0.45h);

                // the shell: lit like the toon characters (a soft wrap, the sky's light), a glossy highlight
                half back, rim;
                Shell(i.normalOS, back, rim);
                half wrap = saturate(dot(n, light.direction) * 0.5h + 0.5h);
                half3 lit = light.color * (0.35h + 0.75h * wrap * wrap) + SampleSH(n) * 0.45h;
                half3 shell = lerp(_ShellColor.rgb, _RimColor.rgb, rim) * lit + spec * 0.6h + edge * 0.12h;
                return half4(lerp(glass, shell, back), lerp(glassAlpha, 1.0h, back));
            }
            ENDHLSL
        }
    }
}
