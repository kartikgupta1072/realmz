Shader "Realmz/GrassTuft"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.36, 0.56, 0.24, 1)
        _TipColor ("Tip Color", Color) = (0.64, 0.78, 0.36, 1)
        _WindSpeed ("Wind Speed", Float) = 2.2
        _WindStrength ("Wind Strength", Range(0, 0.4)) = 0.085
        _WindFrequency ("Wind World Frequency", Float) = 1.15
        _Translucency ("Sun Translucency", Range(0, 1)) = 0.35
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

        CBUFFER_START(UnityPerMaterial)
            half4 _TipColor;
            float _WindSpeed;
            float _WindStrength;
            float _WindFrequency;
            half _Translucency;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
        UNITY_INSTANCING_BUFFER_END(Props)

        float3 ApplyGrassWindWorld(float3 positionWS, float uvY)
        {
            float heightFactor = saturate(uvY);
            float bendWeight = heightFactor * heightFactor;
            float phase = dot(positionWS.xz, float2(_WindFrequency, _WindFrequency * 0.73)) + _Time.y * _WindSpeed;
            float gust = sin(phase) * 0.72 + cos(phase * 1.63 + 1.1) * 0.28;
            float sway = gust * _WindStrength * bendWeight;
            positionWS.x += sway;
            positionWS.z += sway * 0.65;
            positionWS.y -= abs(sway) * 0.14 * heightFactor;
            return positionWS;
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
            #pragma target 2.0
            #pragma vertex GrassVert
            #pragma fragment GrassFrag

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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings GrassVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyGrassWindWorld(positionWS, input.uv.y);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                #else
                    output.shadowCoord = float4(0, 0, 0, 0);
                #endif

                return output;
            }

            half4 GrassFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseProp = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                half tipFactor = saturate((half)input.uv.y);
                half3 rootColor = baseProp.rgb * 0.68h;
                half3 tipColor = _TipColor.rgb * baseProp.rgb;
                half3 albedo = lerp(rootColor, tipColor, tipFactor);

                // Smooth upward normal blended slightly with vertex normal so blades never show dark faceted faces
                float3 vNormalWS = SafeNormalize(input.normalWS);
                half3 normalWS = SafeNormalize(lerp(float3(0.0, 1.0, 0.0), vNormalWS, 0.25));
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

                // Main directional light with wrapped diffuse, shadows, and tip translucency
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, half4(1, 1, 1, 1));
                half mainAtten = saturate(mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half wrapDiffuse = saturate(dot(normalWS, mainLight.direction) * 0.65h + 0.35h);
                half backLit = pow(saturate(dot(-mainLight.direction, viewDirWS)), 2.5h) * _Translucency * tipFactor;
                half3 mainContrib = mainLight.color * mainAtten;
                half3 directLight = albedo * mainContrib * (wrapDiffuse + backLit);

                // Ambient Spherical Harmonics (roots subtly grounded)
                half aoApprox = lerp(0.72h, 1.0h, tipFactor);
                half3 ambientColor = albedo * SampleSH(normalWS) * aoApprox;

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
                    half addWrap = saturate(dot(normalWS, addLight.direction) * 0.65h + 0.35h);
                    addLighting += albedo * addLight.color * addAtten * addWrap;
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    half addWrap = saturate(dot(normalWS, addLight.direction) * 0.65h + 0.35h);
                    addLighting += albedo * addLight.color * addAtten * addWrap;
                LIGHT_LOOP_END
                #endif

                half3 color = ambientColor + directLight + addLighting;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyGrassWindWorld(positionWS, input.uv.y);
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
