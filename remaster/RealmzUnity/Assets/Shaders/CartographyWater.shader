Shader "Realmz/CartographyWater"
{
    Properties
    {
        _BaseMap ("Water Albedo", 2D) = "white" {}
        _BumpMap ("Water Normal", 2D) = "bump" {}
        _BaseColor ("Tint Color", Color) = (0.36, 0.72, 0.88, 1)
        _ShimmerColor ("Pixel Ripple Specular", Color) = (0.94, 0.99, 1.0, 1)
        _ShimmerStrength ("Pixel Ripple Strength", Range(0, 1)) = 0.28
        _WaveScale ("World Tile Meters", Range(0.5, 8)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            TEXTURE2D(_BywaterTopDownLightmap);
            SAMPLER(sampler_BywaterTopDownLightmap);
            float4 _BywaterWorldRect;
            float _CartographyTimeOffset;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShimmerColor;
                float _ShimmerStrength;
                float _WaveScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogCoord : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Snap sampling onto the global 64 px/m pixel grid (1/64 m texels)
                float2 snappedWS = floor(input.positionWS.xz * 64.0) / 64.0;
                float tileStep = 1.0 / max(_WaveScale, 0.25);

                // Discrete pixel flow step along +X
                float waterTime = _Time.y + _CartographyTimeOffset;
                float flowOffset = floor(waterTime * 10.0) / 64.0;
                float2 uvA = (snappedWS + float2(flowOffset, 0.0)) * tileStep;
                float2 uvB = (snappedWS * float2(1.0, 1.15) - float2(flowOffset * 0.5, floor(waterTime * 4.0) / 64.0)) * (tileStep * 1.25);

                half3 baseWater = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvA).rgb;
                half3 overlayWater = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB).rgb;
                half3 col = lerp(baseWater, overlayWater, 0.35h) * _BaseColor.rgb;

                // Subtle normal-mapped pixel surface relief
                half3 nTex = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvA));
                half3 nWS = normalize(half3(nTex.x * 0.55h, 1.0h, nTex.y * 0.55h));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(nWS, mainLight.direction));
                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, 0.85h);

                // Global top-down lightmap (bridge shadows, cloud dapples, riverbank AO)
                float2 mapUV = (input.positionWS.xz - _BywaterWorldRect.xy) / max(_BywaterWorldRect.zw, float2(0.001, 0.001));
                half3 tdLm = half3(1.0h, 1.0h, 1.0h);
                if (mapUV.x >= 0.0 && mapUV.x <= 1.0 && mapUV.y >= 0.0 && mapUV.y <= 1.0)
                {
                    tdLm = SAMPLE_TEXTURE2D(_BywaterTopDownLightmap, sampler_BywaterTopDownLightmap, saturate(mapUV)).rgb;
                }

                // Crisp quantized pixel sparkle glints (1-texel sparkles, no soft oval blobs)
                float2 cell = floor(snappedWS * 16.0);
                float sparkHash = Hash21(cell + floor(waterTime * 3.0) * 0.13);
                float2 localPx = floor(frac(snappedWS * 16.0) * 4.0);
                float isRippleBar = step(1.5, localPx.y) * step(localPx.y, 2.5) * step(1.0, localPx.x) * step(localPx.x, 3.0);
                half glint = step(0.955, sparkHash) * isRippleBar * (half)_ShimmerStrength * shadowAtten;

                // Crisp pixel-dithered shoreline foam edge near z=3.0 (south bank) and z=5.0 (north bank)
                float distToBank = min(abs(input.positionWS.z - 3.0), abs(input.positionWS.z - 5.0));
                float waveEdge = 0.16 + 0.05 * sin(snappedWS.x * 5.0 + floor(waterTime * 4.0) * 0.45);
                float edgeNoise = Hash21(floor(snappedWS * 64.0));
                half foam = step(distToBank + edgeNoise * 0.06, waveEdge) * 0.55h;

                half3 ambient = SampleSH(nWS) * 0.52h;
                half3 lit = col * (mainLight.color * (0.45h + 0.55h * ndl) * shadowAtten + ambient) * tdLm;
                lit = lerp(lit, _ShimmerColor.rgb * 0.92h, foam);
                lit += _ShimmerColor.rgb * glint;

                lit = MixFog(lit, input.fogCoord);
                return half4(lit, 1.0h);
            }
            ENDHLSL
        }
    }
}
