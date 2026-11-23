using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.Cartography
{
    /// <summary>
    /// Maintains the 256x256 top-down lightmap RenderTexture (_TopDownLightmapTex + _TopDownLightmapBounds)
    /// containing drifting two-layer cloud shadows, stationary tree canopy blotches, and warm
    /// orange lantern pools at the inn doorway, stone bridge, and brazier.
    /// Works identically in both EditMode captures and PlayMode runtime.
    /// </summary>
    [ExecuteAlways]
    public sealed class BywaterTopDownLightmap : MonoBehaviour
    {
        public const int LightmapResolution = 256;
        public const string GeneratorShaderName = "Hidden/Realmz/CartographyTopDownLightmapGen";

        private static readonly int TopDownLightmapTexId = Shader.PropertyToID("_TopDownLightmapTex");
        private static readonly int TopDownLightmapBoundsId = Shader.PropertyToID("_TopDownLightmapBounds");

        private static readonly int SimTimeId = Shader.PropertyToID("_SimTime");
        private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");
        private static readonly int CanopyCountId = Shader.PropertyToID("_CanopyCount");
        private static readonly int CanopyCirclesId = Shader.PropertyToID("_CanopyCircles");
        private static readonly int LanternCountId = Shader.PropertyToID("_LanternCount");
        private static readonly int LanternPoolsId = Shader.PropertyToID("_LanternPools");

        [Header("Appearance")]
        public Color cloudShadowColor = new Color(0.56f, 0.62f, 0.74f, 1f);
        public Color canopyShadowColor = new Color(0.48f, 0.54f, 0.66f, 1f);
        [Range(0f, 1f)] public float cloudStrength = 0.82f;

        private RenderTexture _lightmapRT;
        private Texture2D _fallbackTexture;
        private Material _generatorMaterial;

        private readonly Vector4[] _canopyBuffer = new Vector4[24];
        private int _canopyCount;
        private readonly Vector4[] _lanternBuffer = new Vector4[12];
        private int _lanternCount;

        public RenderTexture LightmapRT => _lightmapRT;

        private void OnEnable()
        {
            ConfigureFromWorldData(CartographyWorldData.BuildBywaterProofLayout());
            UpdateLightmap(Application.isPlaying ? Time.time : 0f);
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        public void ConfigureFromWorldData(CartographyWorldData data)
        {
            if (data == null)
            {
                data = CartographyWorldData.BuildBywaterProofLayout();
            }

            _canopyCount = 0;
            for (int i = 0; i < data.Props.Count && _canopyCount < _canopyBuffer.Length; i++)
            {
                CartographyPropPlacement prop = data.Props[i];
                if (!prop.CastsCanopyShadow)
                {
                    continue;
                }

                _canopyBuffer[_canopyCount++] = new Vector4(
                    prop.WorldPosition.x,
                    prop.WorldPosition.z,
                    Mathf.Max(0.5f, prop.CanopyRadius),
                    Mathf.Clamp01(prop.CanopyStrength));
            }

            _lanternCount = 0;
            for (int i = 0; i < data.Lanterns.Count && _lanternCount < _lanternBuffer.Length; i++)
            {
                CartographyLanternSource lantern = data.Lanterns[i];
                _lanternBuffer[_lanternCount++] = new Vector4(
                    lantern.WorldXZ.x,
                    lantern.WorldXZ.y,
                    Mathf.Max(0.5f, lantern.Radius),
                    Mathf.Clamp01(lantern.Intensity));
            }
        }

        public void UpdateLightmap(float simTimeSeconds)
        {
            if (_canopyCount == 0 && _lanternCount == 0)
            {
                ConfigureFromWorldData(CartographyWorldData.BuildBywaterProofLayout());
            }

            Vector4 bounds = new Vector4(
                0f,
                0f,
                1f / CartographyWorldData.GridWidth,
                1f / CartographyWorldData.GridHeight);
            Shader.SetGlobalVector(TopDownLightmapBoundsId, bounds);

            bool hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (hasGpu && EnsureGeneratorMaterial())
            {
                EnsureRenderTexture();
                _generatorMaterial.SetFloat(SimTimeId, simTimeSeconds);
                _generatorMaterial.SetVector(WorldSizeId, new Vector4(
                    CartographyWorldData.GridWidth,
                    CartographyWorldData.GridHeight,
                    0f,
                    0f));
                _generatorMaterial.SetColor("_CloudShadowColor", cloudShadowColor);
                _generatorMaterial.SetColor("_CanopyShadowColor", canopyShadowColor);
                _generatorMaterial.SetFloat("_CloudStrength", cloudStrength);
                _generatorMaterial.SetInt(CanopyCountId, _canopyCount);
                _generatorMaterial.SetVectorArray(CanopyCirclesId, _canopyBuffer);
                _generatorMaterial.SetInt(LanternCountId, _lanternCount);
                _generatorMaterial.SetVectorArray(LanternPoolsId, _lanternBuffer);

                RenderTexture prevActive = RenderTexture.active;
                Graphics.Blit(null, _lightmapRT, _generatorMaterial, 0);
                RenderTexture.active = prevActive;
                Shader.SetGlobalTexture(TopDownLightmapTexId, _lightmapRT);
            }
            else
            {
                EnsureCpuFallbackTexture();
                Shader.SetGlobalTexture(TopDownLightmapTexId, _fallbackTexture);
            }
        }

        private void EnsureRenderTexture()
        {
            if (_lightmapRT != null && _lightmapRT.IsCreated())
            {
                return;
            }

            RenderTextureFormat fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            _lightmapRT = new RenderTexture(LightmapResolution, LightmapResolution, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "Bywater_TopDownLightmap_RT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            _lightmapRT.Create();
        }

        private bool EnsureGeneratorMaterial()
        {
            if (_generatorMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find(GeneratorShaderName);
            if (shader == null || !shader.isSupported)
            {
                return false;
            }

            _generatorMaterial = new Material(shader)
            {
                name = "Runtime_TopDownLightmapGen_Mat",
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private void EnsureCpuFallbackTexture()
        {
            if (_fallbackTexture != null)
            {
                return;
            }

            _fallbackTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true)
            {
                name = "Bywater_TopDownLightmap_Fallback",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    pixels[y * 64 + x] = new Color(1f, 1f, 1f, 0f);
                }
            }
            _fallbackTexture.SetPixels(pixels);
            _fallbackTexture.Apply(false, false);
        }

        private void ReleaseResources()
        {
            if (_lightmapRT != null)
            {
                _lightmapRT.Release();
                DestroySafe(_lightmapRT);
                _lightmapRT = null;
            }

            if (_generatorMaterial != null)
            {
                DestroySafe(_generatorMaterial);
                _generatorMaterial = null;
            }

            if (_fallbackTexture != null)
            {
                DestroySafe(_fallbackTexture);
                _fallbackTexture = null;
            }
        }

        private static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
