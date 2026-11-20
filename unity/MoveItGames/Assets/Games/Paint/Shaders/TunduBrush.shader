// Pikturo!'s brush: stamps shapes from the brush atlas into the paint canvas (a RenderTexture), drawn by
// PaintCanvas with a command buffer. The canvas holds PREMULTIPLIED colour, so it composites correctly over the
// paper or the camera picture (Tundu/Fx with Blend One OneMinusSrcAlpha) and its transparent parts stay clear.
// Pass 0 paints (premultiplied "over"), pass 1 erases (removes coverage, back to transparent), pass 2 paints BEHIND
// what is there (only where the canvas is still clear: filling up a mostly coloured area of a colouring page).
//
// Staying inside the lines: a stamp may carry an area number (uv1.x, 1..250, 0 = none). Then it only paints where
// _RegionTex (PageRegions.Labels as an R8 texture the size of the canvas, point sampled) holds that number.
Shader "Tundu/Brush"
{
    Properties
    {
        [MainTexture] _BaseMap ("Brush shapes (alpha)", 2D) = "white" {}
        [NoScaleOffset] _RegionTex ("Colouring page areas (R = number / 255)", 2D) = "black" {}
        _CanvasSize ("Canvas size in pixels (x, y)", Vector) = (1056, 792, 0, 0)
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
        TEXTURE2D(_RegionTex);
        SAMPLER(sampler_RegionTex);          // the texture itself is point filtered and clamped (PaintCanvas.SetRegions)
        float4 _CanvasSize;

        // xy = position in canvas pixels, uv = cell in the shape atlas, color = straight colour + opacity, clip.x = area
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 clip : TEXCOORD1; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 region : TEXCOORD1; half4 color : COLOR; };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformWorldToHClip(v.positionOS.xyz);   // the command buffer sets an ortho pixel projection
            o.uv = v.uv;
            o.region = float3(v.positionOS.xy / _CanvasSize.xy, v.clip.x);
            o.color = v.color;
            return o;
        }

        half Coverage(Varyings i)
        {
            half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * i.color.a;
            if (i.region.z > 0.5)
            {
                float area = SAMPLE_TEXTURE2D(_RegionTex, sampler_RegionTex, i.region.xy).r * 255.0;
                a *= step(abs(area - i.region.z), 0.5);
            }
            return a;
        }
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

        Pass
        {
            Name "Behind"
            Blend OneMinusDstAlpha One
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
    }
}
