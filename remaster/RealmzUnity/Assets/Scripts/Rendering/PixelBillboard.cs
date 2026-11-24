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

        private static int s_cachedFrame = -1;
        private static Camera s_cachedCam;
        private static Vector3 s_cachedForwardXZ;
        private static Quaternion s_cachedRotation = Quaternion.identity;

        private Camera _cachedCam;
        private Quaternion _lastAppliedRotation = Quaternion.identity;
        private Vector3 _lastFwdXZ;
        private bool _hasAppliedRotation;

        private void OnEnable()
        {
            _hasAppliedRotation = false;
        }

        private void OnDisable()
        {
            _hasAppliedRotation = false;
        }

        private void LateUpdate()
        {
            Camera cam = targetCamera;
            if (cam == null)
            {
                if (_cachedCam == null || !_cachedCam.isActiveAndEnabled)
                {
                    _cachedCam = Camera.main;
                }
                cam = _cachedCam;
            }

            if (cam == null)
            {
                return;
            }

            Vector3 fwdXZ;
            Quaternion targetRot;
            int frame = Time.frameCount;

            if (Application.isPlaying && s_cachedFrame == frame && ReferenceEquals(s_cachedCam, cam))
            {
                fwdXZ = s_cachedForwardXZ;
                targetRot = s_cachedRotation;
            }
            else
            {
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    return;
                }

                fwdXZ = forward.normalized;
                targetRot = Quaternion.LookRotation(fwdXZ, Vector3.up);

                if (Application.isPlaying)
                {
                    s_cachedFrame = frame;
                    s_cachedCam = cam;
                    s_cachedForwardXZ = fwdXZ;
                    s_cachedRotation = targetRot;
                }
            }

            if (_hasAppliedRotation && (_lastFwdXZ - fwdXZ).sqrMagnitude < 1e-7f)
            {
                return;
            }

            _lastFwdXZ = fwdXZ;
            _lastAppliedRotation = targetRot;
            _hasAppliedRotation = true;
            transform.rotation = targetRot;
        }
    }
}
