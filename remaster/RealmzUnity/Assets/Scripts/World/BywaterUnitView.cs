using System.Collections;
using System.Collections.Generic;
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
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color CurrentTurnGold = new Color(0.980f, 0.800f, 0.082f, 0.95f); // #FACC15
        private static readonly Color HitFlashGoldWhite = new Color(1.0f, 0.953f, 0.690f, 1.0f); // #FFF3B0
        private static readonly Color DeadSpriteTint = new Color(0.45f, 0.45f, 0.50f, 0.55f);

        private static Material _sharedRingMaterial;
        private static Texture2D _sharedRingTexture;

        private MeshRenderer _spriteRenderer;
        private Transform _spriteBillboardTransform;
        private MeshRenderer _underRingRenderer;
        private Canvas _worldHudCanvas;
        private TextMeshProUGUI _nameLabel;
        private Image _hpFill;
        private Image _spBarRoot;
        private Image _spFill;
        private TextMeshProUGUI _statusBadgeLabel;

        private MaterialPropertyBlock _ringPropBlock;
        private MaterialPropertyBlock _spritePropBlock;

        private TMP_FontAsset _font;
        private Color _baseRingColor = Color.white;
        private float _spriteSize = 1.42f;
        private bool _showHud = true;
        private bool _isAlive = true;
        private bool _isHitFlashing;
        private float _idlePhase;
        private Coroutine _moveCoroutine;
        private Coroutine _lungeCoroutine;
        private Coroutine _hitFlashCoroutine;

        public string UnitId { get; private set; }

        public void Configure(
            string unitId,
            string displayName,
            Material billboardMat,
            Color ringColor,
            TMP_FontAsset font,
            float spriteSize = 1.42f,
            bool showHud = true)
        {
            UnitId = unitId ?? string.Empty;
            _baseRingColor = ringColor;
            _font = font;
            _spriteSize = Mathf.Max(0.4f, spriteSize);
            _showHud = showHud;
            _isAlive = true;
            _isHitFlashing = false;
            _idlePhase = ComputeDeterministicPhase(UnitId);

            StopLungeCoroutine();
            StopHitFlashCoroutine();
            EnsureHierarchy();

            ResetSpriteIdlePose();
            if (_spriteBillboardTransform != null)
            {
                _spriteBillboardTransform.localPosition = Vector3.zero;
            }

            _spriteRenderer.sharedMaterial = billboardMat;
            ApplySpriteTint(Color.white);

            ApplyRingColor(_baseRingColor);

            _worldHudCanvas.transform.localPosition = new Vector3(0f, _spriteSize + 0.18f, 0f);
            _worldHudCanvas.gameObject.SetActive(showHud);

            if (_font != null)
            {
                _nameLabel.font = _font;
                _statusBadgeLabel.font = _font;
            }

            _nameLabel.text = displayName ?? string.Empty;
            _statusBadgeLabel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            if (!_isAlive || _moveCoroutine != null || _lungeCoroutine != null)
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
            float halfHeight = _spriteSize * 0.5f * breathScale;

            Transform spriteTf = _spriteRenderer.transform;
            spriteTf.localScale = new Vector3(_spriteSize, _spriteSize * breathScale, 1f);
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

            if (!Application.isPlaying || !gameObject.activeInHierarchy || stepDuration <= 0.001f)
            {
                transform.position = worldPoints[worldPoints.Count - 1];
                return;
            }

            _moveCoroutine = StartCoroutine(MovePathRoutine(worldPoints, stepDuration));
        }

        public void SetHp(int hp, int maxHp)
        {
            EnsureHierarchy();
            float safeMaxHp = Mathf.Max(1, maxHp);
            float hpRatio = Mathf.Clamp01(hp / safeMaxHp);
            _hpFill.rectTransform.anchorMax = new Vector2(hpRatio, 1f);
            _hpFill.color = hpRatio > 0.35f
                ? new Color(0.20f, 0.82f, 0.36f, 1f)
                : new Color(0.92f, 0.24f, 0.20f, 1f);
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
            if (!_isAlive)
            {
                _underRingRenderer.gameObject.SetActive(false);
                return;
            }

            _underRingRenderer.gameObject.SetActive(true);
            ApplyRingColor(isCurrentTurn ? CurrentTurnGold : _baseRingColor);
        }

        public void UpdateStats(int hp, int maxHp, int sp, int maxSp, string statusBadge, bool isCurrentTurn, bool isAlive)
        {
            EnsureHierarchy();

            SetHp(hp, maxHp);
            SetSp(sp, maxSp);

            bool hasBadge = !string.IsNullOrEmpty(statusBadge);
            _statusBadgeLabel.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                _statusBadgeLabel.text = statusBadge;
            }

            if (isAlive)
            {
                _isAlive = true;
                _underRingRenderer.gameObject.SetActive(true);
                ApplyRingColor(isCurrentTurn ? CurrentTurnGold : _baseRingColor);
                if (!_isHitFlashing)
                {
                    ApplySpriteTint(Color.white);
                }
                _worldHudCanvas.gameObject.SetActive(_showHud);
            }
            else
            {
                _isAlive = false;
                ResetSpriteIdlePose();
                _underRingRenderer.gameObject.SetActive(false);
                if (!_isHitFlashing)
                {
                    ApplySpriteTint(DeadSpriteTint);
                }
                _worldHudCanvas.gameObject.SetActive(false);
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

            if (!Application.isPlaying || !gameObject.activeInHierarchy || duration <= 0.001f)
            {
                _isHitFlashing = false;
                ApplySpriteTint(GetNormalSpriteTint());
                return;
            }

            _isHitFlashing = true;
            ApplyHitFlashTint();
            _hitFlashCoroutine = StartCoroutine(HitFlashRoutine(duration));
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
            floatGo.transform.position = transform.position + new Vector3(0f, _spriteSize + 0.34f, 0f);
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
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 start = transform.position;
                Vector3 end = points[i];
                float elapsed = 0f;

                while (elapsed < stepDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / stepDuration);
                    Vector3 pos = Vector3.Lerp(start, end, t);
                    pos.y += Mathf.Sin(t * Mathf.PI) * 0.14f;
                    transform.position = pos;
                    yield return null;
                }

                transform.position = end;
            }

            _moveCoroutine = null;
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
            spriteTf.localPosition = new Vector3(0f, _spriteSize * 0.5f, 0f);
            spriteTf.localScale = new Vector3(_spriteSize, _spriteSize, 1f);
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

            GameObject spriteGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spriteGo.name = "SpriteQuad";
            spriteGo.transform.SetParent(_spriteBillboardTransform, false);
            spriteGo.transform.localPosition = new Vector3(0f, _spriteSize * 0.5f, 0f);
            spriteGo.transform.localScale = new Vector3(_spriteSize, _spriteSize, 1f);
            DestroyCollider(spriteGo);
            _spriteRenderer = spriteGo.GetComponent<MeshRenderer>();
            _spriteRenderer.shadowCastingMode = ShadowCastingMode.On;
            _spriteRenderer.receiveShadows = true;

            // 2. UnderRing
            GameObject ringGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringGo.name = "UnderRing";
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = new Vector3(0.86f, 0.86f, 1f);
            DestroyCollider(ringGo);
            _underRingRenderer = ringGo.GetComponent<MeshRenderer>();
            _underRingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _underRingRenderer.receiveShadows = false;
            _underRingRenderer.sharedMaterial = GetOrCreateSharedRingMaterial();

            // 3. WorldHud
            GameObject hudGo = new GameObject("WorldHud");
            hudGo.transform.SetParent(transform, false);
            hudGo.transform.localPosition = new Vector3(0f, _spriteSize + 0.18f, 0f);
            hudGo.transform.localScale = Vector3.one * 0.0074f;
            _worldHudCanvas = hudGo.AddComponent<Canvas>();
            _worldHudCanvas.renderMode = RenderMode.WorldSpace;
            _worldHudCanvas.sortingOrder = 15;
            RectTransform hudRt = hudGo.GetComponent<RectTransform>();
            hudRt.sizeDelta = new Vector2(180f, 56f);
            hudGo.AddComponent<PixelBillboard>();

            // Dark slate nameplate pill with subtle bronze accent edge for crisp readability
            GameObject pillEdgeGo = new GameObject("NamePillEdge");
            pillEdgeGo.transform.SetParent(hudGo.transform, false);
            RectTransform pillEdgeRt = pillEdgeGo.AddComponent<RectTransform>();
            pillEdgeRt.anchorMin = new Vector2(0.5f, 0.5f);
            pillEdgeRt.anchorMax = new Vector2(0.5f, 0.5f);
            pillEdgeRt.anchoredPosition = new Vector2(0f, 15f);
            pillEdgeRt.sizeDelta = new Vector2(144f, 26f);
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
            pillImg.color = new Color(0.06f, 0.08f, 0.14f, 0.90f);

            // Nameplate
            _nameLabel = CreateTmpLabel(hudGo.transform, "NameLabel", new Vector2(0f, 15f), new Vector2(166f, 24f), 19f, Color.white);
            _nameLabel.fontStyle = FontStyles.Bold;

            // HP bar
            _hpFill = CreateBar(hudGo.transform, "HpBar", new Vector2(0f, -1f), new Vector2(108f, 9f), new Color(0.20f, 0.82f, 0.36f, 1f), out _);

            // SP bar
            _spFill = CreateBar(hudGo.transform, "SpBar", new Vector2(0f, -10f), new Vector2(108f, 6f), new Color(0.18f, 0.78f, 0.96f, 1f), out _spBarRoot);

            // Condition badge
            _statusBadgeLabel = CreateTmpLabel(hudGo.transform, "ConditionBadge", new Vector2(0f, 34f), new Vector2(176f, 20f), 15f, new Color(0.98f, 0.84f, 0.22f, 1f));
            _statusBadgeLabel.fontStyle = FontStyles.Bold;
            _statusBadgeLabel.gameObject.SetActive(false);
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
            mat.name = "SharedUnitUnderRing";
            mat.mainTexture = _sharedRingTexture;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", _sharedRingTexture);
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

            _sharedRingMaterial = mat;
            return _sharedRingMaterial;
        }

        private static Texture2D CreateRingTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (size - 1) * 0.5f;
            float outer = half * 0.94f;
            float inner = half * 0.72f;

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
