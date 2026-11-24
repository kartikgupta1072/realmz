using System.Collections.Generic;
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
    /// C-key recenter, canonical terrain collider raycasting, world bounds clamping, safe-viewport
    /// combat auto-framing, and impact shake via UnityEngine.InputSystem.
    /// </summary>
    public sealed class BywaterCameraRig : MonoBehaviour
    {
        public static readonly Rect DefaultSafeViewportRect = new Rect(0.11f, 0.16f, 0.85f, 0.69f);
        private const float UnitHeadHeightMeters = 1.55f;

        [Header("Framing")]
        [SerializeField] private float fieldOfView = 34f;
        [SerializeField] private float pitchDegrees = 48f;
        [SerializeField] private float defaultDistance = 11.5f;
        [SerializeField] private float exploreDistance = 10.5f;
        [SerializeField] private float combatDistance = 12.5f;
        [SerializeField] private float minDistance = 6.0f;
        [SerializeField] private float maxDistance = 32.0f;
        [SerializeField] private float combatMaxDistance = 34.0f;

        public float EffectiveExploreMaxDistance => Mathf.Max(maxDistance, 32.0f);
        public float MaxCombatDistance => Mathf.Max(maxDistance, combatMaxDistance, 34f);
        public bool HasManualPanOffset => _targetManualOffset.sqrMagnitude > 0.04f || _currentManualOffset.sqrMagnitude > 0.04f;

        [Header("Smoothing & Controls")]
        [SerializeField] private float followSmoothTime = 0.18f;
        [SerializeField] private float zoomSmoothSpeed = 9f;
        [SerializeField] private float panSmoothSpeed = 16f;
        [SerializeField] private float scrollZoomStep = 1.6f;
        [SerializeField] private float keyPanSpeed = 12f;
        [SerializeField] private float mouseDragPanSpeed = 0.016f;
        [SerializeField] private float maxManualPanRadius = 60f;

        private Camera _cam;
        private Collider _terrainCollider;
        private Vector3 _followTarget;
        private Vector3 _currentFollowPos;
        private Vector3 _followVelocity;
        private Vector3 _targetManualOffset;
        private Vector3 _currentManualOffset;
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

            _targetDistance = Mathf.Clamp(defaultDistance, minDistance, EffectiveExploreMaxDistance);
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

            _currentManualOffset = Vector3.Lerp(
                _currentManualOffset,
                _targetManualOffset,
                1f - Mathf.Exp(-panSmoothSpeed * dt));

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

        public void FocusWorld(Vector3 worldPos, bool immediate = false, bool recenterManualPan = false)
        {
            SetFollowTarget(worldPos, immediate, recenterManualPan);
        }

        public void SetFollowTarget(Vector3 worldPos, bool immediate = false, bool recenterManualPan = false)
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
            else if (recenterManualPan)
            {
                _targetManualOffset = Vector3.zero;
            }
            else
            {
                ClampPanTarget();
            }
        }

        public void Recenter(bool immediate = false)
        {
            if (immediate)
            {
                _manualOffsetResetImmediate();
                _currentFollowPos = _followTarget;
                _followVelocity = Vector3.zero;
                ApplyCameraTransform();
            }
            else
            {
                _targetManualOffset = Vector3.zero;
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
            float activeMax = isCombat ? MaxCombatDistance : EffectiveExploreMaxDistance;
            _targetDistance = Mathf.Clamp(desired, minDistance, activeMax);
        }

        public static void ComputeCombatFraming(
            IReadOnlyList<Vector3> worldPositions,
            float fieldOfViewDeg,
            float pitchDeg,
            float aspectRatio,
            Rect safeViewportNorm,
            float minDist,
            float maxDist,
            out Vector3 pivotWorld,
            out float targetDistance)
        {
            float clampedMin = Mathf.Max(1.0f, minDist);
            float clampedMax = Mathf.Max(clampedMin, maxDist);

            if (worldPositions == null || worldPositions.Count == 0)
            {
                pivotWorld = Vector3.zero;
                targetDistance = Mathf.Clamp(12.5f, clampedMin, clampedMax);
                return;
            }

            float safePitch = Mathf.Clamp(pitchDeg, 5f, 89f);
            float safeFov = Mathf.Clamp(fieldOfViewDeg, 10f, 120f);
            float safeAspect = Mathf.Max(0.25f, aspectRatio);

            float padX = Mathf.Min(0.004f, safeViewportNorm.width * 0.05f);
            float padY = Mathf.Min(0.004f, safeViewportNorm.height * 0.05f);
            float xMin = Mathf.Clamp(safeViewportNorm.xMin + padX, 0.01f, 0.98f);
            float xMax = Mathf.Clamp(safeViewportNorm.xMax - padX, xMin + 0.01f, 0.99f);
            float yMin = Mathf.Clamp(safeViewportNorm.yMin + padY, 0.01f, 0.98f);
            float yMax = Mathf.Clamp(safeViewportNorm.yMax - padY, yMin + 0.01f, 0.99f);

            float nxMin = 2f * xMin - 1f;
            float nxMax = 2f * xMax - 1f;
            float nyMin = 2f * yMin - 1f;
            float nyMax = 2f * yMax - 1f;

            float tanV = Mathf.Tan(safeFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * safeAspect;
            float pitchRad = safePitch * Mathf.Deg2Rad;
            float sinP = Mathf.Sin(pitchRad);
            float cosP = Mathf.Cos(pitchRad);

            bool SolveForDistance(float d, out float outPx, out float outPz)
            {
                float lowZ = float.NegativeInfinity;
                float highZ = float.PositiveInfinity;

                float denLow = sinP - nyMin * tanV * cosP;
                float denHigh = sinP - nyMax * tanV * cosP;

                for (int i = 0; i < worldPositions.Count; i++)
                {
                    Vector3 foot = worldPositions[i];
                    for (int h = 0; h < 2; h++)
                    {
                        float py = h == 0 ? foot.y : foot.y + UnitHeadHeightMeters;
                        float pzVal = foot.z;
                        float a = py * cosP + pzVal * sinP;
                        float b = -py * sinP + pzVal * cosP;

                        float rhsLow = a - nyMin * tanV * (b + d);
                        if (denLow > 1e-4f)
                        {
                            highZ = Mathf.Min(highZ, rhsLow / denLow);
                        }
                        else if (denLow < -1e-4f)
                        {
                            lowZ = Mathf.Max(lowZ, rhsLow / denLow);
                        }

                        float rhsHigh = a - nyMax * tanV * (b + d);
                        if (denHigh > 1e-4f)
                        {
                            lowZ = Mathf.Max(lowZ, rhsHigh / denHigh);
                        }
                        else if (denHigh < -1e-4f)
                        {
                            highZ = Mathf.Min(highZ, rhsHigh / denHigh);
                        }
                    }
                }

                outPz = 0.5f * (lowZ + highZ);
                if (float.IsInfinity(outPz) || float.IsNaN(outPz))
                {
                    outPz = worldPositions[0].z;
                }

                float lowX = float.NegativeInfinity;
                float highX = float.PositiveInfinity;
                float minDepth = float.PositiveInfinity;

                for (int i = 0; i < worldPositions.Count; i++)
                {
                    Vector3 foot = worldPositions[i];
                    for (int h = 0; h < 2; h++)
                    {
                        float py = h == 0 ? foot.y : foot.y + UnitHeadHeightMeters;
                        float b = -py * sinP + foot.z * cosP;
                        float depth = b - outPz * cosP + d;
                        if (depth < minDepth)
                        {
                            minDepth = depth;
                        }

                        float safeDepth = Mathf.Max(0.25f, depth);
                        lowX = Mathf.Max(lowX, foot.x - nxMax * tanH * safeDepth);
                        highX = Mathf.Min(highX, foot.x - nxMin * tanH * safeDepth);
                    }
                }

                outPx = 0.5f * (lowX + highX);
                if (float.IsInfinity(outPx) || float.IsNaN(outPx))
                {
                    outPx = worldPositions[0].x;
                }

                return minDepth > 0.25f && lowZ <= highZ && lowX <= highX;
            }

            if (SolveForDistance(clampedMin, out float minPx, out float minPz))
            {
                pivotWorld = new Vector3(minPx, 0f, minPz);
                targetDistance = clampedMin;
                return;
            }

            if (!SolveForDistance(clampedMax, out float maxPx, out float maxPz))
            {
                pivotWorld = new Vector3(maxPx, 0f, maxPz);
                targetDistance = clampedMax;
                return;
            }

            float lo = clampedMin;
            float hi = clampedMax;
            float bestPx = maxPx;
            float bestPz = maxPz;
            for (int iter = 0; iter < 24; iter++)
            {
                float mid = 0.5f * (lo + hi);
                if (SolveForDistance(mid, out float midPx, out float midPz))
                {
                    hi = mid;
                    bestPx = midPx;
                    bestPz = midPz;
                }
                else
                {
                    lo = mid;
                }
            }

            pivotWorld = new Vector3(bestPx, 0f, bestPz);
            targetDistance = hi;
        }

        public void FrameCombatantsInSafeViewport(IReadOnlyList<Vector3> worldPositions, bool immediate = false)
        {
            FrameCombatantsInSafeViewport(worldPositions, DefaultSafeViewportRect, immediate);
        }

        public void FrameCombatantsInSafeViewport(IReadOnlyList<Vector3> worldPositions, Rect safeViewportNorm, bool immediate = false)
        {
            Camera cam = ResolveCamera();
            float aspect = cam != null && cam.aspect > 0.1f ? cam.aspect : (16f / 9f);
            float fov = cam != null && cam.fieldOfView > 5f ? cam.fieldOfView : fieldOfView;
            float activeMin = Mathf.Clamp(combatDistance * 0.85f, minDistance, MaxCombatDistance);

            ComputeCombatFraming(
                worldPositions,
                fov,
                pitchDegrees,
                aspect,
                safeViewportNorm,
                activeMin,
                MaxCombatDistance,
                out Vector3 pivotWorld,
                out float solvedDistance);

            _isCombatMode = true;
            _targetDistance = Mathf.Clamp(solvedDistance, minDistance, MaxCombatDistance);
            _followTarget = new Vector3(pivotWorld.x, 0f, pivotWorld.z);
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
                _currentDistance = _targetDistance;
                _manualOffsetResetImmediate();
                ApplyCameraTransform();
            }
            else
            {
                ClampPanTarget();
            }
        }

        public bool IsWorldPointInSafeViewport(Vector3 worldPos)
        {
            return IsWorldPointInSafeViewport(worldPos, DefaultSafeViewportRect);
        }

        public bool IsWorldPointInSafeViewport(Vector3 worldPos, Rect safeViewportNorm)
        {
            Camera cam = ResolveCamera();
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(worldPos);
                return vp.z > 0f &&
                       vp.x >= safeViewportNorm.xMin &&
                       vp.x <= safeViewportNorm.xMax &&
                       vp.y >= safeViewportNorm.yMin &&
                       vp.y <= safeViewportNorm.yMax;
            }

            Vector3 pivot = _currentFollowPos + _currentManualOffset;
            Quaternion rot = Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 camPos = pivot - (rot * Vector3.forward) * _currentDistance;
            Vector3 camLocal = Quaternion.Inverse(rot) * (worldPos - camPos);
            if (camLocal.z <= 0.05f)
            {
                return false;
            }

            float tanV = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * (16f / 9f);
            float vx = 0.5f + 0.5f * (camLocal.x / (camLocal.z * tanH));
            float vy = 0.5f + 0.5f * (camLocal.y / (camLocal.z * tanV));
            return vx >= safeViewportNorm.xMin &&
                   vx <= safeViewportNorm.xMax &&
                   vy >= safeViewportNorm.yMin &&
                   vy <= safeViewportNorm.yMax;
        }

        public void EnsureWorldPointVisible(Vector3 worldPos)
        {
            EnsureWorldPointVisible(worldPos, DefaultSafeViewportRect);
        }

        public void EnsureWorldPointVisible(Vector3 worldPos, Rect safeViewportNorm)
        {
            Vector3 headPos = worldPos + new Vector3(0f, UnitHeadHeightMeters, 0f);
            if (IsWorldPointInSafeViewport(worldPos, safeViewportNorm) &&
                IsWorldPointInSafeViewport(headPos, safeViewportNorm))
            {
                return;
            }

            Camera cam = ResolveCamera();
            float aspect = cam != null && cam.aspect > 0.1f ? cam.aspect : (16f / 9f);
            float fov = cam != null && cam.fieldOfView > 5f ? cam.fieldOfView : fieldOfView;

            Rect padded = new Rect(
                Mathf.Min(safeViewportNorm.xMin + 0.04f, safeViewportNorm.center.x),
                Mathf.Min(safeViewportNorm.yMin + 0.04f, safeViewportNorm.center.y),
                Mathf.Max(0.05f, safeViewportNorm.width - 0.08f),
                Mathf.Max(0.05f, safeViewportNorm.height - 0.08f));

            Vector3[] pts = { worldPos };
            ComputeCombatFraming(
                pts,
                fov,
                pitchDegrees,
                aspect,
                padded,
                _targetDistance,
                _targetDistance,
                out Vector3 targetPivot,
                out _);

            if (_targetManualOffset.sqrMagnitude > 0.001f)
            {
                _targetManualOffset = new Vector3(targetPivot.x - _followTarget.x, 0f, targetPivot.z - _followTarget.z);
                ClampPanTarget();
            }
            else
            {
                SetFollowTarget(targetPivot, immediate: false);
            }
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
                float activeMax = _isCombatMode ? MaxCombatDistance : EffectiveExploreMaxDistance;
                _targetDistance = Mathf.Clamp(_targetDistance - normalized * scrollZoomStep, minDistance, activeMax);
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
                if (kb.homeKey.wasPressedThisFrame)
                {
                    Recenter();
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
                    float distanceScale = _currentDistance / Mathf.Max(1f, defaultDistance);
                    Vector3 dir = new Vector3(moveX, 0f, moveZ).normalized;
                    _targetManualOffset += dir * (keyPanSpeed * distanceScale * Time.unscaledDeltaTime);
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
