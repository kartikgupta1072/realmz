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
                float2 cliffAndAO : TEXCOORD5;
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
                }
                else
                {
                    rawUV = float2(positionWS.x, positionWS.y) * scale;
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
                output.cliffAndAO = input.uv1;
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
                half w5 = saturate((half)input.cliffAndAO.x);
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

                half3 a0, a1, a2, a3, a4, a5;
                half3 n0, n1, n2, n3, n4, n5;
                half s0, s1, s2, s3, s4, s5;

                SampleLayerSlice(0.0, w0, snappedUV, microJitter, a0, n0, s0);
                SampleLayerSlice(1.0, w1, snappedUV, microJitter, a1, n1, s1);
                SampleLayerSlice(2.0, w2, snappedUV, microJitter, a2, n2, s2);
                SampleLayerSlice(3.0, w3, snappedUV, microJitter, a3, n3, s3);
                SampleLayerSlice(4.0, w4, snappedUV, microJitter, a4, n4, s4);
                SampleLayerSlice(5.0, w5, snappedUV, microJitter, a5, n5, s5);

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
                half creaseAO = lerp(1.0h, saturate((half)input.cliffAndAO.y), _CreaseAOStrength);
                albedo *= lerp(0.55h, 1.0h, creaseAO);

                // Tangent-to-world normal
                float3 tangentWS = length(input.tangentWS) > 0.001 ? SafeNormalize(input.tangentWS) : float3(1.0, 0.0, 0.0);
                float3 bitangentWS = length(input.bitangentWS) > 0.001 ? SafeNormalize(input.bitangentWS) : cross(baseNormalWS, tangentWS);
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
                if (_TopDownLightmapBounds.z > 0.00001 && _TopDownLightmapBounds.w > 0.00001)
                {
                    float2 lmUV = saturate((input.positionWS.xz - _TopDownLightmapBounds.xy) * _TopDownLightmapBounds.zw);
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
                half3 lanternPool = albedo * _LanternPoolColor.rgb * (topDownLM.a * _LanternPoolIntensity);

                half3 color = ambientColor + directDiffuse + directSpecular + addLighting + lanternPool;
                color = clamp(color, half3(0.0h, 0.0h, 0.0h), half3(64.0h, 64.0h, 64.0h));
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
