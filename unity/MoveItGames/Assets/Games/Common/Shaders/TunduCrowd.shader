// Sporte!'s crowd: one quad a seat, a person from an atlas rendered from the athletes' own model (art/sports/crowd.py:
// 8 x 4 cells, rows 0-1 clapping, rows 2-3 the same people cheering). Per quad: uv0 = the corner, uv1.x = which person,
// uv1.y = a random phase. They bob a little all the time; when the crowd cheers (_CrowdCheer, set by the game, 0..1),
// more and more of them stand with their arms up and jump. They take the stand's shadow (roofs) and the fog.
Shader "Tundu/Crowd"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        _Tint ("Light Tint", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade", Color) = (0.55, 0.58, 0.78, 1)
        _Bob ("Bob Height", Range(0, 0.2)) = 0.035
        _Jump ("Cheer Jump", Range(0, 0.5)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half4 _ShadeColor;
                half _Bob;
                half _Jump;
            CBUFFER_END

            float _CrowdCheer;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 person : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                half light : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float phase = v.person.y;
                float t = _Time.y;
                // cheering: the keenest first (a low phase), the whole stand when the cheer is full
                float cheering = step(phase * 0.9, _CrowdCheer - 0.05);
                float bob = sin(t * (5.0 + phase * 4.0) + phase * 40.0) * _Bob * (0.4 + 0.6 * saturate(_CrowdCheer * 2.0));
                float jump = abs(sin(t * (7.5 + phase * 3.0) + phase * 25.0)) * _Jump * cheering;
                float3 p = v.positionOS.xyz;
                p.y += bob + jump;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                float person = floor(v.person.x + 0.5);
                float col = fmod(person, 8.0);
                float row = floor(person / 8.0) + cheering * 2.0;
                o.uv = float2((col + v.uv.x) / 8.0, 1.0 - (row + 1.0 - v.uv.y) / 4.0);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                // in the sun or the shade per person, at the quad's corners: a shadow lookup per pixel over thousands of
                // fans cost the TV box's GPU too much
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    o.light = MainLightRealtimeShadow(TransformWorldToShadowCoord(o.positionWS));
                #else
                    o.light = 1.0h;
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - 0.5);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    half light = GetMainLight(TransformWorldToShadowCoord(i.positionWS)).shadowAttenuation;
                #else
                    half light = i.light;
                #endif
                half3 lit = lerp(_ShadeColor.rgb, _Tint.rgb, light);
                return half4(MixFog(c.rgb * lit, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }
    }
}
