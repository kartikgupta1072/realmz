using System;
using System.Collections;
using System.Collections.Generic;
using Realmz.Data;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Realmz.Game
{
    public sealed partial class BywaterHudController
    {
        private const int InterceptEnemySlotCount = 4;

        private sealed class InterceptEnemySlotRefs
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI Label;
        }

        // Encounter Director Explore Top Bar Pills
        private GameObject _topClockPill;
        private Image _topClockPillBg;
        private TextMeshProUGUI _topClockText;
        private GameObject _topThreatPill;
        private Image _topThreatPillBg;
        private TextMeshProUGUI _topThreatText;
        private GameObject _topSurvivalPill;
        private Image _topSurvivalPillBg;
        private TextMeshProUGUI _topSurvivalText;
        private bool _hasSurvivalBannerState;
        private string _survivalWeatherLabel = "Clear";
        private int _survivalVisionRadius = 8;

        private ExplorationSession _cachedExploreSession;
        private string _lastExploreClockText = string.Empty;
        private int _lastExploreClockTotalMinutesInt = int.MinValue;
        private EncounterThreatTier _lastThreatTier = (EncounterThreatTier)(-1);
        private string _lastThreatLabel = string.Empty;
        private bool _hasLastTorchDarkDanger;
        private bool _lastTorchDarkDanger;

        // Pre-Battle Scouting Intercept Modal
        private GameObject _encounterInterceptModalRoot;
        private TextMeshProUGUI _interceptTitleText;
        private TextMeshProUGUI _interceptSubtitleText;
        private GameObject _interceptNightBonusBadge;
        private TextMeshProUGUI _interceptNightBonusText;
        private Image _interceptScoutBannerBg;
        private TextMeshProUGUI _interceptScoutSummaryText;
        private Transform _interceptEnemyRow;
        private readonly List<InterceptEnemySlotRefs> _interceptEnemySlots = new List<InterceptEnemySlotRefs>(InterceptEnemySlotCount);
        private Button _interceptEngageBtn;
        private TextMeshProUGUI _interceptEngageBtnLabel;
        private Button _interceptEvadeBtn;
        private TextMeshProUGUI _interceptEvadeBtnLabel;
        private Button _interceptQuickResolveBtn;
        private TextMeshProUGUI _interceptQuickResolveBtnLabel;

        // BG3-style d20 Skill Check Roll Modal
        private GameObject _diceRollModalRoot;
        private TextMeshProUGUI _diceRollTitleText;
        private TextMeshProUGUI _diceRollSubtitleText;
        private TextMeshProUGUI _diceRollAdvantageBadgeText;
        private GameObject _diceRollAdvantageBadgeRoot;
        private TextMeshProUGUI _diceRollDieText;
        private TextMeshProUGUI _diceRollBreakdownText;
        private TextMeshProUGUI _diceRollOutcomeText;
        private Coroutine _diceRollCoroutine;
        private bool _diceRollSkipRequested;
        private Action _diceRollPendingComplete;

        public bool IsEncounterInterceptOpen => _encounterInterceptModalRoot != null && _encounterInterceptModalRoot.activeSelf;
        public bool IsDiceRollOpen => _diceRollModalRoot != null && _diceRollModalRoot.activeSelf;

        public event Action OnEncounterEngageRequested;
        public event Action OnEncounterEvadeRequested;
        public event Action OnEncounterQuickResolveRequested;
        public event Action OnLightTorchFromHudRequested;

        public void ShowEncounterInterceptModal(
            EncounterScoutingReport report,
            bool canQuickResolve = false,
            string quickResolveRatioText = "",
            EncounterCheckSpec evadeSpec = null,
            EncounterCheckSpec quickResolveSpec = null)
        {
            if (!_uiBuilt || _encounterInterceptModalRoot == null || report == null)
            {
                return;
            }

            if (_partyDrawerRoot != null) _partyDrawerRoot.SetActive(false);
            if (_shopModalRoot != null) _shopModalRoot.SetActive(false);
            if (_templeModalRoot != null) _templeModalRoot.SetActive(false);
            if (_settingsModalRoot != null) _settingsModalRoot.SetActive(false);
            if (_quickResolveModalRoot != null) _quickResolveModalRoot.SetActive(false);
            if (_levelUpModalRoot != null) _levelUpModalRoot.SetActive(false);

            string locationTitle = report.Geography != null && !string.IsNullOrWhiteSpace(report.Geography.LocationLabel)
                ? report.Geography.LocationLabel
                : "Wilderness Encounter";
            _interceptTitleText.text = locationTitle;

            // Spoiler-free subtitle: diegetic terrain/mood only (never expose exact time-of-day or NO TORCH! mechanics).
            string terrainFlavor = report.Geography != null && !string.IsNullOrWhiteSpace(report.Geography.TileTerrainLabel)
                ? report.Geography.TileTerrainLabel
                : "Wilderness Trail";
            _interceptSubtitleText.text = report.IsAmbushedInDark
                ? "Shrouded in Total Darkness"
                : terrainFlavor;
            _interceptSubtitleText.color = report.IsAmbushedInDark
                ? BywaterUiHelpers.DangerRed
                : BywaterUiHelpers.MutedSlate;

            bool showNightBonus = report.RewardBonusPct > 0;
            if (_interceptNightBonusBadge != null)
            {
                _interceptNightBonusBadge.SetActive(showNightBonus);
                if (showNightBonus && _interceptNightBonusText != null)
                {
                    _interceptNightBonusText.text = $"+{report.RewardBonusPct}% Night XP & Gold";
                }
            }

            if (report.IsAmbushedInDark)
            {
                _interceptScoutSummaryText.text = "Blindside ambush from the shadows - enemies seize the initiative!";
                _interceptScoutSummaryText.color = BywaterUiHelpers.DangerRed;
                _interceptScoutBannerBg.color = new Color(0.24f, 0.05f, 0.06f, 0.96f);
                BywaterUiHelpers.SetBorderColor(_interceptScoutBannerBg.transform, BywaterUiHelpers.DangerRed);
                BywaterUiHelpers.SetBorderColor(_encounterInterceptModalRoot.transform, BywaterUiHelpers.DangerRed);
            }
            else if (report.CanAmbush)
            {
                string scoutName = string.IsNullOrWhiteSpace(report.LeadScoutName) ? "Kaelen & Vex" : report.LeadScoutName;
                _interceptScoutSummaryText.text = $"{scoutName} spotted them first! Seize high ground for Round 1 Initiative.";
                _interceptScoutSummaryText.color = BywaterUiHelpers.SuccessGreen;
                _interceptScoutBannerBg.color = new Color(0.05f, 0.16f, 0.09f, 0.95f);
                BywaterUiHelpers.SetBorderColor(_interceptScoutBannerBg.transform, BywaterUiHelpers.SuccessGreen);
                BywaterUiHelpers.SetBorderColor(_encounterInterceptModalRoot.transform, BywaterUiHelpers.BrassHighlight);
            }
            else
            {
                _interceptScoutSummaryText.text = "Enemy patrols crossed your path. Engage tactically or attempt a Stealth check.";
                _interceptScoutSummaryText.color = BywaterUiHelpers.ParchmentText;
                _interceptScoutBannerBg.color = BywaterUiHelpers.CardBg;
                BywaterUiHelpers.SetBorderColor(_interceptScoutBannerBg.transform, BywaterUiHelpers.BrassAccent);
                BywaterUiHelpers.SetBorderColor(_encounterInterceptModalRoot.transform, BywaterUiHelpers.BrassAccent);
            }

            RefreshInterceptEnemySlots(report);

            if (_interceptEngageBtnLabel != null)
            {
                _interceptEngageBtnLabel.text = report.IsAmbushedInDark
                    ? "Defend Yourself"
                    : (report.CanAmbush ? "Ambush! (+Init)" : "Engage");
            }
            BywaterUiHelpers.SetButtonColors(_interceptEngageBtn, active: true);

            if (_interceptEvadeBtn != null)
            {
                if (report.IsAmbushedInDark)
                {
                    _interceptEvadeBtn.interactable = false;
                    if (_interceptEvadeBtnLabel != null)
                    {
                        _interceptEvadeBtnLabel.text = "Evade (Blocked)";
                    }
                }
                else
                {
                    _interceptEvadeBtn.interactable = report.CanEvade;
                    if (_interceptEvadeBtnLabel != null)
                    {
                        EncounterCheckSpec effEvade = evadeSpec ?? EncounterDirector.BuildEvadeCheckSpec(report, null);
                        string advTag = effEvade.AdvantageState == RollAdvantageState.Advantage
                            ? " ADV"
                            : (effEvade.AdvantageState == RollAdvantageState.Disadvantage ? " DIS" : string.Empty);
                        _interceptEvadeBtnLabel.text = $"Evade [{effEvade.StatLabel} DC {effEvade.Dc}{advTag}]";
                    }
                }
            }

            if (_interceptQuickResolveBtn != null)
            {
                bool showQuickResolve = canQuickResolve && !report.IsAmbushedInDark;
                _interceptQuickResolveBtn.gameObject.SetActive(showQuickResolve);
                if (showQuickResolve && _interceptQuickResolveBtnLabel != null)
                {
                    if (quickResolveSpec != null)
                    {
                        _interceptQuickResolveBtnLabel.text = $"Quick Resolve [{quickResolveSpec.TierLabel} | DC {quickResolveSpec.Dc}]";
                    }
                    else
                    {
                        _interceptQuickResolveBtnLabel.text = string.IsNullOrWhiteSpace(quickResolveRatioText)
                            ? "Quick Resolve"
                            : $"Quick Resolve ({quickResolveRatioText})";
                    }
                }
            }

            _encounterInterceptModalRoot.SetActive(true);
            _encounterInterceptModalRoot.transform.SetAsLastSibling();
        }

        public void ShowDiceRollCheck(EncounterCheckSpec spec, EncounterCheckRollResult result, Action onComplete)
        {
            if (!_uiBuilt || _diceRollModalRoot == null || result == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (_diceRollCoroutine != null)
            {
                StopCoroutine(_diceRollCoroutine);
                _diceRollCoroutine = null;
            }

            _diceRollSkipRequested = false;
            _diceRollPendingComplete = onComplete;

            string title = spec != null && !string.IsNullOrWhiteSpace(spec.CheckTitle) ? spec.CheckTitle : "Skill Check";
            string stat = spec != null ? spec.StatLabel : "D20";
            string tier = spec != null ? $" ({spec.TierLabel})" : string.Empty;
            _diceRollTitleText.text = $"{title}{tier}";
            _diceRollSubtitleText.text = $"Target Difficulty Class: DC {result.Dc}  |  {stat} Check";

            bool hasAdv = result.AdvantageState == RollAdvantageState.Advantage;
            bool hasDis = result.AdvantageState == RollAdvantageState.Disadvantage;
            _diceRollAdvantageBadgeRoot.SetActive(hasAdv || hasDis);
            if (hasAdv)
            {
                _diceRollAdvantageBadgeText.text = "ADVANTAGE (Roll 2d20, Keep Highest)";
                _diceRollAdvantageBadgeText.color = BywaterUiHelpers.SuccessGreen;
            }
            else if (hasDis)
            {
                _diceRollAdvantageBadgeText.text = "DISADVANTAGE (Roll 2d20, Keep Lowest)";
                _diceRollAdvantageBadgeText.color = BywaterUiHelpers.DangerRed;
            }

            string modBreakdown = spec != null && !string.IsNullOrWhiteSpace(spec.ModifierBreakdown)
                ? spec.ModifierBreakdown
                : (result.Modifier >= 0 ? $"+{result.Modifier}" : result.Modifier.ToString());
            _diceRollBreakdownText.text = $"{modBreakdown}";
            _diceRollOutcomeText.text = "Rolling d20...";
            _diceRollOutcomeText.color = BywaterUiHelpers.MutedSlate;
            _diceRollDieText.color = BywaterUiHelpers.BrassHighlight;
            BywaterUiHelpers.SetBorderColor(_diceRollModalRoot.transform, BywaterUiHelpers.BrassHighlight);

            _diceRollModalRoot.SetActive(true);
            _diceRollModalRoot.transform.SetAsLastSibling();

            if (Application.isBatchMode || !Application.isPlaying || !gameObject.activeInHierarchy)
            {
                FinalizeDiceRollPresentation(result);
                Action done = _diceRollPendingComplete;
                _diceRollPendingComplete = null;
                _diceRollModalRoot.SetActive(false);
                done?.Invoke();
                return;
            }

            _diceRollCoroutine = StartCoroutine(AnimateDiceRollRoutine(result));
        }

        private void FinalizeDiceRollPresentation(EncounterCheckRollResult result)
        {
            string modSign = result.Modifier >= 0 ? $"+ {result.Modifier}" : $"- {Math.Abs(result.Modifier)}";
            string twinInfo = result.AdvantageState != RollAdvantageState.Normal
                ? $"  [2d20: {result.D20Roll1}, {result.D20Roll2}]"
                : string.Empty;

            _diceRollDieText.text = result.SelectedD20.ToString();
            _diceRollBreakdownText.text = $"d20 ({result.SelectedD20}) {modSign}  =  {result.Total} vs DC {result.Dc}{twinInfo}";

            if (result.IsNatural20)
            {
                _diceRollDieText.color = new Color(1f, 0.85f, 0.25f, 1f);
                _diceRollOutcomeText.text = $"NATURAL 20! {result.Summary}";
                _diceRollOutcomeText.color = BywaterUiHelpers.SuccessGreen;
                BywaterUiHelpers.SetBorderColor(_diceRollModalRoot.transform, new Color(1f, 0.85f, 0.25f, 1f));
            }
            else if (result.IsNatural1)
            {
                _diceRollDieText.color = BywaterUiHelpers.DangerRed;
                _diceRollOutcomeText.text = $"CRITICAL MISS (Nat 1)! {result.Summary}";
                _diceRollOutcomeText.color = BywaterUiHelpers.DangerRed;
                BywaterUiHelpers.SetBorderColor(_diceRollModalRoot.transform, BywaterUiHelpers.DangerRed);
            }
            else if (result.Success)
            {
                _diceRollDieText.color = BywaterUiHelpers.SuccessGreen;
                _diceRollOutcomeText.text = $"SUCCESS! {result.Summary}";
                _diceRollOutcomeText.color = BywaterUiHelpers.SuccessGreen;
                BywaterUiHelpers.SetBorderColor(_diceRollModalRoot.transform, BywaterUiHelpers.SuccessGreen);
            }
            else
            {
                _diceRollDieText.color = BywaterUiHelpers.DangerRed;
                _diceRollOutcomeText.text = $"FAILED! {result.Summary}";
                _diceRollOutcomeText.color = BywaterUiHelpers.DangerRed;
                BywaterUiHelpers.SetBorderColor(_diceRollModalRoot.transform, BywaterUiHelpers.DangerRed);
            }
        }

        private IEnumerator AnimateDiceRollRoutine(EncounterCheckRollResult result)
        {
            float rollDuration = 0.48f;
            float elapsed = 0f;
            int ticker = 0;

            while (elapsed < rollDuration && !_diceRollSkipRequested)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                {
                    _diceRollSkipRequested = true;
                    break;
                }

                ticker = (ticker + 3) % 20 + 1;
                _diceRollDieText.text = ticker.ToString();
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            FinalizeDiceRollPresentation(result);

            float holdDuration = _diceRollSkipRequested ? 0.12f : 0.65f;
            float held = 0f;
            while (held < holdDuration && !_diceRollSkipRequested)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                {
                    break;
                }

                held += Time.unscaledDeltaTime;
                yield return null;
            }

            _diceRollCoroutine = null;
            _diceRollModalRoot.SetActive(false);
            Action done = _diceRollPendingComplete;
            _diceRollPendingComplete = null;
            done?.Invoke();
        }

        public void HideEncounterInterceptModal()
        {
            if (_encounterInterceptModalRoot != null)
            {
                _encounterInterceptModalRoot.SetActive(false);
            }
        }

        private void BuildEncounterDirectorTopBarPills(Transform bar)
        {
            // Clock Pill
            RectTransform clockPillRt = BywaterUiHelpers.CreatePanel(
                bar,
                "ClockPill",
                BywaterUiHelpers.CardBg,
                withBrassOutline: true,
                outlinePx: 1.2f);
            _topClockPill = clockPillRt.gameObject;
            _topClockPillBg = clockPillRt.GetComponent<Image>();
            LayoutElement clockLe = _topClockPill.AddComponent<LayoutElement>();
            clockLe.preferredWidth = 248f;
            clockLe.minWidth = 228f;

            _topClockText = BywaterUiHelpers.CreateLabel(
                clockPillRt,
                "ClockText",
                "Day 1 - 08:00 AM (Morning)",
                _font,
                15f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            _topClockText.enableAutoSizing = false;
            _topClockText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_topClockText);
            _topClockText.rectTransform.anchorMin = Vector2.zero;
            _topClockText.rectTransform.anchorMax = Vector2.one;
            _topClockText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _topClockText.rectTransform.offsetMax = new Vector2(-6f, -2f);

            AttachTooltipCallbacks(_topClockPill, BuildClockPillTooltip);

            // Threat Pill
            RectTransform threatPillRt = BywaterUiHelpers.CreatePanel(
                bar,
                "ThreatPill",
                BywaterUiHelpers.CardBg,
                withBrassOutline: true,
                outlinePx: 1.2f);
            _topThreatPill = threatPillRt.gameObject;
            _topThreatPillBg = threatPillRt.GetComponent<Image>();
            LayoutElement threatLe = _topThreatPill.AddComponent<LayoutElement>();
            threatLe.preferredWidth = 152f;
            threatLe.minWidth = 152f;

            _topThreatText = BywaterUiHelpers.CreateLabel(
                threatPillRt,
                "ThreatText",
                "SAFE",
                _font,
                15f,
                BywaterUiHelpers.SuccessGreen,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            _topThreatText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_topThreatText);
            _topThreatText.rectTransform.anchorMin = Vector2.zero;
            _topThreatText.rectTransform.anchorMax = Vector2.one;
            _topThreatText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _topThreatText.rectTransform.offsetMax = new Vector2(-6f, -2f);

            AttachTooltipCallbacks(_topThreatPill, BuildThreatPillTooltip);

            // Survival Weather & Vision Pill
            RectTransform survivalPillRt = BywaterUiHelpers.CreatePanel(
                bar,
                "SurvivalPill",
                BywaterUiHelpers.CardBg,
                withBrassOutline: true,
                outlinePx: 1.2f);
            _topSurvivalPill = survivalPillRt.gameObject;
            _topSurvivalPillBg = survivalPillRt.GetComponent<Image>();
            LayoutElement survivalLe = _topSurvivalPill.AddComponent<LayoutElement>();
            survivalLe.preferredWidth = 148f;
            survivalLe.minWidth = 132f;

            _topSurvivalText = BywaterUiHelpers.CreateLabel(
                survivalPillRt,
                "SurvivalText",
                "Clear | Vis 8",
                _font,
                15f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            _topSurvivalText.enableAutoSizing = true;
            _topSurvivalText.fontSizeMin = 11f;
            _topSurvivalText.fontSizeMax = 15f;
            _topSurvivalText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_topSurvivalText);
            _topSurvivalText.rectTransform.anchorMin = Vector2.zero;
            _topSurvivalText.rectTransform.anchorMax = Vector2.one;
            _topSurvivalText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _topSurvivalText.rectTransform.offsetMax = new Vector2(-6f, -2f);

            AttachTooltipCallbacks(_topSurvivalPill, BuildSurvivalPillTooltip);
            if (_hasSurvivalBannerState)
            {
                ApplySurvivalBannerVisuals();
            }
            else
            {
                _topSurvivalPill.SetActive(false);
            }

            // Make Torch label clickable and tooltip-equipped
            if (_topTorchText != null)
            {
                _topTorchText.raycastTarget = true;
                Button torchBtn = _topTorchText.gameObject.GetComponent<Button>()
                    ?? _topTorchText.gameObject.AddComponent<Button>();
                torchBtn.targetGraphic = _topTorchText;
                ColorBlock colors = torchBtn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.16f, 1.12f, 0.88f, 1f);
                colors.pressedColor = new Color(0.80f, 0.72f, 0.58f, 1f);
                colors.disabledColor = new Color(0.60f, 0.60f, 0.60f, 0.70f);
                colors.colorMultiplier = 1f;
                torchBtn.colors = colors;
                torchBtn.onClick.RemoveAllListeners();
                torchBtn.onClick.AddListener(() => OnLightTorchFromHudRequested?.Invoke());

                AttachTooltipCallbacks(_topTorchText.gameObject, BuildTorchHintTooltip);
            }
        }

        private void ApplyEncounterDirectorTopBarVisibility(bool inCombat)
        {
            _topClockPill?.SetActive(!inCombat);
            _topThreatPill?.SetActive(!inCombat);
            _topSurvivalPill?.SetActive(!inCombat && _hasSurvivalBannerState);
        }

        public void SetSurvivalBannerState(string weatherLabel, int visionRadius)
        {
            if (string.IsNullOrWhiteSpace(weatherLabel) && visionRadius <= 0)
            {
                _hasSurvivalBannerState = false;
                _survivalWeatherLabel = "Clear";
                _survivalVisionRadius = 0;
                if (_topSurvivalPill != null)
                {
                    _topSurvivalPill.SetActive(false);
                }
                return;
            }

            _hasSurvivalBannerState = true;
            _survivalWeatherLabel = string.IsNullOrWhiteSpace(weatherLabel) ? "Clear" : weatherLabel.Trim();
            _survivalVisionRadius = Math.Max(0, visionRadius);

            if (_uiBuilt && _topSurvivalPill != null)
            {
                ApplySurvivalBannerVisuals();
            }
        }

        private void ApplySurvivalBannerVisuals()
        {
            if (_topSurvivalPill == null || _topSurvivalText == null)
            {
                return;
            }

            _topSurvivalText.text = $"{_survivalWeatherLabel} | Vis {_survivalVisionRadius}";

            string lower = _survivalWeatherLabel.ToLowerInvariant();
            Color textColor = BywaterUiHelpers.ParchmentText;
            Color borderColor = BywaterUiHelpers.BrassAccent;
            Color bgColor = BywaterUiHelpers.CardBg;

            if (lower.Contains("storm") || lower.Contains("blizzard") || lower.Contains("tempest"))
            {
                textColor = BywaterUiHelpers.DangerRed;
                borderColor = BywaterUiHelpers.DangerRed;
                bgColor = new Color(0.18f, 0.06f, 0.08f, 0.96f);
            }
            else if (lower.Contains("rain") || lower.Contains("fog") || lower.Contains("mist") || lower.Contains("snow"))
            {
                textColor = BywaterUiHelpers.InfoCyan;
                borderColor = BywaterUiHelpers.InfoCyan;
                bgColor = new Color(0.06f, 0.11f, 0.19f, 0.96f);
            }
            else if (lower.Contains("overcast") || lower.Contains("wind") || lower.Contains("dusk") || lower.Contains("night"))
            {
                textColor = BywaterUiHelpers.BrassHighlight;
                borderColor = BywaterUiHelpers.BrassAccent;
            }

            _topSurvivalText.color = textColor;
            BywaterUiHelpers.SetBorderColor(_topSurvivalPill.transform, borderColor);
            if (_topSurvivalPillBg != null)
            {
                _topSurvivalPillBg.color = bgColor;
            }

            _topSurvivalPill.SetActive(!_isCombatHudActive && _hasSurvivalBannerState);
        }

        private void RefreshEncounterDirectorExploreHud(ExplorationSession session)
        {
            if (session == null)
            {
                return;
            }

            _cachedExploreSession = session;
            WorldClock clock = session.CurrentWorldClock;
            bool isDungeon = session.CurrentMap != null && session.CurrentMap.IsDungeon;
            int torchTurns = session.Party != null ? session.Party.TorchTurnsRemaining : 0;

            if (!string.Equals(_lastExploreClockText, clock.DisplayLabel, StringComparison.Ordinal) ||
                _lastExploreClockTotalMinutesInt != (int)clock.TotalMinutes)
            {
                _lastExploreClockText = clock.DisplayLabel;
                _lastExploreClockTotalMinutesInt = (int)clock.TotalMinutes;
                if (_topClockText != null)
                {
                    _topClockText.text = clock.DisplayLabel;
                    _topClockText.color = ResolveClockPhaseColor(clock.Phase);
                }

                if (_topClockPill != null)
                {
                    Color clockBorder = clock.IsNight
                        ? new Color(0.38f, 0.58f, 0.88f, 1f)
                        : BywaterUiHelpers.BrassAccent;
                    BywaterUiHelpers.SetBorderColor(_topClockPill.transform, clockBorder);
                }
                if (_topClockPillBg != null)
                {
                    _topClockPillBg.color = clock.IsNight
                        ? new Color(0.05f, 0.08f, 0.16f, 0.96f)
                        : BywaterUiHelpers.CardBg;
                }
            }

            EncounterThreatAssessment threat = session.GetCurrentThreatAssessment();
            if (threat != null &&
                (_lastThreatTier != threat.Tier || !string.Equals(_lastThreatLabel, threat.Label, StringComparison.Ordinal)))
            {
                _lastThreatTier = threat.Tier;
                _lastThreatLabel = threat.Label;
                ApplyThreatPillStyle(threat);
            }

            bool darkDangerTorch = (clock.IsNight || isDungeon) && torchTurns <= 0;
            if (!_hasLastTorchDarkDanger || _lastTorchDarkDanger != darkDangerTorch)
            {
                _hasLastTorchDarkDanger = true;
                _lastTorchDarkDanger = darkDangerTorch;
                if (_topTorchText != null)
                {
                    _topTorchText.color = darkDangerTorch
                        ? new Color(1.0f, 0.34f, 0.22f, 1f)
                        : new Color(1.0f, 0.72f, 0.32f, 1f);
                }
            }
        }

        private void BuildEncounterInterceptModal(Transform parent)
        {
            RectTransform modal = BywaterUiHelpers.CreatePanel(
                parent,
                "EncounterInterceptModal",
                BywaterUiHelpers.PanelDarkBg,
                withBrassOutline: true,
                outlinePx: 2.2f);
            modal.anchorMin = new Vector2(0.5f, 0.5f);
            modal.anchorMax = new Vector2(0.5f, 0.5f);
            modal.pivot = new Vector2(0.5f, 0.5f);
            modal.anchoredPosition = Vector2.zero;
            modal.sizeDelta = new Vector2(660f, 450f);
            _encounterInterceptModalRoot = modal.gameObject;

            VerticalLayoutGroup vlg = modal.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 18, 18);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 1. Header banner
            GameObject headerGo = new GameObject("InterceptHeader", typeof(RectTransform), typeof(VerticalLayoutGroup));
            headerGo.transform.SetParent(modal, false);
            headerGo.AddComponent<LayoutElement>().preferredHeight = 72f;
            VerticalLayoutGroup headerVlg = headerGo.GetComponent<VerticalLayoutGroup>();
            headerVlg.spacing = 4f;
            headerVlg.childControlWidth = true;
            headerVlg.childControlHeight = true;
            headerVlg.childForceExpandWidth = true;
            headerVlg.childForceExpandHeight = false;

            _interceptTitleText = BywaterUiHelpers.CreateLabel(
                headerGo.transform,
                "InterceptTitle",
                "Wilderness Encounter",
                _font,
                22f,
                BywaterUiHelpers.BrassHighlight,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            _interceptTitleText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_interceptTitleText);
            _interceptTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            GameObject subRow = new GameObject("InterceptSubtitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            subRow.transform.SetParent(headerGo.transform, false);
            subRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            HorizontalLayoutGroup subHlg = subRow.GetComponent<HorizontalLayoutGroup>();
            subHlg.spacing = 10f;
            subHlg.childControlWidth = true;
            subHlg.childControlHeight = true;
            subHlg.childForceExpandWidth = false;
            subHlg.childForceExpandHeight = false;
            subHlg.childAlignment = TextAnchor.MiddleLeft;

            _interceptSubtitleText = BywaterUiHelpers.CreateLabel(
                subRow.transform,
                "InterceptSubtitle",
                "Day 1 - Noon  |  MODERATE  |  Torch: 0t",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            _interceptSubtitleText.overflowMode = TextOverflowModes.Truncate;
            BywaterUiHelpers.AddTextDropShadow(_interceptSubtitleText);
            LayoutElement subLe = _interceptSubtitleText.gameObject.AddComponent<LayoutElement>();
            subLe.flexibleWidth = 1f;
            subLe.preferredHeight = 28f;

            RectTransform nightBadgeRt = BywaterUiHelpers.CreatePanel(
                subRow.transform,
                "InterceptNightBonusBadge",
                new Color(0.08f, 0.14f, 0.28f, 0.96f),
                withBrassOutline: true,
                outlinePx: 1.2f);
            BywaterUiHelpers.SetBorderColor(nightBadgeRt, BywaterUiHelpers.InfoCyan);
            LayoutElement badgeLe = nightBadgeRt.gameObject.AddComponent<LayoutElement>();
            badgeLe.preferredWidth = 205f;
            badgeLe.minWidth = 205f;
            badgeLe.preferredHeight = 28f;
            _interceptNightBonusBadge = nightBadgeRt.gameObject;

            _interceptNightBonusText = BywaterUiHelpers.CreateLabel(
                nightBadgeRt,
                "InterceptNightBonusText",
                "+25% Night XP & Gold",
                _font,
                15f,
                BywaterUiHelpers.InfoCyan,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_interceptNightBonusText);
            _interceptNightBonusText.rectTransform.anchorMin = Vector2.zero;
            _interceptNightBonusText.rectTransform.anchorMax = Vector2.one;
            _interceptNightBonusText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _interceptNightBonusText.rectTransform.offsetMax = new Vector2(-6f, -2f);
            _interceptNightBonusBadge.SetActive(false);

            // 2. Scouting banner
            RectTransform scoutBannerRt = BywaterUiHelpers.CreatePanel(
                modal,
                "InterceptScoutBanner",
                BywaterUiHelpers.CardBg,
                withBrassOutline: true,
                outlinePx: 1.2f);
            scoutBannerRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
            _interceptScoutBannerBg = scoutBannerRt.GetComponent<Image>();

            _interceptScoutSummaryText = BywaterUiHelpers.CreateLabel(
                scoutBannerRt,
                "InterceptScoutSummary",
                "Kaelen & Vex scouted the approaching hostiles.",
                _font,
                15.5f,
                BywaterUiHelpers.ParchmentText,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Normal,
                wrap: true);
            BywaterUiHelpers.AddTextDropShadow(_interceptScoutSummaryText);
            _interceptScoutSummaryText.rectTransform.anchorMin = Vector2.zero;
            _interceptScoutSummaryText.rectTransform.anchorMax = Vector2.one;
            _interceptScoutSummaryText.rectTransform.offsetMin = new Vector2(14f, 8f);
            _interceptScoutSummaryText.rectTransform.offsetMax = new Vector2(-14f, -8f);

            // 3. Scouted Enemies strip (4 pooled preview slots)
            RectTransform enemyStripRt = BywaterUiHelpers.CreatePanel(
                modal,
                "InterceptEnemyStrip",
                new Color(0.045f, 0.065f, 0.115f, 0.95f),
                withBrassOutline: true,
                outlinePx: 1.2f);
            LayoutElement stripLe = enemyStripRt.gameObject.AddComponent<LayoutElement>();
            stripLe.flexibleHeight = 1f;
            stripLe.minHeight = 152f;

            VerticalLayoutGroup stripVlg = enemyStripRt.gameObject.AddComponent<VerticalLayoutGroup>();
            stripVlg.padding = new RectOffset(12, 12, 10, 10);
            stripVlg.spacing = 8f;
            stripVlg.childControlWidth = true;
            stripVlg.childControlHeight = true;
            stripVlg.childForceExpandWidth = true;
            stripVlg.childForceExpandHeight = false;

            TextMeshProUGUI stripHeader = BywaterUiHelpers.CreateLabel(
                enemyStripRt,
                "InterceptEnemyHeader",
                "Scouted Enemies",
                _font,
                15f,
                BywaterUiHelpers.MutedSlate,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                wrap: false);
            stripHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            GameObject enemyRowGo = new GameObject("InterceptEnemyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            enemyRowGo.transform.SetParent(enemyStripRt, false);
            enemyRowGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _interceptEnemyRow = enemyRowGo.transform;

            HorizontalLayoutGroup rowHlg = enemyRowGo.GetComponent<HorizontalLayoutGroup>();
            rowHlg.spacing = 10f;
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = true;
            rowHlg.childForceExpandHeight = true;

            _interceptEnemySlots.Clear();
            for (int i = 0; i < InterceptEnemySlotCount; i++)
            {
                RectTransform slotRt = BywaterUiHelpers.CreatePanel(
                    _interceptEnemyRow,
                    $"InterceptEnemySlot_{i}",
                    BywaterUiHelpers.CardBg,
                    withBrassOutline: true,
                    outlinePx: 1.2f);
                VerticalLayoutGroup slotVlg = slotRt.gameObject.AddComponent<VerticalLayoutGroup>();
                slotVlg.padding = new RectOffset(8, 8, 10, 8);
                slotVlg.spacing = 6f;
                slotVlg.childAlignment = TextAnchor.MiddleCenter;
                slotVlg.childControlWidth = true;
                slotVlg.childControlHeight = true;
                slotVlg.childForceExpandWidth = false;
                slotVlg.childForceExpandHeight = false;

                Image icon = BywaterUiHelpers.CreateImage(slotRt, "EnemyIcon", null, Color.white, preserveAspect: true);
                icon.raycastTarget = false;
                LayoutElement iconLe = icon.gameObject.AddComponent<LayoutElement>();
                iconLe.preferredWidth = 44f;
                iconLe.preferredHeight = 44f;
                iconLe.minWidth = 44f;
                iconLe.minHeight = 44f;

                TextMeshProUGUI label = BywaterUiHelpers.CreateLabel(
                    slotRt,
                    "EnemyLabel",
                    string.Empty,
                    _font,
                    15f,
                    BywaterUiHelpers.ParchmentText,
                    TextAlignmentOptions.Center,
                    FontStyles.Bold,
                    wrap: true);
                label.overflowMode = TextOverflowModes.Truncate;
                BywaterUiHelpers.AddTextDropShadow(label);
                LayoutElement labelLe = label.gameObject.AddComponent<LayoutElement>();
                labelLe.preferredWidth = 132f;
                labelLe.preferredHeight = 36f;

                slotRt.gameObject.SetActive(false);
                _interceptEnemySlots.Add(new InterceptEnemySlotRefs
                {
                    Root = slotRt.gameObject,
                    Icon = icon,
                    Label = label
                });
            }

            // 4. Action button row
            GameObject btnRow = new GameObject("InterceptButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnRow.transform.SetParent(modal, false);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 50f;
            HorizontalLayoutGroup bHlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            bHlg.spacing = 12f;
            bHlg.childControlWidth = true;
            bHlg.childControlHeight = true;
            bHlg.childForceExpandWidth = true;

            _interceptEngageBtn = BywaterUiHelpers.CreateButton(
                btnRow.transform,
                "InterceptEngageBtn",
                "Engage",
                _font,
                () =>
                {
                    HideEncounterInterceptModal();
                    OnEncounterEngageRequested?.Invoke();
                },
                16f);
            _interceptEngageBtnLabel = _interceptEngageBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            BywaterUiHelpers.SetButtonColors(_interceptEngageBtn, active: true);

            _interceptEvadeBtn = BywaterUiHelpers.CreateButton(
                btnRow.transform,
                "InterceptEvadeBtn",
                "Evade (50%)",
                _font,
                () =>
                {
                    HideEncounterInterceptModal();
                    OnEncounterEvadeRequested?.Invoke();
                },
                16f);
            _interceptEvadeBtnLabel = _interceptEvadeBtn.GetComponentInChildren<TextMeshProUGUI>(true);

            _interceptQuickResolveBtn = BywaterUiHelpers.CreateButton(
                btnRow.transform,
                "InterceptQuickResolveBtn",
                "Quick Resolve",
                _font,
                () =>
                {
                    HideEncounterInterceptModal();
                    OnEncounterQuickResolveRequested?.Invoke();
                },
                15.5f);
            _interceptQuickResolveBtnLabel = _interceptQuickResolveBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            _interceptQuickResolveBtn.gameObject.SetActive(false);

            BuildDiceRollOverlay(parent);
        }

        private void BuildDiceRollOverlay(Transform parent)
        {
            RectTransform d20Root = BywaterUiHelpers.CreatePanel(
                parent,
                "DiceRollOverlayModal",
                BywaterUiHelpers.PanelDarkBg,
                withBrassOutline: true,
                outlinePx: 2.4f);
            d20Root.anchorMin = new Vector2(0.5f, 0.5f);
            d20Root.anchorMax = new Vector2(0.5f, 0.5f);
            d20Root.pivot = new Vector2(0.5f, 0.5f);
            d20Root.anchoredPosition = Vector2.zero;
            d20Root.sizeDelta = new Vector2(440f, 340f);
            _diceRollModalRoot = d20Root.gameObject;

            VerticalLayoutGroup dVlg = d20Root.gameObject.AddComponent<VerticalLayoutGroup>();
            dVlg.padding = new RectOffset(20, 20, 16, 16);
            dVlg.spacing = 10f;
            dVlg.childAlignment = TextAnchor.MiddleCenter;
            dVlg.childControlWidth = true;
            dVlg.childControlHeight = true;
            dVlg.childForceExpandWidth = true;
            dVlg.childForceExpandHeight = false;

            _diceRollTitleText = BywaterUiHelpers.CreateLabel(
                d20Root, "DiceRollTitle", "Skill Check", _font, 20f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            _diceRollTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _diceRollSubtitleText = BywaterUiHelpers.CreateLabel(
                d20Root, "DiceRollSubtitle", "Target DC 12", _font, 15f, BywaterUiHelpers.MutedSlate, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            _diceRollSubtitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            RectTransform advRt = BywaterUiHelpers.CreatePanel(
                d20Root, "DiceRollAdvBadge", new Color(0.08f, 0.14f, 0.24f, 0.96f), withBrassOutline: true, outlinePx: 1f);
            advRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            _diceRollAdvantageBadgeRoot = advRt.gameObject;
            _diceRollAdvantageBadgeText = BywaterUiHelpers.CreateLabel(
                advRt, "DiceRollAdvText", "ADVANTAGE", _font, 15f, BywaterUiHelpers.SuccessGreen, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            _diceRollAdvantageBadgeText.rectTransform.anchorMin = Vector2.zero;
            _diceRollAdvantageBadgeText.rectTransform.anchorMax = Vector2.one;
            _diceRollAdvantageBadgeText.rectTransform.offsetMin = Vector2.zero;
            _diceRollAdvantageBadgeText.rectTransform.offsetMax = Vector2.zero;
            _diceRollAdvantageBadgeRoot.SetActive(false);

            RectTransform dieCard = BywaterUiHelpers.CreatePanel(
                d20Root, "DiceRollD20Card", new Color(0.05f, 0.08f, 0.14f, 0.98f), withBrassOutline: true, outlinePx: 2f);
            BywaterUiHelpers.SetBorderColor(dieCard, BywaterUiHelpers.BrassHighlight);
            LayoutElement dieCardLe = dieCard.gameObject.AddComponent<LayoutElement>();
            dieCardLe.preferredWidth = 104f;
            dieCardLe.preferredHeight = 96f;

            Button skipBtn = dieCard.gameObject.AddComponent<Button>();
            skipBtn.transition = Selectable.Transition.None;
            skipBtn.onClick.AddListener(() => _diceRollSkipRequested = true);

            _diceRollDieText = BywaterUiHelpers.CreateLabel(
                dieCard, "DiceRollD20Number", "20", _font, 46f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: false);
            BywaterUiHelpers.AddTextDropShadow(_diceRollDieText);
            _diceRollDieText.rectTransform.anchorMin = Vector2.zero;
            _diceRollDieText.rectTransform.anchorMax = Vector2.one;
            _diceRollDieText.rectTransform.offsetMin = Vector2.zero;
            _diceRollDieText.rectTransform.offsetMax = Vector2.zero;

            _diceRollBreakdownText = BywaterUiHelpers.CreateLabel(
                d20Root, "DiceRollBreakdown", "DEX +3", _font, 15.5f, BywaterUiHelpers.ParchmentText, TextAlignmentOptions.Center, FontStyles.Normal, wrap: true);
            _diceRollBreakdownText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _diceRollOutcomeText = BywaterUiHelpers.CreateLabel(
                d20Root, "DiceRollOutcome", "Rolling...", _font, 16.5f, BywaterUiHelpers.BrassHighlight, TextAlignmentOptions.Center, FontStyles.Bold, wrap: true);
            _diceRollOutcomeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            _diceRollModalRoot.SetActive(false);
        }

        private void RefreshInterceptEnemySlots(EncounterScoutingReport report)
        {
            IReadOnlyList<EncounterUnitCount> counts = report.MonsterCounts;
            int totalGroups = counts != null ? counts.Count : 0;

            for (int i = 0; i < _interceptEnemySlots.Count; i++)
            {
                InterceptEnemySlotRefs slot = _interceptEnemySlots[i];
                if (totalGroups == 0)
                {
                    if (i == 0)
                    {
                        slot.Root.SetActive(true);
                        slot.Icon.sprite = BywaterUiHelpers.GetProceduralIconSprite("sword");
                        slot.Icon.color = report.IsAmbushedInDark
                            ? new Color(0.08f, 0.10f, 0.16f, 0.92f)
                            : BywaterUiHelpers.BrassHighlight;
                        slot.Label.text = report.IsAmbushedInDark ? "??x Shadow Stalkers" : "Hostile Band";
                        slot.Label.color = report.IsAmbushedInDark
                            ? BywaterUiHelpers.MutedSlate
                            : BywaterUiHelpers.ParchmentText;
                    }
                    else
                    {
                        slot.Root.SetActive(false);
                    }
                    continue;
                }

                if (i >= totalGroups)
                {
                    slot.Root.SetActive(false);
                    continue;
                }

                slot.Root.SetActive(true);
                EncounterUnitCount unitCount = counts[i];
                MonsterDefinition def = unitCount.Definition;

                Sprite sprite = null;
                if (_catalog != null && def != null)
                {
                    sprite = _catalog.GetMonsterPortrait(def.Id, def.IconId)
                        ?? _catalog.GetMonsterArt(def.Id, def.IconId)?.sprite;
                    if (sprite == null && !string.IsNullOrWhiteSpace(def.Name))
                    {
                        sprite = _catalog.GetMonsterPortrait(def.Name, def.IconId)
                            ?? _catalog.GetMonsterArt(def.Name, def.IconId)?.sprite;
                    }
                }
                if (sprite == null)
                {
                    sprite = BywaterUiHelpers.GetProceduralIconSprite("sword");
                }

                slot.Icon.sprite = sprite;

                if (report.IsAmbushedInDark)
                {
                    slot.Icon.color = new Color(0.08f, 0.10f, 0.16f, 0.92f);
                    slot.Label.text = "??x Shadow Stalkers";
                    slot.Label.color = BywaterUiHelpers.MutedSlate;
                }
                else
                {
                    slot.Icon.color = Color.white;
                    string enemyName = def != null && !string.IsNullOrWhiteSpace(def.Name) ? def.Name : "Hostile";
                    slot.Label.text = $"{unitCount.Count}x {enemyName}";
                    slot.Label.color = BywaterUiHelpers.ParchmentText;
                }
            }
        }

        private void ApplyThreatPillStyle(EncounterThreatAssessment threat)
        {
            if (_topThreatText == null)
            {
                return;
            }

            Color textCol;
            Color bgCol;
            Color borderCol;
            string displayLabel = threat.Label;

            switch (threat.Tier)
            {
                case EncounterThreatTier.Safe:
                    textCol = BywaterUiHelpers.SuccessGreen;
                    bgCol = new Color(0.05f, 0.15f, 0.08f, 0.95f);
                    borderCol = new Color(0.24f, 0.70f, 0.38f, 1f);
                    break;
                case EncounterThreatTier.Low:
                    textCol = BywaterUiHelpers.InfoCyan;
                    bgCol = new Color(0.06f, 0.12f, 0.18f, 0.95f);
                    borderCol = new Color(0.32f, 0.64f, 0.84f, 1f);
                    break;
                case EncounterThreatTier.Moderate:
                    textCol = BywaterUiHelpers.BrassHighlight;
                    bgCol = new Color(0.18f, 0.13f, 0.04f, 0.95f);
                    borderCol = BywaterUiHelpers.BrassAccent;
                    break;
                case EncounterThreatTier.High:
                    textCol = new Color(0.98f, 0.52f, 0.24f, 1f);
                    bgCol = new Color(0.24f, 0.09f, 0.04f, 0.96f);
                    borderCol = new Color(0.92f, 0.42f, 0.18f, 1f);
                    break;
                case EncounterThreatTier.DarkDanger:
                    textCol = new Color(1.0f, 0.32f, 0.32f, 1f);
                    bgCol = new Color(0.30f, 0.04f, 0.06f, 0.98f);
                    borderCol = BywaterUiHelpers.DangerRed;
                    displayLabel = "DARK DANGER!";
                    break;
                default:
                    textCol = BywaterUiHelpers.ParchmentText;
                    bgCol = BywaterUiHelpers.CardBg;
                    borderCol = BywaterUiHelpers.BrassAccent;
                    break;
            }

            _topThreatText.text = displayLabel;
            _topThreatText.color = textCol;
            if (_topThreatPillBg != null)
            {
                _topThreatPillBg.color = bgCol;
            }
            if (_topThreatPill != null)
            {
                BywaterUiHelpers.SetBorderColor(_topThreatPill.transform, borderCol);
            }
        }

        private static Color ResolveClockPhaseColor(WorldTimePhase phase)
        {
            switch (phase)
            {
                case WorldTimePhase.Dawn:
                    return new Color(0.98f, 0.79f, 0.36f, 1f);
                case WorldTimePhase.Day:
                    return BywaterUiHelpers.ParchmentText;
                case WorldTimePhase.Dusk:
                    return new Color(0.95f, 0.58f, 0.24f, 1f);
                case WorldTimePhase.Night:
                    return new Color(0.56f, 0.78f, 0.99f, 1f);
                default:
                    return BywaterUiHelpers.BrassHighlight;
            }
        }

        private (string title, string body) BuildClockPillTooltip()
        {
            if (_cachedExploreSession == null)
            {
                return ("World Clock", "Time advances with exploration steps and camp rests.");
            }

            WorldClock clock = _cachedExploreSession.CurrentWorldClock;
            EncounterGeographyInfo geo = _cachedExploreSession.GetCurrentGeography();
            EncounterThreatAssessment threat = _cachedExploreSession.GetCurrentThreatAssessment();
            int torchTurns = _cachedExploreSession.Party != null ? _cachedExploreSession.Party.TorchTurnsRemaining : 0;
            bool isDungeon = _cachedExploreSession.CurrentMap != null && _cachedExploreSession.CurrentMap.IsDungeon;

            int minsToNext = Math.Max(0, (int)Math.Ceiling((double)clock.MinutesUntilNextPhase));
            string title = $"{clock.DisplayLabel} ({geo.LocationLabel})";
            string body =
                $"Time: {clock.DisplayLabel}\n" +
                $"Next phase in {minsToNext} min\n" +
                "Overland exploring ticks calmly (~24 min/day; paused in combat, shops, and menus).\n" +
                $"Biome: {geo.LocationLabel} ({geo.TileTerrainLabel})\n" +
                $"Threat: {threat.Label} ({threat.EffectiveChanceBasisPoints / 100.0:0.0}% per step)";

            if (threat.IsInGracePeriod)
            {
                body += $"\nPost-combat grace: {threat.GraceStepsRemaining} step(s) remaining";
            }

            if (threat.IsDarkWithoutTorch || ((clock.IsNight || isDungeon) && torchTurns <= 0))
            {
                body += "\nWARNING: Darkness without a lit torch! Enemies ambush on Round 1 and Evade is blocked.";
            }
            else if (clock.IsNight && !isDungeon)
            {
                body += "\nNightfall: +40% encounter rate, +25% Night XP & Gold.";
            }

            return (title, body);
        }

        private (string title, string body) BuildThreatPillTooltip()
        {
            if (_cachedExploreSession == null)
            {
                return ("Threat: SAFE", "Shows local biome danger, encounter odds, grace steps, and darkness warnings.");
            }

            EncounterGeographyInfo geo = _cachedExploreSession.GetCurrentGeography();
            WorldClock clock = _cachedExploreSession.CurrentWorldClock;
            EncounterThreatAssessment threat = _cachedExploreSession.GetCurrentThreatAssessment();
            int torchTurns = _cachedExploreSession.Party != null ? _cachedExploreSession.Party.TorchTurnsRemaining : 0;
            bool isDungeon = _cachedExploreSession.CurrentMap != null && _cachedExploreSession.CurrentMap.IsDungeon;

            int minsToNext = Math.Max(0, (int)Math.Ceiling((double)clock.MinutesUntilNextPhase));
            string title = $"Threat: {threat.Label} ({geo.LocationLabel})";
            string body =
                $"Biome: {geo.LocationLabel}\n" +
                $"Encounter Odds: {threat.EffectiveChanceBasisPoints / 100.0:0.0}% ({threat.EffectiveChanceBasisPoints} bps)\n" +
                $"Time: {clock.DisplayLabel} ({minsToNext} min to next phase)";

            if (!string.IsNullOrWhiteSpace(threat.Reason))
            {
                body += $"\nStatus: {threat.Reason}";
            }

            if (threat.IsInGracePeriod)
            {
                body += $"\nGrace Steps: {threat.GraceStepsRemaining} step(s) left before encounters can trigger.";
            }

            if (threat.IsDarkWithoutTorch || ((clock.IsNight || isDungeon) && torchTurns <= 0))
            {
                body += "\nDARKNESS WARNING: No torch lit! You will be ambushed in the dark (Evade blocked, enemies act first).";
            }
            else
            {
                body += torchTurns > 0
                    ? $"\nTorchlight: {torchTurns}t remaining."
                    : "\nTorchlight: Unlit (safe during daylight outdoors).";
            }

            return (title, body);
        }

        private (string title, string body) BuildTorchHintTooltip()
        {
            if (_cachedExploreSession == null)
            {
                return ("Torchlight (Click to Light)", "Click to light a torch from party supplies. Keeps night and cavern ambushes at bay.");
            }

            WorldClock clock = _cachedExploreSession.CurrentWorldClock;
            bool isDungeon = _cachedExploreSession.CurrentMap != null && _cachedExploreSession.CurrentMap.IsDungeon;
            int torchTurns = _cachedExploreSession.Party != null ? _cachedExploreSession.Party.TorchTurnsRemaining : 0;
            bool darkWarning = (clock.IsNight || isDungeon) && torchTurns <= 0;

            string title = torchTurns > 0
                ? $"Torchlight: {torchTurns}t (Click to Refresh)"
                : "Torchlight: Unlit (Click to Light)";

            string body = "Torches burn for 24 turns and protect the party from darkness ambushes at night and inside caverns.\n" +
                          "Click here to light a torch from your stash.";

            if (darkWarning)
            {
                body += "\nWARNING: It is dark and no torch is lit! Light a torch immediately to avoid being ambushed in the dark.";
            }

            return (title, body);
        }

        private (string title, string body) BuildSurvivalPillTooltip()
        {
            string title = $"Weather: {_survivalWeatherLabel} (Vision {_survivalVisionRadius})";
            string body = $"Current weather: {_survivalWeatherLabel}\n" +
                          $"Party sight radius: {_survivalVisionRadius} tile(s).\n" +
                          "Weather and ambient light affect visibility and encounter scouting range.";
            return (title, body);
        }
    }
}
