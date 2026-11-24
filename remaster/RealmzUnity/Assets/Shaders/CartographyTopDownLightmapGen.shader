Shader "Hidden/Realmz/CartographyTopDownLightmapGen"
{
    Properties
    {
        _SimTime ("Simulation Time", Float) = 0.0
        _WorldSize ("World Size XZ", Vector) = (18, 18, 0, 0)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.56, 0.62, 0.74, 1)
        _CanopyShadowColor ("Canopy Shadow Color", Color) = (0.48, 0.54, 0.66, 1)
        _CloudStrength ("Cloud Shadow Strength", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "GenerateTopDownLightmap"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _SimTime;
            float4 _WorldSize;
            half4 _CloudShadowColor;
            half4 _CanopyShadowColor;
            half _CloudStrength;

            TEXTURE2D(_WorldStaticMap);
            SAMPLER(sampler_WorldStaticMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i + float2(0.0, 0.0));
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float SoftCloudFbm(float2 p)
            {
                float n = ValueNoise(p) * 0.55;
                n += ValueNoise(p * 2.07 + float2(5.2, 1.3)) * 0.30;
                n += ValueNoise(p * 4.19 + float2(9.7, 4.8)) * 0.15;
                return n;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 worldXZ = input.uv * _WorldSize.xy;

                // Two drifting cloud cookie layers across the 18x18 diorama
                float2 cloudUvA = worldXZ * 0.115 + float2(0.18, -0.11) * _SimTime;
                float2 cloudUvB = worldXZ * 0.175 + float2(0.11, 0.07) * _SimTime + float2(4.3, 8.1);
                float cA = SoftCloudFbm(cloudUvA);
                float cB = SoftCloudFbm(cloudUvB);
                float cloudCoverage = smoothstep(0.50, 0.78, cA * 0.62 + cB * 0.38);

                half3 lightMul = lerp(half3(1.0h, 1.0h, 1.0h), _CloudShadowColor.rgb, (half)(cloudCoverage * _CloudStrength));

                // Baked static world data: R = 1 - canopy/cliff AO, G = shoreline proximity, B = static lantern glow
                half4 staticData = SAMPLE_TEXTURE2D(_WorldStaticMap, sampler_WorldStaticMap, input.uv);
                half canopyMask = saturate(1.0h - staticData.r);
                lightMul = lerp(lightMul, lightMul * _CanopyShadowColor.rgb, canopyMask);

                // Warm animated lantern / brazier pools stored in alpha
                float flicker = 0.94 + 0.06 * sin(_SimTime * 6.5 + worldXZ.x * 0.7 + worldXZ.y * 0.5);
                half lanternGlow = saturate(staticData.b * (half)flicker);

                return half4(saturate(lightMul), lanternGlow);
            }
            ENDHLSL
        }
    }
}
