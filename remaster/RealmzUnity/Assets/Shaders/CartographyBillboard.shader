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
        _FlipX ("Flip Horizontal (0/1)", Float) = 0.0

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
        _LanternPoolIntensity ("Lantern Pool Intensity", Range(0, 3)) = 0.78
        _EmissionColor ("Optional Emission", Color) = (0, 0, 0, 1)
        _EmissionWarmMask ("Mask Emission To Warm Bright Pixels (0/1)", Float) = 1.0
        _EmissionMinV ("Min UV.y For Emission", Range(0, 1)) = 0.0
        _ReceiveRootShadowOnly ("Sample Shadow At Root (0/1)", Float) = 1.0
        _HorizonHazeColor ("North Horizon Haze Color", Color) = (0.91, 0.83, 0.70, 1)
        _HorizonHazeStrength ("North Horizon Haze Strength", Range(0, 0.35)) = 0.08
        _NightRimColor ("Moonlight Rim Color", Color) = (0.659, 0.733, 0.878, 1)
        _NightRimStrength ("Local Night Rim Multiplier", Range(0, 2)) = 1.0
        _HoverOutlineColor ("Hover Outline Color", Color) = (0.98, 0.80, 0.08, 1)
        _HoverOutlineStrength ("Hover Outline Strength", Range(0, 1)) = 0.0

        [Header(Character 3D Depth and Stance)]
        _UnitDepthSculpt ("Unit Cylindrical 3D Sculpt (0..1)", Range(0, 1)) = 0.0
        _UnitSunRimStrength ("Unit Alpha-Edge Sun Rim", Range(0, 1.5)) = 0.0
        _UnitSpecStrength ("Unit Armor Specular Glint", Range(0, 1.5)) = 0.0
        _UnitFootAO ("Unit Ground Contact Foot AO", Range(0, 0.6)) = 0.0
        _StanceAuraColor ("Unit Guard/Buff Aura Color", Color) = (0.38, 0.78, 1.0, 1)
        _StanceAuraStrength ("Unit Guard/Buff Aura Strength", Range(0, 1)) = 0.0

        [HideInInspector] _SrcBlend ("SrcBlend", Float) = 1.0
        [HideInInspector] _DstBlend ("DstBlend", Float) = 0.0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1.0
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
        TEXTURE2D(_FogOfWarTex);
        SAMPLER(sampler_FogOfWarTex);
        float _FogOfWarEnabled;
        float _GlobalNightRimStrength;

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
            half _EmissionWarmMask;
            half _EmissionMinV;
            float _ReceiveRootShadowOnly;
            half4 _HorizonHazeColor;
            half _HorizonHazeStrength;
            half4 _NightRimColor;
            half _NightRimStrength;
            half4 _HoverOutlineColor;
            half _HoverOutlineStrength;
            half _UnitDepthSculpt;
            half _UnitSunRimStrength;
            half _UnitSpecStrength;
            half _UnitFootAO;
            half4 _StanceAuraColor;
            half _StanceAuraStrength;
        #if !defined(UNITY_INSTANCING_ENABLED)
            float _VariantIndex;
            float _WindPhase;
            float4 _TintColor;
            float _FrameCount;
            float _AnimFps;
            float _FlipX;
        #endif
        CBUFFER_END

        #if defined(UNITY_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _VariantIndex)
                UNITY_DEFINE_INSTANCED_PROP(float, _WindPhase)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TintColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _FrameCount)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimFps)
                UNITY_DEFINE_INSTANCED_PROP(float, _FlipX)
            UNITY_INSTANCING_BUFFER_END(Props)
            #define CARTO_INST_PROP(name) UNITY_ACCESS_INSTANCED_PROP(Props, name)
        #else
            #define CARTO_INST_PROP(name) name
        #endif

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
            float variant = max(0.0, floor(CARTO_INST_PROP(_VariantIndex) + 0.5));
            float windPhase = CARTO_INST_PROP(_WindPhase);
            float frameCount = max(1.0, floor(CARTO_INST_PROP(_FrameCount) + 0.5));
            float fps = max(0.0, CARTO_INST_PROP(_AnimFps));

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
            float flipX = CARTO_INST_PROP(_FlipX);
            if (flipX > 0.5)
            {
                localUV.x = 1.0 - localUV.x;
            }
            return lerp(uvMin, uvMax, saturate(localUV));
        }

        void ComputeBillboardVertexExtended(
            float3 positionOS,
            float2 uv2,
            bool allowViewMatrix,
            bool overrideFwd,
            float2 customFwdXZ,
            float tiltOverrideDeg,
            out float3 positionWS,
            out float3 rootWS,
            out float3 facingNormalWS)
        {
            rootWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float2 fwdXZ2 = overrideFwd ? customFwdXZ : ResolveCameraForwardXZ(allowViewMatrix);
            if (dot(fwdXZ2, fwdXZ2) > 0.0001)
            {
                fwdXZ2 = normalize(fwdXZ2);
            }
            else
            {
                fwdXZ2 = float2(0.0, 1.0);
            }
            float3 fwdXZ = float3(fwdXZ2.x, 0.0, fwdXZ2.y);
            float3 rightXZ = float3(fwdXZ.z, 0.0, -fwdXZ.x);

            float tiltRad = radians(tiltOverrideDeg);
            float tipWeight = saturate(uv2.y);

            if (_BillboardMode > 0.5 || overrideFwd)
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
                if (abs(tiltOverrideDeg) > 0.001)
                {
                    float h = positionWS.y - rootWS.y;
                    positionWS.y = rootWS.y + h * cos(tiltRad);
                    positionWS += fwdXZ * (h * sin(tiltRad));
                }
            }

            // Subtle vertex wind tip sway synchronized with traveling gust wave
            float timeSec = _Time.y + _CartographyTimeOffset;
            float windPhase = CARTO_INST_PROP(_WindPhase);
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

        void ComputeBillboardVertex(
            float3 positionOS,
            float2 uv2,
            bool allowViewMatrix,
            out float3 positionWS,
            out float3 rootWS,
            out float3 facingNormalWS)
        {
            ComputeBillboardVertexExtended(
                positionOS,
                uv2,
                allowViewMatrix,
                false,
                float2(0.0, 1.0),
                _CameraPitchTiltDeg,
                positionWS,
                rootWS,
                facingNormalWS);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZTest LEqual
            Cull Off
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

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
                float3 rootWS : TEXCOORD7;
                float2 localUV : TEXCOORD8;
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
                output.rootWS = rootWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = ComputeBillboardAtlasUV(input.uv0, rootWS);
                output.localUV = float2(input.uv0.x, input.uv0.y);
                output.rootInfo = input.uv1;
                output.normalWS = SafeNormalize(lerp(facingNormalWS, float3(0.0, 1.0, 0.0), _NormalUpBias));
                output.tangentWS = rightXZ;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                float3 shadowSampleWS = lerp(positionWS, rootWS + float3(0.0, 0.08, 0.0), saturate(_ReceiveRootShadowOnly));
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = TransformWorldToShadowCoord(shadowSampleWS);
                #else
                    output.shadowCoord = float4(0, 0, 0, 0);
                #endif

                return output;
            }

            half4 BillboardFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(baseSample.a - _Cutoff);

                half4 instTint = (half4)CARTO_INST_PROP(_TintColor);
                half finalAlpha = saturate(baseSample.a * instTint.a);

                half4 albedoAlpha = half4(baseSample.rgb * instTint.rgb, baseSample.a);

                half tipFactor = saturate((half)input.rootInfo.y);
                half3 rootAO = lerp(_RootTint.rgb, half3(1.0h, 1.0h, 1.0h), tipFactor);
                half3 albedo = albedoAlpha.rgb * rootAO;

                if (_UnitFootAO > 0.001h)
                {
                    half footMask = smoothstep(0.0h, 0.24h, (half)input.localUV.y);
                    albedo *= lerp(1.0h - _UnitFootAO, 1.0h, footMask);
                }

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float flipXVal = CARTO_INST_PROP(_FlipX);
                if (flipXVal > 0.5)
                {
                    normalTS.x = -normalTS.x;
                }
                if (_UnitDepthSculpt > 0.001h)
                {
                    half xNorm = (half)(input.localUV.x * 2.0 - 1.0);
                    half yNorm = (half)input.localUV.y;
                    half cylX = clamp(xNorm * 0.72h, -0.78h, 0.78h);
                    half cylY = clamp((yNorm - 0.52h) * 0.36h, -0.24h, 0.34h);
                    half3 macroN = normalize(half3(cylX, cylY, sqrt(max(0.18h, 1.0h - cylX * cylX - cylY * cylY))));
                    normalTS = SafeNormalize(lerp(normalTS, SafeNormalize(normalTS + macroN * 0.85h), _UnitDepthSculpt));
                }
                float3 baseN = SafeNormalize(input.normalWS);
                float3 tang = SafeNormalize(input.tangentWS);
                float3 bitang = SafeNormalize(cross(tang, baseN));
                half3 normalWS = SafeNormalize(normalTS.x * tang + normalTS.y * bitang + normalTS.z * baseN);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 shadowSampleWS = lerp(input.positionWS, input.rootWS + float3(0.0, 0.08, 0.0), saturate(_ReceiveRootShadowOnly));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(shadowSampleWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                half4 topDownLM = half4(1.0h, 1.0h, 1.0h, 0.0h);
                bool hasBounds = (_TopDownLightmapBounds.z > 0.00001 && _TopDownLightmapBounds.w > 0.00001);
                float2 lmUV = hasBounds ? saturate((input.positionWS.xz - _TopDownLightmapBounds.xy) * _TopDownLightmapBounds.zw) : float2(0.0, 0.0);
                if (hasBounds)
                {
                    topDownLM = SAMPLE_TEXTURE2D(_TopDownLightmapTex, sampler_TopDownLightmapTex, lmUV);
                }

                Light mainLight = GetMainLight(inputData.shadowCoord, shadowSampleWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half wrapDiffuse = saturate(dot(normalWS, mainLight.direction) * 0.65h + 0.35h);
                half backLit = pow(saturate(dot(-mainLight.direction, viewDirWS)), 2.5h) * _Translucency * tipFactor;
                half3 mainContrib = mainLight.color * mainAtten * topDownLM.rgb;
                half3 directLight = albedo * mainContrib * (wrapDiffuse + backLit);

                if (_UnitSpecStrength > 0.001h)
                {
                    half maxC = max(albedoAlpha.r, max(albedoAlpha.g, albedoAlpha.b));
                    half minC = min(albedoAlpha.r, min(albedoAlpha.g, albedoAlpha.b));
                    half steelMask = smoothstep(0.32h, 0.72h, maxC) * (1.0h - smoothstep(0.18h, 0.50h, maxC - minC) * 0.65h);
                    half3 mainHalf = SafeNormalize((half3)mainLight.direction + (half3)viewDirWS);
                    half specTerm = pow(saturate(dot(normalWS, mainHalf)), 28.0h);
                    directLight += mainContrib * (specTerm * steelMask * _UnitSpecStrength * 0.40h);
                }

                if (_UnitSunRimStrength > 0.001h)
                {
                    float2 sunDir2D = float2(dot(mainLight.direction, tang), dot(mainLight.direction, bitang));
                    if (dot(sunDir2D, sunDir2D) > 0.0001)
                    {
                        sunDir2D = normalize(sunDir2D);
                        float2 stepUV = float2((flipXVal > 0.5 ? -sunDir2D.x : sunDir2D.x) * _BaseMap_TexelSize.x, sunDir2D.y * _BaseMap_TexelSize.y) * 1.25;
                        half sunNeighborA = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv + stepUV, 0).a;
                        half sunRimMask = (sunNeighborA < _Cutoff) ? 1.0h : 0.0h;
                        directLight += mainContrib * (sunRimMask * _UnitSunRimStrength * smoothstep(0.16h, 0.52h, (half)input.localUV.y) * 0.48h);
                    }
                }

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

                half warmBright = smoothstep(0.68h, 0.88h, albedoAlpha.r)
                                * smoothstep(0.45h, 0.72h, albedoAlpha.g)
                                * smoothstep(0.22h, 0.45h, albedoAlpha.r - albedoAlpha.b);
                half vMask = smoothstep((half)_EmissionMinV - 0.04h, (half)_EmissionMinV + 0.04h, tipFactor);
                half emissionMask = lerp(1.0h, warmBright * vMask, saturate((half)_EmissionWarmMask));
                half3 emission = _EmissionColor.rgb * albedoAlpha.rgb * emissionMask;

                half3 rawLit = ambientColor + directLight + addLighting + lanternPool;
                // Soft-shoulder highlight compression prevents torch and lantern blowouts on bright sprites.
                half maxLum = max(rawLit.r, max(rawLit.g, rawLit.b));
                half compressedLum = maxLum <= 0.92h ? maxLum : 0.92h + (maxLum - 0.92h) / (1.0h + (maxLum - 0.92h) * 1.45h);
                half3 litColor = maxLum > 0.0001h ? rawLit * (compressedLum / maxLum) : rawLit;
                half3 color = litColor + emission;
                half rim = pow(1.0h - saturate(dot(normalWS, (half3)viewDirWS)), 2.2h) * (half)(max(0.0, _GlobalNightRimStrength) * max(0.0, (float)_NightRimStrength));
                rim = min(rim, 0.55h);
                color += albedo * _NightRimColor.rgb * rim;

                if (_StanceAuraStrength > 0.001h)
                {
                    half saL = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv - float2(_BaseMap_TexelSize.x, 0.0), 0).a;
                    half saR = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv + float2(_BaseMap_TexelSize.x, 0.0), 0).a;
                    half saD = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv - float2(0.0, _BaseMap_TexelSize.y), 0).a;
                    half saU = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv + float2(0.0, _BaseMap_TexelSize.y), 0).a;
                    half stanceEdge = (min(min(saL, saR), min(saD, saU)) < _Cutoff) ? 1.0h : 0.0h;
                    half pulse = (half)(0.65 + 0.35 * sin(_Time.y * 4.2));
                    color = lerp(color, max(color, _StanceAuraColor.rgb), saturate(stanceEdge * _StanceAuraStrength * pulse * 0.55h));
                }

                if (_HoverOutlineStrength > 0.001h)
                {
                    half aL = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv - float2(_BaseMap_TexelSize.x, 0.0), 0).a;
                    half aR = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv + float2(_BaseMap_TexelSize.x, 0.0), 0).a;
                    half aD = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv - float2(0.0, _BaseMap_TexelSize.y), 0).a;
                    half aU = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv + float2(0.0, _BaseMap_TexelSize.y), 0).a;
                    half minNeighborA = min(min(aL, aR), min(aD, aU));
                    half edgeMask = (minNeighborA < _Cutoff) ? 1.0h : 0.0h;
                    half hoverFresnel = pow(1.0h - saturate(dot(normalWS, (half3)viewDirWS)), 1.8h) * 0.45h;
                    half hoverMask = saturate(max(edgeMask, hoverFresnel)) * saturate(_HoverOutlineStrength);
                    color = lerp(color, max(color, _HoverOutlineColor.rgb), hoverMask);
                }

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

                return half4(color, finalAlpha);
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
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    ComputeBillboardVertex(input.positionOS.xyz, input.uv1, false, positionWS, rootWS, facingNormalWS);
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    ComputeBillboardVertexExtended(
                        input.positionOS.xyz,
                        input.uv1,
                        false,
                        true,
                        -_LightDirection.xz,
                        0.0,
                        positionWS,
                        rootWS,
                        facingNormalWS);
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
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
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
                half baseAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                clip(baseAlpha - _Cutoff);

                half tintAlpha = (half)CARTO_INST_PROP(_TintColor).a;
                clip(tintAlpha - 0.995h);

                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
