// Superboks!'s portraits in the HUD (URP, drawn with Graphics.DrawTexture inside OnGUI): a boxer's face (Portraits'
// render texture) cut round with a soft edge. IMGUI writes what a texture's sample is without converting it (a sampled sRGB
// texture comes out darker: Vishu!'s lesson), so the sample goes back to sRGB here. No tint (IMGUI's vertex colours
// aren't what GUI.color says).
Shader "Tundu/Portrait"
{
    Properties
    {
        _MainTex ("Portrait", 2D) = "white" {}
        _Edge ("Edge Softness", Range(0.001, 0.2)) = 0.025
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Edge;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

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
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                c.rgb = LinearToSRGB(c.rgb);
                float d = length(i.uv - 0.5) * 2.0;
                half round = saturate((1.0 - d) / _Edge);
                return half4(c.rgb, c.a * round);
            }
            ENDHLSL
        }
    }
}
