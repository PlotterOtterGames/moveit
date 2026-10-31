// Fluturo!'s city (URP): one material for a whole block. The tiles come from a 2D texture array (uv = repeating
// coordinates, uv2.x = which tile), the vertex colour paints walls (rgb) and darkens corners (a). The tile's alpha
// says what a pixel is (tools/fly/city_textures.py): 1 = wall, takes the building's colour; 0.5 = fixed colour
// (frames, doors, road paint); 0 = glass or water, which reflects the sky with a fresnel and catches the sun.
// Toon light like Tundu/Toon (two soft tones, tinted shade), fog and the curved world. No shadows: cheap on the box.
Shader "Tundu/City"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Tiles (2D array)", 2DArray) = "" {}
        _ShadeColor ("Shade Tint", Color) = (0.7, 0.72, 0.9, 1)
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = 0.05
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.6)) = 0.18
        _Ambient ("Ambient Fill", Range(0, 1)) = 0.3
        _SkyTop ("Reflected Sky (top)", Color) = (0.36, 0.72, 0.95, 1)
        _SkyHorizon ("Reflected Sky (horizon)", Color) = (0.86, 0.94, 0.97, 1)
        _Reflection ("Glass Reflection", Range(0, 1)) = 0.75
        _SunGlint ("Sun Glint", Range(0, 4)) = 1.6
        _RimColor ("Rim Light (alpha = strength)", Color) = (1, 0.97, 0.9, 0.12)
        _Curve ("Bends With The World (0/1)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _Ambient;
            half4 _SkyTop;
            half4 _SkyHorizon;
            half _Reflection;
            half _SunGlint;
            half4 _RimColor;
            half _Curve;
        CBUFFER_END

        float _TunduCurve;

        float3 CurveWorld(float3 positionWS)
        {
            float d = max(0.0, positionWS.z - _WorldSpaceCameraPos.z);
            positionWS.y -= _TunduCurve * _Curve * d * d;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 tile : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0;          // xy repeat, z tile
                half4 color : COLOR;
                half3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(CurveWorld(ws));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = float3(v.uv, v.tile.x);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D_ARRAY(_BaseMap, sampler_BaseMap, i.uv.xy, i.uv.z);
                half wall = saturate(tex.a * 2.0h - 1.0h);
                half glass = saturate(1.0h - tex.a * 2.0h);
                half3 albedo = tex.rgb * lerp(half3(1.0h, 1.0h, 1.0h), i.color.rgb, wall);

                half3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);
                half3 color = lerp(albedo * _ShadeColor.rgb, albedo * light.color, lit);
                color += albedo * SampleSH(n) * _Ambient;

                // glass and water: the sky mirrored in them, stronger at a glancing angle, and a glint of the sun
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half facing = saturate(dot(n, view));
                half3 r = reflect(-view, n);
                half3 sky = lerp(_SkyHorizon.rgb, _SkyTop.rgb, saturate(r.y * 1.4h + 0.15h));
                half fresnel = 0.3h + 0.7h * pow(1.0h - facing, 3.0h);
                color = lerp(color, sky, glass * fresnel * _Reflection);
                color += light.color * pow(saturate(dot(r, light.direction)), 48.0h) * _SunGlint * glass;

                color += _RimColor.rgb * pow(1.0h - facing, 4.0h) * _RimColor.a;
                color *= i.color.a;     // corner darkening baked into the vertices
                return half4(MixFog(color, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            struct DepthAttributes { float4 positionOS : POSITION; };

            float4 vertDepth(DepthAttributes v) : SV_POSITION
            {
                return TransformWorldToHClip(CurveWorld(TransformObjectToWorld(v.positionOS.xyz)));
            }

            half fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
