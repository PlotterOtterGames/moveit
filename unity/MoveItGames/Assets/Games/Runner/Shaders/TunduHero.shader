// Vrapo!'s hero (URP), 2026-11-18: the dino lit per pixel (it covers a small part of the screen, and it's what every eye
// follows): a two-tone toon light with a soft edge, the shade a hue-shifted colour of the time of day, the sky's light
// from above and the ground's bounce from below, a warm rim of light that lifts the silhouette off the path, a glint
// where it's glossy (the eyes), the baked occlusion, the hit's flash. Bends with the runner's world like the valley.
//
// Vertex data from art/runner/dino.py (the kit's): colour rgb = the colour (linear), a = ambient occlusion; uv0.x = how
// glossy, uv0.y = how much it glows (the eyes' glints: always bright). Globals from RunnerWorld: _ValleyShade, _ValleySky,
// _ValleyBounce, _ValleyRim, _ValleyNight.
Shader "Tundu/Hero"
{
    Properties
    {
        _Softness ("Terminator Softness", Range(0.01, 0.5)) = 0.07
        _Threshold ("Terminator", Range(-1, 1)) = 0.05
        _Ambient ("Sky Ambient", Range(0, 1.5)) = 0.45
        _AOStrength ("Ambient Occlusion", Range(0, 1)) = 0.7
        _RimStrength ("Rim", Range(0, 2)) = 0.6
        _RimPower ("Rim Tightness", Range(1, 8)) = 3.2
        _Gloss ("Gloss", Range(0, 2)) = 0.9
        _GlossPower ("Gloss Tightness", Range(4, 256)) = 60
        _FlashColor ("Flash (alpha = amount)", Color) = (1, 1, 1, 0)
        _Curve ("Bends with the world", Range(0, 1)) = 1
        _ShadeColor ("Shade (when no valley light is set)", Color) = (0.47, 0.51, 0.78, 1)
        _SkyColor ("Sky light (when no valley light is set)", Color) = (0.66, 0.78, 1, 1)
        _BounceColor ("Ground bounce (when no valley light is set)", Color) = (0.55, 0.78, 0.43, 1)
        _RimColor ("Rim (when no valley light is set; a = strength)", Color) = (1, 0.93, 0.8, 0.4)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Threshold;
                half _Ambient;
                half _AOStrength;
                half _RimStrength;
                half _RimPower;
                half _Gloss;
                half _GlossPower;
                half4 _FlashColor;
                half _Curve;
                half4 _ShadeColor;
                half4 _SkyColor;
                half4 _BounceColor;
                half4 _RimColor;
            CBUFFER_END

            float _TunduCurve;
            float4 _ValleyShade, _ValleySky, _ValleyBounce, _ValleyRim;
            float _ValleyNight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half3 ambient : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                float d = max(0.0, ws.z - _WorldSpaceCameraPos.z);
                o.positionCS = TransformWorldToHClip(ws - float3(0, _TunduCurve * _Curve * d * d, 0));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                half up = saturate(o.normalWS.y * 0.5h + 0.5h);
                // outside Vrapo! (Gara!'s dino by the start) nobody sets the valley's light: the material's own colours
                half3 sky = _ValleySky.a > 0.0 ? _ValleySky.rgb : _SkyColor.rgb;
                half3 bounce = _ValleySky.a > 0.0 ? _ValleyBounce.rgb : _BounceColor.rgb;
                o.ambient = lerp(bounce, sky, up) * _Ambient;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half lit = smoothstep(_Threshold - _Softness, _Threshold + _Softness, ndl);
                half3 albedo = lerp(i.color.rgb, _FlashColor.rgb, _FlashColor.a);
                half occ = lerp(1.0h, i.color.a, _AOStrength);
                bool valley = _ValleySky.a > 0.0;
                half3 shade = valley ? _ValleyShade.rgb : _ShadeColor.rgb;
                half4 rimColour = valley ? _ValleyRim : _RimColor;
                half3 skyColour = valley ? _ValleySky.rgb : _SkyColor.rgb;
                half3 direct = lerp(shade, light.color, lit);
                half3 colour = albedo * (direct * lerp(1.0h, occ, 0.5h) + i.ambient * occ);
                // the rim: warm where the sun is, the sky's colour in the shade
                half f = 1.0h - saturate(dot(n, view));
                half rim = pow(f, _RimPower) * _RimStrength * (0.4h + 0.6h * occ);
                colour += lerp(skyColour * 0.7h, rimColour.rgb, lit) * rim * (0.5h + 0.5h * rimColour.a);
                // the glint where it's glossy
                half3 h = normalize(light.direction + view);
                colour += light.color * pow(saturate(dot(n, h)), _GlossPower) * _Gloss * i.uv.x * (0.3h + 0.7h * lit);
                // glowing bits stay bright
                colour = lerp(colour, albedo * (1.2h + _ValleyNight * 0.5h), i.uv.y);
                colour = lerp(colour, _FlashColor.rgb, _FlashColor.a * 0.5h);
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
}
