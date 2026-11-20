// IMGUI's pictures drawn as one mesh (HudBatch): images from a game's interface sheet and shapes (rounded panels, frame
// rings) from a small second texture, words from a font's texture (its alpha: a glyph's coverage), each quad saying which
// (uv.z: 0, 1, 2). Drawn inside OnGUI like IMGUI's own pieces, so it
// writes what IMGUI writes: the texture's sample (an sRGB texture's decoded value) times the vertex colour (GUI.color,
// as given), with nothing converted on the way out, blended the same way. Checked on the box against IMGUI's own
// drawing of Vishu!'s bubbles (2026-11-12): within a level of 255; re-encoding the sample to sRGB made them ~5 % brighter.
Shader "Hidden/Tundu/HudBatch"
{
    Properties
    {
        _MainTex ("Interface sheet", 2D) = "white" {}
        _ShapeTex ("Shapes", 2D) = "white" {}
        _FontTex ("Font", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);
            TEXTURE2D(_FontTex);
            SAMPLER(sampler_FontTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t;
                // one texture per quad: a branch, not three samples
                if (i.uv.z < 0.5) t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                else if (i.uv.z < 1.5) t = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, i.uv.xy);
                else t = half4(1.0, 1.0, 1.0, SAMPLE_TEXTURE2D(_FontTex, sampler_FontTex, i.uv.xy).a);
                return t * i.color;
            }
            ENDHLSL
        }
    }
}
