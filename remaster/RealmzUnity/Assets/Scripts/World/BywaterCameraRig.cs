using RealmzCore.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Realmz.World
{
    /// <summary>
    /// Perspective HD-2D tactical/exploration camera rig with scroll zoom, right-drag / WASD panning,
    /// C-key recenter, and ground-plane GridCoord raycasting via UnityEngine.InputSystem.
    /// </summary>
    public sealed class BywaterCameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] private float fieldOfView = 34f;
        [SerializeField] private float pitchDegrees = 48f;
        [SerializeField] private float defaultDistance = 11.5f;
        [SerializeField] private float exploreDistance = 10.5f;
        [SerializeField] private float combatDistance = 12.5f;
        [SerializeField] private float minDistance = 6.0f;
        [SerializeField] private float maxDistance = 19.0f;

        [Header("Smoothing & Controls")]
        [SerializeField] private float followSmoothTime = 0.14f;
        [SerializeField] private float zoomSmoothSpeed = 10f;
        [SerializeField] private float scrollZoomStep = 1.25f;
        [SerializeField] private float keyPanSpeed = 11f;
        [SerializeField] private float mouseDragPanSpeed = 0.016f;
        [SerializeField] private float maxManualPanRadius = 40f;

        private Camera _cam;
        private Vector3 _followTarget;
        private Vector3 _currentFollowPos;
        private Vector3 _followVelocity;
        private Vector3 _targetManualOffset;
        private Vector3 _currentManualOffset;
        private Vector3 _offsetVelocity;
        private float _targetDistance;
        private float _currentDistance;
        private bool _hasTarget;
        private bool _isCombatMode;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                _cam = GetComponentInChildren<Camera>();
            }

            if (_cam != null)
            {
                _cam.fieldOfView = fieldOfView;
            }

            _targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
            _currentDistance = _targetDistance;
            ApplyCameraTransform();
        }

        private void Update()
        {
            HandleZoomInput();
            HandlePanInput();
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            _currentFollowPos = Vector3.SmoothDamp(
                _currentFollowPos,
                _followTarget,
                ref _followVelocity,
                followSmoothTime,
                Mathf.Infinity,
                dt);

            _currentManualOffset = Vector3.SmoothDamp(
                _currentManualOffset,
                _targetManualOffset,
                ref _offsetVelocity,
                0.08f,
                Mathf.Infinity,
                dt);

            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, 1f - Mathf.Exp(-zoomSmoothSpeed * dt));

            ApplyCameraTransform();
        }

        public static Vector3 GridToWorld(GridCoord coord, float y = 0f)
        {
            return new Vector3(coord.X, y, -coord.Y);
        }

        public void SetFollowTarget(Vector3 worldPos, bool immediate = false)
        {
            _followTarget = new Vector3(worldPos.x, 0f, worldPos.z);

            if (immediate || !_hasTarget)
            {
                _hasTarget = true;
                _currentFollowPos = _followTarget;
                _followVelocity = Vector3.zero;
                _manualOffsetResetImmediate();
                ApplyCameraTransform();
            }
        }

        public void SetCombatMode(bool isCombat)
        {
            if (_isCombatMode == isCombat)
            {
                return;
            }

            _isCombatMode = isCombat;
            float desired = isCombat ? combatDistance : exploreDistance;
            _targetDistance = Mathf.Clamp(desired, minDistance, maxDistance);
        }

        public bool ScreenPointToGridCoord(Vector2 screenPos, out GridCoord coord)
        {
            coord = default;
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(screenPos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float enter) || enter < 0f)
            {
                return false;
            }

            Vector3 hit = ray.GetPoint(enter);
            coord = new GridCoord(Mathf.RoundToInt(hit.x), Mathf.RoundToInt(-hit.z));
            return true;
        }

        private void HandleZoomInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                float normalized = Mathf.Abs(scrollY) >= 10f ? scrollY / 120f : scrollY;
                _targetDistance = Mathf.Clamp(_targetDistance - normalized * scrollZoomStep, minDistance, maxDistance);
            }
        }

        private void HandlePanInput()
        {
            if (IsTypingInInputField())
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.0001f)
                {
                    float distanceScale = _currentDistance / Mathf.Max(1f, defaultDistance);
                    Vector3 dragPan = new Vector3(-delta.x, 0f, -delta.y) * (mouseDragPanSpeed * distanceScale);
                    _targetManualOffset += dragPan;
                    ClampManualOffset();
                }
            }

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.cKey.wasPressedThisFrame)
                {
                    _targetManualOffset = Vector3.zero;
                    return;
                }

                float moveX = 0f;
                float moveZ = 0f;

                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) moveX -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveX += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) moveZ -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) moveZ += 1f;

                if (Mathf.Abs(moveX) > 0.01f || Mathf.Abs(moveZ) > 0.01f)
                {
                    Vector3 dir = new Vector3(moveX, 0f, moveZ).normalized;
                    _targetManualOffset += dir * (keyPanSpeed * Time.unscaledDeltaTime);
                    ClampManualOffset();
                }
            }
        }

        private void ClampManualOffset()
        {
            if (_targetManualOffset.sqrMagnitude > maxManualPanRadius * maxManualPanRadius)
            {
                _targetManualOffset = _targetManualOffset.normalized * maxManualPanRadius;
            }
        }

        private void _manualOffsetResetImmediate()
        {
            _targetManualOffset = Vector3.zero;
            _currentManualOffset = Vector3.zero;
            _offsetVelocity = Vector3.zero;
        }

        private static bool IsTypingInInputField()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            {
                return false;
            }

            return eventSystem.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

        private Camera ResolveCamera()
        {
            if (_cam == null)
            {
                _cam = GetComponent<Camera>();
                if (_cam == null)
                {
                    _cam = GetComponentInChildren<Camera>();
                }

                if (_cam == null)
                {
                    _cam = Camera.main;
                }
            }

            return _cam;
        }

        private void ApplyCameraTransform()
        {
            Camera cam = ResolveCamera();
            if (cam != null && Mathf.Abs(cam.fieldOfView - fieldOfView) > 0.01f)
            {
                cam.fieldOfView = fieldOfView;
            }

            Quaternion rot = Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 pivot = _currentFollowPos + _currentManualOffset;
            Vector3 forward = rot * Vector3.forward;
            transform.rotation = rot;
            transform.position = pivot - forward * _currentDistance;
        }
    }
}
