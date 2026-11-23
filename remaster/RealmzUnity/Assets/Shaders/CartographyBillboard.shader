Shader "Realmz/CartographyBillboard"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas / Sprite Albedo", 2D) = "white" {}
        _BumpMap ("Optional Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0, 2)) = 0.65
        _Cutoff ("Alpha Cutoff", Range(0.01, 0.99)) = 0.45

        [Header(Atlas and Animation)]
        _IsTwoBankAtlas ("Two-Bank 512x256 Foliage Atlas (0/1)", Float) = 1.0
        _Cols ("Atlas Columns (non-twobank)", Float) = 1.0
        _Rows ("Atlas Rows (non-twobank)", Float) = 1.0
        _VariantIndex ("Variant Index", Float) = 0.0
        _FrameCount ("Animation Frame Count", Float) = 8.0
        _AnimFps ("Animation FPS", Float) = 7.0
        _WindPhase ("Per-Instance Wind Phase", Float) = 0.0
        _TintColor ("Instance Tint Color", Color) = (1, 1, 1, 1)

        [Header(Wind Gust and Trample)]
        _WindDir ("Wind Gust Direction XZ", Vector) = (0.86, 0.51, 0, 0)
        _WindFreq ("Wind Spatial Frequency", Float) = 0.45
        _WindWaveSpeed ("Wind Wave Speed", Float) = 1.15
        _WindGustInfluence ("Wind Gust Phase Influence", Float) = 0.16
        _WindTipSway ("Tip Vertex Sway", Range(0, 0.25)) = 0.045
        _TrampleRadius ("Hero Trample Radius", Float) = 0.90
        _TrampleStrength ("Hero Trample Strength", Range(0, 0.8)) = 0.28

        [Header(Billboard Tilt and Lighting)]
        _BillboardMode ("Align To Camera Yaw (0/1)", Float) = 1.0
        _CameraPitchTiltDeg ("Root-Anchored X-Axis Tilt Deg", Range(-45, 45)) = 22.0
        _NormalUpBias ("Hemisphere Up Normal Bias", Range(0, 1)) = 0.72
        _RootTint ("Root Ground AO Tint", Color) = (0.50, 0.56, 0.46, 1)
        _Translucency ("Sun Tip Translucency", Range(0, 1)) = 0.28
        _LanternPoolColor ("Lantern Pool Color", Color) = (1.0, 0.62, 0.24, 1)
        _LanternPoolIntensity ("Lantern Pool Intensity", Range(0, 3)) = 1.25
        _EmissionColor ("Optional Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 250

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        float4 _BaseMap_TexelSize;
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        // Global controls updated by BywaterTopDownLightmap and BywaterDioramaController
        TEXTURE2D(_TopDownLightmapTex);
        SAMPLER(sampler_TopDownLightmapTex);
        float4 _TopDownLightmapBounds;
        float4 _HeroWorldPos;
        float4 _CartographyCamForwardXZ;
        float _CartographyTimeOffset;

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _BumpScale;
            half _Cutoff;
            float _IsTwoBankAtlas;
            float _Cols;
            float _Rows;
            float4 _WindDir;
            float _WindFreq;
            float _WindWaveSpeed;
            float _WindGustInfluence;
            float _WindTipSway;
            float _TrampleRadius;
            float _TrampleStrength;
            float _BillboardMode;
            float _CameraPitchTiltDeg;
            half _NormalUpBias;
            half4 _RootTint;
            half _Translucency;
            half4 _LanternPoolColor;
            half _LanternPoolIntensity;
            half4 _EmissionColor;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _VariantIndex)
            UNITY_DEFINE_INSTANCED_PROP(float, _WindPhase)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TintColor)
            UNITY_DEFINE_INSTANCED_PROP(float, _FrameCount)
            UNITY_DEFINE_INSTANCED_PROP(float, _AnimFps)
        UNITY_INSTANCING_BUFFER_END(Props)

        float2 ResolveCameraForwardXZ(bool allowViewMatrix)
        {
            float2 camFwdXZ = float2(0.0, 1.0);
            if (allowViewMatrix)
            {
                float2 vFwd = float2(-UNITY_MATRIX_V[2][0], -UNITY_MATRIX_V[2][2]);
                if (dot(vFwd, vFwd) > 0.0001)
                {
                    return normalize(vFwd);
                }
            }

            if (dot(_CartographyCamForwardXZ.xz, _CartographyCamForwardXZ.xz) > 0.0001)
            {
                camFwdXZ = normalize(_CartographyCamForwardXZ.xz);
            }
            return camFwdXZ;
        }

        float2 ComputeBillboardAtlasUV(float2 localUV, float3 rootWS)
        {
            float variant = max(0.0, floor(UNITY_ACCESS_INSTANCED_PROP(Props, _VariantIndex) + 0.5));
            float windPhase = UNITY_ACCESS_INSTANCED_PROP(Props, _WindPhase);
            float frameCount = max(1.0, floor(UNITY_ACCESS_INSTANCED_PROP(Props, _FrameCount) + 0.5));
            float fps = max(0.0, UNITY_ACCESS_INSTANCED_PROP(Props, _AnimFps));

            float timeSec = _Time.y + _CartographyTimeOffset;
            float gustWave = dot(rootWS.xz, _WindDir.xy) * _WindFreq + timeSec * _WindWaveSpeed;
            float windSample = windPhase + gustWave * _WindGustInfluence;

            float frame = 0.0;
            if (fps > 0.0001 && frameCount > 1.0)
            {
                frame = floor(frac(timeSec * (fps / max(1.0, frameCount)) + windSample) * frameCount);
                frame = clamp(frame, 0.0, frameCount - 1.0);
            }

            float uMin;
            float vMin;
            float2 cellSize;

            if (_IsTwoBankAtlas > 0.5)
            {
                float clampedVariant = min(variant, 15.0);
                float bank = floor(clampedVariant / 8.0);
                float rowFromTop = clampedVariant - bank * 8.0;
                uMin = (bank * 8.0 + frame) / 16.0;
                vMin = 1.0 - (rowFromTop + 1.0) / 8.0;
                cellSize = float2(1.0 / 16.0, 1.0 / 8.0);
            }
            else
            {
                float cols = max(1.0, _Cols);
                float rows = max(1.0, _Rows);
                float rowFromTop = min(variant, rows - 1.0);
                uMin = frame / cols;
                vMin = 1.0 - (rowFromTop + 1.0) / rows;
                cellSize = float2(1.0 / cols, 1.0 / rows);
            }

            float2 texelPad = _BaseMap_TexelSize.xy * 0.5;
            float2 uvMin = float2(uMin, vMin) + texelPad;
            float2 uvMax = float2(uMin, vMin) + cellSize - texelPad;
            return lerp(uvMin, uvMax, saturate(localUV));
        }

        void ComputeBillboardVertex(
            float3 positionOS,
            float2 uv2,
            bool allowViewMatrix,
            out float3 positionWS,
            out float3 rootWS,
            out float3 facingNormalWS)
        {
            rootWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float2 fwdXZ2 = ResolveCameraForwardXZ(allowViewMatrix);
            float3 fwdXZ = float3(fwdXZ2.x, 0.0, fwdXZ2.y);
            float3 rightXZ = float3(fwdXZ.z, 0.0, -fwdXZ.x);

            float tiltRad = radians(_CameraPitchTiltDeg);
            float tipWeight = saturate(uv2.y);

            if (_BillboardMode > 0.5)
            {
                float scaleX = length(float3(UNITY_MATRIX_M[0][0], UNITY_MATRIX_M[1][0], UNITY_MATRIX_M[2][0]));
                float scaleY = length(float3(UNITY_MATRIX_M[0][1], UNITY_MATRIX_M[1][1], UNITY_MATRIX_M[2][1]));
                float scaleZ = length(float3(UNITY_MATRIX_M[0][2], UNITY_MATRIX_M[1][2], UNITY_MATRIX_M[2][2]));

                float h = positionOS.y * scaleY;
                float yTilted = h * cos(tiltRad);
                float zTilted = h * sin(tiltRad);

                positionWS = rootWS
                    + rightXZ * (positionOS.x * scaleX)
                    + float3(0.0, yTilted, 0.0)
                    + fwdXZ * (zTilted + positionOS.z * scaleZ);
            }
            else
            {
                positionWS = TransformObjectToWorld(positionOS);
                float h = max(0.0, positionWS.y - rootWS.y);
                positionWS.y = rootWS.y + h * cos(tiltRad);
                positionWS += fwdXZ * (h * sin(tiltRad));
            }

            // Subtle vertex wind tip sway synchronized with traveling gust wave
            float timeSec = _Time.y + _CartographyTimeOffset;
            float windPhase = UNITY_ACCESS_INSTANCED_PROP(Props, _WindPhase);
            float gustWave = dot(rootWS.xz, _WindDir.xy) * _WindFreq + timeSec * _WindWaveSpeed + windPhase * 6.28318;
            float sway = sin(gustWave) * _WindTipSway * (tipWeight * tipWeight);
            positionWS += rightXZ * sway;

            // Hero trample pushes foliage tips outward when knight walks through
            if (_TrampleStrength > 0.0001 && _HeroWorldPos.w > 0.01)
            {
                float2 deltaXZ = rootWS.xz - _HeroWorldPos.xz;
                float dist = length(deltaXZ);
                float radius = max(0.05, _TrampleRadius);
                if (dist < radius)
                {
                    float trample = (1.0 - dist / radius);
                    trample = trample * trample * tipWeight;
                    float2 pushDir = dist > 0.001 ? (deltaXZ / dist) : float2(1.0, 0.0);
                    positionWS.xz += pushDir * (trample * _TrampleStrength);
                    positionWS.y -= trample * _TrampleStrength * 0.42;
                }
            }

            facingNormalWS = SafeNormalize(float3(-fwdXZ.x * cos(tiltRad), sin(tiltRad), -fwdXZ.z * cos(tiltRad)));
        }
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
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BillboardVert
            #pragma fragment BillboardFrag

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
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 rootInfo : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings BillboardVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS;
                float3 rootWS;
                float3 facingNormalWS;
                ComputeBillboardVertex(input.positionOS.xyz, input.uv1, true, positionWS, rootWS, facingNormalWS);

                float2 camFwdXZ = ResolveCameraForwardXZ(true);
                float3 rightXZ = float3(camFwdXZ.y, 0.0, -camFwdXZ.x);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = ComputeBillboardAtlasUV(input.uv0, rootWS);
                output.rootInfo = input.uv1;
                output.normalWS = SafeNormalize(lerp(facingNormalWS, float3(0.0, 1.0, 0.0), _NormalUpBias));
                output.tangentWS = rightXZ;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                #else
                    output.shadowCoord = float4(0, 0, 0, 0);
                #endif

                return output;
            }

            half4 BillboardFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 instTint = (half4)UNITY_ACCESS_INSTANCED_PROP(Props, _TintColor);
                half4 albedoAlpha = baseSample * instTint;
                clip(albedoAlpha.a - _Cutoff);

                half tipFactor = saturate((half)input.rootInfo.y);
                half3 rootAO = lerp(_RootTint.rgb, half3(1.0h, 1.0h, 1.0h), tipFactor);
                half3 albedo = albedoAlpha.rgb * rootAO;

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 baseN = SafeNormalize(input.normalWS);
                float3 tang = SafeNormalize(input.tangentWS);
                float3 bitang = SafeNormalize(cross(baseN, tang));
                half3 normalWS = SafeNormalize(normalTS.x * tang + normalTS.y * bitang + normalTS.z * baseN);

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

                half4 topDownLM = half4(1.0h, 1.0h, 1.0h, 0.0h);
                if (_TopDownLightmapBounds.z > 0.00001 && _TopDownLightmapBounds.w > 0.00001)
                {
                    float2 lmUV = saturate((input.positionWS.xz - _TopDownLightmapBounds.xy) * _TopDownLightmapBounds.zw);
                    topDownLM = SAMPLE_TEXTURE2D(_TopDownLightmapTex, sampler_TopDownLightmapTex, lmUV);
                }

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half wrapDiffuse = saturate(dot(normalWS, mainLight.direction) * 0.65h + 0.35h);
                half backLit = pow(saturate(dot(-mainLight.direction, viewDirWS)), 2.5h) * _Translucency * tipFactor;
                half3 mainContrib = mainLight.color * mainAtten * topDownLM.rgb;
                half3 directLight = albedo * mainContrib * (wrapDiffuse + backLit);

                half3 ambientSH = SampleSH(normalWS);
                half3 ambientCloudMod = lerp(half3(1.0h, 1.0h, 1.0h), topDownLM.rgb, 0.40h);
                half3 ambientColor = albedo * ambientSH * ambientCloudMod;

                half3 addLighting = half3(0.0h, 0.0h, 0.0h);
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addWrap = saturate(dot(normalWS, addLight.direction) * 0.65h + 0.35h);
                    addLighting += albedo * addLight.color * (addAtten * addWrap);
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addWrap = saturate(dot(normalWS, addLight.direction) * 0.65h + 0.35h);
                    addLighting += albedo * addLight.color * (addAtten * addWrap);
                LIGHT_LOOP_END
                #endif

                half3 lanternPool = albedo * _LanternPoolColor.rgb * (topDownLM.a * _LanternPoolIntensity);

                half3 color = ambientColor + directLight + addLighting + lanternPool + _EmissionColor.rgb;
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
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
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
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS;
                float3 rootWS;
                float3 facingNormalWS;
                ComputeBillboardVertex(input.positionOS.xyz, input.uv1, false, positionWS, rootWS, facingNormalWS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, facingNormalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                output.uv = ComputeBillboardAtlasUV(input.uv0, rootWS);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * (half)UNITY_ACCESS_INSTANCED_PROP(Props, _TintColor).a;
                clip(alpha - _Cutoff);
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
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS;
                float3 rootWS;
                float3 facingNormalWS;
                ComputeBillboardVertex(input.positionOS.xyz, input.uv1, true, positionWS, rootWS, facingNormalWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = ComputeBillboardAtlasUV(input.uv0, rootWS);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * (half)UNITY_ACCESS_INSTANCED_PROP(Props, _TintColor).a;
                clip(alpha - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
