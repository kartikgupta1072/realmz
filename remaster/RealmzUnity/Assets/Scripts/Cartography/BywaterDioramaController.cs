using UnityEngine;

namespace Realmz.Cartography
{
    /// <summary>
    /// Runtime coordinator for the Bywater Cartography proof diorama.
    /// Drives top-down cloud/lantern lightmap updates, global camera forward alignment,
    /// and knight hero trample world coordinates for CartographyBillboard foliage.
    /// </summary>
    [ExecuteAlways]
    public sealed class BywaterDioramaController : MonoBehaviour
    {
        private static readonly int HeroWorldPosId = Shader.PropertyToID("_HeroWorldPos");
        private static readonly int CamForwardXZId = Shader.PropertyToID("_CartographyCamForwardXZ");
        private static readonly int TimeOffsetId = Shader.PropertyToID("_CartographyTimeOffset");

        [Header("Scene References")]
        public BywaterTopDownLightmap topDownLightmap;
        public BywaterFoliageScatter foliageScatter;
        public Transform heroTransform;
        public Camera targetCamera;

        private const float LightmapUpdateIntervalSeconds = 0.05f;

        [Header("PlayMode Demo Patrol")]
        public bool enableDemoPatrol = true;
        public Vector3 patrolCenter = new Vector3(8.5f, 0f, -10.5f);
        public float patrolRadiusX = 1.6f;
        public float patrolRadiusZ = 1.1f;
        public float patrolSpeed = 0.55f;

        private float _nextLightmapUpdateTime;
        private Vector4 _lastCamFwd;
        private Vector4 _lastHeroPos;
        private float _lastTimeOffset;
        private bool _hasSyncedGlobals;

        private void OnEnable()
        {
            _hasSyncedGlobals = false;
            _nextLightmapUpdateTime = 0f;
            ResolveReferencesIfNeeded();
            SyncGlobals(Application.isPlaying ? Time.time : 0f);
        }

        private void OnDisable()
        {
            _hasSyncedGlobals = false;
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                SyncGlobals(0f);
                return;
            }

            float simTime = Time.time;

            if (enableDemoPatrol && heroTransform != null)
            {
                float angle = simTime * patrolSpeed;
                Vector3 pos = patrolCenter + new Vector3(
                    Mathf.Sin(angle) * patrolRadiusX,
                    0f,
                    Mathf.Sin(angle * 2f) * 0.5f * patrolRadiusZ);
                heroTransform.position = pos;
            }

            float unscaledNow = Time.unscaledTime;
            bool shouldUpdateLightmap = unscaledNow >= _nextLightmapUpdateTime;
            if (shouldUpdateLightmap)
            {
                _nextLightmapUpdateTime = unscaledNow + LightmapUpdateIntervalSeconds;
            }

            SyncGlobalsInternal(simTime, 0f, shouldUpdateLightmap);
        }

        public void SyncGlobals(float simTimeSeconds, float timeOffset = 0f)
        {
            if (Application.isPlaying)
            {
                _nextLightmapUpdateTime = Time.unscaledTime + LightmapUpdateIntervalSeconds;
            }

            SyncGlobalsInternal(simTimeSeconds, timeOffset, updateLightmap: true);
        }

        private void SyncGlobalsInternal(float simTimeSeconds, float timeOffset, bool updateLightmap)
        {
            ResolveReferencesIfNeeded();

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            Vector4 camFwdVec = new Vector4(0f, 0f, 1f, 1f);
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    camFwdVec = new Vector4(fwd.x, 0f, fwd.z, 1f);
                }
            }

            if (!_hasSyncedGlobals || updateLightmap || (_lastCamFwd - camFwdVec).sqrMagnitude > 1e-7f)
            {
                Shader.SetGlobalVector(CamForwardXZId, camFwdVec);
                _lastCamFwd = camFwdVec;
            }

            Vector4 heroPosVec = heroTransform != null
                ? new Vector4(heroTransform.position.x, heroTransform.position.y, heroTransform.position.z, 1f)
                : new Vector4(9999f, 0f, 9999f, 0f);

            if (!_hasSyncedGlobals || updateLightmap || (_lastHeroPos - heroPosVec).sqrMagnitude > 1e-7f)
            {
                Shader.SetGlobalVector(HeroWorldPosId, heroPosVec);
                _lastHeroPos = heroPosVec;
            }

            if (!_hasSyncedGlobals || updateLightmap || !Mathf.Approximately(_lastTimeOffset, timeOffset))
            {
                Shader.SetGlobalFloat(TimeOffsetId, timeOffset);
                _lastTimeOffset = timeOffset;
            }

            _hasSyncedGlobals = true;

            if (updateLightmap && topDownLightmap != null)
            {
                topDownLightmap.UpdateLightmap(simTimeSeconds);
            }
        }

        private void ResolveReferencesIfNeeded()
        {
            if (topDownLightmap == null || foliageScatter == null || targetCamera == null)
            {
                ResolveReferences();
            }
        }

        private void ResolveReferences()
        {
            if (topDownLightmap == null)
            {
                topDownLightmap = FindFirstObjectByType<BywaterTopDownLightmap>();
            }

            if (foliageScatter == null)
            {
                foliageScatter = FindFirstObjectByType<BywaterFoliageScatter>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }
    }
}
