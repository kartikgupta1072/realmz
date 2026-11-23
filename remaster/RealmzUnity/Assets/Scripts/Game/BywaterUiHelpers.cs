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
        // Crisp dark-fantasy RPG palette
        public static readonly Color PanelBg = new Color(0.055f, 0.078f, 0.141f, 0.94f);        // #0E1424
        public static readonly Color PanelDarkBg = new Color(0.039f, 0.055f, 0.102f, 0.97f);    // #0A0E1A
        public static readonly Color CardBg = new Color(0.082f, 0.114f, 0.200f, 0.95f);
        public static readonly Color CardSelectedBg = new Color(0.180f, 0.155f, 0.090f, 0.96f);
        public static readonly Color BrassAccent = new Color(0.784f, 0.608f, 0.235f, 1.0f);     // #C89B3C
        public static readonly Color BrassHighlight = new Color(0.953f, 0.784f, 0.345f, 1.0f);
        public static readonly Color ParchmentText = new Color(0.953f, 0.910f, 0.824f, 1.0f);   // #F3E8D2
        public static readonly Color MutedSlate = new Color(0.620f, 0.680f, 0.760f, 1.0f);      // #9EAEC2
        public static readonly Color ButtonBg = new Color(0.106f, 0.149f, 0.251f, 0.96f);
        public static readonly Color ButtonActiveBg = new Color(0.275f, 0.215f, 0.090f, 0.98f);
        public static readonly Color InputBg = new Color(0.043f, 0.063f, 0.118f, 0.98f);
        public static readonly Color HpFillColor = new Color(0.130f, 0.510f, 0.240f, 1.0f);
        public static readonly Color HpLowColor = new Color(0.760f, 0.190f, 0.170f, 1.0f);
        public static readonly Color SpFillColor = new Color(0.110f, 0.450f, 0.660f, 1.0f);
        public static readonly Color BarTrackColor = new Color(0.030f, 0.040f, 0.075f, 0.94f);
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

        public static TextMeshProUGUI CreateLabel(
            Transform parent,
            string name,
            string text,
            TMP_FontAsset font,
            float fontSize = 14f,
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
            float fontSize = 14f,
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
            float fontSize = 14f)
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
            input.selectionColor = new Color(0.784f, 0.608f, 0.235f, 0.35f);
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
            rootBg.color = new Color(0.035f, 0.048f, 0.090f, 0.88f);

            AddBrassBorder(root.GetComponent<RectTransform>(), new Color(0.784f, 0.608f, 0.235f, 0.65f), 1.0f);

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

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            RectTransform fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);

            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
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
    }
}
