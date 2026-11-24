using System.Collections;
using System.Collections.Generic;
using Realmz.Data;
using Realmz.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Realmz.World
{
    /// <summary>
    /// Foot-anchored HD-2D sprite unit with turn/team under-ring, billboarded world-space HUD,
    /// stepped path coroutine animation, combat lunge/hit-flash micro-juice, and arcing floating text.
    /// </summary>
    public sealed class BywaterUnitView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int WindPhaseId = Shader.PropertyToID("_WindPhase");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color CurrentTurnGold = new Color(0.980f, 0.800f, 0.082f, 0.95f); // #FACC15
        private static readonly Color HitFlashGoldWhite = new Color(1.0f, 0.953f, 0.690f, 1.0f); // #FFF3B0
        private static readonly Color DeadSpriteTint = new Color(0.45f, 0.45f, 0.50f, 0.55f);
        private static readonly Color AllyHpBlue = new Color(0.220f, 0.741f, 0.973f, 1f);      // #38BDF8
        private static readonly Color EnemyHpRed = new Color(0.937f, 0.267f, 0.267f, 1f);      // #EF4444
        private static readonly Color ContactShadowColor = new Color(0.05f, 0.06f, 0.10f, 0.55f);

        private static Material _sharedRingMaterial;
        private static Texture2D _sharedRingTexture;
        private static Material _sharedShadowMaterial;
        private static Texture2D _sharedShadowTexture;
        private static Material _sharedChevronMaterial;
        private static Texture2D _sharedChevronTexture;
        private static Material _sharedDustMaterial;
        private static Texture2D _sharedDustTexture;
        private static int _dustMaterialRefCount;
        private static Mesh _sharedUnitQuadMesh;

        private MeshRenderer _spriteRenderer;
        private Transform _spriteBillboardTransform;
        private MeshRenderer _underRingRenderer;
        private MeshRenderer _contactShadowRenderer;
        private Transform _turnChevronTransform;
        private MeshRenderer _turnChevronRenderer;

        private Canvas _microHudCanvas;
        private Canvas _worldHudCanvas;
        private GameObject _expandedNameplateRoot;
        private TextMeshProUGUI _nameLabel;
        private TextMeshProUGUI _detailLabel;
        private Image _hpFill;
        private Image _spBarRoot;
        private Image _spFill;
        private TextMeshProUGUI _statusBadgeLabel;
        private Image _statusBadgeIcon;

        private MaterialPropertyBlock _ringPropBlock;
        private MaterialPropertyBlock _spritePropBlock;
        private MaterialPropertyBlock _dustPropBlock;

        private string _unitId;
        private BywaterAssetCatalog _catalog;
        private Material _idleMaterial;
        private Material _walkMaterial;
        private Material _attackMaterial;
        private bool _hasWalkSheet;
        private bool _hasAttackSheet;
        private UnitAnimState _animState = UnitAnimState.Idle;
        private float _attackHoldUntil;
        private bool _facingRight = true;
        private bool _registeredDustUser;

        private TMP_FontAsset _font;
        private Color _baseRingColor = Color.white;
        private float _spriteSize = 2.0f;
        private float _spriteWidth = 2.0f;
        private float _spriteHeight = 2.0f;
        private bool _showHud = true;
        private bool _isAlive = true;
        private bool _isAlly = true;
        private bool _isCurrentTurn;
        private bool _nameplateVisible;
        private string _lastDetailLine;
        private float _verticalStaggerWorld;
        private bool _isHitFlashing;
        private float _idlePhase;
        private float _windPhaseNorm;
        private Coroutine _moveCoroutine;
        private Coroutine _lungeCoroutine;
        private Coroutine _hitFlashCoroutine;

        private struct DustPuffInstance
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public Vector3 StartPos;
            public Vector3 Velocity;
            public float GroundY;
            public float Age;
            public float Lifetime;
            public bool Active;
        }

        private readonly List<DustPuffInstance> _dustPuffs = new List<DustPuffInstance>(8);
        private readonly List<Vector3> _pathScratch = new List<Vector3>(16);

        private struct StaggerEntry
        {
            public BywaterUnitView View;
            public Vector3 ScreenPos;
            public Vector3 HeadWorldPos;
        }

        private static readonly List<StaggerEntry> _staggerScratch = new List<StaggerEntry>(16);
        private static readonly List<Vector2> _placedNameplatesScratch = new List<Vector2>(16);

        public string UnitId
        {
            get => _unitId;
            private set => _unitId = value;
        }

        public UnitAnimState CurrentAnimState => _animState;
        public bool FacingRight => _facingRight;
        public bool IsNameplateVisible => _nameplateVisible && _showHud && _isAlive;

        private float GetVisualHeadHeight()
        {
            return _spriteSize >= 1.75f ? 1.48f : _spriteSize;
        }

        public Vector3 GetHeadWorldPosition()
        {
            return transform.position + new Vector3(0f, GetVisualHeadHeight() + 0.16f, 0f);
        }

        public void SetCatalog(BywaterAssetCatalog catalog)
        {
            _catalog = catalog;
            if (_catalog != null && !string.IsNullOrEmpty(_unitId))
            {
                ResolveStateMaterials(_idleMaterial);
                ApplyAnimStateMaterial();
            }
        }

        public void SetAnimState(UnitAnimState state, float holdSeconds = 0f)
        {
            _animState = state;
            if (state == UnitAnimState.Attack && holdSeconds > 0f)
            {
                _attackHoldUntil = Time.time + holdSeconds;
            }
            else if (state != UnitAnimState.Attack)
            {
                _attackHoldUntil = 0f;
            }

            ApplyAnimStateMaterial();
        }

        public void SetFacingRight(bool facingRight)
        {
            _facingRight = facingRight;
            if (_spriteBillboardTransform != null)
            {
                _spriteBillboardTransform.localScale = new Vector3(_facingRight ? 1f : -1f, 1f, 1f);
            }
        }

        private void ResolveStateMaterials(Material fallback)
        {
            if (_catalog != null && !string.IsNullOrEmpty(_unitId))
            {
                _idleMaterial = _catalog.ResolveCartographyUnitMaterial(_unitId, UnitAnimState.Idle, fallback, out _);
                _walkMaterial = _catalog.ResolveCartographyUnitMaterial(_unitId, UnitAnimState.Walk, _idleMaterial, out _hasWalkSheet);
                _attackMaterial = _catalog.ResolveCartographyUnitMaterial(_unitId, UnitAnimState.Attack, _idleMaterial, out _hasAttackSheet);
            }
            else
            {
                _idleMaterial = fallback;
                _walkMaterial = fallback;
                _attackMaterial = fallback;
                _hasWalkSheet = false;
                _hasAttackSheet = false;
            }
        }

        private void ApplyAnimStateMaterial()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            Material targetMat;
            switch (_animState)
            {
                case UnitAnimState.Walk:
                    targetMat = (_hasWalkSheet && _walkMaterial != null) ? _walkMaterial : _idleMaterial;
                    break;
                case UnitAnimState.Attack:
                    targetMat = (_hasAttackSheet && _attackMaterial != null) ? _attackMaterial : _idleMaterial;
                    break;
                default:
                    targetMat = _idleMaterial;
                    break;
            }

            if (targetMat != null && _spriteRenderer.sharedMaterial != targetMat)
            {
                _spriteRenderer.sharedMaterial = targetMat;
            }
        }

        public void Configure(
            string unitId,
            string displayName,
            Material billboardMat,
            Color ringColor,
            TMP_FontAsset font,
            float spriteSize = 2.0f,
            bool showHud = true,
            bool? isAllyOverride = null,
            BywaterAssetCatalog catalog = null)
        {
            UnitId = unitId ?? string.Empty;
            if (catalog != null)
            {
                _catalog = catalog;
            }
            _baseRingColor = ringColor;
            _isAlly = isAllyOverride ?? (ringColor.b >= ringColor.r);
            _font = font;
            _spriteSize = Mathf.Max(0.4f, spriteSize);
            _spriteWidth = _spriteSize;
            _spriteHeight = _spriteSize;

            ResolveStateMaterials(billboardMat);
            billboardMat = _idleMaterial;

            _showHud = showHud;
            _isAlive = true;
            _isCurrentTurn = false;
            _nameplateVisible = false;
            _lastDetailLine = null;
            _verticalStaggerWorld = 0f;
            _isHitFlashing = false;
            _animState = UnitAnimState.Idle;
            _attackHoldUntil = 0f;
            _facingRight = true;
            _idlePhase = ComputeDeterministicPhase(UnitId);
            _windPhaseNorm = _idlePhase / (Mathf.PI * 2f);

            StopMoveCoroutine();
            StopLungeCoroutine();
            StopHitFlashCoroutine();
            EnsureHierarchy();

            ResetSpriteIdlePose();
            if (_spriteBillboardTransform != null)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
                _spriteBillboardTransform.localScale = Vector3.one;
            }

            _spriteRenderer.sharedMaterial = billboardMat;
            ApplySpriteTint(Color.white);

            if (_contactShadowRenderer != null)
            {
                _contactShadowRenderer.gameObject.SetActive(true);
            }

            if (_underRingRenderer != null)
            {
                _underRingRenderer.gameObject.SetActive(false);
            }

            float headHeight = GetVisualHeadHeight();
            if (_turnChevronTransform != null)
            {
                _turnChevronTransform.localPosition = new Vector3(0f, headHeight + 0.10f, 0f);
                _turnChevronTransform.gameObject.SetActive(false);
            }

            if (_microHudCanvas != null)
            {
                _microHudCanvas.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                _microHudCanvas.gameObject.SetActive(showHud);
            }

            ApplyTeamHpColor();

            _worldHudCanvas.transform.localPosition = new Vector3(0f, headHeight + 0.16f, 0f);
            _expandedNameplateRoot.SetActive(false);
            _worldHudCanvas.gameObject.SetActive(false);

            if (_font != null)
            {
                _nameLabel.font = _font;
                _detailLabel.font = _font;
                _statusBadgeLabel.font = _font;
            }

            _nameLabel.text = displayName ?? string.Empty;
            _detailLabel.text = string.Empty;
            _detailLabel.gameObject.SetActive(false);
            _statusBadgeLabel.gameObject.SetActive(false);
            if (_statusBadgeIcon != null)
            {
                _statusBadgeIcon.sprite = null;
                _statusBadgeIcon.gameObject.SetActive(false);
            }
        }

        public void SetTeamColor(bool isAlly)
        {
            _isAlly = isAlly;
            EnsureHierarchy();
            ApplyTeamHpColor();
        }

        public void SetNameplateVisible(bool visible, string detailLine = null, float verticalStaggerWorld = 0f)
        {
            EnsureHierarchy();
            _nameplateVisible = visible;
            _lastDetailLine = detailLine;
            _verticalStaggerWorld = Mathf.Max(0f, verticalStaggerWorld);

            _worldHudCanvas.transform.localPosition = new Vector3(0f, GetVisualHeadHeight() + 0.16f + _verticalStaggerWorld, 0f);

            bool hasDetail = !string.IsNullOrEmpty(detailLine);
            _detailLabel.text = hasDetail ? detailLine : string.Empty;
            _detailLabel.gameObject.SetActive(hasDetail);

            bool shouldShow = _showHud && _isAlive && _nameplateVisible;
            _expandedNameplateRoot.SetActive(shouldShow);
            _worldHudCanvas.gameObject.SetActive(shouldShow);
        }

        public static void ResolveNameplateStagger(IReadOnlyList<BywaterUnitView> views, Camera cam)
        {
            if (views == null || views.Count == 0)
            {
                return;
            }

            Camera activeCam = cam != null ? cam : Camera.main;
            _staggerScratch.Clear();
            _placedNameplatesScratch.Clear();

            for (int i = 0; i < views.Count; i++)
            {
                BywaterUnitView v = views[i];
                if (v == null || !v.IsNameplateVisible)
                {
                    continue;
                }

                Vector3 headWorld = v.GetHeadWorldPosition();
                if (activeCam == null)
                {
                    v.SetNameplateVisible(true, v._lastDetailLine, 0f);
                    continue;
                }

                Vector3 sp = activeCam.WorldToScreenPoint(headWorld);
                if (sp.z <= 0.05f)
                {
                    v.SetNameplateVisible(true, v._lastDetailLine, 0f);
                    continue;
                }

                _staggerScratch.Add(new StaggerEntry
                {
                    View = v,
                    ScreenPos = sp,
                    HeadWorldPos = headWorld
                });
            }

            if (_staggerScratch.Count == 0)
            {
                return;
            }

            if (_staggerScratch.Count == 1)
            {
                _staggerScratch[0].View.SetNameplateVisible(true, _staggerScratch[0].View._lastDetailLine, 0f);
                return;
            }

            _staggerScratch.Sort((a, b) => a.ScreenPos.y.CompareTo(b.ScreenPos.y));

            const float minOverlapX = 128f;
            const float minOverlapY = 40f;

            for (int i = 0; i < _staggerScratch.Count; i++)
            {
                StaggerEntry entry = _staggerScratch[i];
                float targetX = entry.ScreenPos.x;
                float targetY = entry.ScreenPos.y;

                for (int bump = 0; bump < 5; bump++)
                {
                    bool overlaps = false;
                    for (int p = 0; p < _placedNameplatesScratch.Count; p++)
                    {
                        Vector2 placed = _placedNameplatesScratch[p];
                        if (Mathf.Abs(targetX - placed.x) < minOverlapX && Mathf.Abs(targetY - placed.y) < minOverlapY)
                        {
                            targetY = placed.y + minOverlapY;
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        break;
                    }
                }

                _placedNameplatesScratch.Add(new Vector2(targetX, targetY));

                float screenDeltaY = targetY - entry.ScreenPos.y;
                float worldStagger = 0f;
                if (screenDeltaY > 0.5f && activeCam != null)
                {
                    Vector3 bumpedWorld = activeCam.ScreenToWorldPoint(new Vector3(targetX, targetY, entry.ScreenPos.z));
                    worldStagger = Mathf.Max(0f, bumpedWorld.y - entry.HeadWorldPos.y);
                }

                entry.View.SetNameplateVisible(true, entry.View._lastDetailLine, worldStagger);
            }
        }

        private void Update()
        {
            UpdateDustPuffs(Time.deltaTime);

            if (_animState == UnitAnimState.Attack &&
                _attackHoldUntil > 0f &&
                Time.time >= _attackHoldUntil &&
                _lungeCoroutine == null)
            {
                SetAnimState(_moveCoroutine != null ? UnitAnimState.Walk : UnitAnimState.Idle);
            }

            if (_turnChevronTransform != null && _isCurrentTurn && _isAlive)
            {
                float bob = Mathf.Sin(Time.time * 4.2f + _idlePhase) * 0.055f;
                _turnChevronTransform.localPosition = new Vector3(0f, GetVisualHeadHeight() + 0.10f + bob, 0f);
            }

            if (_spriteRenderer == null)
            {
                return;
            }

            if (_moveCoroutine != null)
            {
                return;
            }

            if (!_isAlive || _lungeCoroutine != null)
            {
                ResetSpriteIdlePose();
                return;
            }

            float t = Time.time * 2.15f + _idlePhase;
            float breathWave = Mathf.Sin(t);
            float swayWave = Mathf.Sin(t * 0.68f + 0.85f);

            float breathScale = 1.0f + breathWave * 0.025f;
            float tiltDeg = swayWave * 1.4f;
            float tiltRad = tiltDeg * Mathf.Deg2Rad;
            float halfHeight = _spriteHeight * 0.5f * breathScale;

            Transform spriteTf = _spriteRenderer.transform;
            spriteTf.localScale = new Vector3(_spriteWidth, _spriteHeight * breathScale, 1f);
            spriteTf.localPosition = new Vector3(-Mathf.Sin(tiltRad) * halfHeight, Mathf.Cos(tiltRad) * halfHeight, 0f);
            spriteTf.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
        }

        public void SetWorldPosition(Vector3 pos)
        {
            StopMoveCoroutine();
            transform.position = pos;
        }

        public void MoveAlongPath(IReadOnlyList<Vector3> worldPoints, float stepDuration = 0.08f)
        {
            MoveAlongWorldPath(worldPoints, stepDuration);
        }

        public void MoveAlongWorldPath(IReadOnlyList<Vector3> worldPoints, float stepDuration)
        {
            StopMoveCoroutine();

            if (worldPoints == null || worldPoints.Count == 0)
            {
                return;
            }

            _pathScratch.Clear();
            for (int i = 0; i < worldPoints.Count; i++)
            {
                _pathScratch.Add(worldPoints[i]);
            }

            Vector3 finalPoint = _pathScratch[_pathScratch.Count - 1];
            float finalDeltaX = ComputeHorizontalDelta(transform.position, finalPoint);
            if (Mathf.Abs(finalDeltaX) > 0.02f)
            {
                SetFacingRight(finalDeltaX >= 0f);
            }

            if (!Application.isPlaying || !gameObject.activeInHierarchy || stepDuration <= 0.001f)
            {
                transform.position = finalPoint;
                if (_spriteBillboardTransform != null && _lungeCoroutine == null)
                {
                    _spriteBillboardTransform.localPosition = Vector3.zero;
                }
                ResetSpriteIdlePose();
                SetAnimState(UnitAnimState.Idle);
                return;
            }

            SetAnimState(UnitAnimState.Walk);
            _moveCoroutine = StartCoroutine(MovePathRoutine(_pathScratch, stepDuration));
        }

        public void SetHp(int hp, int maxHp)
        {
            EnsureHierarchy();
            float safeMaxHp = Mathf.Max(1, maxHp);
            float hpRatio = Mathf.Clamp01(hp / safeMaxHp);
            _hpFill.rectTransform.anchorMax = new Vector2(hpRatio, 1f);
            ApplyTeamHpColor();
        }

        private void ApplyTeamHpColor()
        {
            if (_hpFill != null)
            {
                _hpFill.color = _isAlly ? AllyHpBlue : EnemyHpRed;
            }
        }

        public void SetSp(int sp, int maxSp)
        {
            EnsureHierarchy();
            bool hasSp = maxSp > 0;
            _spBarRoot.gameObject.SetActive(hasSp);
            if (hasSp)
            {
                float spRatio = Mathf.Clamp01(sp / (float)maxSp);
                _spFill.rectTransform.anchorMax = new Vector2(spRatio, 1f);
            }
        }

        public void SetTurnHighlight(bool isCurrentTurn)
        {
            EnsureHierarchy();
            _isCurrentTurn = isCurrentTurn;
            if (!_isAlive)
            {
                _underRingRenderer.gameObject.SetActive(false);
                if (_turnChevronTransform != null)
                {
                    _turnChevronTransform.gameObject.SetActive(false);
                }
                return;
            }

            _underRingRenderer.gameObject.SetActive(isCurrentTurn);
            if (isCurrentTurn)
            {
                ApplyRingColor(CurrentTurnGold);
            }

            if (_turnChevronTransform != null)
            {
                _turnChevronTransform.gameObject.SetActive(isCurrentTurn);
                if (isCurrentTurn)
                {
                    _turnChevronTransform.localPosition = new Vector3(0f, GetVisualHeadHeight() + 0.10f, 0f);
                }
            }
        }

        public void UpdateStats(int hp, int maxHp, int sp, int maxSp, string statusBadge, bool isCurrentTurn, bool isAlive)
        {
            EnsureHierarchy();

            _isCurrentTurn = isCurrentTurn;
            SetHp(hp, maxHp);
            SetSp(sp, maxSp);

            bool hasBadge = !string.IsNullOrEmpty(statusBadge);
            _statusBadgeLabel.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                _statusBadgeLabel.text = statusBadge;
            }
            LayoutStatusBadge();

            if (isAlive)
            {
                _isAlive = true;
                if (_contactShadowRenderer != null)
                {
                    _contactShadowRenderer.gameObject.SetActive(true);
                }
                _underRingRenderer.gameObject.SetActive(isCurrentTurn);
                if (isCurrentTurn)
                {
                    ApplyRingColor(CurrentTurnGold);
                }
                if (_turnChevronTransform != null)
                {
                    _turnChevronTransform.gameObject.SetActive(isCurrentTurn);
                }
                if (!_isHitFlashing)
                {
                    ApplySpriteTint(Color.white);
                }
                if (_microHudCanvas != null)
                {
                    _microHudCanvas.gameObject.SetActive(_showHud);
                }

                bool showExpanded = _showHud && _nameplateVisible;
                _expandedNameplateRoot.SetActive(showExpanded);
                _worldHudCanvas.gameObject.SetActive(showExpanded);
            }
            else
            {
                _isAlive = false;
                ResetSpriteIdlePose();
                if (_contactShadowRenderer != null)
                {
                    _contactShadowRenderer.gameObject.SetActive(false);
                }
                _underRingRenderer.gameObject.SetActive(false);
                if (_turnChevronTransform != null)
                {
                    _turnChevronTransform.gameObject.SetActive(false);
                }
                if (!_isHitFlashing)
                {
                    ApplySpriteTint(DeadSpriteTint);
                }
                if (_microHudCanvas != null)
                {
                    _microHudCanvas.gameObject.SetActive(false);
                }
                if (_statusBadgeIcon != null)
                {
                    _statusBadgeIcon.gameObject.SetActive(false);
                }
                _expandedNameplateRoot.SetActive(false);
                _worldHudCanvas.gameObject.SetActive(false);
            }
        }

        public void UpdateConditionBadgeIcon(Sprite iconSprite)
        {
            EnsureHierarchy();
            if (_statusBadgeIcon == null)
            {
                return;
            }

            if (iconSprite == null || !_isAlive || !_showHud)
            {
                _statusBadgeIcon.sprite = null;
                _statusBadgeIcon.gameObject.SetActive(false);
                LayoutStatusBadge();
                return;
            }

            _statusBadgeIcon.sprite = iconSprite;
            _statusBadgeIcon.color = Color.white;
            _statusBadgeIcon.gameObject.SetActive(true);
            LayoutStatusBadge();
        }

        private void LayoutStatusBadge()
        {
            if (_statusBadgeLabel == null)
            {
                return;
            }

            bool iconActive = _statusBadgeIcon != null && _statusBadgeIcon.gameObject.activeSelf && _statusBadgeIcon.sprite != null;
            bool labelActive = _statusBadgeLabel.gameObject.activeSelf;

            if (iconActive && labelActive)
            {
                _statusBadgeIcon.rectTransform.anchoredPosition = new Vector2(-58f, 38f);
                _statusBadgeLabel.rectTransform.anchoredPosition = new Vector2(14f, 36f);
            }
            else if (iconActive)
            {
                _statusBadgeIcon.rectTransform.anchoredPosition = new Vector2(0f, 38f);
            }
            else
            {
                _statusBadgeLabel.rectTransform.anchoredPosition = new Vector2(0f, 36f);
            }
        }

        public void PlayAttackLunge(Vector3 targetWorldPos, float lungeDistance = 0.28f, float totalDuration = 0.07f)
        {
            EnsureHierarchy();
            StopLungeCoroutine();

            if (_spriteBillboardTransform == null)
            {
                return;
            }

            float targetDeltaX = ComputeHorizontalDelta(transform.position, targetWorldPos);
            if (Mathf.Abs(targetDeltaX) > 0.02f)
            {
                SetFacingRight(targetDeltaX >= 0f);
            }

            SetAnimState(UnitAnimState.Attack, Mathf.Max(0.42f, totalDuration));

            Vector3 delta = targetWorldPos - transform.position;
            delta.y = 0f;
            Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
            float clampedDistance = Mathf.Max(0f, lungeDistance);

            if (!Application.isPlaying || !gameObject.activeInHierarchy || totalDuration <= 0.001f || clampedDistance <= 0.0001f)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
                return;
            }

            _lungeCoroutine = StartCoroutine(AttackLungeRoutine(dir * clampedDistance, totalDuration));
        }

        public void PlayHitFlash(float duration = 0.055f)
        {
            EnsureHierarchy();
            StopHitFlashCoroutine();

            float hitDuration = Mathf.Max(0.25f, duration);
            if (!Application.isPlaying || !gameObject.activeInHierarchy || duration <= 0.001f)
            {
                _isHitFlashing = false;
                ApplySpriteTint(GetNormalSpriteTint());
                SetAnimState(_moveCoroutine != null ? UnitAnimState.Walk : UnitAnimState.Idle);
                return;
            }

            SetAnimState(UnitAnimState.Hit);
            _isHitFlashing = true;
            ApplyHitFlashTint();
            _hitFlashCoroutine = StartCoroutine(HitFlashRoutine(hitDuration));
        }

        public void ShowFloatingText(string message, Color color)
        {
            ShowArcingFloatingText(message, color, isCrit: false);
        }

        public void ShowArcingFloatingText(string text, Color color, bool isCrit = false)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GameObject floatGo = new GameObject("FloatingText");
            floatGo.transform.position = transform.position + new Vector3(0f, GetVisualHeadHeight() + 0.34f, 0f);
            float baseScale = 0.01f;
            floatGo.transform.localScale = Vector3.one * baseScale;

            Canvas canvas = floatGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = isCrit ? 30 : 25;
            RectTransform canvasRt = floatGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(260f, 64f);

            floatGo.AddComponent<PixelBillboard>();

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(floatGo.transform, false);
            RectTransform textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                tmp.font = _font;
            }
            tmp.text = text;
            tmp.fontSize = isCrit ? 30f : 26f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            if (!Application.isPlaying)
            {
                DestroyImmediate(floatGo);
                return;
            }

            if (gameObject.activeInHierarchy)
            {
                float horizontalDrift = UnityEngine.Random.Range(-0.22f, 0.22f);
                Camera cam = Camera.main;
                Vector3 driftAxis = cam != null ? cam.transform.right : Vector3.right;
                driftAxis.y = 0f;
                if (driftAxis.sqrMagnitude < 0.0001f)
                {
                    driftAxis = Vector3.right;
                }
                else
                {
                    driftAxis.Normalize();
                }

                StartCoroutine(AnimateArcingFloatingText(floatGo, tmp, color, baseScale, driftAxis * horizontalDrift, isCrit));
            }
            else
            {
                Destroy(floatGo, 0.9f);
            }
        }

        private IEnumerator MovePathRoutine(IReadOnlyList<Vector3> points, float stepDuration)
        {
            float totalElapsed = 0f;
            float dustTimer = 0f;
            float dustInterval = Mathf.Max(0.10f, stepDuration * 0.5f);
            EmitDustPuffBurst(transform.position, 2);

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 start = transform.position;
                Vector3 end = points[i];
                float deltaX = ComputeHorizontalDelta(start, end);
                if (Mathf.Abs(deltaX) > 0.02f)
                {
                    SetFacingRight(deltaX >= 0f);
                }

                float elapsed = 0f;
                while (elapsed < stepDuration)
                {
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    totalElapsed += dt;
                    dustTimer += dt;

                    float t = Mathf.Clamp01(elapsed / stepDuration);
                    // Fix #3: keep root transform.position flush on ground elevation so UnderRing and ContactShadow never lift.
                    transform.position = Vector3.Lerp(start, end, t);

                    float stridePhase = totalElapsed * 15.0f;
                    if (_spriteBillboardTransform != null && _lungeCoroutine == null)
                    {
                        float bobY = Mathf.Abs(Mathf.Sin(stridePhase)) * 0.10f;
                        _spriteBillboardTransform.localPosition = new Vector3(0f, bobY, 0f);
                    }

                    if (_spriteRenderer != null)
                    {
                        float walkTiltDeg = Mathf.Sin(stridePhase) * (_hasWalkSheet ? 1.8f : 4.2f);
                        float tiltRad = walkTiltDeg * Mathf.Deg2Rad;
                        float halfH = _spriteHeight * 0.5f;
                        Transform spriteTf = _spriteRenderer.transform;
                        spriteTf.localScale = new Vector3(_spriteWidth, _spriteHeight, 1f);
                        spriteTf.localPosition = new Vector3(-Mathf.Sin(tiltRad) * halfH, Mathf.Cos(tiltRad) * halfH, 0f);
                        spriteTf.localRotation = Quaternion.Euler(0f, 0f, walkTiltDeg);
                    }

                    if (dustTimer >= dustInterval)
                    {
                        dustTimer = 0f;
                        EmitDustPuffBurst(transform.position, 2);
                    }

                    yield return null;
                }

                transform.position = end;
            }

            if (_spriteBillboardTransform != null && _lungeCoroutine == null)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
            }

            ResetSpriteIdlePose();
            _moveCoroutine = null;

            if (_animState == UnitAnimState.Walk)
            {
                SetAnimState(UnitAnimState.Idle);
            }
        }

        private IEnumerator AttackLungeRoutine(Vector3 peakLocalOffset, float totalDuration)
        {
            float halfDuration = Mathf.Max(0.005f, totalDuration * 0.5f);
            float elapsed = 0f;

            while (elapsed < halfDuration && _spriteBillboardTransform != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                float eased = 1f - ((1f - t) * (1f - t));
                _spriteBillboardTransform.localPosition = peakLocalOffset * eased;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < halfDuration && _spriteBillboardTransform != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                _spriteBillboardTransform.localPosition = Vector3.Lerp(peakLocalOffset, Vector3.zero, t);
                yield return null;
            }

            if (_spriteBillboardTransform != null)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
            }

            _lungeCoroutine = null;
        }

        private IEnumerator HitFlashRoutine(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _isHitFlashing = false;
            _hitFlashCoroutine = null;
            ApplySpriteTint(GetNormalSpriteTint());

            if (_animState == UnitAnimState.Hit)
            {
                SetAnimState(_moveCoroutine != null ? UnitAnimState.Walk : UnitAnimState.Idle);
            }
        }

        private static IEnumerator AnimateArcingFloatingText(
            GameObject floatGo,
            TextMeshProUGUI tmp,
            Color baseColor,
            float baseScale,
            Vector3 horizontalOffset,
            bool isCrit)
        {
            float duration = isCrit ? 1.05f : 0.88f;
            float initialVy = isCrit ? 2.85f : 2.35f;
            float gravity = isCrit ? 4.4f : 3.9f;
            float popPeak = isCrit ? 1.35f : 1.14f;
            float settleScale = isCrit ? 1.12f : 1.0f;

            Vector3 startPos = floatGo.transform.position;
            float elapsed = 0f;

            while (elapsed < duration && floatGo != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float arcY = (initialVy * elapsed) - (0.5f * gravity * elapsed * elapsed);
                Vector3 drift = horizontalOffset * t;
                floatGo.transform.position = startPos + drift + new Vector3(0f, arcY, 0f);

                float scaleMult;
                if (elapsed < 0.09f)
                {
                    float popT = elapsed / 0.09f;
                    scaleMult = Mathf.Lerp(0.85f, popPeak, popT);
                }
                else if (elapsed < 0.22f)
                {
                    float settleT = (elapsed - 0.09f) / 0.13f;
                    scaleMult = Mathf.Lerp(popPeak, settleScale, settleT);
                }
                else
                {
                    scaleMult = settleScale;
                }
                floatGo.transform.localScale = Vector3.one * (baseScale * scaleMult);

                Color c = baseColor;
                float fadeT = t > 0.55f ? (t - 0.55f) / 0.45f : 0f;
                c.a = Mathf.Lerp(1f, 0f, fadeT * fadeT);
                tmp.color = c;

                yield return null;
            }

            if (floatGo != null)
            {
                Destroy(floatGo);
            }
        }

        private void StopMoveCoroutine()
        {
            if (_moveCoroutine != null)
            {
                StopCoroutine(_moveCoroutine);
                _moveCoroutine = null;
                if (_spriteBillboardTransform != null && _lungeCoroutine == null)
                {
                    _spriteBillboardTransform.localPosition = Vector3.zero;
                }
                ResetSpriteIdlePose();
                if (_animState == UnitAnimState.Walk)
                {
                    SetAnimState(UnitAnimState.Idle);
                }
            }
        }

        private void StopLungeCoroutine()
        {
            if (_lungeCoroutine != null)
            {
                StopCoroutine(_lungeCoroutine);
                _lungeCoroutine = null;
            }

            if (_spriteBillboardTransform != null)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
            }
        }

        private void StopHitFlashCoroutine()
        {
            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = null;
            }

            _isHitFlashing = false;
            if (_animState == UnitAnimState.Hit)
            {
                SetAnimState(_moveCoroutine != null ? UnitAnimState.Walk : UnitAnimState.Idle);
            }
        }

        private Color GetNormalSpriteTint()
        {
            return _isAlive ? Color.white : DeadSpriteTint;
        }

        private void ResetSpriteIdlePose()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            Transform spriteTf = _spriteRenderer.transform;
            spriteTf.localPosition = new Vector3(0f, _spriteHeight * 0.5f, 0f);
            spriteTf.localScale = new Vector3(_spriteWidth, _spriteHeight, 1f);
            spriteTf.localRotation = Quaternion.identity;
        }

        private static float ComputeDeterministicPhase(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return 0f;
            }

            int hash = 17;
            for (int i = 0; i < unitId.Length; i++)
            {
                hash = unchecked(hash * 31 + unitId[i]);
            }

            return (hash & 0xFFFF) * (Mathf.PI * 2f / 65536f);
        }

        private void ApplyRingColor(Color color)
        {
            if (_underRingRenderer == null)
            {
                return;
            }

            _ringPropBlock ??= new MaterialPropertyBlock();
            _ringPropBlock.Clear();
            _ringPropBlock.SetColor(BaseColorId, color);
            _ringPropBlock.SetColor(ColorId, color);
            _underRingRenderer.SetPropertyBlock(_ringPropBlock);
        }

        private void ApplySpriteTint(Color color)
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            _spritePropBlock ??= new MaterialPropertyBlock();
            _spritePropBlock.Clear();
            _spritePropBlock.SetColor(BaseColorId, color);
            _spritePropBlock.SetColor(ColorId, color);
            _spritePropBlock.SetColor(TintColorId, color);
            _spritePropBlock.SetFloat(WindPhaseId, _windPhaseNorm);
            _spriteRenderer.SetPropertyBlock(_spritePropBlock);
        }

        private void ApplyHitFlashTint()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            _spritePropBlock ??= new MaterialPropertyBlock();
            _spritePropBlock.Clear();
            _spritePropBlock.SetColor(BaseColorId, HitFlashGoldWhite);
            _spritePropBlock.SetColor(ColorId, HitFlashGoldWhite);
            _spritePropBlock.SetColor(TintColorId, HitFlashGoldWhite * 1.45f);
            _spritePropBlock.SetFloat(WindPhaseId, _windPhaseNorm);

            Material mat = _spriteRenderer.sharedMaterial;
            if (mat != null && mat.HasProperty(EmissionColorId))
            {
                _spritePropBlock.SetColor(EmissionColorId, new Color(1.0f, 0.92f, 0.55f, 1.0f) * 0.65f);
            }

            _spriteRenderer.SetPropertyBlock(_spritePropBlock);
        }

        private void EnsureHierarchy()
        {
            if (_spriteRenderer != null)
            {
                return;
            }

            // 1. SpriteBillboard (foot-anchored Y-axis billboard) -> SpriteQuad (animated child)
            GameObject billboardGo = new GameObject("SpriteBillboard");
            billboardGo.transform.SetParent(transform, false);
            billboardGo.transform.localPosition = Vector3.zero;
            billboardGo.AddComponent<PixelBillboard>();
            _spriteBillboardTransform = billboardGo.transform;

            GameObject spriteGo = new GameObject("SpriteQuad", typeof(MeshFilter), typeof(MeshRenderer));
            spriteGo.transform.SetParent(_spriteBillboardTransform, false);
            spriteGo.transform.localPosition = new Vector3(0f, _spriteHeight * 0.5f, 0f);
            spriteGo.transform.localScale = new Vector3(_spriteWidth, _spriteHeight, 1f);
            spriteGo.GetComponent<MeshFilter>().sharedMesh = GetOrCreateSharedUnitQuadMesh();
            _spriteRenderer = spriteGo.GetComponent<MeshRenderer>();
            _spriteRenderer.shadowCastingMode = ShadowCastingMode.On;
            _spriteRenderer.receiveShadows = true;

            // 2. ContactShadow (soft dark oval beneath living unit)
            GameObject shadowGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowGo.name = "ContactShadow";
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowGo.transform.localScale = new Vector3(0.92f, 0.56f, 1f);
            DestroyCollider(shadowGo);
            _contactShadowRenderer = shadowGo.GetComponent<MeshRenderer>();
            _contactShadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _contactShadowRenderer.receiveShadows = false;
            _contactShadowRenderer.sharedMaterial = GetOrCreateSharedShadowMaterial();

            // 3. UnderRing (thin gold ring shown ONLY on active turn)
            GameObject ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "UnderRing";
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
            DestroyCollider(ringGo);
            _underRingRenderer = ringGo.GetComponent<MeshRenderer>();
            _underRingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _underRingRenderer.receiveShadows = false;
            _underRingRenderer.sharedMaterial = GetOrCreateSharedRingMaterial();
            ringGo.SetActive(false);

            float headHeight = GetVisualHeadHeight();

            // 4. TurnChevron (glowing gold diamond/chevron above active unit)
            GameObject chevronGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            chevronGo.name = "TurnChevron";
            chevronGo.transform.SetParent(transform, false);
            chevronGo.transform.localPosition = new Vector3(0f, headHeight + 0.10f, 0f);
            chevronGo.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
            DestroyCollider(chevronGo);
            chevronGo.AddComponent<PixelBillboard>();
            _turnChevronTransform = chevronGo.transform;
            _turnChevronRenderer = chevronGo.GetComponent<MeshRenderer>();
            _turnChevronRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _turnChevronRenderer.receiveShadows = false;
            _turnChevronRenderer.sharedMaterial = GetOrCreateSharedChevronMaterial();
            chevronGo.SetActive(false);

            // 5. MicroHud (foot-anchored ultra-slim HP bar)
            GameObject microHudGo = new GameObject("MicroHud");
            microHudGo.transform.SetParent(transform, false);
            microHudGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            microHudGo.transform.localScale = Vector3.one * 0.0068f;
            _microHudCanvas = microHudGo.AddComponent<Canvas>();
            _microHudCanvas.renderMode = RenderMode.WorldSpace;
            _microHudCanvas.sortingOrder = 16;
            RectTransform microRt = microHudGo.GetComponent<RectTransform>();
            microRt.sizeDelta = new Vector2(110f, 10f);
            microHudGo.AddComponent<PixelBillboard>();

            _hpFill = CreateSlimBar(microHudGo.transform, "HpBar", Vector2.zero, new Vector2(92f, 5f), AllyHpBlue);

            // 6. WorldHud (hover/Alt expanded nameplate, hidden by default)
            EnsureWorldHud(headHeight);
        }

        private void EnsureWorldHud(float headHeight)
        {
            if (_worldHudCanvas != null)
            {
                return;
            }

            GameObject hudGo = new GameObject("WorldHud");
            hudGo.transform.SetParent(transform, false);
            hudGo.transform.localPosition = new Vector3(0f, headHeight + 0.16f, 0f);
            hudGo.transform.localScale = Vector3.one * 0.0074f;
            _worldHudCanvas = hudGo.AddComponent<Canvas>();
            _worldHudCanvas.renderMode = RenderMode.WorldSpace;
            _worldHudCanvas.sortingOrder = 17;
            RectTransform hudRt = hudGo.GetComponent<RectTransform>();
            hudRt.sizeDelta = new Vector2(210f, 74f);
            hudGo.AddComponent<PixelBillboard>();

            _expandedNameplateRoot = new GameObject("ExpandedNameplate");
            _expandedNameplateRoot.transform.SetParent(hudGo.transform, false);
            RectTransform expRt = _expandedNameplateRoot.AddComponent<RectTransform>();
            expRt.anchorMin = Vector2.zero;
            expRt.anchorMax = Vector2.one;
            expRt.offsetMin = Vector2.zero;
            expRt.offsetMax = Vector2.zero;

            // Dark slate nameplate pill with subtle bronze accent edge for crisp readability
            GameObject pillEdgeGo = new GameObject("NamePillEdge");
            pillEdgeGo.transform.SetParent(_expandedNameplateRoot.transform, false);
            RectTransform pillEdgeRt = pillEdgeGo.AddComponent<RectTransform>();
            pillEdgeRt.anchorMin = new Vector2(0.5f, 0.5f);
            pillEdgeRt.anchorMax = new Vector2(0.5f, 0.5f);
            pillEdgeRt.anchoredPosition = new Vector2(0f, 10f);
            pillEdgeRt.sizeDelta = new Vector2(172f, 38f);
            Image pillEdgeImg = pillEdgeGo.AddComponent<Image>();
            pillEdgeImg.color = new Color(0.76f, 0.58f, 0.28f, 0.85f);

            GameObject pillGo = new GameObject("NamePill");
            pillGo.transform.SetParent(pillEdgeGo.transform, false);
            RectTransform pillRt = pillGo.AddComponent<RectTransform>();
            pillRt.anchorMin = Vector2.zero;
            pillRt.anchorMax = Vector2.one;
            pillRt.offsetMin = new Vector2(1.5f, 1.5f);
            pillRt.offsetMax = new Vector2(-1.5f, -1.5f);
            Image pillImg = pillGo.AddComponent<Image>();
            pillImg.color = new Color(0.06f, 0.08f, 0.14f, 0.92f);

            // Nameplate
            _nameLabel = CreateTmpLabel(_expandedNameplateRoot.transform, "NameLabel", new Vector2(0f, 18f), new Vector2(188f, 22f), 18f, Color.white);
            _nameLabel.fontStyle = FontStyles.Bold;

            // Optional detail line (HP / AC / COND)
            _detailLabel = CreateTmpLabel(_expandedNameplateRoot.transform, "DetailText", new Vector2(0f, 1f), new Vector2(196f, 16f), 13f, new Color(0.84f, 0.90f, 0.96f, 1f));
            _detailLabel.gameObject.SetActive(false);

            // SP bar
            _spFill = CreateBar(_expandedNameplateRoot.transform, "SpBar", new Vector2(0f, -14f), new Vector2(116f, 6f), new Color(0.18f, 0.78f, 0.96f, 1f), out _spBarRoot);

            // Condition badge icon (32x32) beside label
            GameObject iconGo = new GameObject("ConditionBadgeIcon");
            iconGo.transform.SetParent(_expandedNameplateRoot.transform, false);
            RectTransform iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(-58f, 38f);
            iconRt.sizeDelta = new Vector2(32f, 32f);
            _statusBadgeIcon = iconGo.AddComponent<Image>();
            _statusBadgeIcon.preserveAspect = true;
            _statusBadgeIcon.raycastTarget = false;
            iconGo.SetActive(false);

            // Condition badge
            _statusBadgeLabel = CreateTmpLabel(_expandedNameplateRoot.transform, "ConditionBadge", new Vector2(0f, 36f), new Vector2(176f, 20f), 14f, new Color(0.98f, 0.84f, 0.22f, 1f));
            _statusBadgeLabel.fontStyle = FontStyles.Bold;
            _statusBadgeLabel.gameObject.SetActive(false);

            _expandedNameplateRoot.SetActive(false);
            hudGo.SetActive(false);
        }

        private static Image CreateSlimBar(
            Transform parent,
            string barName,
            Vector2 anchoredPos,
            Vector2 size,
            Color fillColor)
        {
            GameObject bgGo = new GameObject(barName);
            bgGo.transform.SetParent(parent, false);
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = anchoredPos;
            bgRt.sizeDelta = size;

            Image background = bgGo.AddComponent<Image>();
            background.color = new Color(0.03f, 0.04f, 0.07f, 0.72f);

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(bgGo.transform, false);
            RectTransform fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(0.5f, 0.5f);
            fillRt.offsetMax = new Vector2(-0.5f, -0.5f);

            Image fill = fillGo.AddComponent<Image>();
            fill.color = fillColor;
            return fill;
        }

        private static TextMeshProUGUI CreateTmpLabel(
            Transform parent,
            string labelName,
            Vector2 anchoredPos,
            Vector2 size,
            float fontSize,
            Color color)
        {
            GameObject go = new GameObject(labelName);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }

        private static Image CreateBar(
            Transform parent,
            string barName,
            Vector2 anchoredPos,
            Vector2 size,
            Color fillColor,
            out Image background)
        {
            GameObject bgGo = new GameObject(barName);
            bgGo.transform.SetParent(parent, false);
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = anchoredPos;
            bgRt.sizeDelta = size;

            background = bgGo.AddComponent<Image>();
            background.color = new Color(0.06f, 0.07f, 0.11f, 0.85f);

            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(bgGo.transform, false);
            RectTransform fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);

            Image fill = fillGo.AddComponent<Image>();
            fill.color = fillColor;
            return fill;
        }

        private static Mesh GetOrCreateSharedUnitQuadMesh()
        {
            if (_sharedUnitQuadMesh != null)
            {
                return _sharedUnitQuadMesh;
            }

            _sharedUnitQuadMesh = new Mesh
            {
                name = "SharedUnitBillboardQuad"
            };
            _sharedUnitQuadMesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            });
            _sharedUnitQuadMesh.SetNormals(new[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            });
            Vector2[] uvs =
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            _sharedUnitQuadMesh.SetUVs(0, uvs);
            _sharedUnitQuadMesh.SetUVs(1, uvs);
            _sharedUnitQuadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _sharedUnitQuadMesh.RecalculateBounds();
            return _sharedUnitQuadMesh;
        }

        private static Material CreateSharedTransparentMaterial(string matName, Texture2D tex, Color baseColor)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader);
            mat.name = matName;
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
            }
            if (mat.HasProperty(BaseColorId))
            {
                mat.SetColor(BaseColorId, baseColor);
            }
            if (mat.HasProperty(ColorId))
            {
                mat.SetColor(ColorId, baseColor);
            }
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            return mat;
        }

        private static Material GetOrCreateSharedRingMaterial()
        {
            if (_sharedRingMaterial != null)
            {
                return _sharedRingMaterial;
            }

            if (_sharedRingTexture == null)
            {
                _sharedRingTexture = CreateRingTexture(64);
            }

            _sharedRingMaterial = CreateSharedTransparentMaterial("SharedUnitUnderRing", _sharedRingTexture, Color.white);
            return _sharedRingMaterial;
        }

        private static Material GetOrCreateSharedShadowMaterial()
        {
            if (_sharedShadowMaterial != null)
            {
                return _sharedShadowMaterial;
            }

            if (_sharedShadowTexture == null)
            {
                _sharedShadowTexture = CreateContactShadowTexture(64);
            }

            _sharedShadowMaterial = CreateSharedTransparentMaterial("SharedUnitContactShadow", _sharedShadowTexture, ContactShadowColor);
            return _sharedShadowMaterial;
        }

        private static Material GetOrCreateSharedChevronMaterial()
        {
            if (_sharedChevronMaterial != null)
            {
                return _sharedChevronMaterial;
            }

            if (_sharedChevronTexture == null)
            {
                _sharedChevronTexture = CreateTurnChevronTexture(32);
            }

            _sharedChevronMaterial = CreateSharedTransparentMaterial("SharedUnitTurnChevron", _sharedChevronTexture, Color.white);
            return _sharedChevronMaterial;
        }

        private static Texture2D CreateRingTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (size - 1) * 0.5f;
            float outer = half * 0.94f;
            float inner = half * 0.82f;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half;
                    float dy = y - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 0f;
                    if (dist <= outer && dist >= inner)
                    {
                        float edgeOut = Mathf.Clamp01((outer - dist) * 0.9f);
                        float edgeIn = Mathf.Clamp01((dist - inner) * 0.9f);
                        alpha = Mathf.Min(edgeOut, edgeIn);
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateContactShadowTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (size - 1) * 0.5f;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) / half;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateTurnChevronTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color outline = new Color(0.14f, 0.10f, 0.04f, 0.95f);
            Color goldCore = new Color(0.98f, 0.84f, 0.16f, 1.0f);
            Color goldBright = new Color(1.00f, 0.95f, 0.55f, 1.0f);

            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) / half; // -1 bottom, +1 top
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x - half) / half);
                    // Crisp downward-pointing diamond/chevron
                    float diamondDist = nx * 1.18f + Mathf.Abs(ny - 0.05f) * 1.05f;
                    if (diamondDist <= 0.62f)
                    {
                        pixels[y * size + x] = diamondDist <= 0.30f ? goldBright : goldCore;
                    }
                    else if (diamondDist <= 0.78f)
                    {
                        pixels[y * size + x] = outline;
                    }
                    else
                    {
                        pixels[y * size + x] = clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static float ComputeHorizontalDelta(Vector3 fromWorld, Vector3 toWorld)
        {
            Vector3 delta = toWorld - fromWorld;
            delta.y = 0f;
            Camera cam = Camera.main;
            Vector3 rightAxis = cam != null ? cam.transform.right : Vector3.right;
            rightAxis.y = 0f;
            if (rightAxis.sqrMagnitude < 0.0001f)
            {
                rightAxis = Vector3.right;
            }
            else
            {
                rightAxis.Normalize();
            }

            return Vector3.Dot(delta, rightAxis);
        }

        private void EmitDustPuffBurst(Vector3 footWorldPos, int count)
        {
            if (!Application.isPlaying || !gameObject.activeInHierarchy || count <= 0)
            {
                return;
            }

            Material dustMat = GetOrCreateSharedDustMaterial();
            if (dustMat == null)
            {
                return;
            }

            if (!_registeredDustUser)
            {
                _registeredDustUser = true;
                _dustMaterialRefCount++;
            }

            Mesh quadMesh = GetOrCreateSharedUnitQuadMesh();
            for (int i = 0; i < count; i++)
            {
                int slotIndex = AcquireDustPuffSlot(quadMesh, dustMat);
                if (slotIndex < 0)
                {
                    break;
                }

                DustPuffInstance puff = _dustPuffs[slotIndex];
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(0.06f, 0.22f);
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                puff.StartPos = footWorldPos + radial * radius + new Vector3(0f, 0.04f, 0f);
                puff.Velocity = radial * UnityEngine.Random.Range(0.24f, 0.52f) + new Vector3(0f, UnityEngine.Random.Range(0.18f, 0.36f), 0f);
                puff.GroundY = footWorldPos.y;
                puff.Age = 0f;
                puff.Lifetime = UnityEngine.Random.Range(0.24f, 0.34f);
                puff.Active = true;

                puff.Root.transform.position = puff.StartPos;
                puff.Root.transform.localScale = Vector3.one * 0.16f;
                puff.Root.SetActive(true);
                ApplyDustPuffColor(puff.Renderer, new Color(0.76f, 0.70f, 0.58f, 0.50f));
                _dustPuffs[slotIndex] = puff;
            }
        }

        private int AcquireDustPuffSlot(Mesh quadMesh, Material dustMat)
        {
            for (int i = 0; i < _dustPuffs.Count; i++)
            {
                if (!_dustPuffs[i].Active && _dustPuffs[i].Root != null)
                {
                    return i;
                }
            }

            if (_dustPuffs.Count >= 8)
            {
                int oldestIndex = 0;
                float oldestAge = -1f;
                for (int i = 0; i < _dustPuffs.Count; i++)
                {
                    if (_dustPuffs[i].Root != null && _dustPuffs[i].Age > oldestAge)
                    {
                        oldestAge = _dustPuffs[i].Age;
                        oldestIndex = i;
                    }
                }
                return _dustPuffs[oldestIndex].Root != null ? oldestIndex : -1;
            }

            GameObject go = new GameObject("FootstepDustPuff", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = quadMesh;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = dustMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<PixelBillboard>();
            go.SetActive(false);

            _dustPuffs.Add(new DustPuffInstance
            {
                Root = go,
                Renderer = mr,
                Active = false
            });
            return _dustPuffs.Count - 1;
        }

        private void UpdateDustPuffs(float dt)
        {
            if (_dustPuffs.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _dustPuffs.Count; i++)
            {
                DustPuffInstance puff = _dustPuffs[i];
                if (!puff.Active || puff.Root == null)
                {
                    continue;
                }

                puff.Age += dt;
                if (puff.Age >= puff.Lifetime)
                {
                    puff.Active = false;
                    puff.Root.SetActive(false);
                    _dustPuffs[i] = puff;
                    continue;
                }

                float t = Mathf.Clamp01(puff.Age / Mathf.Max(0.01f, puff.Lifetime));
                Vector3 pos = puff.StartPos + puff.Velocity * puff.Age;
                if (pos.y < puff.GroundY + 0.02f)
                {
                    pos.y = puff.GroundY + 0.02f;
                }
                puff.Root.transform.position = pos;

                float scale = Mathf.Lerp(0.16f, 0.34f, t);
                puff.Root.transform.localScale = Vector3.one * scale;
                float alpha = Mathf.Lerp(0.50f, 0f, t * t);
                ApplyDustPuffColor(puff.Renderer, new Color(0.76f, 0.70f, 0.58f, alpha));
                _dustPuffs[i] = puff;
            }
        }

        private void ApplyDustPuffColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            _dustPropBlock ??= new MaterialPropertyBlock();
            _dustPropBlock.Clear();
            _dustPropBlock.SetColor(BaseColorId, color);
            _dustPropBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_dustPropBlock);
        }

        private static Material GetOrCreateSharedDustMaterial()
        {
            if (_sharedDustMaterial != null)
            {
                return _sharedDustMaterial;
            }

            if (_sharedDustTexture == null)
            {
                _sharedDustTexture = CreateDustPuffTexture(16);
            }

            _sharedDustMaterial = CreateSharedTransparentMaterial("SharedUnitFootstepDust", _sharedDustTexture, Color.white);
            return _sharedDustMaterial;
        }

        private static Texture2D CreateDustPuffTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (size - 1) * 0.5f;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - half) / half;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha *= alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDisable()
        {
            for (int i = 0; i < _dustPuffs.Count; i++)
            {
                DustPuffInstance puff = _dustPuffs[i];
                if (puff.Root != null)
                {
                    puff.Root.SetActive(false);
                }
                puff.Active = false;
                _dustPuffs[i] = puff;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _dustPuffs.Count; i++)
            {
                GameObject puffGo = _dustPuffs[i].Root;
                if (puffGo != null)
                {
                    DestroyRuntimeObject(puffGo);
                }
            }
            _dustPuffs.Clear();

            if (_registeredDustUser)
            {
                _registeredDustUser = false;
                _dustMaterialRefCount = Mathf.Max(0, _dustMaterialRefCount - 1);
                if (_dustMaterialRefCount == 0)
                {
                    if (_sharedDustMaterial != null)
                    {
                        DestroyRuntimeObject(_sharedDustMaterial);
                        _sharedDustMaterial = null;
                    }
                    if (_sharedDustTexture != null)
                    {
                        DestroyRuntimeObject(_sharedDustTexture);
                        _sharedDustTexture = null;
                    }
                }
            }
        }

        private static void DestroyRuntimeObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(col);
                }
                else
                {
                    DestroyImmediate(col);
                }
            }
        }
    }
}
