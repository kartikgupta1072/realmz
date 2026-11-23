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

        [Header("PlayMode Demo Patrol")]
        public bool enableDemoPatrol = true;
        public Vector3 patrolCenter = new Vector3(8.5f, 0f, 7.5f);
        public float patrolRadiusX = 1.6f;
        public float patrolRadiusZ = 1.1f;
        public float patrolSpeed = 0.55f;

        private void OnEnable()
        {
            ResolveReferences();
            SyncGlobals(Application.isPlaying ? Time.time : 0f);
        }

        private void Update()
        {
            float simTime = Application.isPlaying ? Time.time : 0f;

            if (Application.isPlaying && enableDemoPatrol && heroTransform != null)
            {
                float angle = simTime * patrolSpeed;
                Vector3 pos = patrolCenter + new Vector3(
                    Mathf.Sin(angle) * patrolRadiusX,
                    0f,
                    Mathf.Sin(angle * 2f) * 0.5f * patrolRadiusZ);
                heroTransform.position = pos;
            }

            SyncGlobals(simTime);
        }

        public void SyncGlobals(float simTimeSeconds, float timeOffset = 0f)
        {
            ResolveReferences();

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
            {
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();
                    Shader.SetGlobalVector(CamForwardXZId, new Vector4(fwd.x, 0f, fwd.z, 1f));
                }
            }
            else
            {
                Shader.SetGlobalVector(CamForwardXZId, new Vector4(0f, 0f, 1f, 1f));
            }

            if (heroTransform != null)
            {
                Vector3 hp = heroTransform.position;
                Shader.SetGlobalVector(HeroWorldPosId, new Vector4(hp.x, hp.y, hp.z, 1f));
            }
            else
            {
                Shader.SetGlobalVector(HeroWorldPosId, new Vector4(9999f, 0f, 9999f, 0f));
            }

            Shader.SetGlobalFloat(TimeOffsetId, timeOffset);

            if (topDownLightmap != null)
            {
                topDownLightmap.UpdateLightmap(simTimeSeconds);
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
