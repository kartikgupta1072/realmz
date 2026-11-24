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
}
