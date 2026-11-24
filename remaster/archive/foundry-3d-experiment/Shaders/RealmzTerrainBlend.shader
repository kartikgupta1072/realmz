Shader "Realmz/TerrainBlend"
{
    Properties
    {
        [MainTexture] _BaseMap ("Fallback Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint Color", Color) = (1,1,1,1)
        _SplatMap ("Splat Control Map (RGBA)", 2D) = "red" {}
        _WorldBounds ("World Bounds (minX, minZ, invW, invD)", Vector) = (0, 0, 0.0111, 0.0111)
        _WorldTileScale ("World UV Scale (repeats per meter)", Float) = 0.5
        _BlendContrast ("Height Blend Contrast", Range(0.02, 0.6)) = 0.16
        _BumpScale ("Normal Scale", Range(0, 2)) = 1.0
        _Smoothness ("Base Smoothness Scale", Range(0, 2)) = 1.0
        _MacroScale ("Macro Variation Scale", Float) = 0.085
        _MacroStrength ("Macro Variation Strength", Range(0, 0.5)) = 0.15
        _GridHintStrength ("Tactical Grid Hint Strength", Range(0, 0.25)) = 0.0

        _Layer0Albedo ("Layer 0 Albedo", 2D) = "white" {}
        _Layer0Normal ("Layer 0 Normal", 2D) = "bump" {}
        _Layer0Mask ("Layer 0 Mask", 2D) = "white" {}
        _Layer0Color ("Layer 0 Tint", Color) = (1,1,1,1)

        _Layer1Albedo ("Layer 1 Albedo", 2D) = "white" {}
        _Layer1Normal ("Layer 1 Normal", 2D) = "bump" {}
        _Layer1Mask ("Layer 1 Mask", 2D) = "white" {}
        _Layer1Color ("Layer 1 Tint", Color) = (1,1,1,1)

        _Layer2Albedo ("Layer 2 Albedo", 2D) = "white" {}
        _Layer2Normal ("Layer 2 Normal", 2D) = "bump" {}
        _Layer2Mask ("Layer 2 Mask", 2D) = "white" {}
        _Layer2Color ("Layer 2 Tint", Color) = (1,1,1,1)

        _Layer3Albedo ("Layer 3 Albedo", 2D) = "white" {}
        _Layer3Normal ("Layer 3 Normal", 2D) = "bump" {}
        _Layer3Mask ("Layer 3 Mask", 2D) = "white" {}
        _Layer3Color ("Layer 3 Tint", Color) = (1,1,1,1)
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

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_SplatMap);
        SAMPLER(sampler_SplatMap);

        TEXTURE2D(_Layer0Albedo);
        SAMPLER(sampler_Layer0Albedo);
        TEXTURE2D(_Layer0Normal);
        TEXTURE2D(_Layer0Mask);

        TEXTURE2D(_Layer1Albedo);
        SAMPLER(sampler_Layer1Albedo);
        TEXTURE2D(_Layer1Normal);
        TEXTURE2D(_Layer1Mask);

        TEXTURE2D(_Layer2Albedo);
        SAMPLER(sampler_Layer2Albedo);
        TEXTURE2D(_Layer2Normal);
        TEXTURE2D(_Layer2Mask);

        TEXTURE2D(_Layer3Albedo);
        SAMPLER(sampler_Layer3Albedo);
        TEXTURE2D(_Layer3Normal);
        TEXTURE2D(_Layer3Mask);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _SplatMap_ST;
            float4 _WorldBounds;
            float _WorldTileScale;
            half _BlendContrast;
            half _BumpScale;
            half _Smoothness;
            float _MacroScale;
            half _MacroStrength;
            half _GridHintStrength;
            float4 _Layer0Albedo_ST;
            float4 _Layer1Albedo_ST;
            float4 _Layer2Albedo_ST;
            float4 _Layer3Albedo_ST;
            half4 _Layer0Color;
            half4 _Layer1Color;
            half4 _Layer2Color;
            half4 _Layer3Color;
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
            #pragma target 3.0
            #pragma vertex TerrainVert
            #pragma fragment TerrainFrag

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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float TerrainHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 TerrainHash21(float n)
            {
                float3 p3 = frac(float3(n, n, n) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float TerrainValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = TerrainHash12(i + float2(0.0, 0.0));
                float b = TerrainHash12(i + float2(1.0, 0.0));
                float c = TerrainHash12(i + float2(0.0, 1.0));
                float d = TerrainHash12(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half TerrainMacroFbm(float2 p)
            {
                float n1 = TerrainValueNoise(p);
                float n2 = TerrainValueNoise(p * 2.13 + float2(7.4, 3.1));
                float combined = n1 * 0.67 + n2 * 0.33;
                return (half)(combined * 2.0 - 1.0);
            }

            Varyings TerrainVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normInputs.normalWS;
                output.tangentWS = normInputs.tangentWS;
                output.bitangentWS = normInputs.bitangentWS;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.shadowCoord = GetShadowCoord(posInputs);

                return output;
            }

            #define SAMPLE_TERRAIN_LAYER(L, splatWeight, worldUV, uvDx, uvDy, lOffset, outAlb, outNrm, outMask) \
                half3 outAlb = half3(0.0h, 0.0h, 0.0h); \
                half3 outNrm = half3(0.0h, 0.0h, 1.0h); \
                half4 outMask = half4(0.0h, 1.0h, -2.0h, 0.0h); \
                if (splatWeight > 0.001h) \
                { \
                    float noiseVal##L = TerrainValueNoise(worldUV * 0.37 + lOffset); \
                    float lVal##L = noiseVal##L * 8.0; \
                    float fVal##L = frac(lVal##L); \
                    float ia##L = floor(lVal##L); \
                    float ib##L = ia##L + 1.0; \
                    float2 uvA##L = worldUV + TerrainHash21(ia##L + L * 19.0) * 17.31; \
                    float2 uvB##L = worldUV + TerrainHash21(ib##L + L * 19.0) * 17.31; \
                    half4 mA##L = SAMPLE_TEXTURE2D_GRAD(_Layer##L##Mask, sampler_Layer##L##Albedo, uvA##L, uvDx, uvDy); \
                    half4 mB##L = SAMPLE_TEXTURE2D_GRAD(_Layer##L##Mask, sampler_Layer##L##Albedo, uvB##L, uvDx, uvDy); \
                    half t##L = smoothstep(0.15h, 0.85h, saturate((half)fVal##L + (mB##L.b - mA##L.b) * 0.25h)); \
                    half4 aA##L = SAMPLE_TEXTURE2D_GRAD(_Layer##L##Albedo, sampler_Layer##L##Albedo, uvA##L, uvDx, uvDy); \
                    half4 aB##L = SAMPLE_TEXTURE2D_GRAD(_Layer##L##Albedo, sampler_Layer##L##Albedo, uvB##L, uvDx, uvDy); \
                    half3 nA##L = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_Layer##L##Normal, sampler_Layer##L##Albedo, uvA##L, uvDx, uvDy), _BumpScale); \
                    half3 nB##L = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_Layer##L##Normal, sampler_Layer##L##Albedo, uvB##L, uvDx, uvDy), _BumpScale); \
                    outAlb = lerp(aA##L.rgb, aB##L.rgb, t##L) * _Layer##L##Color.rgb; \
                    outNrm = SafeNormalize(lerp(nA##L, nB##L, t##L)); \
                    outMask = lerp(mA##L, mB##L, t##L); \
                }

            half4 TerrainFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Compute world-space splat UV with organic fbm perturbation (~0.35 world tiles)
                bool hasWorldBounds = (_WorldBounds.z > 0.00001 && _WorldBounds.w > 0.00001);
                float2 splatUV = hasWorldBounds
                    ? ((input.positionWS.xz - _WorldBounds.xy) * _WorldBounds.zw)
                    : input.uv;

                float2 perturbScale = hasWorldBounds ? _WorldBounds.zw : float2(0.0111, 0.0111);
                float2 perturb = float2(
                    TerrainValueNoise(input.positionWS.xz * 0.65 + float2(1.7, 9.2)) * 2.0 - 1.0,
                    TerrainValueNoise(input.positionWS.xz * 0.65 + float2(8.3, 2.8)) * 2.0 - 1.0);
                splatUV = saturate(splatUV + perturb * (0.35 * perturbScale));

                half4 splat = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, splatUV);
                half splatSum = dot(splat, half4(1.0h, 1.0h, 1.0h, 1.0h));
                splat = splatSum > 1e-4h ? (splat / splatSum) : half4(1.0h, 0.0h, 0.0h, 0.0h);

                // World-space repeating UV and precomputed mipmap derivatives for artifact-free stochastic sampling
                float tileScale = max(0.01, _WorldTileScale);
                float2 worldUV = input.positionWS.xz * tileScale;
                float2 uvDx = ddx(worldUV);
                float2 uvDy = ddy(worldUV);

                SAMPLE_TERRAIN_LAYER(0, splat.x, worldUV, uvDx, uvDy, float2(0.0, 0.0), alb0, nrm0, mask0)
                SAMPLE_TERRAIN_LAYER(1, splat.y, worldUV, uvDx, uvDy, float2(3.17, 5.43), alb1, nrm1, mask1)
                SAMPLE_TERRAIN_LAYER(2, splat.z, worldUV, uvDx, uvDy, float2(7.91, 1.87), alb2, nrm2, mask2)
                SAMPLE_TERRAIN_LAYER(3, splat.w, worldUV, uvDx, uvDy, float2(2.39, 8.61), alb3, nrm3, mask3)

                // True height-based splat blend
                half4 heights = half4(mask0.b, mask1.b, mask2.b, mask3.b);
                half4 hScore = heights + splat * 1.15h;
                half maxS = max(max(hScore.x, hScore.y), max(hScore.z, hScore.w)) - _BlendContrast;
                half4 w = max(hScore - maxS, 0.0h);
                w /= max(dot(w, 1.0h), 1e-4h);

                half3 fallbackSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 albedo = (alb0 * w.x + alb1 * w.y + alb2 * w.z + alb3 * w.w) * _BaseColor.rgb * fallbackSample;
                half3 normalTS = SafeNormalize(nrm0 * w.x + nrm1 * w.y + nrm2 * w.z + nrm3 * w.w);
                half ao = saturate(mask0.g * w.x + mask1.g * w.y + mask2.g * w.z + mask3.g * w.w);
                half smoothness = saturate((mask0.a * w.x + mask1.a * w.y + mask2.a * w.z + mask3.a * w.w) * _Smoothness);

                // World-space macro variation (breaks up 90x90 tile repetition naturally)
                half macroN = TerrainMacroFbm(input.positionWS.xz * _MacroScale);
                albedo *= (1.0h + macroN * _MacroStrength);
                smoothness = saturate(smoothness + macroN * (_MacroStrength * 0.25h));

                // Optional faint tactical grid hint aligned to 1.0m tile centers at integer XZ
                if (_GridHintStrength > 0.001h)
                {
                    float2 tileFrac = abs(frac(input.positionWS.xz + 0.5) - 0.5);
                    half borderMask = smoothstep(0.455h, 0.495h, (half)max(tileFrac.x, tileFrac.y));
                    half centerDot = 1.0h - smoothstep(0.03h, 0.08h, (half)length(tileFrac));
                    half gridHint = max(borderMask, centerDot * 0.35h);
                    albedo = lerp(albedo, albedo * 0.72h + half3(0.06h, 0.06h, 0.05h), gridHint * _GridHintStrength);
                }

                float3 baseNormalWS = SafeNormalize(input.normalWS);
                float3 tangentWS = length(input.tangentWS) > 0.001 ? normalize(input.tangentWS) : float3(1.0, 0.0, 0.0);
                float3 bitangentWS = length(input.bitangentWS) > 0.001 ? normalize(input.bitangentWS) : cross(baseNormalWS, tangentWS);
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

                half specPower = exp2(2.0h + smoothness * 7.0h);
                half specStrength = smoothness * 0.45h;

                // Main directional light with shadows and Blinn-Phong specular
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                half3 mainContrib = mainLight.color * mainAtten;

                half3 mainHalf = SafeNormalize(mainLight.direction + viewDirWS);
                half mainNdotH = saturate(dot(normalWS, mainHalf));
                half mainSpec = mainNdotH > 0.001h ? (pow(mainNdotH, specPower) * specStrength) : 0.0h;

                half3 directDiffuse = albedo * mainContrib * mainNdotL;
                half3 directSpecular = mainContrib * mainSpec;

                // Ambient Spherical Harmonics modulated by blended layer AO
                half3 ambientSH = SampleSH(normalWS);
                half3 ambientColor = albedo * ambientSH * ao;

                // Additional lights (torches, lanterns, spell glow) with Forward+ support
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
                    half3 addContrib = addLight.color * addAtten;
                    addLighting += albedo * addContrib * addNdotL;

                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addNdotH = saturate(dot(normalWS, addHalf));
                    half addSpec = addNdotH > 0.001h ? (pow(addNdotH, specPower) * specStrength * 1.35h) : 0.0h;
                    addLighting += addContrib * addSpec;
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    half3 addContrib = addLight.color * addAtten;
                    addLighting += albedo * addContrib * addNdotL;

                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addNdotH = saturate(dot(normalWS, addHalf));
                    half addSpec = addNdotH > 0.001h ? (pow(addNdotH, specPower) * specStrength * 1.35h) : 0.0h;
                    addLighting += addContrib * addSpec;
                LIGHT_LOOP_END
                #endif

                half3 color = ambientColor + directDiffuse + directSpecular + addLighting;
                color = clamp(color, half3(0.0h, 0.0h, 0.0h), half3(64.0h, 64.0h, 64.0h));
                color = MixFog(color, input.fogFactor);

                return half4(max(half3(0.0h, 0.0h, 0.0h), color), 1.0h);
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
