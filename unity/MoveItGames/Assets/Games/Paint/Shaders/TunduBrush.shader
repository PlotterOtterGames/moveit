// Pikturo!'s brush: stamps shapes from the brush atlas into the paint canvas (a RenderTexture), drawn by
// PaintCanvas with a command buffer. The canvas holds PREMULTIPLIED colour, so it composites correctly over the
// paper or the camera picture (Tundu/Fx with Blend One OneMinusSrcAlpha) and its transparent parts stay clear.
// Pass 0 paints (premultiplied "over"), pass 1 erases (removes coverage, back to transparent).
Shader "Tundu/Brush"
{
    Properties
    {
        [MainTexture] _BaseMap ("Brush shapes (alpha)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // xy = position in canvas pixels, uv = cell in the shape atlas, color = straight colour + opacity
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformWorldToHClip(v.positionOS.xyz);   // the command buffer sets an ortho pixel projection
            o.uv = v.uv;
            o.color = v.color;
            return o;
        }

        half Coverage(Varyings i) { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * i.color.a; }
        ENDHLSL

        Pass
        {
            Name "Paint"
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half a = Coverage(i);
                return half4(i.color.rgb * a, a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Erase"
            Blend Zero OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                return half4(0, 0, 0, Coverage(i));
            }
            ENDHLSL
        }
    }
}
