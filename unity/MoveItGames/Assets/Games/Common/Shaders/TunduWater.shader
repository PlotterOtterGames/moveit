// Tundu's cartoon water (URP), for Aventura!'s river: the mesh's vertex colours give the water's colour (alpha = how
// much white foam, e.g. near the banks and in the rapids); foam streaks drift down the river with _FlowOffset (metres
// along it, set by the game from the current's speed, so it never loses precision); small waves move the surface and
// its glints; the sky shows in it at grazing angles; fog like everything else. Opaque, one pass, cheap on the box.
Shader "Tundu/Water"
{
    Properties
    {
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _SkyColor ("Sky Reflection", Color) = (0.82, 0.93, 0.97, 1)
        _FlowOffset ("Flow Offset (metres along the river)", Float) = 0
        _StreakScale ("Streaks per Metre", Float) = 0.35
        _WaveHeight ("Wave Height", Float) = 0.05
        _Ambient ("Ambient Fill", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half4 _SkyColor;
                float _FlowOffset;
                float _StreakScale;
                float _WaveHeight;
                half _Ambient;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;
                ws.y += _WaveHeight * (sin(ws.x * 1.3 + t * 2.1) + sin(ws.z * 0.9 - t * 1.7)) * 0.5;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float along = i.uv.y - _FlowOffset;
                float across = i.uv.x;
                // foam streaks: wavy lines down the river, broken up so they look like drifting patches
                float wobble = sin(along * 0.21 + across * 9.0) * 1.3 + sin(across * 23.0 + along * 0.05) * 0.4;
                float streak = sin(along * _StreakScale * 6.2831 + wobble);
                float patch = sin(across * 31.0 + along * 0.7) * sin(along * 0.43 - across * 7.0);
                float foam = smoothstep(0.86, 0.97, streak) * smoothstep(-0.2, 0.6, patch);
                foam = saturate(foam * 0.55 + i.color.a * smoothstep(0.35, 0.9, streak * 0.5 + patch * 0.5 + i.color.a));

                // little waves: the normal tips with two moving sine fields, glints where it faces the sun
                float3 n = normalize(float3(cos(i.positionWS.x * 1.3 + t * 2.1) * 0.12 + sin(along * 1.7 + t) * 0.06,
                                            1.0,
                                            cos(i.positionWS.z * 0.9 - t * 1.7) * 0.12 + cos(across * 17.0 - t * 1.3) * 0.05));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 water = i.color.rgb;
                half lit = lerp(0.75h, 1.0h, saturate(dot(n, light.direction))) * lerp(1.0h, light.shadowAttenuation, 0.5h);
                half3 color = water * light.color * lit + water * _Ambient;
                half fresnel = pow(1.0h - saturate(dot(n, view)), 4.0h);
                color = lerp(color, _SkyColor.rgb, fresnel * 0.6h);
                half3 h = normalize(light.direction + view);
                color += light.color * pow(saturate(dot(n, h)), 90.0h) * 0.8h;
                color = lerp(color, _FoamColor.rgb, foam);
                return half4(MixFog(color, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }
    }
}
