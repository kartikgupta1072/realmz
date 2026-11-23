Shader "Realmz/BywaterShimmerWater"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.35, 0.72, 0.90, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 0.85
        _ScrollSpeedA ("Scroll Speed A", Vector) = (0.045, 0.025, 0, 0)
        _ScrollSpeedB ("Scroll Speed B", Vector) = (-0.032, 0.040, 0, 0)
        _ShimmerColor ("Shimmer Color", Color) = (0.90, 0.98, 1.0, 1)
        _ShimmerStrength ("Shimmer Strength", Range(0, 2)) = 0.65
        _WaveScale ("Wave Scale", Float) = 3.2
        _Smoothness ("Smoothness", Range(0, 1)) = 0.72
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
            float4 _ScrollSpeedA;
            float4 _ScrollSpeedB;
            half4 _ShimmerColor;
            half _ShimmerStrength;
            float _WaveScale;
            half _Smoothness;
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
            #pragma target 2.0
            #pragma vertex WaterVert
            #pragma fragment WaterFrag

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

            Varyings WaterVert(Attributes input)
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

            half4 WaterFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float t = _Time.y;
                float2 uvBaseA = input.uv + t * _ScrollSpeedA.xy;
                float2 uvBaseB = input.uv * 1.15 + float2(0.37, 0.19) + t * _ScrollSpeedB.xy;

                float2 bumpBaseUV = input.uv * _BumpMap_ST.xy + _BumpMap_ST.zw;
                float2 uvBumpA = bumpBaseUV + t * _ScrollSpeedA.xy;
                float2 uvBumpB = bumpBaseUV * 1.15 + float2(0.37, 0.19) + t * _ScrollSpeedB.xy;

                half4 texA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvBaseA);
                half4 texB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvBaseB);
                half3 albedo = lerp(texA.rgb, texB.rgb, 0.45h) * _BaseColor.rgb;

                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvBumpA), _BumpScale);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvBumpB), _BumpScale * 0.85h);
                half3 normalTS = normalize(half3(nA.xy + nB.xy, max(0.01h, nA.z * nB.z)));

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

                // Procedural seamless wave interference across world XZ for caustics and crest sparkles
                float2 wavePos = input.positionWS.xz * _WaveScale;
                float waveA = sin(wavePos.x * 1.45 + wavePos.y * 0.85 + t * 2.15);
                float waveB = sin(wavePos.x * -1.10 + wavePos.y * 1.65 - t * 1.80);
                float waveC = sin((wavePos.x + wavePos.y) * 2.20 + t * 1.35);
                half crest = saturate((waveA * 0.45 + waveB * 0.45 + waveC * 0.25) * 0.5 + 0.5);
                half causticMask = pow(crest, 3.5h);
                half normalTilt = saturate(length(normalTS.xy) * 1.6h);
                half sparkleCrest = pow(saturate(crest * 0.70h + normalTilt * 0.45h), 6.0h);

                // Main directional light with shadows and Blinn-Phong specular highlight
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1, 1, 1, 1));
                half mainAtten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                half3 mainLightContrib = mainLight.color * mainAtten;

                half specPower = exp2(4.0h + _Smoothness * 7.0h);
                half3 mainHalf = SafeNormalize(mainLight.direction + viewDirWS);
                half mainNdotH = saturate(dot(normalWS, mainHalf));
                half mainSpec = pow(mainNdotH, specPower) * (_Smoothness * 0.85h + 0.15h);

                half3 directDiffuse = albedo * mainLightContrib * (mainNdotL * 0.88h + 0.12h);
                half3 directSpecular = _ShimmerColor.rgb * mainLightContrib * mainSpec * (0.45h + 0.55h * crest);

                // Ambient Spherical Harmonics
                half3 ambientSH = SampleSH(normalWS);
                half3 ambientColor = albedo * ambientSH;

                // Additional lights (torches, party lantern, spell glow)
                half3 addDiffuse = half3(0, 0, 0);
                half3 addSpecular = half3(0, 0, 0);
                half3 addEnergy = half3(0, 0, 0);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addNdotH = saturate(dot(normalWS, addHalf));
                    half3 addContrib = addLight.color * addAtten;
                    addDiffuse += albedo * addContrib * addNdotL;
                    addSpecular += _ShimmerColor.rgb * addContrib * pow(addNdotH, specPower) * (_Smoothness * 0.60h);
                    addEnergy += addContrib;
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addNdotL = saturate(dot(normalWS, addLight.direction));
                    half3 addHalf = SafeNormalize(addLight.direction + viewDirWS);
                    half addNdotH = saturate(dot(normalWS, addHalf));
                    half3 addContrib = addLight.color * addAtten;
                    addDiffuse += albedo * addContrib * addNdotL;
                    addSpecular += _ShimmerColor.rgb * addContrib * pow(addNdotH, specPower) * (_Smoothness * 0.60h);
                    addEnergy += addContrib;
                LIGHT_LOOP_END
                #endif

                // Shimmer crests scale naturally with scene lighting so daylight sparkles and dungeon water cools
                half3 sceneLightEnergy = mainLightContrib + ambientSH * 0.75h + addEnergy * 0.45h;
                half3 shimmerGlow = _ShimmerColor.rgb * (causticMask * 0.35h + sparkleCrest * 0.85h) * _ShimmerStrength * sceneLightEnergy;

                half3 color = ambientColor + directDiffuse + directSpecular + addDiffuse + addSpecular + shimmerGlow;
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
    }

    Fallback "Universal Render Pipeline/Lit"
}
