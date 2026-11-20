// Yjet!'s generated planets (URP, 2026-11-14, the exploration rework): every planet of the galaxy is one of eight shared
// height maps (tools/space/heights.py: a texture array) turned and maybe mirrored, coloured through its own row of the
// ramps texture (made at runtime from Galaxy.cs's PlanetLook; alpha = glow), with seas below its sea level (flat,
// glossy), relief from the map's slopes, a finer copy of the map for detail up close, and a rainbow or a gas giant's
// colours ordered by latitude. Lit by the system's star (_TunduStar, set by SpaceWorld): a soft two-tone day and night,
// a warm glow along the night's edge, the sun glinting on water, a rim of air; the light turned toward the camera (a
// planet always shows its colours). Five texture reads a pixel.
Shader "Tundu/ProcPlanet"
{
    Properties
    {
        _Heights ("Height maps (array)", 2DArray) = "" {}
        _Ramps ("Colour ramps (a row a planet; alpha = glow)", 2D) = "white" {}
        _ShadeColor ("Night side tint", Color) = (0.3, 0.3, 0.62, 1)
        _Ambient ("Night side brightness", Range(0, 1)) = 0.2
        _Softness ("Day/night edge softness", Range(0.01, 0.6)) = 0.2
        _EdgeColor ("Day/night edge glow (alpha = strength)", Color) = (1, 0.6, 0.45, 0.3)
        // per planet (a property block): x = map slice, y = ramp row (v), z = turn (u), w = 1 or -1 (mirrored)
        _Planet ("Map, row, turn, mirror", Vector) = (0, 0.5, 0, 1)
        // x = sea level (-1 = none), y = latitude share (rainbow 1, gas giants a little), z = relief, w = glow strength
        _Surface ("Sea, latitude, relief, glow", Vector) = (-1, 0, 0.04, 1)
        _Atmo ("Atmosphere (rgb, a = strength)", Color) = (0.6, 0.8, 1, 1)
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
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadeColor;
                half _Ambient;
                half _Softness;
                half4 _EdgeColor;
                float4 _Planet;
                float4 _Surface;
                half4 _Atmo;
            CBUFFER_END
            TEXTURE2D_ARRAY(_Heights);
            SAMPLER(sampler_Heights);
            TEXTURE2D(_Ramps);
            SAMPLER(sampler_Ramps);
            float4 _TunduStar;        // the star's place (world); w = 1
            half4 _TunduStarColor;    // its light

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 eastWS : TEXCOORD3;    // the way the map's u grows; w = the latitude's cosine
                float3 northWS : TEXCOORD4;   // the way its v grows
                float3 lightWS : TEXCOORD5;   // the light's way (the star's, turned toward the camera)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 n = normalize(v.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldDir(n);
                float coslat = sqrt(saturate(1.0 - n.y * n.y));
                float3 east = float3(-n.z, 0.0, n.x) / max(coslat, 1e-4);
                float3 north = float3(-n.y * n.x, 1.0 - n.y * n.y, -n.y * n.z) / max(coslat, 1e-4);
                o.eastWS = float4(TransformObjectToWorldDir(east) * _Planet.w, coslat);
                o.northWS = TransformObjectToWorldDir(north);
                o.uv = float2(v.uv.x * _Planet.w + _Planet.z, v.uv.y);
                // the star's light turned toward the camera: a planet is seen lit from the side or the front, never only
                // its night (flying in from outside, the planets' sunny sides face away)
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float3 toStar = normalize(_TunduStar.xyz - centre);
                float3 toCam = normalize(_WorldSpaceCameraPos - centre);
                o.lightWS = normalize(toStar + toCam * 1.1 + float3(0, 0.25, 0));
                return o;
            }

            half Height(float2 uv)
            {
                return SAMPLE_TEXTURE2D_ARRAY(_Heights, sampler_Heights, uv, _Planet.x).r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                const float2 texel = float2(1.0 / 2048.0, 1.0 / 1024.0) * 1.5;
                half h = Height(i.uv);
                half hu = Height(i.uv + float2(texel.x, 0.0));
                half hv = Height(i.uv + float2(0.0, texel.y));
                // a finer copy of the same map: detail up close
                half detail = Height(i.uv * float2(5.0, 5.0) + float2(0.37, 0.11)) - 0.5h;
                half sea = (half)_Surface.x;
                half land = sea < 0.0h ? 1.0h : smoothstep(sea - 0.004h, sea + 0.004h, h);
                half hc = saturate(h + detail * 0.06h * land);
                // a rainbow's stripes (and a gas giant's colours) ordered by latitude
                hc = lerp(hc, saturate(i.uv.y + (hc - 0.5h) * 0.3h), (half)_Surface.y);
                half4 ramp = SAMPLE_TEXTURE2D(_Ramps, sampler_Ramps, float2(hc * (255.0 / 256.0) + 0.5 / 256.0, _Planet.y));

                float3 n = normalize(i.normalWS);
                // relief: the map's slopes bend the normal (none on the sea; the poles' squeezed longitudes eased)
                float coslat = max(0.25, i.eastWS.w);
                float3 east = i.eastWS.xyz;
                float3 north = i.northWS;
                float relief = _Surface.z * land;
                float su = (hu - h) / (texel.x * 6.2832 * coslat);
                float sv = (hv - h) / (texel.y * 3.1416);
                float3 nb = normalize(n - relief * (su * east + sv * north));

                float3 L = normalize(i.lightWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = dot(n, L);
                half lit = smoothstep(-_Softness, _Softness, ndl);
                half shade = 0.72h + 0.42h * saturate(dot(nb, L)) - 0.25h * saturate(-dot(nb, L) + ndl);
                half3 albedo = ramp.rgb;
                half3 day = albedo * _TunduStarColor.rgb * shade;
                half3 night = albedo * _ShadeColor.rgb * _Ambient;
                half3 color = lerp(night, day, lit);
                color += _EdgeColor.rgb * (_EdgeColor.a * 4.0h * lit * (1.0h - lit)) * albedo;
                // the sun glinting on water, a little sheen on land
                float3 H = normalize(L + V);
                half spec = pow(saturate(dot(nb, H)), lerp(70.0h, 18.0h, land)) * lerp(1.1h, 0.08h, land);
                color += _TunduStarColor.rgb * spec * lit;
                // a rim of air
                half rim = pow(1.0h - saturate(dot(n, V)), 2.4h);
                color += _Atmo.rgb * (rim * _Atmo.a * (0.2h + 0.8h * lit));
                // lava, crystal veins: their glow shows on the night side too
                color += albedo * ramp.a * (half)_Surface.w * (1.2h - 0.6h * lit);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
