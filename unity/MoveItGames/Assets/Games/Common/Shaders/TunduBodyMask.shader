// Vishu!'s body mask: soft shapes of the player's body drawn from the skeleton into a small render texture
// (R = the whole body: torso, head, arms, legs; G = the arms and hands). Tundu/Outfit reads it to hide wings, capes
// and long hair behind the body, and tops and skirts behind the arms. Vertex colour = which channels; uv.x = the
// distance across the shape (-1..1, soft at the edge), so overlapping shapes keep the stronger value (max).
Shader "Tundu/BodyMask"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        BlendOp Max
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                half d = length(i.uv);
                half k = 1.0 - smoothstep(0.7, 1.0, d);
                return half4(i.color.rgb * k, 1.0);
            }
            ENDHLSL
        }
    }
}
