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
        // Realmz Heritage Chiseled Warm-Granite & Forged Gold palette
        public static readonly Color PanelBg = new Color(0.133f, 0.129f, 0.125f, 0.96f);        // #222120
        public static readonly Color PanelDarkBg = new Color(0.094f, 0.090f, 0.086f, 0.98f);    // #181716
        public static readonly Color CardBg = new Color(0.149f, 0.141f, 0.133f, 0.96f);         // #262422
        public static readonly Color CardSelectedBg = new Color(0.200f, 0.153f, 0.078f, 0.98f); // #332714
        public static readonly Color BrassAccent = new Color(0.769f, 0.584f, 0.275f, 1.0f);     // #C49546
        public static readonly Color BrassHighlight = new Color(0.953f, 0.800f, 0.424f, 1.0f);  // #F3CC6C
        public static readonly Color ParchmentText = new Color(0.957f, 0.925f, 0.863f, 1.0f);   // #F4ECDC
        public static readonly Color MutedSlate = new Color(0.737f, 0.718f, 0.678f, 1.0f);      // #BCB7AD
        public static readonly Color ButtonBg = new Color(0.200f, 0.188f, 0.176f, 0.98f);       // #33302D
        public static readonly Color ButtonActiveBg = new Color(0.357f, 0.251f, 0.102f, 0.98f); // #5B401A
        public static readonly Color InputBg = new Color(0.086f, 0.082f, 0.078f, 0.98f);        // #161514
        public static readonly Color HpFillColor = new Color(0.173f, 0.608f, 0.306f, 1.0f);     // #2C9B4E
        public static readonly Color HpLowColor = new Color(0.812f, 0.227f, 0.204f, 1.0f);      // #CF3A34
        public static readonly Color SpFillColor = new Color(0.204f, 0.659f, 0.800f, 1.0f);     // #34A8CC
        public static readonly Color ApBarFillColor = new Color(0.851f, 0.643f, 0.263f, 0.95f); // #D9A443
        public static readonly Color BarTrackColor = new Color(0.067f, 0.063f, 0.059f, 0.98f);  // #11100F
        public static readonly Color DangerRed = new Color(0.886f, 0.345f, 0.302f, 1.0f);       // #E2584D
        public static readonly Color SuccessGreen = new Color(0.345f, 0.839f, 0.502f, 1.0f);    // #58D680
        public static readonly Color InfoCyan = new Color(0.408f, 0.831f, 0.910f, 1.0f);        // #68D4E8

        public static TMP_FontAsset DisplayFont { get; private set; }
        public static Sprite StonePanelSprite { get; private set; }
        public static Sprite StoneBarSprite { get; private set; }
        public static Sprite StoneWellSprite { get; private set; }
        public static Sprite TintPanelSprite { get; private set; }
        public static Sprite ButtonStoneSprite { get; private set; }
        public static Sprite ButtonGoldSprite { get; private set; }
        public static Sprite BarTrackSprite { get; private set; }
        public static Sprite BarFillSprite { get; private set; }
        public static Sprite HeaderRuleSprite { get; private set; }
        public static Sprite GemPipOffSprite { get; private set; }
        public static Sprite GemPipGoldSprite { get; private set; }
        public static Sprite GemPipCyanSprite { get; private set; }
        public static Sprite GemPipEmeraldSprite { get; private set; }
        public static Sprite ModalScrimSprite { get; private set; }

        public static bool HasChromeSprites => StonePanelSprite != null && ButtonStoneSprite != null;

        public static void ApplyTheme(Realmz.Data.BywaterAssetCatalog catalog)
        {
            if (catalog == null)
            {
                DisplayFont = null;
                StonePanelSprite = null;
                StoneBarSprite = null;
                StoneWellSprite = null;
                TintPanelSprite = null;
                ButtonStoneSprite = null;
                ButtonGoldSprite = null;
                BarTrackSprite = null;
                BarFillSprite = null;
                HeaderRuleSprite = null;
                GemPipOffSprite = null;
                GemPipGoldSprite = null;
                GemPipCyanSprite = null;
                GemPipEmeraldSprite = null;
                ModalScrimSprite = null;
                return;
            }

            DisplayFont = catalog.uiDisplayFont;
            StonePanelSprite = catalog.uiStonePanelSprite;
            StoneBarSprite = catalog.uiStoneBarSprite;
            StoneWellSprite = catalog.uiStoneWellSprite;
            TintPanelSprite = catalog.uiTintPanelSprite;
            ButtonStoneSprite = catalog.uiButtonStoneSprite;
            ButtonGoldSprite = catalog.uiButtonGoldSprite;
            BarTrackSprite = catalog.uiBarTrackSprite;
            BarFillSprite = catalog.uiBarFillSprite;
            HeaderRuleSprite = catalog.uiHeaderRuleSprite;
            GemPipOffSprite = catalog.uiGemPipOffSprite;
            GemPipGoldSprite = catalog.uiGemPipGoldSprite;
            GemPipCyanSprite = catalog.uiGemPipCyanSprite;
            GemPipEmeraldSprite = catalog.uiGemPipEmeraldSprite;
            ModalScrimSprite = catalog.uiModalScrimSprite;
        }

        public static TMP_FontAsset ResolveFont(TMP_FontAsset font)
        {
            return font != null ? font : TMP_Settings.defaultFontAsset;
        }

        public static TMP_FontAsset ResolveDisplayFont(TMP_FontAsset bodyFallback)
        {
            return DisplayFont != null ? DisplayFont : ResolveFont(bodyFallback);
        }

        private static bool IsCloseColor(Color a, Color b, float tolerance = 0.04f)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance &&
                   Mathf.Abs(a.g - b.g) <= tolerance &&
                   Mathf.Abs(a.b - b.b) <= tolerance;
        }

        private static bool IsSemanticChromaticColor(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (max - min <= 0.085f)
            {
                return false;
            }

            // Exclude our neutral slate theme colors even if slightly cool-tinted.
            if (IsCloseColor(c, PanelBg, 0.04f) ||
                IsCloseColor(c, PanelDarkBg, 0.04f) ||
                IsCloseColor(c, CardBg, 0.04f) ||
                IsCloseColor(c, ButtonBg, 0.04f) ||
                IsCloseColor(c, InputBg, 0.04f) ||
                IsCloseColor(c, BarTrackColor, 0.04f))
            {
                return false;
            }

            return true;
        }

        private static bool IsDefaultChromeBorder(Color borderCol)
        {
            if (IsCloseColor(borderCol, BrassAccent, 0.08f) ||
                IsCloseColor(borderCol, BrassHighlight, 0.08f) ||
                IsCloseColor(borderCol, MutedSlate, 0.12f))
            {
                return true;
            }

            // Dark track / muted neutral borders on inactive slots/cards
            float max = Mathf.Max(borderCol.r, Mathf.Max(borderCol.g, borderCol.b));
            float min = Mathf.Min(borderCol.r, Mathf.Min(borderCol.g, borderCol.b));
            if (max <= 0.48f && (max - min) <= 0.18f)
            {
                return true;
            }

            return false;
        }

        public static Color BoostSemanticTint(Color bg)
        {
            Color boosted = Color.Lerp(bg, Color.white, 0.18f);
            boosted.a = bg.a;
            return boosted;
        }

        private static bool NameContainsAny(string name, params string[] tokens)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (name.IndexOf(tokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public const float ChromePpuMultiplier = 0.78125f;

        public static void ApplySurfaceState(Image img, bool selected, bool isButton = false)
        {
            if (img == null)
            {
                return;
            }

            if (HasChromeSprites)
            {
                Sprite targetSprite = isButton
                    ? (selected ? (ButtonGoldSprite ?? ButtonStoneSprite) : ButtonStoneSprite)
                    : (selected ? (ButtonGoldSprite ?? StoneBarSprite ?? StonePanelSprite) : (StoneBarSprite ?? StonePanelSprite));
                img.sprite = targetSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                img.color = Color.white;
                return;
            }

            img.color = isButton
                ? (selected ? ButtonActiveBg : ButtonBg)
                : (selected ? CardSelectedBg : CardBg);
        }

        public static void ApplyTintedChrome(Image img, Color bgColor, bool preferBar = true)
        {
            if (img == null)
            {
                return;
            }

            if (HasChromeSprites)
            {
                img.sprite = TintPanelSprite ?? (preferBar ? (StoneBarSprite ?? StonePanelSprite) : StonePanelSprite);
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                img.color = BoostSemanticTint(bgColor);
                return;
            }

            img.color = bgColor;
        }

        public static RectTransform CreateHeaderDivider(Transform parent, string name = "HeaderRule")
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;

            LayoutElement le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;

            if (HeaderRuleSprite != null)
            {
                img.sprite = HeaderRuleSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                img.color = Color.white;
                le.preferredHeight = 6f;
                le.minHeight = 6f;
            }
            else
            {
                img.color = new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.55f);
                le.preferredHeight = 2f;
                le.minHeight = 2f;
            }

            return go.GetComponent<RectTransform>();
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
            Color resolvedBg = bgColor ?? PanelBg;

            if (HasChromeSprites)
            {
                bool isModal = !string.IsNullOrEmpty(name) && (
                    name.EndsWith("Modal", System.StringComparison.OrdinalIgnoreCase) ||
                    NameContainsAny(
                        name,
                        "PartyDrawerModal",
                        "TownShopModal",
                        "TempleShrineModal",
                        "SettingsModal",
                        "QuickResolveModal",
                        "EncounterInterceptModal",
                        "OutcomeModal",
                        "LevelUpModal",
                        "DiceRollModal"));

                if (isModal)
                {
                    img.sprite = StonePanelSprite;
                    img.color = Color.white;
                }
                else if (IsCloseColor(resolvedBg, ButtonActiveBg) || IsCloseColor(resolvedBg, CardSelectedBg))
                {
                    img.sprite = ButtonGoldSprite ?? StoneBarSprite ?? StonePanelSprite;
                    img.color = Color.white;
                }
                else if (IsCloseColor(resolvedBg, ButtonBg))
                {
                    img.sprite = ButtonStoneSprite ?? StoneBarSprite ?? StonePanelSprite;
                    img.color = Color.white;
                }
                else if (IsSemanticChromaticColor(resolvedBg))
                {
                    img.sprite = TintPanelSprite ?? StoneBarSprite ?? StonePanelSprite;
                    img.color = BoostSemanticTint(resolvedBg);
                }
                else if (IsCloseColor(resolvedBg, InputBg) ||
                         IsCloseColor(resolvedBg, BarTrackColor) ||
                         NameContainsAny(
                             name,
                             "Well",
                             "Tray",
                             "IconBox",
                             "CheckBox",
                             "PreviewBox",
                             "XpTrack",
                             "EnemyStrip",
                             "PaperDollPanel",
                             "CharacterSheetPanel",
                             "LevelUpStat",
                             "Stat_",
                             "Hint",
                             "Tip",
                             "Col",
                             "Body",
                             "PartyConditionStatus"))
                {
                    img.sprite = StoneWellSprite ?? StoneBarSprite ?? StonePanelSprite;
                    img.color = Color.white;
                }
                else
                {
                    img.sprite = StoneBarSprite ?? StonePanelSprite;
                    img.color = Color.white;
                }

                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                return rt;
            }

            img.color = resolvedBg;

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

            if (HasChromeSprites)
            {
                frameImg.sprite = StoneWellSprite ?? StonePanelSprite;
                frameImg.type = Image.Type.Sliced;
                frameImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                frameImg.color = Color.white;
                imgRt.offsetMin = new Vector2(2f, 2f);
                imgRt.offsetMax = new Vector2(-2f, -2f);
            }
            else
            {
                frameImg.color = PanelDarkBg;
                imgRt.offsetMin = new Vector2(3f, 3f);
                imgRt.offsetMax = new Vector2(-3f, -3f);
            }

            portraitImage = imgGo.GetComponent<Image>();
            portraitImage.color = Color.white;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            if (!HasChromeSprites || (borderColor.HasValue && !IsDefaultChromeBorder(borderColor.Value)))
            {
                AddBrassBorder(frameRt, borderColor ?? BrassAccent, 1.8f);
            }
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
            tmp.fontSize = Mathf.Max(15f, fontSize);
            tmp.color = color ?? ParchmentText;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static TextMeshProUGUI CreateDisplayLabel(
            Transform parent,
            string name,
            string text,
            TMP_FontAsset font,
            float fontSize = 18f,
            Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Bold,
            bool wrap = true)
        {
            TextMeshProUGUI tmp = CreateLabel(
                parent,
                name,
                text,
                ResolveDisplayFont(font),
                Mathf.Max(15f, fontSize),
                color,
                alignment,
                style,
                wrap);
            AddTextDropShadow(tmp);
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
            if (HasChromeSprites)
            {
                bool useGold = bgColor.HasValue && (IsCloseColor(bgColor.Value, ButtonActiveBg) || IsCloseColor(bgColor.Value, CardSelectedBg));
                bool useTint = bgColor.HasValue && !useGold && IsSemanticChromaticColor(bgColor.Value);
                img.sprite = useTint
                    ? (TintPanelSprite ?? ButtonStoneSprite)
                    : (useGold ? (ButtonGoldSprite ?? ButtonStoneSprite) : ButtonStoneSprite);
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                img.color = useTint ? BoostSemanticTint(bgColor.Value) : Color.white;

                if (outlineColor.HasValue && !IsDefaultChromeBorder(outlineColor.Value))
                {
                    AddBrassBorder(go.GetComponent<RectTransform>(), outlineColor.Value, 1.5f);
                }
            }
            else
            {
                img.color = bgColor ?? ButtonBg;
                AddBrassBorder(go.GetComponent<RectTransform>(), outlineColor ?? BrassAccent, 1.2f);
            }

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.08f, 0.92f, 1f);
            colors.pressedColor = new Color(0.84f, 0.78f, 0.68f, 1f);
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
            if (HasChromeSprites)
            {
                bg.sprite = StoneWellSprite ?? StonePanelSprite;
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                bg.color = Color.white;
            }
            else
            {
                bg.color = InputBg;
                AddBrassBorder(root.GetComponent<RectTransform>(), BrassAccent, 1.2f);
            }

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
            if (HasChromeSprites)
            {
                rootBg.sprite = StoneWellSprite ?? StonePanelSprite;
                rootBg.type = Image.Type.Sliced;
                rootBg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                rootBg.color = Color.white;
            }
            else
            {
                rootBg.color = new Color(0.031f, 0.047f, 0.086f, 0.90f);
                AddBrassBorder(root.GetComponent<RectTransform>(), new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.65f), 1.0f);
            }

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
            bgImg.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;

            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
            fillImg.raycastTarget = false;

            if (HasChromeSprites)
            {
                bgImg.sprite = BarTrackSprite ?? StoneWellSprite;
                bgImg.type = Image.Type.Sliced;
                bgImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                bgImg.color = Color.white;

                fillRt.offsetMin = new Vector2(2f, 2f);
                fillRt.offsetMax = new Vector2(-2f, -2f);
                if (BarFillSprite != null)
                {
                    fillImg.sprite = BarFillSprite;
                    fillImg.type = Image.Type.Sliced;
                    fillImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                }
            }
            else
            {
                bgImg.color = BarTrackColor;
                fillRt.offsetMin = new Vector2(1f, 1f);
                fillRt.offsetMax = new Vector2(-1f, -1f);
                AddBrassBorder(barRoot, new Color(0.01f, 0.015f, 0.03f, 0.95f), 1f);
            }

            return fillImg;
        }

        public static void SetBorderColor(Transform target, Color borderCol)
        {
            if (target == null)
            {
                return;
            }

            bool isSlicedChrome = target.TryGetComponent<Image>(out var targetImg) &&
                                  targetImg.sprite != null &&
                                  targetImg.type == Image.Type.Sliced;
            bool suppressBorder = isSlicedChrome && IsDefaultChromeBorder(borderCol);

            string[] edgeNames = { "BorderTop", "BorderBottom", "BorderLeft", "BorderRight" };
            bool foundAnyEdge = false;
            for (int i = 0; i < edgeNames.Length; i++)
            {
                Transform edge = target.Find(edgeNames[i]);
                if (edge != null && edge.TryGetComponent<Image>(out var edgeImg))
                {
                    foundAnyEdge = true;
                    edge.gameObject.SetActive(!suppressBorder);
                    if (!suppressBorder)
                    {
                        edgeImg.color = borderCol;
                    }
                }
            }

            if (!foundAnyEdge && isSlicedChrome && !suppressBorder && target is RectTransform rt)
            {
                AddBrassBorder(rt, borderCol, 2f);
            }
        }

        public static void SetButtonColors(Button btn, bool active)
        {
            if (btn == null)
            {
                return;
            }

            Image img = btn.GetComponent<Image>();
            ApplySurfaceState(img, active, isButton: true);
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
            if (HasChromeSprites)
            {
                bgImg.sprite = BarTrackSprite ?? StoneWellSprite;
                bgImg.type = Image.Type.Sliced;
                bgImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = BarTrackColor;
                AddBrassBorder(bgRt, new Color(BrassAccent.r, BrassAccent.g, BrassAccent.b, 0.55f), 1f);
            }

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
            if (HasChromeSprites && BarFillSprite != null)
            {
                fillImg.sprite = BarFillSprite;
                fillImg.type = Image.Type.Sliced;
                fillImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
            }
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
            if (HasChromeSprites && (GemPipGoldSprite != null || ButtonGoldSprite != null))
            {
                if (GemPipGoldSprite != null)
                {
                    handleImg.sprite = GemPipGoldSprite;
                    handleImg.type = Image.Type.Simple;
                    handleImg.preserveAspect = true;
                }
                else
                {
                    handleImg.sprite = ButtonGoldSprite;
                    handleImg.type = Image.Type.Sliced;
                    handleImg.pixelsPerUnitMultiplier = ChromePpuMultiplier;
                }
                handleImg.color = Color.white;
            }
            else
            {
                handleImg.color = BrassHighlight;
                AddBrassBorder(handleRt, PanelDarkBg, 1f);
            }

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
                case "gold":
                case "gold_coins":
                    DrawGoldCoins(pixels, size);
                    break;
                case "level_up":
                case "upgrade_arrow":
                    DrawUpArrow(pixels, size);
                    break;
                case "revive":
                case "revived":
                    DrawReviveHeart(pixels, size);
                    break;
                case "check":
                case "checkmark":
                    DrawCheckmark(pixels, size);
                    break;
                case "torch":
                    DrawTorch(pixels, size);
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

        private static void DrawGoldCoins(Color[] px, int s)
        {
            Color outline = new Color(0.26f, 0.16f, 0.06f, 1f);
            Color shade = new Color(0.62f, 0.42f, 0.10f, 1f);
            Color gold = new Color(0.90f, 0.70f, 0.22f, 1f);
            Color glint = new Color(1.00f, 0.94f, 0.58f, 1f);
            // Back stack + front coin
            for (int y = 3; y <= 12; y++)
            {
                for (int x = 3; x <= 12; x++)
                {
                    bool edge = (x == 3 || x == 12 || y == 3 || y == 12);
                    if (edge && (x == 3 || x == 12) && (y == 3 || y == 12)) continue;
                    if (y >= 4 && y <= 11 && x >= 4 && x <= 11)
                    {
                        SetPx(px, s, x, y, (y % 2 == 0) ? shade : gold);
                    }
                    else
                    {
                        SetPx(px, s, x, y, outline);
                    }
                }
            }
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 <= 9)
                    {
                        Color c = d2 >= 7 ? outline : (dx + dy <= -2 ? glint : (dx + dy >= 2 ? shade : gold));
                        SetPx(px, s, 10 + dx, 6 + dy, c);
                    }
                }
            }
        }

        private static void DrawUpArrow(Color[] px, int s)
        {
            Color c = Color.white;
            for (int y = 3; y <= 12; y++)
            {
                SetPx(px, s, 7, y, c);
                SetPx(px, s, 8, y, c);
            }
            for (int d = 1; d <= 4; d++)
            {
                for (int x = 7 - d; x <= 8 + d; x++)
                {
                    SetPx(px, s, x, 13 - d, c);
                }
            }
        }

        private static void DrawReviveHeart(Color[] px, int s)
        {
            Color heart = new Color(0.92f, 0.30f, 0.36f, 1f);
            Color cross = Color.white;
            for (int y = 4; y <= 12; y++)
            {
                for (int x = 3; x <= 12; x++)
                {
                    bool topLobes = y >= 10 && ((x >= 4 && x <= 6) || (x >= 9 && x <= 11));
                    bool midBody = y >= 7 && y <= 9 && x >= 3 && x <= 12;
                    bool taper = (y == 6 && x >= 4 && x <= 11) || (y == 5 && x >= 5 && x <= 10) || (y == 4 && x >= 6 && x <= 9);
                    if (topLobes || midBody || taper)
                    {
                        SetPx(px, s, x, y, heart);
                    }
                }
            }
            for (int i = 6; i <= 10; i++)
            {
                SetPx(px, s, 7, i, cross);
                SetPx(px, s, 8, i, cross);
            }
            for (int x = 5; x <= 10; x++)
            {
                SetPx(px, s, x, 8, cross);
            }
        }

        private static void DrawCheckmark(Color[] px, int s)
        {
            Color c = Color.white;
            int[] xs = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            int[] ys = { 7, 6, 5, 6, 7, 8, 9, 10, 11, 12 };
            for (int i = 0; i < xs.Length; i++)
            {
                SetPx(px, s, xs[i], ys[i], c);
                SetPx(px, s, xs[i], ys[i] + 1, c);
            }
        }

        private static Sprite _cachedD20DieSprite;

        public static Sprite GetD20DieSprite()
        {
            if (_cachedD20DieSprite != null)
            {
                return _cachedD20DieSprite;
            }

            const int s = 128;
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] px = new Color[s * s];
            Vector2 c = new Vector2(63.5f, 63.5f);
            float rOuter = 58f;
            float rInner = 29f;

            Vector2[] outerVerts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (90f - i * 60f);
                outerVerts[i] = c + new Vector2(Mathf.Cos(a) * rOuter, Mathf.Sin(a) * rOuter);
            }

            // Inner upright triangle matching regular icosahedron front face (vertices at top, bottom-right, bottom-left)
            Vector2[] triVerts = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Deg2Rad * (90f - i * 120f);
                triVerts[i] = c + new Vector2(Mathf.Cos(a) * rInner, Mathf.Sin(a) * rInner);
            }

            // 9 facet triangles surrounding center face + 1 center equilateral face
            Vector2[][] facets =
            {
                new[] { triVerts[0], outerVerts[1], triVerts[1] }, // upper-right lit
                new[] { outerVerts[0], outerVerts[1], triVerts[0] }, // top-right bevel
                new[] { triVerts[1], outerVerts[1], outerVerts[2] }, // mid-right
                new[] { triVerts[1], outerVerts[2], outerVerts[3] }, // lower-right shadow
                new[] { triVerts[1], outerVerts[3], triVerts[2] }, // bottom bevel
                new[] { triVerts[2], outerVerts[3], outerVerts[4] }, // lower-left
                new[] { triVerts[2], outerVerts[4], outerVerts[5] }, // mid-left
                new[] { triVerts[0], triVerts[2], outerVerts[5] }, // upper-left lit
                new[] { outerVerts[0], triVerts[0], outerVerts[5] }  // top-left highlight
            };
            Color[] facetColors =
            {
                new Color(0.14f, 0.21f, 0.34f, 1f),
                new Color(0.20f, 0.29f, 0.44f, 1f),
                new Color(0.11f, 0.16f, 0.28f, 1f),
                new Color(0.07f, 0.10f, 0.19f, 1f),
                new Color(0.08f, 0.12f, 0.22f, 1f),
                new Color(0.09f, 0.14f, 0.24f, 1f),
                new Color(0.13f, 0.19f, 0.32f, 1f),
                new Color(0.18f, 0.26f, 0.41f, 1f),
                new Color(0.24f, 0.34f, 0.52f, 1f)
            };

            Color centerFace = new Color(0.16f, 0.24f, 0.39f, 1f);
            Color brassEdge = new Color(0.92f, 0.75f, 0.34f, 1f);
            Color darkInk = new Color(0.03f, 0.04f, 0.08f, 1f);

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    if (!PointInConvexPoly(p, outerVerts))
                    {
                        px[y * s + x] = Color.clear;
                        continue;
                    }

                    Color col = centerFace;
                    if (!PointInTriangle(p, triVerts[0], triVerts[1], triVerts[2]))
                    {
                        col = facetColors[0];
                        for (int f = 0; f < facets.Length; f++)
                        {
                            if (PointInTriangle(p, facets[f][0], facets[f][1], facets[f][2]))
                            {
                                col = facetColors[f];
                                break;
                            }
                        }
                    }

                    float outerEdgeDist = MinEdgeDistancePoly(p, outerVerts);
                    float innerEdgeDist = MinEdgeDistancePoly(p, triVerts);
                    float spokeDist = MinFacetSpokeDist(p, outerVerts, triVerts);

                    if (outerEdgeDist < 1.8f)
                    {
                        col = darkInk;
                    }
                    else if (outerEdgeDist < 4.2f)
                    {
                        col = brassEdge;
                    }
                    else if (innerEdgeDist < 2.1f)
                    {
                        col = brassEdge;
                    }
                    else if (spokeDist < 1.5f)
                    {
                        col = Color.Lerp(col, brassEdge, 0.65f);
                    }

                    px[y * s + x] = col;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, true);
            _cachedD20DieSprite = Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f), 100f);
            _cachedD20DieSprite.name = "ProceduralD20Die";
            return _cachedD20DieSprite;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static bool PointInConvexPoly(Vector2 p, Vector2[] verts)
        {
            bool hasNeg = false;
            bool hasPos = false;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 a = verts[i];
                Vector2 b = verts[(i + 1) % verts.Length];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross < -0.001f) hasNeg = true;
                else if (cross > 0.001f) hasPos = true;
                if (hasNeg && hasPos) return false;
            }
            return true;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = Vector2.Dot(ab, ab);
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + t * ab);
        }

        private static float MinEdgeDistancePoly(Vector2 p, Vector2[] verts)
        {
            float best = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float d = DistancePointToSegment(p, verts[i], verts[(i + 1) % verts.Length]);
                if (d < best) best = d;
            }
            return best;
        }

        private static float MinFacetSpokeDist(Vector2 p, Vector2[] o, Vector2[] t)
        {
            float d = DistancePointToSegment(p, o[0], t[0]);
            d = Mathf.Min(d, DistancePointToSegment(p, o[1], t[0]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[1], t[1]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[2], t[1]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[3], t[1]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[3], t[2]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[4], t[2]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[5], t[2]));
            d = Mathf.Min(d, DistancePointToSegment(p, o[5], t[0]));
            return d;
        }

        private static void DrawTorch(Color[] px, int s)
        {
            Color wood = new Color(0.56f, 0.34f, 0.16f, 1f);
            Color ember = new Color(1.0f, 0.52f, 0.12f, 1f);
            Color flame = new Color(1.0f, 0.90f, 0.34f, 1f);
            for (int y = 2; y <= 8; y++)
            {
                SetPx(px, s, 7, y, wood);
                SetPx(px, s, 8, y, wood);
            }
            for (int y = 9; y <= 14; y++)
            {
                int hw = y <= 11 ? 2 : 1;
                for (int x = 8 - hw; x <= 7 + hw; x++)
                {
                    SetPx(px, s, x, y, (y >= 11 && x >= 7 && x <= 8) ? flame : ember);
                }
            }
        }
    }
}
