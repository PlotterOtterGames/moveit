// Tundu's "ghost" (URP): the player's see-through body in Shpata!, like the semi-transparent Mii in Wii Sports Resort's
// Swordplay. A soft toon tint, see-through in the middle and glowing at the edges (fresnel), with faint moving
// hologram lines.
//
// One body built from many parts (head, torso, limbs) would show darker patches where see-through parts overlap.
// So it is drawn in two materials of this shader: "depth" (ColorMask 0, ZWrite On, queue 2999) lays down the nearest
// surface of the whole body first, then "colour" (ZWrite Off, ZTest LEqual, queue 3000) tints only that surface,
// once: one even, glassy figure. _Flash adds a colour for a moment (hit, block).
Shader "Tundu/Ghost"
{
    Properties
    {
        [MainColor] _BaseColor ("Body Colour (alpha = see-through amount)", Color) = (0.19, 0.78, 0.96, 0.42)
        _RimColor ("Edge Glow (alpha = strength)", Color) = (0.85, 0.97, 1, 0.9)
        _RimPower ("Edge Glow Width", Range(0.5, 8)) = 1.6
        _LineStrength ("Hologram Lines", Range(0, 1)) = 0.12
        _Flash ("Flash Colour (alpha = amount)", Color) = (1, 1, 1, 0)
        [Enum(UnityEngine.Rendering.ColorWriteMask)] _ColorMask ("Colour Mask (0 = depth material)", Float) = 15
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write (On = depth material)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _RimPower;
                half _LineStrength;
                half4 _Flash;
                half _ColorMask;
                half _ZWrite;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight();
                half lit = smoothstep(-0.2h, 0.35h, dot(n, light.direction));
                half3 color = _BaseColor.rgb * lerp(0.7h, 1.15h, lit);
                half rim = pow(1.0h - saturate(dot(n, view)), _RimPower) * _RimColor.a;
                color = lerp(color, _RimColor.rgb, saturate(rim));
                half lines = 0.5h + 0.5h * sin(i.positionWS.y * 70.0 - _Time.y * 3.0);
                half alpha = saturate(_BaseColor.a + rim * 0.6h + _LineStrength * lines * 0.25h);
                color = lerp(color, _Flash.rgb, _Flash.a);
                alpha = saturate(alpha + _Flash.a * 0.4h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
