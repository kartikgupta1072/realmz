using UnityEngine;

namespace Realmz.World
{
    /// <summary>
    /// Smooth Perlin-noise torchlight flicker with optional flame ember scale pulse.
    /// </summary>
    public sealed class BywaterTorchFlicker : MonoBehaviour
    {
        public float baseIntensity = 6.2f;
        public float flickerAmplitude = 0.75f;
        public float flickerSpeed = 6.5f;
        public Transform emberTransform;

        private Light _light;
        private float _seed;
        private Transform _lastEmberTransform;
        private Vector3 _emberBaseScale = Vector3.one;

        private void Awake()
        {
            _light = GetComponent<Light>();
            if (_light == null)
            {
                _light = GetComponentInChildren<Light>();
            }

            _seed = Random.Range(0f, 100f);
            CacheEmberScale();
        }

        private void Update()
        {
            float noise = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed) * 2f - 1f;

            if (_light != null)
            {
                _light.intensity = Mathf.Max(0.5f, baseIntensity + noise * flickerAmplitude);
            }

            if (emberTransform != null)
            {
                if (_lastEmberTransform != emberTransform)
                {
                    CacheEmberScale();
                }

                float heightPulse = 1f + noise * 0.16f;
                float widthPulse = 1f + noise * 0.08f;
                emberTransform.localScale = new Vector3(
                    _emberBaseScale.x * widthPulse,
                    _emberBaseScale.y * heightPulse,
                    _emberBaseScale.z * widthPulse);
            }
        }

        private void CacheEmberScale()
        {
            _lastEmberTransform = emberTransform;
            if (emberTransform != null)
            {
                _emberBaseScale = emberTransform.localScale;
            }
        }
    }
}
