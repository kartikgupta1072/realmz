Shader "Realmz/Stylized3D"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _RampSteps ("Stylized Ramp Steps", Range(2, 8)) = 4.0
        _ShadowWarmth ("Shadow Tint", Color) = (0.28, 0.32, 0.45, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.92, 0.72, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 3.5
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.22
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _BumpMap_ST;
            half _BumpScale;
            half _Smoothness;
            half _RampSteps;
            half4 _ShadowWarmth;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half4 _EmissionColor;
            half _Cutoff;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex StylizedVert
            #pragma fragment StylizedFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
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

            half QuantizeStylizedRamp(half value, half steps)
            {
                half clampedSteps = max(2.0h, round(steps));
                half scaled = saturate(value) * (clampedSteps - 0.001h);
                half band = floor(scaled) / (clampedSteps - 1.0h);
                return saturate(band);
            }

            Varyings StylizedVert(Attributes input)
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

            half4 StylizedFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedoAlpha = baseSample * _BaseColor;

                #if defined(_ALPHATEST_ON)
                    clip(albedoAlpha.a - _Cutoff);
                #endif

                half3 albedo = albedoAlpha.rgb;

                float2 bumpUV = input.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, bumpUV), _BumpScale);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 baseNormalWS = SafeNormalize(input.normalWS);
                if (dot(baseNormalWS, viewDirWS) < 0.0)
                {
                    baseNormalWS = -baseNormalWS;
                }
                float3 tangentWS = length(input.tangentWS) > 0.001 ? normalize(input.tangentWS) : float3(1.0, 0.0, 0.0);
                float3 bitangentWS = length(input.bitangentWS) > 0.001 ? normalize(input.bitangentWS) : cross(baseNormalWS, tangentWS);
                half3 normalWS = SafeNormalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * baseNormalWS);

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

                // Main directional light with stepped half-lambert ramp and cool/warm shadow tint
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half halfLambert = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half steppedMain = QuantizeStylizedRamp(halfLambert * mainAtten, _RampSteps);

                half3 mainLightColor = lerp(_ShadowWarmth.rgb, mainLight.color, steppedMain);
                half3 directDiffuse = albedo * mainLightColor;

                half specPower = exp2(3.0h + _Smoothness * 6.0h);
                half3 mainHalf = SafeNormalize(mainLight.direction + viewDirWS);
                half mainNdotH = saturate(dot(normalWS, mainHalf));
                half mainSpec = pow(mainNdotH, specPower) * _Smoothness * steppedMain;
                half3 directSpecular = mainLight.color * mainSpec * 0.65h;

                // Ambient spherical harmonics
                half3 ambientSH = SampleSH(normalWS);
                half3 ambientColor = albedo * ambientSH * 0.65h;

                // Additional lights (torches, lanterns, spell glow)
                half3 addLighting = half3(0, 0, 0);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addHalfLamb = saturate(dot(normalWS, addLight.direction) * 0.5h + 0.5h);
                    half addStepped = QuantizeStylizedRamp(addHalfLamb, _RampSteps);
                    half3 addContrib = addLight.color * addAtten;
                    addLighting += albedo * addContrib * addStepped;

                    half3 addHalfVec = SafeNormalize(addLight.direction + viewDirWS);
                    half addSpec = pow(saturate(dot(normalWS, addHalfVec)), specPower) * _Smoothness * 0.45h;
                    addLighting += addContrib * addSpec;
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addHalfLamb = saturate(dot(normalWS, addLight.direction) * 0.5h + 0.5h);
                    half addStepped = QuantizeStylizedRamp(addHalfLamb, _RampSteps);
                    half3 addContrib = addLight.color * addAtten;
                    addLighting += albedo * addContrib * addStepped;

                    half3 addHalfVec = SafeNormalize(addLight.direction + viewDirWS);
                    half addSpec = pow(saturate(dot(normalWS, addHalfVec)), specPower) * _Smoothness * 0.45h;
                    addLighting += addContrib * addSpec;
                LIGHT_LOOP_END
                #endif

                // Subtle rim light weighted toward the main key light direction
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half3 rimLight = _RimColor.rgb * fresnel * _RimStrength * (0.35h + 0.65h * halfLambert);

                half3 color = ambientColor + directDiffuse + directSpecular + addLighting + rimLight + _EmissionColor.rgb;
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
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHATEST_ON)
                    float2 uv : TEXCOORD0;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #if defined(_ALPHATEST_ON)
                    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #endif

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
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
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
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHATEST_ON)
                    float2 uv : TEXCOORD0;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #if defined(_ALPHATEST_ON)
                    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
