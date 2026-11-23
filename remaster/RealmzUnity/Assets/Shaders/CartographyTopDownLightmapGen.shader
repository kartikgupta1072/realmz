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

            // Packed canopy circles: xy = worldXZ, z = radius, w = darkness
            int _CanopyCount;
            float4 _CanopyCircles[24];

            // Packed lantern pools: xy = worldXZ, z = radius, w = intensity
            int _LanternCount;
            float4 _LanternPools[12];

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

                // Stationary dark canopy shadow blotches under trees and north ridge
                float canopyMask = 0.0;
                int cCount = min(_CanopyCount, 24);
                [loop] for (int i = 0; i < cCount; i++)
                {
                    float4 c = _CanopyCircles[i];
                    float dist = length(worldXZ - c.xy);
                    float r = max(0.1, c.z);
                    float blotch = (1.0 - smoothstep(r * 0.25, r, dist)) * c.w;
                    canopyMask = max(canopyMask, blotch);
                }

                // Northern ridge subtle ambient overhang shadow along z ~ 12.6..13.5
                float ridgeOverhang = smoothstep(13.6, 12.7, worldXZ.y) * smoothstep(11.9, 12.8, worldXZ.y) * 0.35;
                canopyMask = saturate(max(canopyMask, ridgeOverhang));

                lightMul = lerp(lightMul, lightMul * _CanopyShadowColor.rgb, (half)canopyMask);

                // Warm lantern / brazier pools stored in alpha
                float lanternGlow = 0.0;
                int lCount = min(_LanternCount, 12);
                [loop] for (int j = 0; j < lCount; j++)
                {
                    float4 l = _LanternPools[j];
                    float dist = length(worldXZ - l.xy);
                    float r = max(0.1, l.z);
                    float falloff = saturate(1.0 - dist / r);
                    float flicker = 0.94 + 0.06 * sin(_SimTime * 6.5 + j * 2.1);
                    lanternGlow += falloff * falloff * l.w * flicker;
                }

                return half4(saturate(lightMul), saturate((half)lanternGlow));
            }
            ENDHLSL
        }
    }
}
