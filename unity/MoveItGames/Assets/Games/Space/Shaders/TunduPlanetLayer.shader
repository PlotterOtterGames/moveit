// Yjet!'s see-through planet layers (URP): a planet's clouds (a sphere a little bigger than it, turning slowly) and a
// giant's rings (a flat ring, seen from both sides): the map's colour and alpha, lit by the sun (softly), blended over.
Shader "Tundu/PlanetLayer"
{
    Properties
    {
        [MainTexture] _BaseMap ("Map (alpha = how much shows)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint (alpha = strength)", Color) = (1, 1, 1, 1)
        _Ambient ("Night side brightness", Range(0, 1)) = 0.35
        _Spin ("Turning (map widths a second)", Float) = 0
        _TwoSided ("Lit from both sides (rings)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _StarLit ("Lit by the system's star like Tundu/ProcPlanet (0 = the main light)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Ambient;
                float _Spin;
                half _TwoSided;
                half _Cull;
                half _StarLit;
            CBUFFER_END
            float4 _TunduStar;
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 lightWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap) + float2(_Time.y * _Spin, 0.0);
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float3 star = normalize(normalize(_TunduStar.xyz - centre) + normalize(_WorldSpaceCameraPos - centre) * 1.1 + float3(0, 0.25, 0));
                o.lightWS = _StarLit > 0.5 ? star : GetMainLight().direction;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 map = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS);
                half ndl = dot(n, normalize(i.lightWS));
                ndl = _TwoSided > 0.5h ? abs(ndl) * 0.6h + 0.4h : ndl;
                half lit = smoothstep(-0.2h, 0.25h, ndl);
                half3 color = map.rgb * lerp(_Ambient, 1.0h, lit);
                return half4(color, map.a);
            }
            ENDHLSL
        }
    }
}
