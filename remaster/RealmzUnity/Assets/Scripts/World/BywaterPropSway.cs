using UnityEngine;

namespace Realmz.World
{
    /// <summary>
    /// Deterministic wind-sway and gentle vertical bob animation for pooled outdoor foliage props.
    /// </summary>
    public sealed class BywaterPropSway : MonoBehaviour
    {
        public Transform targetTransform;
        public float swayAmplitudeDeg = 2.6f;
        public float swaySpeed = 1.45f;
        public float bobAmplitude = 0.015f;

        private float _phase;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation = Quaternion.identity;
        private bool _hasCachedBase;

        private void Awake()
        {
            CacheBaseIfNeeded();
        }

        private void OnDisable()
        {
            if (_hasCachedBase && targetTransform != null)
            {
                targetTransform.localPosition = _baseLocalPosition;
                targetTransform.localRotation = _baseLocalRotation;
            }
        }

        public void ConfigureSeed(int x, int y)
        {
            CacheBaseIfNeeded();
            int hash = unchecked(((x * 73856093) ^ (y * 19349663) ^ ((x + y) * 83492791)) & 0x7FFFFFFF);
            _phase = (hash & 0xFFFF) * (Mathf.PI * 2f / 65536f);
        }

        public void CacheBasePose()
        {
            if (targetTransform == null)
            {
                targetTransform = transform;
            }

            _baseLocalPosition = targetTransform.localPosition;
            _baseLocalRotation = targetTransform.localRotation;
            _hasCachedBase = true;
        }

        private void Update()
        {
            if (targetTransform == null)
            {
                return;
            }

            if (!_hasCachedBase)
            {
                CacheBasePose();
            }

            float t = Time.time * swaySpeed + _phase;
            float swayZ = Mathf.Sin(t) * swayAmplitudeDeg;
            float swayX = Mathf.Cos(t * 0.73f + 0.6f) * (swayAmplitudeDeg * 0.55f);
            float bobY = Mathf.Sin(t * 1.35f + 0.4f) * bobAmplitude;

            targetTransform.localRotation = _baseLocalRotation * Quaternion.Euler(swayX, 0f, swayZ);
            targetTransform.localPosition = _baseLocalPosition + new Vector3(0f, bobY, 0f);
        }

        private void CacheBaseIfNeeded()
        {
            if (targetTransform == null)
            {
                targetTransform = transform;
            }

            if (!_hasCachedBase)
            {
                _baseLocalPosition = targetTransform.localPosition;
                _baseLocalRotation = targetTransform.localRotation;
                _hasCachedBase = true;
            }
        }
    }
}
