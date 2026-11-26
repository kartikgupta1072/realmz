Shader "Realmz/CartographyTerrain"
{
    Properties
    {
        _TerrainArray ("Terrain Albedo Array (RGBA=Albedo+NoiseMask)", 2DArray) = "" {}
        _NormalArray ("Terrain Normal Array", 2DArray) = "" {}
        _WorldTexelScale ("World Tile Scale (tiles per meter)", Float) = 0.5
        _TexelResolution ("Layer Texel Resolution", Float) = 128.0
        _NoiseBlendSharpness ("Noise Blend Sharpness", Range(1, 32)) = 18.0
        _NoiseMaskStrength ("Noise Mask Contrast", Range(0, 1)) = 0.55
        _BumpScale ("Normal Scale", Range(0, 2)) = 1.0
        _CreaseAOStrength ("Crease & Footprint AO Strength", Range(0, 1)) = 1.0
        _MacroScale ("Macro Variation Scale", Float) = 0.085
        _MacroStrength ("Macro Variation Strength", Range(0, 0.4)) = 0.10
        _Smoothness ("Smoothness", Range(0, 1)) = 0.06
        _LanternPoolColor ("Lantern Pool Color", Color) = (1.0, 0.62, 0.24, 1)
        _LanternPoolIntensity ("Lantern Pool Intensity", Range(0, 3)) = 1.35
        _GroundValueScale ("SoC Ground Chiaroscuro Scale", Range(0.5, 1.2)) = 0.78
        _RoadValueBoost ("Road & Cobble Value Scale", Range(0.6, 1.2)) = 0.88
        _HorizonHazeColor ("North Horizon Haze Color", Color) = (0.91, 0.83, 0.70, 1)
        _HorizonHazeStrength ("North Horizon Haze Strength", Range(0, 0.35)) = 0.09
        _LayerCount ("Terrain Array Layer Count (0=Auto)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_ARRAY(_TerrainArray);
        SAMPLER(sampler_TerrainArray);
        TEXTURE2D_ARRAY(_NormalArray);
        SAMPLER(sampler_NormalArray);

        // Global top-down lightmap updated by BywaterTopDownLightmap
        TEXTURE2D(_TopDownLightmapTex);
        SAMPLER(sampler_TopDownLightmapTex);
        float4 _TopDownLightmapBounds;
        TEXTURE2D(_FogOfWarTex);
        SAMPLER(sampler_FogOfWarTex);
        float _FogOfWarEnabled;

        CBUFFER_START(UnityPerMaterial)
            float _WorldTexelScale;
            float _TexelResolution;
            half _NoiseBlendSharpness;
            half _NoiseMaskStrength;
            half _BumpScale;
            half _CreaseAOStrength;
            float _MacroScale;
            half _MacroStrength;
            half _Smoothness;
            half4 _LanternPoolColor;
            half _LanternPoolIntensity;
            half _GroundValueScale;
            half _RoadValueBoost;
            half4 _HorizonHazeColor;
            half _HorizonHazeStrength;
            float _LayerCount;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CartoTerrainVert
            #pragma fragment CartoTerrainFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float4 splatWeights : TEXCOORD4;
                float4 cliffAOAndBiome : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                float4 shadowCoord : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float CartoHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float CartoValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = CartoHash12(i + float2(0.0, 0.0));
                float b = CartoHash12(i + float2(1.0, 0.0));
                float c = CartoHash12(i + float2(0.0, 1.0));
                float d = CartoHash12(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half CartoMacroFbm(float2 p)
            {
                float n1 = CartoValueNoise(p);
                float n2 = CartoValueNoise(p * 2.13 + float2(7.4, 3.1));
                return (half)((n1 * 0.67 + n2 * 0.33) * 2.0 - 1.0);
            }

            half3 DecodeTangentNormal(half4 packedNormal, half scale)
            {
                half3 n;
                n.xy = (packedNormal.xy * 2.0h - 1.0h) * scale;
                n.z = sqrt(max(0.001h, 1.0h - saturate(dot(n.xy, n.xy))));
                return SafeNormalize(n);
            }

            float2 ComputePixelLockedUV(float3 positionWS, float3 normalWS, out float2 pixelCoord)
            {
                float scale = max(0.05, _WorldTexelScale);
                float texRes = max(16.0, _TexelResolution);

                // Use world XZ for horizontal plateaus and planar wall projection for steep cliff faces
                float3 absN = abs(normalWS);
                float2 rawUV;
                if (absN.y >= 0.55)
                {
                    rawUV = positionWS.xz * scale;
                }
                else if (absN.x >= absN.z)
                {
                    rawUV = float2(positionWS.z, positionWS.y) * scale;
                    rawUV.y += CartoHash12(floor(positionWS.xz - normalWS.xz * 0.02)) * 0.37;
                }
                else
                {
                    rawUV = float2(positionWS.x, positionWS.y) * scale;
                    rawUV.y += CartoHash12(floor(positionWS.xz - normalWS.xz * 0.02)) * 0.37;
                }

                pixelCoord = floor(rawUV * texRes);
                return frac((pixelCoord + 0.5) / texRes);
            }

            void SampleLayerSlice(
                float sliceIndex,
                half weight,
                float2 snappedUV,
                half microJitter,
                out half3 albedo,
                out half3 normalTS,
                out half score)
            {
                if (weight > 0.002h)
                {
                    half4 albSample = SAMPLE_TEXTURE2D_ARRAY_LOD(_TerrainArray, sampler_TerrainArray, snappedUV, sliceIndex, 0);
                    half4 nrmSample = SAMPLE_TEXTURE2D_ARRAY_LOD(_NormalArray, sampler_NormalArray, snappedUV, sliceIndex, 0);
                    albedo = albSample.rgb;
                    normalTS = DecodeTangentNormal(nrmSample, _BumpScale);

                    half maskVal = saturate(albSample.a + microJitter);
                    score = weight + (maskVal - 0.5h) * _NoiseMaskStrength;
                }
                else
                {
                    albedo = half3(0.0h, 0.0h, 0.0h);
                    normalTS = half3(0.0h, 0.0h, 1.0h);
                    score = -10.0h;
                }
            }

            void SampleBiomeBlendSlice(
                float baseSlice,
                float extSlice,
                half extBlend,
                uint arrayDepth,
                half weight,
                float2 snappedUV,
                half microJitter,
                out half3 albedo,
                out half3 normalTS,
                out half score)
            {
                if (weight <= 0.002h)
                {
                    albedo = half3(0.0h, 0.0h, 0.0h);
                    normalTS = half3(0.0h, 0.0h, 1.0h);
                    score = -10.0h;
                    return;
                }

                half4 baseAlb = SAMPLE_TEXTURE2D_ARRAY_LOD(_TerrainArray, sampler_TerrainArray, snappedUV, baseSlice, 0);
                half4 baseNrm = SAMPLE_TEXTURE2D_ARRAY_LOD(_NormalArray, sampler_NormalArray, snappedUV, baseSlice, 0);
                half3 baseN = DecodeTangentNormal(baseNrm, _BumpScale);

                if (extBlend > 0.01h && extSlice >= 0.0 && max(0.0, extSlice) < (float)arrayDepth)
                {
                    half4 extAlb = SAMPLE_TEXTURE2D_ARRAY_LOD(_TerrainArray, sampler_TerrainArray, snappedUV, extSlice, 0);
                    half4 extNrm = SAMPLE_TEXTURE2D_ARRAY_LOD(_NormalArray, sampler_NormalArray, snappedUV, extSlice, 0);
                    half3 extN = DecodeTangentNormal(extNrm, _BumpScale);

                    half noiseEdge = saturate((extBlend - 0.5h) * 2.2h + (extAlb.a - baseAlb.a) * _NoiseMaskStrength + 0.5h);
                    albedo = lerp(baseAlb.rgb, extAlb.rgb, noiseEdge);
                    normalTS = SafeNormalize(lerp(baseN, extN, noiseEdge));
                    half combinedMask = lerp(baseAlb.a, extAlb.a, noiseEdge);
                    half maskVal = saturate(combinedMask + microJitter);
                    score = weight + (maskVal - 0.5h) * _NoiseMaskStrength;
                }
                else
                {
                    albedo = baseAlb.rgb;
                    normalTS = baseN;
                    half maskVal = saturate(baseAlb.a + microJitter);
                    score = weight + (maskVal - 0.5h) * _NoiseMaskStrength;
                }
            }

            Varyings CartoTerrainVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.tangentWS = normInputs.tangentWS;
                output.bitangentWS = normInputs.bitangentWS;
                output.splatWeights = input.color;
                output.cliffAOAndBiome = float4(input.uv1.x, input.uv1.y, input.uv2.x, input.uv2.y);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.shadowCoord = GetShadowCoord(posInputs);

                return output;
            }

            half4 CartoTerrainFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 baseNormalWS = SafeNormalize(input.normalWS);
                float2 pixelCoord;
                float2 snappedUV = ComputePixelLockedUV(input.positionWS, baseNormalWS, pixelCoord);
                half microJitter = (half)((CartoHash12(pixelCoord) - 0.5) * 0.06);

                // Resolve 6 terrain layer weights:
                // 0=meadow_grass, 1=moss_patch, 2=dirt_road, 3=cobble_plaza, 4=river_mud, 5=cliff_rock
                half w5 = saturate((half)input.cliffAOAndBiome.x);
                half groundBudget = 1.0h - w5;

                half w1 = saturate((half)input.splatWeights.r);
                half w2 = saturate((half)input.splatWeights.g);
                half w3 = saturate((half)input.splatWeights.b);
                half w4 = saturate((half)input.splatWeights.a);
                half sumOverlay = w1 + w2 + w3 + w4;
                if (sumOverlay > 1.0h)
                {
                    half invSum = 1.0h / sumOverlay;
                    w1 *= invSum;
                    w2 *= invSum;
                    w3 *= invSum;
                    w4 *= invSum;
                    sumOverlay = 1.0h;
                }
                half w0 = max(0.0h, 1.0h - sumOverlay) * groundBudget;
                w1 *= groundBudget;
                w2 *= groundBudget;
                w3 *= groundBudget;
                w4 *= groundBudget;

                uint texW = 0, texH = 0, texD = 6;
                #if defined(SHADER_API_D3D11) || defined(SHADER_API_D3D12) || defined(SHADER_API_VULKAN) || defined(SHADER_API_METAL) || defined(SHADER_API_GLCORE)
                    _TerrainArray.GetDimensions(texW, texH, texD);
                #endif
                uint arrayDepth = (_LayerCount > 0.5) ? (uint)floor(_LayerCount + 0.5) : max(texD, 6u);

                half3 a0, a1, a2, a3, a4, a5;
                half3 n0, n1, n2, n3, n4, n5;
                half s0, s1, s2, s3, s4, s5;

                if (arrayDepth <= 6u)
                {
                    SampleLayerSlice(0.0, w0, snappedUV, microJitter, a0, n0, s0);
                    SampleLayerSlice(1.0, w1, snappedUV, microJitter, a1, n1, s1);
                    SampleLayerSlice(2.0, w2, snappedUV, microJitter, a2, n2, s2);
                    SampleLayerSlice(3.0, w3, snappedUV, microJitter, a3, n3, s3);
                    SampleLayerSlice(4.0, w4, snappedUV, microJitter, a4, n4, s4);
                    SampleLayerSlice(5.0, w5, snappedUV, microJitter, a5, n5, s5);
                }
                else
                {
                    half biomeHint = saturate((half)input.cliffAOAndBiome.z);
                    half elevHint = (half)input.cliffAOAndBiome.w;
                    half creaseVal = saturate((half)input.cliffAOAndBiome.y);
                    half macroA = CartoMacroFbm(input.positionWS.xz * 0.055);
                    half macroB = CartoMacroFbm(input.positionWS.xz * 0.11 + float2(19.3, 7.1));

                    // Slot 0: meadow_grass -> alpine_frost_grass (13 only on high peaks), dark_forest_moss (7 on plateaus), or marsh_bog_turf (10)
                    float ext0 = -1.0;
                    half blend0 = 0.0h;
                    if (elevHint >= 0.92h && arrayDepth > 13u)
                    {
                        ext0 = 13.0;
                        blend0 = smoothstep(-0.35h, 0.35h, macroA + 0.25h);
                    }
                    else if (elevHint >= 0.45h && arrayDepth > 7u)
                    {
                        ext0 = 7.0;
                        blend0 = smoothstep(-0.35h, 0.35h, macroA + 0.15h) * 0.65h;
                    }
                    else if (biomeHint >= 0.40h && biomeHint < 0.65h && arrayDepth > 10u)
                    {
                        ext0 = 10.0;
                        blend0 = smoothstep(-0.45h, 0.25h, macroB + 0.35h);
                    }
                    else if (biomeHint >= 0.65h && biomeHint < 0.90h && arrayDepth > 7u)
                    {
                        ext0 = 7.0;
                        blend0 = smoothstep(-0.40h, 0.25h, macroA + 0.30h);
                    }
                    else if (macroA > 0.35h && arrayDepth > 10u)
                    {
                        ext0 = 10.0;
                        blend0 = smoothstep(0.35h, 0.85h, macroA) * 0.65h;
                    }

                    // Slot 1: moss_patch -> dark_forest_moss (7), fallen_leaves_loam (8), or marsh_bog_turf (10)
                    float ext1 = -1.0;
                    half blend1 = 0.0h;
                    if (biomeHint >= 0.65h && biomeHint < 0.90h)
                    {
                        if (macroB > 0.12h && arrayDepth > 8u)
                        {
                            ext1 = 8.0;
                            blend1 = smoothstep(0.12h, 0.55h, macroB);
                        }
                        else if (arrayDepth > 7u)
                        {
                            ext1 = 7.0;
                            blend1 = smoothstep(-0.55h, 0.20h, macroA + 0.35h);
                        }
                    }
                    else if (biomeHint >= 0.40h && biomeHint < 0.65h && arrayDepth > 10u)
                    {
                        ext1 = 10.0;
                        blend1 = smoothstep(-0.30h, 0.40h, macroB + 0.25h);
                    }
                    else if (macroA > 0.20h && arrayDepth > 7u)
                    {
                        ext1 = 7.0;
                        blend1 = smoothstep(0.20h, 0.70h, macroA);
                    }

                    // Slot 2: dirt_road -> highland_scree (9), forest_root_dirt (6), or fallen_leaves_loam (8)
                    float ext2 = -1.0;
                    half blend2 = 0.0h;
                    if (elevHint >= 0.45h && arrayDepth > 9u)
                    {
                        ext2 = 9.0;
                        blend2 = smoothstep(-0.40h, 0.20h, macroB + 0.35h);
                    }
                    else if (biomeHint >= 0.65h && biomeHint < 0.90h)
                    {
                        if (macroB > 0.20h && arrayDepth > 8u)
                        {
                            ext2 = 8.0;
                            blend2 = 0.75h;
                        }
                        else if (arrayDepth > 6u)
                        {
                            ext2 = 6.0;
                            blend2 = 0.85h;
                        }
                    }

                    // Slot 3: cobble_plaza -> inn_wood_planks (16) / hearth_ash_stone (15) / temple_marble_tile (17)
                    float ext3 = -1.0;
                    half blend3 = 0.0h;
                    if (biomeHint >= 0.90h && elevHint <= 0.02h && creaseVal < 0.72h)
                    {
                        if (arrayDepth > 16u && macroB >= 0.0h)
                        {
                            ext3 = 16.0;
                            blend3 = 0.80h;
                        }
                        else if (arrayDepth > 15u)
                        {
                            ext3 = 15.0;
                            blend3 = 0.70h;
                        }
                    }

                    // Slot 4: river_mud -> swamp_mire_mud (11)
                    float ext4 = -1.0;
                    half blend4 = 0.0h;
                    if (arrayDepth > 11u && macroB > -0.25h)
                    {
                        ext4 = 11.0;
                        blend4 = smoothstep(-0.25h, 0.35h, macroB);
                    }

                    // Slot 5: cliff_rock -> highland_scree (9) on elevated highland walls
                    float ext5 = -1.0;
                    half blend5 = 0.0h;
                    if (elevHint >= 0.45h && arrayDepth > 9u && macroA > -0.10h)
                    {
                        ext5 = 9.0;
                        blend5 = smoothstep(-0.10h, 0.45h, macroA) * 0.80h;
                    }

                    SampleBiomeBlendSlice(0.0, ext0, blend0, arrayDepth, w0, snappedUV, microJitter, a0, n0, s0);
                    SampleBiomeBlendSlice(1.0, ext1, blend1, arrayDepth, w1, snappedUV, microJitter, a1, n1, s1);
                    SampleBiomeBlendSlice(2.0, ext2, blend2, arrayDepth, w2, snappedUV, microJitter, a2, n2, s2);
                    SampleBiomeBlendSlice(3.0, ext3, blend3, arrayDepth, w3, snappedUV, microJitter, a3, n3, s3);
                    SampleBiomeBlendSlice(4.0, ext4, blend4, arrayDepth, w4, snappedUV, microJitter, a4, n4, s4);
                    SampleBiomeBlendSlice(5.0, ext5, blend5, arrayDepth, w5, snappedUV, microJitter, a5, n5, s5);
                }

                half maxScore = max(max(s0, s1), max(s2, max(s3, max(s4, s5))));
                half sharpness = max(1.0h, _NoiseBlendSharpness);
                half invSharp = 1.0h / sharpness;
                half threshold = maxScore - invSharp;

                half b0 = w0 > 0.002h ? saturate((s0 - threshold) * sharpness) : 0.0h;
                half b1 = w1 > 0.002h ? saturate((s1 - threshold) * sharpness) : 0.0h;
                half b2 = w2 > 0.002h ? saturate((s2 - threshold) * sharpness) : 0.0h;
                half b3 = w3 > 0.002h ? saturate((s3 - threshold) * sharpness) : 0.0h;
                half b4 = w4 > 0.002h ? saturate((s4 - threshold) * sharpness) : 0.0h;
                half b5 = w5 > 0.002h ? saturate((s5 - threshold) * sharpness) : 0.0h;

                half bSum = b0 + b1 + b2 + b3 + b4 + b5;
                if (bSum <= 1e-4h)
                {
                    b0 = 1.0h;
                    bSum = 1.0h;
                }
                half invBSum = 1.0h / bSum;
                b0 *= invBSum;
                b1 *= invBSum;
                b2 *= invBSum;
                b3 *= invBSum;
                b4 *= invBSum;
                b5 *= invBSum;

                half3 albedo = a0 * b0 + a1 * b1 + a2 * b2 + a3 * b3 + a4 * b4 + a5 * b5;
                half3 normalTS = SafeNormalize(n0 * b0 + n1 * b1 + n2 * b2 + n3 * b3 + n4 * b4 + n5 * b5);

                // Subtle macro tint across broad terrain patches
                half macroN = CartoMacroFbm(input.positionWS.xz * _MacroScale);
                albedo *= (1.0h + macroN * _MacroStrength);

                // Elevation crease and structure footprint AO from TEXCOORD1.y
                half creaseAO = lerp(1.0h, saturate((half)input.cliffAOAndBiome.y), _CreaseAOStrength);
                albedo *= lerp(0.45h, 1.0h, creaseAO);

                // SoC chiaroscuro ground-vs-road value split
                half roadFactor = saturate(b2 * 0.65h + b3);
                albedo *= lerp(_GroundValueScale, _RoadValueBoost, roadFactor);

                // Planar TBN aligned strictly with ComputePixelLockedUV
                float3 absN = abs(baseNormalWS);
                float3 rawT;
                float3 rawB;
                if (absN.y >= 0.55)
                {
                    rawT = float3(1.0, 0.0, 0.0);
                    rawB = float3(0.0, 0.0, 1.0);
                }
                else if (absN.x >= absN.z)
                {
                    rawT = float3(0.0, 0.0, 1.0);
                    rawB = float3(0.0, 1.0, 0.0);
                }
                else
                {
                    rawT = float3(1.0, 0.0, 0.0);
                    rawB = float3(0.0, 1.0, 0.0);
                }
                float3 tangentWS = SafeNormalize(rawT - baseNormalWS * dot(rawT, baseNormalWS));
                float3 bitangentWS = SafeNormalize(rawB - baseNormalWS * dot(rawB, baseNormalWS));
                half3 normalWS = SafeNormalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * baseNormalWS);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                // Global top-down cloud/canopy shadow (rgb) and warm lantern pools (a)
                half4 topDownLM = half4(1.0h, 1.0h, 1.0h, 0.0h);
                bool hasBounds = (_TopDownLightmapBounds.z > 0.00001 && _TopDownLightmapBounds.w > 0.00001);
                float2 lmUV = hasBounds ? saturate((input.positionWS.xz - _TopDownLightmapBounds.xy) * _TopDownLightmapBounds.zw) : float2(0.0, 0.0);
                if (hasBounds)
                {
                    topDownLM = SAMPLE_TEXTURE2D(_TopDownLightmapTex, sampler_TopDownLightmapTex, lmUV);
                }

                // Main directional sun + cascades modulated by top-down clouds/canopy
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                half3 mainContrib = mainLight.color * mainAtten * topDownLM.rgb;

                half specPower = exp2(2.0h + _Smoothness * 7.0h);
                half specStrength = _Smoothness * 0.35h;
                half3 mainHalf = SafeNormalize(mainLight.direction + viewDirWS);
                half mainNdotH = saturate(dot(normalWS, mainHalf));
                half mainSpec = mainNdotH > 0.001h ? (pow(mainNdotH, specPower) * specStrength) : 0.0h;

                half3 directDiffuse = albedo * mainContrib * mainNdotL;
                half3 directSpecular = mainContrib * mainSpec;

                // Ambient Spherical Harmonics softened by cloud/canopy + crease AO
                half3 ambientSH = SampleSH(normalWS);
                half3 ambientCloudMod = lerp(half3(1.0h, 1.0h, 1.0h), topDownLM.rgb, 0.40h);
                half3 ambientColor = albedo * ambientSH * ambientCloudMod * creaseAO;

                // Additional URP lights (Forward+ compatible)
                half3 addLighting = half3(0.0h, 0.0h, 0.0h);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    addLighting += albedo * addLight.color * (addAtten * addNdotL);
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    addLighting += albedo * addLight.color * (addAtten * addNdotL);
                LIGHT_LOOP_END
                #endif

                // Warm top-down lantern/torch pool contribution
                half3 lanternPool = albedo * _LanternPoolColor.rgb * (topDownLM.a * (_LanternPoolIntensity * 0.72h));

                half3 rawLit = ambientColor + directDiffuse + directSpecular + addLighting + lanternPool;
                half maxLum = max(rawLit.r, max(rawLit.g, rawLit.b));
                half compressedLum = maxLum <= 0.94h ? maxLum : 0.94h + (maxLum - 0.94h) / (1.0h + (maxLum - 0.94h) * 1.40h);
                half3 color = maxLum > 0.0001h ? rawLit * (compressedLum / maxLum) : rawLit;
                if (_TopDownLightmapBounds.w > 0.00001)
                {
                    half northFrac = saturate((half)((input.positionWS.z - _TopDownLightmapBounds.y) * _TopDownLightmapBounds.w));
                    color = lerp(color, _HorizonHazeColor.rgb, smoothstep(0.76h, 1.0h, northFrac) * _HorizonHazeStrength);
                }
                color = clamp(color, half3(0.0h, 0.0h, 0.0h), half3(64.0h, 64.0h, 64.0h));
                if (_FogOfWarEnabled > 0.5 && hasBounds)
                {
                    half fogState = (half)SAMPLE_TEXTURE2D_LOD(_FogOfWarTex, sampler_FogOfWarTex, lmUV, 0).r;
                    half luma = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                    half3 desat = lerp(color, half3(luma, luma, luma), 0.55h);
                    half3 exploredColor = desat * half3(0.84h, 0.92h, 1.12h) * 0.42h;
                    half3 unseenColor = lerp(half3(0.051h, 0.063h, 0.106h) * 0.25h, desat * half3(0.75h, 0.85h, 1.12h) * 0.08h, 0.45h);
                    half toExplored = saturate(fogState / 0.45h);
                    half toVisible = saturate((fogState - 0.45h) / 0.55h);
                    color = lerp(lerp(unseenColor, exploredColor, toExplored), color, toVisible);
                }
                color = MixFog(color, input.fogFactor);

                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
