using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Realmz.Game
{
    /// <summary>
    /// Pure static uGUI builder helpers and dark-fantasy RPG palette for City of Bywater HUD & modals.
    /// Uses TMPro and UnityEngine.InputSystem.UI exclusively.
    /// </summary>
    public static class BywaterUiHelpers
    {
        // Crisp dark-fantasy RPG palette (BG3 / DOS2 inspired dark slate + brushed bronze)
        public static readonly Color PanelBg = new Color(0.047f, 0.067f, 0.118f, 0.94f);        // #0C111E
        public static readonly Color PanelDarkBg = new Color(0.031f, 0.047f, 0.086f, 0.97f);    // #080C16
        public static readonly Color CardBg = new Color(0.075f, 0.106f, 0.184f, 0.96f);
        public static readonly Color CardSelectedBg = new Color(0.200f, 0.160f, 0.078f, 0.98f);
        public static readonly Color BrassAccent = new Color(0.753f, 0.569f, 0.259f, 1.0f);     // #C09142 brushed bronze
        public static readonly Color BrassHighlight = new Color(0.961f, 0.800f, 0.388f, 1.0f);  // #F5CC63
        public static readonly Color ParchmentText = new Color(0.965f, 0.933f, 0.867f, 1.0f);   // #F6EEDD
        public static readonly Color MutedSlate = new Color(0.710f, 0.765f, 0.840f, 1.0f);      // #B5C3D6
        public static readonly Color ButtonBg = new Color(0.106f, 0.149f, 0.251f, 0.96f);
        public static readonly Color ButtonActiveBg = new Color(0.275f, 0.215f, 0.090f, 0.98f);
        public static readonly Color InputBg = new Color(0.043f, 0.063f, 0.118f, 0.98f);
        public static readonly Color HpFillColor = new Color(0.130f, 0.510f, 0.240f, 1.0f);
        public static readonly Color HpLowColor = new Color(0.760f, 0.190f, 0.170f, 1.0f);
        public static readonly Color SpFillColor = new Color(0.110f, 0.450f, 0.660f, 1.0f);
        public static readonly Color BarTrackColor = new Color(0.024f, 0.035f, 0.065f, 0.96f);
        public static readonly Color DangerRed = new Color(0.920f, 0.320f, 0.280f, 1.0f);
        public static readonly Color SuccessGreen = new Color(0.280f, 0.850f, 0.450f, 1.0f);
        public static readonly Color InfoCyan = new Color(0.350f, 0.820f, 0.960f, 1.0f);

        public static TMP_FontAsset ResolveFont(TMP_FontAsset font)
        {
            return font != null ? font : TMP_Settings.defaultFontAsset;
        }

        public static void AddBrassBorder(RectTransform target, Color color, float thickness = 1.5f)
        {
            if (target == null)
            {
                return;
            }

            CreateBorderEdge(target, "BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero, color);
            CreateBorderEdge(target, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness), color);
            CreateBorderEdge(target, "BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f), color);
            CreateBorderEdge(target, "BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero, color);
        }

        private static void CreateBorderEdge(
            RectTransform parent,
            string edgeName,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            GameObject go = new GameObject(edgeName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            LayoutElement le = go.GetComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        public static RectTransform CreatePanel(
            Transform parent,
            string name,
            Color? bgColor = null,
            bool withBrassOutline = true,
            float outlinePx = 1.5f)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            Image img = go.GetComponent<Image>();
            img.color = bgColor ?? PanelBg;

            if (withBrassOutline)
            {
                AddBrassBorder(rt, BrassAccent, outlinePx);
            }

            return rt;
        }

        public static Shadow AddTextDropShadow(Graphic target)
        {
            if (target == null)
            {
                return null;
            }

            Shadow shadow = target.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = target.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);
            shadow.useGraphicAlpha = true;
            return shadow;
        }

        public static RectTransform CreatePortraitFrame(
            Transform parent,
            string name,
            float sizePx,
            out Image portraitImage,
            Color? borderColor = null)
        {
            GameObject frameGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frameGo.transform.SetParent(parent, false);

            RectTransform frameRt = frameGo.GetComponent<RectTransform>();
            frameRt.sizeDelta = new Vector2(sizePx, sizePx);

            Image frameImg = frameGo.GetComponent<Image>();
            frameImg.color = PanelDarkBg;
            frameImg.raycastTarget = false;

            LayoutElement frameLe = frameGo.GetComponent<LayoutElement>();
            frameLe.preferredWidth = sizePx;
            frameLe.preferredHeight = sizePx;
            frameLe.minWidth = sizePx;
            frameLe.minHeight = sizePx;

            GameObject imgGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(frameGo.transform, false);
            RectTransform imgRt = imgGo.GetComponent<RectTransform>();
            imgRt.anchorMin = Vector2.zero;
            imgRt.anchorMax = Vector2.one;
            imgRt.offsetMin = new Vector2(3f, 3f);
            imgRt.offsetMax = new Vector2(-3f, -3f);

            portraitImage = imgGo.GetComponent<Image>();
            portraitImage.color = Color.white;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            AddBrassBorder(frameRt, borderColor ?? BrassAccent, 1.8f);
            return frameRt;
        }

        public static TextMeshProUGUI CreateLabel(
            Transform parent,
            string name,
            string text,
            TMP_FontAsset font,
            float fontSize = 16f,
            Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal,
            bool wrap = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset resolved = ResolveFont(font);
            if (resolved != null)
            {
                tmp.font = resolved;
            }

            tmp.text = text ?? string.Empty;
            tmp.fontSize = fontSize;
            tmp.color = color ?? ParchmentText;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            TMP_FontAsset font,
            UnityAction onClick = null,
            float fontSize = 16f,
            Color? bgColor = null,
            Color? outlineColor = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = bgColor ?? ButtonBg;

            AddBrassBorder(go.GetComponent<RectTransform>(), outlineColor ?? BrassAccent, 1.2f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.12f, 0.96f, 1f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.70f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.65f);
            colors.colorMultiplier = 1f;
            btn.colors = colors;

            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            TextMeshProUGUI tmp = CreateLabel(
                go.transform,
                "Label",
                label,
                font,
                fontSize,
                ParchmentText,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            AddTextDropShadow(tmp);

            RectTransform labelRt = tmp.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-6f, -2f);

            return btn;
        }

        public static TMP_InputField CreateInputField(
            Transform parent,
            string name,
            string placeholderText,
            TMP_FontAsset font,
            float fontSize = 16f)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);

            Image bg = root.GetComponent<Image>();
            bg.color = InputBg;

            AddBrassBorder(root.GetComponent<RectTransform>(), BrassAccent, 1.2f);

            GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(root.transform, false);
            RectTransform areaRt = textArea.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 4f);
            areaRt.offsetMax = new Vector2(-8f, -4f);

            TextMeshProUGUI placeholder = CreateLabel(
                textArea.transform,
                "Placeholder",
                placeholderText,
                font,
                fontSize,
                MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Italic,
                wrap: false);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = Vector2.zero;
            placeholder.rectTransform.offsetMax = Vector2.zero;

            TextMeshProUGUI textComp = CreateLabel(
                textArea.transform,
                "Text",
                string.Empty,
                font,
                fontSize,
                ParchmentText,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Normal,
                wrap: false);
            textComp.rectTransform.anchorMin = Vector2.zero;
            textComp.rectTransform.anchorMax = Vector2.one;
            textComp.rectTransform.offsetMin = Vector2.zero;
            textComp.rectTransform.offsetMax = Vector2.zero;

            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = textComp;
            input.placeholder = placeholder;

            TMP_FontAsset resolved = ResolveFont(font);
            if (resolved != null)
            {
                input.fontAsset = resolved;
            }

            input.pointSize = fontSize;
            input.caretColor = BrassHighlight;
            input.selectionColor = new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.35f);
            return input;
        }

        public static Image CreateImage(
            Transform parent,
            string name,
            Sprite sprite = null,
            Color? color = null,
            bool preserveAspect = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color ?? Color.white;
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static ScrollRect CreateScrollView(
            Transform parent,
            string name,
            TMP_FontAsset font = null,
            float spacing = 4f,
            RectOffset padding = null)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            root.transform.SetParent(parent, false);

            Image rootBg = root.GetComponent<Image>();
            rootBg.color = new Color(0.031f, 0.047f, 0.086f, 0.90f);

            AddBrassBorder(root.GetComponent<RectTransform>(), new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.65f), 1.0f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f, 4f);
            vpRt.offsetMax = new Vector2(-4f, -4f);

            Image vpImg = viewport.GetComponent<Image>();
            vpImg.color = Color.white;
            Mask mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = padding ?? new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = vpRt;
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            return scroll;
        }

        public static Image CreateProgressBar(
            Transform parent,
            string name,
            Color fillColor,
            out RectTransform barRoot)
        {
            GameObject bgGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            barRoot = bgGo.GetComponent<RectTransform>();
            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.color = BarTrackColor;
            bgImg.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);

            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
            fillImg.raycastTarget = false;

            AddBrassBorder(barRoot, new Color(0.01f, 0.015f, 0.03f, 0.95f), 1f);
            return fillImg;
        }

        public static void SetBorderColor(Transform target, Color borderCol)
        {
            if (target == null)
            {
                return;
            }

            string[] edgeNames = { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" };
            for (int i = 0; i < edgeNames.Length; i++)
            {
                Transform edge = target.Find(edgeNames[i]);
                if (edge != null && edge.TryGetComponent<Image>(out var edgeImg))
                {
                    edgeImg.color = borderCol;
                }
            }
        }

        public static void SetButtonColors(Button btn, bool active)
        {
            if (btn == null)
            {
                return;
            }

            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = active ? ButtonActiveBg : ButtonBg;
            }

            SetBorderColor(btn.transform, active ? BrassHighlight : BrassAccent);
        }

        public static void EnsureEventSystemWithInputSystem()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                es = Object.FindFirstObjectByType<EventSystem>();
            }

            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es = esGo.GetComponent<EventSystem>();
                return;
            }

            Component legacy = es.GetComponent("StandaloneInputModule");
            if (legacy != null)
            {
                Object.Destroy(legacy);
            }

            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        public static void SetRaycastTargetRecursive(Transform root, bool raycastTarget)
        {
            if (root == null)
            {
                return;
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = raycastTarget;
            }
        }

        public static Slider CreateSlider(
            Transform parent,
            string name,
            float minValue,
            float maxValue,
            float initialValue,
            UnityAction<float> onValueChanged = null)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);

            GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(root.transform, false);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.28f);
            bgRt.anchorMax = new Vector2(1f, 0.72f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.color = BarTrackColor;
            AddBrassBorder(bgRt, new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.55f), 1f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(root.transform, false);
            RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.28f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.72f);
            fillAreaRt.offsetMin = new Vector2(2f, 2f);
            fillAreaRt.offsetMax = new Vector2(-2f, -2f);

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = BrassAccent;

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(8f, 0f);
            handleAreaRt.offsetMax = new Vector2(-8f, 0f);

            GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleArea.transform, false);
            RectTransform handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 24f);
            Image handleImg = handleGo.GetComponent<Image>();
            handleImg.color = BrassHighlight;
            AddBrassBorder(handleRt, PanelDarkBg, 1f);

            Slider slider = root.GetComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = Mathf.Clamp(initialValue, minValue, maxValue);
            if (onValueChanged != null)
            {
                slider.onValueChanged.AddListener(onValueChanged);
            }

            return slider;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> ProceduralIconCache =
            new System.Collections.Generic.Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);

        public static Sprite GetProceduralIconSprite(string key)
        {
            string normalized = string.IsNullOrWhiteSpace(key) ? "sword" : key.Trim().ToLowerInvariant();
            if (ProceduralIconCache.TryGetValue(normalized, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite created = BuildProceduralIconSprite(normalized);
            ProceduralIconCache[normalized] = created;
            return created;
        }

        private static Sprite BuildProceduralIconSprite(string key)
        {
            const int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            switch (key)
            {
                case "circle":
                    DrawCircle(pixels, size, Color.white);
                    break;
                case "diamond":
                    DrawDiamond(pixels, size, Color.white);
                    break;
                case "bow":
                case "ranged":
                    DrawBow(pixels, size);
                    break;
                case "dagger":
                    DrawDagger(pixels, size);
                    break;
                case "mace":
                    DrawMace(pixels, size);
                    break;
                case "staff":
                    DrawStaff(pixels, size);
                    break;
                case "fire":
                    DrawFlame(pixels, size);
                    break;
                case "bolt":
                case "dart":
                    DrawBolt(pixels, size);
                    break;
                case "heal":
                    DrawCross(pixels, size, new Color(0.32f, 0.92f, 0.48f, 1f));
                    break;
                case "shield":
                case "guard":
                    DrawShield(pixels, size);
                    break;
                case "shove":
                    DrawShove(pixels, size);
                    break;
                case "potion":
                    DrawPotion(pixels, size);
                    break;
                case "endturn":
                    DrawHourglass(pixels, size);
                    break;
                case "undo":
                    DrawUndoArrow(pixels, size);
                    break;
                case "spell":
                    DrawStar(pixels, size, new Color(0.42f, 0.86f, 1f, 1f));
                    break;
                default:
                    DrawSword(pixels, size);
                    break;
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            sprite.name = $"proc_icon_{key}";
            return sprite;
        }

        private static void SetPx(Color[] pixels, int size, int x, int y, Color col)
        {
            if (x >= 0 && x < size && y >= 0 && y < size)
            {
                pixels[y * size + x] = col;
            }
        }

        private static void DrawCircle(Color[] px, int s, Color col)
        {
            float c = (s - 1) * 0.5f;
            float rSq = 6.2f * 6.2f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c;
                    float dy = y - c;
                    if (dx * dx + dy * dy <= rSq)
                    {
                        SetPx(px, s, x, y, col);
                    }
                }
            }
        }

        private static void DrawDiamond(Color[] px, int s, Color col)
        {
            int c = s / 2;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    int dist = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                    if (dist <= 6)
                    {
                        SetPx(px, s, x, y, col);
                    }
                }
            }
        }

        private static void DrawSword(Color[] px, int s)
        {
            Color blade = new Color(0.86f, 0.90f, 0.96f, 1f);
            Color edge = new Color(0.62f, 0.68f, 0.78f, 1f);
            Color hilt = new Color(0.88f, 0.68f, 0.25f, 1f);
            for (int i = 4; i < 14; i++)
            {
                SetPx(px, s, i, i, blade);
                SetPx(px, s, i - 1, i, edge);
            }
            SetPx(px, s, 13, 14, blade);
            SetPx(px, s, 14, 13, blade);
            SetPx(px, s, 14, 14, blade);
            SetPx(px, s, 3, 5, hilt);
            SetPx(px, s, 4, 4, hilt);
            SetPx(px, s, 5, 3, hilt);
            SetPx(px, s, 2, 2, hilt);
            SetPx(px, s, 3, 3, hilt);
        }

        private static void DrawDagger(Color[] px, int s)
        {
            Color blade = new Color(0.86f, 0.90f, 0.96f, 1f);
            Color hilt = new Color(0.76f, 0.56f, 0.24f, 1f);
            for (int i = 5; i < 12; i++)
            {
                SetPx(px, s, i, i, blade);
            }
            SetPx(px, s, 4, 6, hilt);
            SetPx(px, s, 6, 4, hilt);
            SetPx(px, s, 4, 4, hilt);
            SetPx(px, s, 3, 3, hilt);
        }

        private static void DrawMace(Color[] px, int s)
        {
            Color iron = new Color(0.78f, 0.82f, 0.88f, 1f);
            Color wood = new Color(0.62f, 0.42f, 0.22f, 1f);
            for (int i = 2; i < 9; i++)
            {
                SetPx(px, s, i, i, wood);
            }
            for (int y = 9; y <= 13; y++)
            {
                for (int x = 9; x <= 13; x++)
                {
                    SetPx(px, s, x, y, iron);
                }
            }
            SetPx(px, s, 11, 14, Color.white);
            SetPx(px, s, 14, 11, Color.white);
            SetPx(px, s, 8, 11, Color.white);
        }

        private static void DrawStaff(Color[] px, int s)
        {
            Color wood = new Color(0.66f, 0.44f, 0.22f, 1f);
            Color gem = new Color(0.35f, 0.85f, 1f, 1f);
            for (int i = 2; i < 12; i++)
            {
                SetPx(px, s, i, i, wood);
            }
            SetPx(px, s, 12, 12, gem);
            SetPx(px, s, 12, 13, gem);
            SetPx(px, s, 13, 12, gem);
            SetPx(px, s, 13, 13, Color.white);
        }

        private static void DrawBow(Color[] px, int s)
        {
            Color wood = new Color(0.80f, 0.56f, 0.26f, 1f);
            Color str = new Color(0.88f, 0.88f, 0.92f, 1f);
            Color tip = new Color(0.95f, 0.80f, 0.35f, 1f);
            for (int y = 2; y <= 13; y++)
            {
                SetPx(px, s, 4, y, str);
            }
            for (int y = 2; y <= 13; y++)
            {
                int offset = y <= 7 ? (y - 2) : (13 - y);
                int x = Mathf.Clamp(5 + offset, 5, 11);
                SetPx(px, s, x, y, wood);
            }
            for (int x = 3; x <= 13; x++)
            {
                SetPx(px, s, x, 8, str);
            }
            SetPx(px, s, 13, 8, tip);
            SetPx(px, s, 12, 7, tip);
            SetPx(px, s, 12, 9, tip);
        }

        private static void DrawFlame(Color[] px, int s)
        {
            Color outer = new Color(0.96f, 0.40f, 0.14f, 1f);
            Color inner = new Color(1.0f, 0.82f, 0.24f, 1f);
            for (int y = 2; y <= 13; y++)
            {
                int halfW = y < 6 ? (y - 1) : Mathf.Max(1, (14 - y) / 2 + 1);
                for (int x = 8 - halfW; x <= 8 + halfW; x++)
                {
                    SetPx(px, s, x, y, y < 8 && Mathf.Abs(x - 8) <= 1 ? inner : outer);
                }
            }
            SetPx(px, s, 8, 14, outer);
        }

        private static void DrawBolt(Color[] px, int s)
        {
            Color col = new Color(0.45f, 0.88f, 1.0f, 1f);
            int[] xs = { 9, 8, 7, 6, 7, 8, 9, 10, 9, 8, 7 };
            for (int i = 0; i < xs.Length; i++)
            {
                SetPx(px, s, xs[i], 13 - i, col);
                SetPx(px, s, xs[i] + 1, 13 - i, Color.white);
            }
        }

        private static void DrawCross(Color[] px, int s, Color col)
        {
            for (int i = 3; i <= 12; i++)
            {
                SetPx(px, s, 7, i, col);
                SetPx(px, s, 8, i, col);
                SetPx(px, s, i, 7, col);
                SetPx(px, s, i, 8, col);
            }
        }

        private static void DrawShield(Color[] px, int s)
        {
            Color rim = new Color(0.85f, 0.68f, 0.28f, 1f);
            Color body = new Color(0.24f, 0.44f, 0.72f, 1f);
            for (int y = 3; y <= 12; y++)
            {
                int halfW = y >= 7 ? 5 : Mathf.Max(1, y - 2);
                for (int x = 8 - halfW; x <= 8 + halfW; x++)
                {
                    bool isBorder = y == 12 || x == 8 - halfW || x == 8 + halfW || y == 3;
                    SetPx(px, s, x, y, isBorder ? rim : body);
                }
            }
        }

        private static void DrawShove(Color[] px, int s)
        {
            Color col = new Color(0.96f, 0.76f, 0.26f, 1f);
            for (int x = 2; x <= 11; x++)
            {
                SetPx(px, s, x, 7, col);
                SetPx(px, s, x, 8, col);
            }
            for (int d = 0; d <= 4; d++)
            {
                SetPx(px, s, 13 - d, 8 + d, col);
                SetPx(px, s, 13 - d, 7 - d, col);
            }
        }

        private static void DrawPotion(Color[] px, int s)
        {
            Color cork = new Color(0.76f, 0.56f, 0.28f, 1f);
            Color glass = new Color(0.75f, 0.85f, 0.96f, 1f);
            Color liquid = new Color(0.88f, 0.20f, 0.24f, 1f);
            SetPx(px, s, 7, 13, cork);
            SetPx(px, s, 8, 13, cork);
            SetPx(px, s, 7, 12, glass);
            SetPx(px, s, 8, 12, glass);
            for (int y = 3; y <= 10; y++)
            {
                int halfW = y >= 9 ? 2 : 4;
                for (int x = 8 - halfW; x <= 8 + halfW; x++)
                {
                    bool border = y == 3 || x == 8 - halfW || x == 8 + halfW;
                    SetPx(px, s, x, y, border ? glass : (y <= 7 ? liquid : glass));
                }
            }
        }

        private static void DrawHourglass(Color[] px, int s)
        {
            Color frame = new Color(0.88f, 0.72f, 0.30f, 1f);
            Color sand = new Color(0.95f, 0.88f, 0.64f, 1f);
            for (int x = 4; x <= 11; x++)
            {
                SetPx(px, s, x, 2, frame);
                SetPx(px, s, x, 13, frame);
            }
            for (int y = 3; y <= 12; y++)
            {
                int halfW = Mathf.Abs(y - 7);
                if (halfW < 1) halfW = 1;
                SetPx(px, s, 8 - halfW, y, frame);
                SetPx(px, s, 8 + halfW, y, frame);
                if (y <= 5 || (y >= 9 && y <= 11))
                {
                    for (int x = 8 - halfW + 1; x <= 8 + halfW - 1; x++)
                    {
                        SetPx(px, s, x, y, sand);
                    }
                }
            }
        }

        private static void DrawUndoArrow(Color[] px, int s)
        {
            Color col = new Color(0.55f, 0.85f, 0.98f, 1f);
            for (int x = 5; x <= 11; x++)
            {
                SetPx(px, s, x, 10, col);
                SetPx(px, s, x, 5, col);
            }
            for (int y = 5; y <= 10; y++)
            {
                SetPx(px, s, 11, y, col);
            }
            SetPx(px, s, 4, 9, col);
            SetPx(px, s, 4, 11, col);
            SetPx(px, s, 3, 10, col);
        }

        private static void DrawStar(Color[] px, int s, Color col)
        {
            for (int i = 3; i <= 12; i++)
            {
                SetPx(px, s, 8, i, col);
                SetPx(px, s, i, 8, col);
            }
            for (int d = -3; d <= 3; d++)
            {
                SetPx(px, s, 8 + d, 8 + d, col);
                SetPx(px, s, 8 + d, 8 - d, col);
            }
            SetPx(px, s, 8, 8, Color.white);
        }
    }
}
