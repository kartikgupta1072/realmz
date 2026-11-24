using Realmz.Cartography;
using RealmzCore.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Realmz.World
{
    /// <summary>
    /// Perspective HD-2D tactical/exploration camera rig with scroll zoom, right-drag / WASD panning,
    /// C-key recenter, canonical terrain collider raycasting, world bounds clamping, and impact shake
    /// via UnityEngine.InputSystem.
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
        private Collider _terrainCollider;
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

        private bool _hasWorldBounds;
        private float _worldWidth;
        private float _worldHeight;

        private float _impulseStrength;
        private float _impulseDuration;
        private float _impulseRemaining;
        private float _impulsePhase;
        private Vector3 _currentShakeOffset;

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

            UpdateImpactImpulse(dt);
            ApplyCameraTransform();
        }

        public static Vector3 GridToWorld(GridCoord coord, float y = 0f)
        {
            return new Vector3(coord.X + 0.5f, y, -(coord.Y + 0.5f));
        }

        public void SetTerrainCollider(Collider col)
        {
            _terrainCollider = col;
        }

        public void SetWorldBounds(int width, int height)
        {
            if (width > 0 && height > 0)
            {
                _hasWorldBounds = true;
                _worldWidth = width;
                _worldHeight = height;
                ClampPanTarget();
                ApplyCameraTransform();
            }
            else
            {
                _hasWorldBounds = false;
                _worldWidth = 0f;
                _worldHeight = 0f;
            }
        }

        public void FocusWorld(Vector3 worldPos, bool immediate = false)
        {
            SetFollowTarget(worldPos, immediate);
        }

        public void SetFollowTarget(Vector3 worldPos, bool immediate = false)
        {
            _followTarget = new Vector3(worldPos.x, 0f, worldPos.z);
            if (_hasWorldBounds)
            {
                _followTarget.x = Mathf.Clamp(_followTarget.x, 0f, _worldWidth);
                _followTarget.z = Mathf.Clamp(_followTarget.z, -_worldHeight, 0f);
            }

            if (immediate || !_hasTarget)
            {
                _hasTarget = true;
                _currentFollowPos = _followTarget;
                _followVelocity = Vector3.zero;
                _manualOffsetResetImmediate();
                ApplyCameraTransform();
            }
            else
            {
                ClampPanTarget();
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

        public void PlayImpactImpulse(float strength = 0.14f, float duration = 0.16f)
        {
            if (strength <= 0f || duration <= 0f)
            {
                _impulseRemaining = 0f;
                _currentShakeOffset = Vector3.zero;
                return;
            }

            _impulseStrength = strength;
            _impulseDuration = duration;
            _impulseRemaining = duration;
            _impulsePhase = Time.unscaledTime * 37.3f;
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
            Vector3 hitPoint;

            if (_terrainCollider != null &&
                _terrainCollider.enabled &&
                _terrainCollider.gameObject.activeInHierarchy &&
                _terrainCollider.Raycast(ray, out RaycastHit terrainHit, 300f))
            {
                hitPoint = terrainHit.point;
            }
            else if (_terrainCollider == null &&
                Physics.Raycast(ray, out RaycastHit physHit, 300f) &&
                physHit.collider != null &&
                (physHit.collider is MeshCollider || physHit.collider.GetComponent<BywaterElevationBuilder>() != null))
            {
                hitPoint = physHit.point;
            }
            else
            {
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                if (!groundPlane.Raycast(ray, out float enter) || enter < 0f)
                {
                    return false;
                }

                hitPoint = ray.GetPoint(enter);
            }

            coord = new GridCoord(Mathf.FloorToInt(hitPoint.x), Mathf.FloorToInt(-hitPoint.z));
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
                    ClampPanTarget();
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
                    ClampPanTarget();
                }
            }
        }

        private void ClampPanTarget()
        {
            if (_targetManualOffset.sqrMagnitude > maxManualPanRadius * maxManualPanRadius)
            {
                _targetManualOffset = _targetManualOffset.normalized * maxManualPanRadius;
            }

            if (_hasWorldBounds)
            {
                Vector3 desiredPivot = _followTarget + _targetManualOffset;
                desiredPivot.x = Mathf.Clamp(desiredPivot.x, 0f, _worldWidth);
                desiredPivot.z = Mathf.Clamp(desiredPivot.z, -_worldHeight, 0f);
                _targetManualOffset = desiredPivot - _followTarget;
                _targetManualOffset.y = 0f;
            }
        }

        private void _manualOffsetResetImmediate()
        {
            _targetManualOffset = Vector3.zero;
            _currentManualOffset = Vector3.zero;
            _offsetVelocity = Vector3.zero;
        }

        private void UpdateImpactImpulse(float dt)
        {
            if (_impulseRemaining <= 0f)
            {
                _currentShakeOffset = Vector3.zero;
                return;
            }

            _impulseRemaining = Mathf.Max(0f, _impulseRemaining - dt);
            float norm = _impulseDuration > 0f ? (_impulseRemaining / _impulseDuration) : 0f;
            float envelope = norm * norm;
            float t = (_impulseDuration - _impulseRemaining) * 58f + _impulsePhase;

            float sx = Mathf.Sin(t * 1.13f) * _impulseStrength * envelope;
            float sy = Mathf.Cos(t * 1.47f) * (_impulseStrength * 0.65f) * envelope;
            float sz = Mathf.Sin(t * 0.89f + 1.2f) * (_impulseStrength * 0.45f) * envelope;
            _currentShakeOffset = new Vector3(sx, sy, sz);
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
            if (_hasWorldBounds)
            {
                pivot.x = Mathf.Clamp(pivot.x, 0f, _worldWidth);
                pivot.z = Mathf.Clamp(pivot.z, -_worldHeight, 0f);
            }

            Vector3 forward = rot * Vector3.forward;
            transform.rotation = rot;
            transform.position = pivot - forward * _currentDistance + _currentShakeOffset;
        }
    }
}
