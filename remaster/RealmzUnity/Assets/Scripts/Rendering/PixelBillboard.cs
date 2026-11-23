using UnityEngine;

namespace Realmz.Rendering
{
    /// <summary>
    /// Foot-anchored upright Y-axis billboard for Songs-of-Conquest style HD-2D sprites.
    /// Keeps the sprite grounded on the 3D floor without tilting backward into terrain.
    /// </summary>
    [ExecuteAlways]
    public sealed class PixelBillboard : MonoBehaviour
    {
        [Tooltip("Optional override camera; defaults to Camera.main when null.")]
        public Camera targetCamera;

        private void LateUpdate()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// Subtle warm torchlight flicker for dynamic normal-mapped pixel sprites.
    /// </summary>
    public sealed class TorchFlicker : MonoBehaviour
    {
        public float baseIntensity = 6.0f;
        public float flickerAmplitude = 0.6f;
        public float flickerSpeed = 7.0f;

        private Light _light;
        private float _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.Range(0f, 100f);
        }

        private void Update()
        {
            if (_light == null)
            {
                return;
            }

            float noise = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed) * 2f - 1f;
            _light.intensity = Mathf.Max(0.5f, baseIntensity + noise * flickerAmplitude);
        }
    }
}
