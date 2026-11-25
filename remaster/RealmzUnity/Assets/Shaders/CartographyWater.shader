Shader "Realmz/CartographyWater"
{
    Properties
    {
        _BaseMap ("Water Albedo", 2D) = "white" {}
        _BumpMap ("Water Normal", 2D) = "bump" {}
        _BaseColor ("Tint Color", Color) = (0.16, 0.38, 0.46, 1)
        _ShimmerColor ("Pixel Ripple Specular", Color) = (0.94, 0.99, 1.0, 1)
        _ShimmerStrength ("Pixel Ripple Strength", Range(0, 1)) = 0.34
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        // Global maps updated by BywaterTopDownLightmap and BywaterDioramaController
        TEXTURE2D(_TopDownLightmapTex);
        SAMPLER(sampler_TopDownLightmapTex);
        TEXTURE2D(_WorldStaticMap);
        SAMPLER(sampler_WorldStaticMap);
        float4 _TopDownLightmapBounds;
        float _CartographyTimeOffset;
        TEXTURE2D(_FogOfWarTex);
        SAMPLER(sampler_FogOfWarTex);
        float _FogOfWarEnabled;

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShimmerColor;
            float _ShimmerStrength;
            float _WaveScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

                // Global top-down lightmap (bridge shadows, cloud dapples, lantern pools) + baked SDF shoreline proximity
                half4 tdLm = half4(1.0h, 1.0h, 1.0h, 0.0h);
                half shoreProx = 0.0h;
                bool hasBounds = (_TopDownLightmapBounds.z > 0.00001 && _TopDownLightmapBounds.w > 0.00001);
                float2 lmUV = hasBounds ? saturate((input.positionWS.xz - _TopDownLightmapBounds.xy) * _TopDownLightmapBounds.zw) : float2(0.0, 0.0);
                if (hasBounds)
                {
                    tdLm = SAMPLE_TEXTURE2D(_TopDownLightmapTex, sampler_TopDownLightmapTex, lmUV);
                    shoreProx = SAMPLE_TEXTURE2D(_WorldStaticMap, sampler_WorldStaticMap, lmUV).g;
                }

                // Shallow bank absorption blend toward dark mossy riverbed mud
                half bankMud = smoothstep(0.55h, 0.96h, shoreProx) * 0.42h;
                col = lerp(col, half3(0.11h, 0.18h, 0.14h), bankMud);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half ndl = saturate(dot(nWS, mainLight.direction));
                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, 0.94h);

                half3 addLighting = half3(0.0h, 0.0h, 0.0h);
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(nWS, addLight.direction));
                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addSpec = pow(saturate(dot(nWS, addHalf)), 28.0h) * 1.15h;
                    addLighting += (col * (addNdotL * 1.35h) + addSpec) * addLight.color * addAtten;
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(nWS, addLight.direction));
                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addSpec = pow(saturate(dot(nWS, addHalf)), 28.0h) * 1.15h;
                    addLighting += (col * (addNdotL * 1.35h) + addSpec) * addLight.color * addAtten;
                LIGHT_LOOP_END
                #endif

                // Crisp quantized pixel sparkle glints (1-texel sparkles, no soft oval blobs)
                float2 cell = floor(snappedWS * 16.0);
                float sparkHash = Hash21(cell + floor(waterTime * 3.0) * 0.13);
                float2 localPx = floor(frac(snappedWS * 16.0) * 4.0);
                float isRippleBar = step(1.5, localPx.y) * step(localPx.y, 2.5) * step(1.0, localPx.x) * step(localPx.x, 3.0);
                half glint = step(0.955, sparkHash) * isRippleBar * (half)_ShimmerStrength * shadowAtten;

                // Dynamic pixel-dithered shoreline foam driven by baked _WorldStaticMap.g SDF proximity
                float wavePerturb = Hash21(floor(snappedWS * 16.0)) * 0.07 + sin(snappedWS.x * 3.5 + snappedWS.y * 2.7) * 0.04;
                float distToBank = (1.0 - (float)shoreProx) * 0.55 + wavePerturb;
                float waveEdge = 0.16 + 0.05 * sin(snappedWS.x * 5.0 + snappedWS.y * 3.0 + floor(waterTime * 4.0) * 0.45);
                float edgeNoise = Hash21(floor(snappedWS * 64.0));
                half foam = step(distToBank + edgeNoise * 0.06, waveEdge) * 0.55h;

                half3 ambient = SampleSH(nWS) * lerp(0.44h, 0.28h, 1.0h - tdLm.r);
                half3 lanternPool = col * half3(1.0h, 0.62h, 0.24h) * (tdLm.a * 1.15h);
                half3 lit = col * (mainLight.color * (0.45h + 0.55h * ndl) * shadowAtten + ambient) * tdLm.rgb + addLighting + lanternPool;
                lit = lerp(lit, _ShimmerColor.rgb * 0.88h * tdLm.rgb, foam);
                lit += _ShimmerColor.rgb * glint * tdLm.rgb;

                if (_FogOfWarEnabled > 0.5 && hasBounds)
                {
                    half fogState = (half)SAMPLE_TEXTURE2D_LOD(_FogOfWarTex, sampler_FogOfWarTex, lmUV, 0).r;
                    half luma = dot(lit, half3(0.2126h, 0.7152h, 0.0722h));
                    half3 desat = lerp(lit, half3(luma, luma, luma), 0.55h);
                    half3 exploredColor = desat * half3(0.84h, 0.92h, 1.12h) * 0.42h;
                    half3 unseenColor = lerp(half3(0.051h, 0.063h, 0.106h) * 0.25h, desat * half3(0.75h, 0.85h, 1.12h) * 0.08h, 0.45h);
                    half toExplored = saturate(fogState / 0.45h);
                    half toVisible = saturate((fogState - 0.45h) / 0.55h);
                    lit = lerp(lerp(unseenColor, exploredColor, toExplored), lit, toVisible);
                }

                lit = MixFog(lit, input.fogCoord);
                return half4(lit, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
