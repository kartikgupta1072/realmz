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
    /// stepped path coroutine animation, and rising floating combat text.
    /// </summary>
    public sealed class BywaterUnitView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color CurrentTurnGold = new Color(0.980f, 0.800f, 0.082f, 0.95f); // #FACC15

        private static Material _sharedRingMaterial;
        private static Texture2D _sharedRingTexture;

        private MeshRenderer _spriteRenderer;
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
        private Coroutine _moveCoroutine;

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

            EnsureHierarchy();

            _spriteRenderer.transform.localPosition = new Vector3(0f, _spriteSize * 0.5f, 0f);
            _spriteRenderer.transform.localScale = new Vector3(_spriteSize, _spriteSize, 1f);
            _spriteRenderer.sharedMaterial = billboardMat;
            ApplySpriteTint(Color.white);

            ApplyRingColor(_baseRingColor);

            _worldHudCanvas.transform.localPosition = new Vector3(0f, _spriteSize + 0.16f, 0f);
            _worldHudCanvas.gameObject.SetActive(showHud);

            if (_font != null)
            {
                _nameLabel.font = _font;
                _statusBadgeLabel.font = _font;
            }

            _nameLabel.text = displayName ?? string.Empty;
            _statusBadgeLabel.gameObject.SetActive(false);
        }

        public void SetWorldPosition(Vector3 pos)
        {
            StopMoveCoroutine();
            transform.position = pos;
        }

        public void MoveAlongWorldPath(IReadOnlyList<Vector3> worldPoints, float stepDuration)
        {
            StopMoveCoroutine();

            if (worldPoints == null || worldPoints.Count == 0)
            {
                return;
            }

            if (!gameObject.activeInHierarchy || stepDuration <= 0.001f)
            {
                transform.position = worldPoints[worldPoints.Count - 1];
                return;
            }

            _moveCoroutine = StartCoroutine(MovePathRoutine(worldPoints, stepDuration));
        }

        public void UpdateStats(int hp, int maxHp, int sp, int maxSp, string statusBadge, bool isCurrentTurn, bool isAlive)
        {
            EnsureHierarchy();

            float safeMaxHp = Mathf.Max(1, maxHp);
            float hpRatio = Mathf.Clamp01(hp / safeMaxHp);
            _hpFill.rectTransform.anchorMax = new Vector2(hpRatio, 1f);
            _hpFill.color = hpRatio > 0.35f
                ? new Color(0.20f, 0.82f, 0.36f, 1f)
                : new Color(0.92f, 0.24f, 0.20f, 1f);

            bool hasSp = maxSp > 0;
            _spBarRoot.gameObject.SetActive(hasSp);
            if (hasSp)
            {
                float spRatio = Mathf.Clamp01(sp / (float)maxSp);
                _spFill.rectTransform.anchorMax = new Vector2(spRatio, 1f);
            }

            bool hasBadge = !string.IsNullOrEmpty(statusBadge);
            _statusBadgeLabel.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                _statusBadgeLabel.text = statusBadge;
            }

            if (isAlive)
            {
                _underRingRenderer.gameObject.SetActive(true);
                ApplyRingColor(isCurrentTurn ? CurrentTurnGold : _baseRingColor);
                ApplySpriteTint(Color.white);
                _worldHudCanvas.gameObject.SetActive(_showHud);
            }
            else
            {
                _underRingRenderer.gameObject.SetActive(false);
                ApplySpriteTint(new Color(0.45f, 0.45f, 0.50f, 0.55f));
                _worldHudCanvas.gameObject.SetActive(false);
            }
        }

        public void ShowFloatingText(string message, Color color)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            GameObject floatGo = new GameObject("FloatingText");
            floatGo.transform.position = transform.position + new Vector3(0f, _spriteSize + 0.32f, 0f);
            floatGo.transform.localScale = Vector3.one * 0.01f;

            Canvas canvas = floatGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            RectTransform canvasRt = floatGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(220f, 54f);

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
            tmp.text = message;
            tmp.fontSize = 26f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(AnimateFloatingText(floatGo, tmp, color));
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

        private static IEnumerator AnimateFloatingText(GameObject floatGo, TextMeshProUGUI tmp, Color baseColor)
        {
            const float duration = 0.9f;
            Vector3 startPos = floatGo.transform.position;
            float elapsed = 0f;

            while (elapsed < duration && floatGo != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                floatGo.transform.position = startPos + new Vector3(0f, t * 0.85f, 0f);

                Color c = baseColor;
                c.a = Mathf.Lerp(1f, 0f, t * t);
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

        private void EnsureHierarchy()
        {
            if (_spriteRenderer != null)
            {
                return;
            }

            // 1. SpriteQuad
            GameObject spriteGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spriteGo.name = "SpriteQuad";
            spriteGo.transform.SetParent(transform, false);
            spriteGo.transform.localPosition = new Vector3(0f, _spriteSize * 0.5f, 0f);
            spriteGo.transform.localScale = new Vector3(_spriteSize, _spriteSize, 1f);
            DestroyCollider(spriteGo);
            spriteGo.AddComponent<PixelBillboard>();
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
            hudGo.transform.localPosition = new Vector3(0f, _spriteSize + 0.10f, 0f);
            hudGo.transform.localScale = Vector3.one * 0.0072f;
            _worldHudCanvas = hudGo.AddComponent<Canvas>();
            _worldHudCanvas.renderMode = RenderMode.WorldSpace;
            _worldHudCanvas.sortingOrder = 15;
            RectTransform hudRt = hudGo.GetComponent<RectTransform>();
            hudRt.sizeDelta = new Vector2(150f, 48f);
            hudGo.AddComponent<PixelBillboard>();

            // Subtle dark nameplate pill behind name for crisp readability
            GameObject pillGo = new GameObject("NamePill");
            pillGo.transform.SetParent(hudGo.transform, false);
            RectTransform pillRt = pillGo.AddComponent<RectTransform>();
            pillRt.anchorMin = new Vector2(0.5f, 0.5f);
            pillRt.anchorMax = new Vector2(0.5f, 0.5f);
            pillRt.anchoredPosition = new Vector2(0f, 14f);
            pillRt.sizeDelta = new Vector2(126f, 22f);
            Image pillImg = pillGo.AddComponent<Image>();
            pillImg.color = new Color(0.04f, 0.06f, 0.12f, 0.82f);

            // Nameplate
            _nameLabel = CreateTmpLabel(hudGo.transform, "NameLabel", new Vector2(0f, 14f), new Vector2(150f, 22f), 17f, Color.white);
            _nameLabel.fontStyle = FontStyles.Bold;

            // HP bar
            _hpFill = CreateBar(hudGo.transform, "HpBar", new Vector2(0f, 0f), new Vector2(104f, 8f), new Color(0.20f, 0.82f, 0.36f, 1f), out _);

            // SP bar
            _spFill = CreateBar(hudGo.transform, "SpBar", new Vector2(0f, -9f), new Vector2(104f, 6f), new Color(0.18f, 0.78f, 0.96f, 1f), out _spBarRoot);

            // Condition badge
            _statusBadgeLabel = CreateTmpLabel(hudGo.transform, "ConditionBadge", new Vector2(0f, 30f), new Vector2(160f, 18f), 13f, new Color(0.98f, 0.84f, 0.22f, 1f));
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
