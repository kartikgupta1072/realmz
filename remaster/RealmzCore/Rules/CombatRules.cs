using System;
using System.Collections.Generic;

namespace RealmzCore.Rules
{
    public enum CombatActionSlot
    {
        Move,
        MainAction,
        BonusAction,
        Reaction
    }

    public sealed class CombatActionEconomy
    {
        public const int DefaultBaseMpPerTurn = 4;
        public const int DefaultBaseApPerTurn = 6;
        public const int DefaultMaxCarryoverAp = 2;
        public const int NormalTileMoveApCost = 1;
        public const int RoughTileMoveApCost = 2;
        public const int LightWeaponApCost = 3;
        public const int StandardWeaponApCost = 4;
        public const int DefaultShoveApCost = 2;
        public const int DefaultPotionApCost = 2;
        public const int DefaultTorchApCost = 2;
        public const int DefaultGuardApCost = 2;
        public const int DefaultDashApCost = 2;

        private readonly Stack<(int apCost, int freeMovesUsed)> _apMoveHistory = new Stack<(int, int)>();
        private int _legacyMoveBudget;
        private int _legacyMoveRemaining;
        private bool _legacyHasStandardAction;
        private bool _legacyHasSwiftAction;
        private bool _undoLockedByAction;
        private int _carryoverBankAp;

        public CombatActionEconomy()
        {
            ResetSplitMpApTurn(DefaultBaseMpPerTurn, 0, DefaultBaseApPerTurn, 0, 0);
        }

        public CombatActionEconomy(int moveBudget)
        {
            ResetTurn(moveBudget);
        }

        public bool IsUnifiedApMode { get; private set; }
        public bool IsDnd5eMode { get; private set; }
        public bool IsSplitMpApMode { get; private set; }
        public bool UsesApForActions => IsUnifiedApMode || IsSplitMpApMode;
        public int BaseApPerTurn { get; private set; } = DefaultBaseApPerTurn;
        public int EquipApBonus { get; private set; }
        public int CarriedInAp { get; private set; }
        public int MaxAp { get; private set; } = DefaultBaseApPerTurn;
        public int NominalMaxAp => BaseApPerTurn + EquipApBonus;
        public int CurrentAp { get; private set; } = DefaultBaseApPerTurn;
        public int PendingCarryoverAp => IsUnifiedApMode ? Math.Min(DefaultMaxCarryoverAp, Math.Max(0, CurrentAp)) : 0;
        public int FreeMoveTilesMax { get; private set; }
        public int FreeMoveTilesRemaining { get; private set; }
        public int GuardApDiscount { get; private set; }
        public int EffectiveGuardApCost => IsDnd5eMode ? 0 : Math.Max(1, DefaultGuardApCost - GuardApDiscount);
        public bool MovementLocked { get; private set; }
        public bool HasUsedDash { get; private set; }
        public bool BonusActionGuardEnabled { get; private set; }

        public int MoveBudget => IsUnifiedApMode ? (MaxAp + FreeMoveTilesMax) : _legacyMoveBudget;
        public int MoveRemaining => IsUnifiedApMode
            ? (MovementLocked ? 0 : (FreeMoveTilesRemaining + CurrentAp))
            : (MovementLocked ? 0 : _legacyMoveRemaining);
        public bool HasStandardAction => UsesApForActions ? CurrentAp >= LightWeaponApCost : _legacyHasStandardAction;
        public bool HasSwiftAction => UsesApForActions ? CurrentAp >= DefaultShoveApCost : _legacyHasSwiftAction;
        public bool HasMainAction => HasStandardAction;
        public bool HasBonusAction => HasSwiftAction;
        public bool CanPerformDash =>
            (IsSplitMpApMode && CurrentAp >= DefaultDashApCost && !HasUsedDash && !MovementLocked) ||
            (IsDnd5eMode && _legacyHasStandardAction && !HasUsedDash && !MovementLocked);
        public CombatActionSlot GuardSlot => BonusActionGuardEnabled ? CombatActionSlot.BonusAction : CombatActionSlot.MainAction;
        public bool HasReaction { get; private set; }
        public bool CanUndoMove => _apMoveHistory.Count > 0 && !_undoLockedByAction;

        public void ResetTurn(int moveBudget)
        {
            if (moveBudget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moveBudget));
            }

            IsUnifiedApMode = false;
            IsDnd5eMode = false;
            IsSplitMpApMode = false;
            _legacyMoveBudget = moveBudget;
            _legacyMoveRemaining = moveBudget;
            _legacyHasStandardAction = true;
            _legacyHasSwiftAction = true;
            HasUsedDash = false;
            BonusActionGuardEnabled = false;
            _undoLockedByAction = false;
            MovementLocked = false;
            HasReaction = true;
            _apMoveHistory.Clear();
        }

        public void ResetSplitMpApTurn(
            int baseMp = DefaultBaseMpPerTurn,
            int bonusMp = 0,
            int baseAp = DefaultBaseApPerTurn,
            int equipApBonus = 0,
            int guardApDiscount = 0)
        {
            int totalMove = Math.Max(1, baseMp) + Math.Max(0, bonusMp);
            IsUnifiedApMode = false;
            IsDnd5eMode = false;
            IsSplitMpApMode = true;
            BaseApPerTurn = Math.Max(1, baseAp);
            EquipApBonus = Math.Max(0, equipApBonus);
            CarriedInAp = 0;
            MaxAp = BaseApPerTurn + EquipApBonus;
            CurrentAp = MaxAp;
            FreeMoveTilesMax = Math.Max(0, bonusMp);
            FreeMoveTilesRemaining = 0;
            GuardApDiscount = Math.Max(0, guardApDiscount);
            BonusActionGuardEnabled = GuardApDiscount > 0;
            _legacyMoveBudget = totalMove;
            _legacyMoveRemaining = totalMove;
            _legacyHasStandardAction = true;
            _legacyHasSwiftAction = true;
            HasUsedDash = false;
            _undoLockedByAction = false;
            MovementLocked = false;
            HasReaction = true;
            _apMoveHistory.Clear();
        }

        public void ResetDndTurn(int moveBudget, int bonusMoveTiles = 0, bool bonusActionGuard = false)
        {
            int totalMove = Math.Max(1, moveBudget) + Math.Max(0, bonusMoveTiles);
            IsUnifiedApMode = false;
            IsDnd5eMode = true;
            IsSplitMpApMode = false;
            BaseApPerTurn = 0;
            EquipApBonus = Math.Max(0, bonusMoveTiles);
            CarriedInAp = 0;
            MaxAp = 0;
            CurrentAp = 0;
            FreeMoveTilesMax = Math.Max(0, bonusMoveTiles);
            FreeMoveTilesRemaining = 0;
            GuardApDiscount = bonusActionGuard ? 1 : 0;
            _legacyMoveBudget = totalMove;
            _legacyMoveRemaining = totalMove;
            _legacyHasStandardAction = true;
            _legacyHasSwiftAction = true;
            HasUsedDash = false;
            BonusActionGuardEnabled = bonusActionGuard;
            _undoLockedByAction = false;
            MovementLocked = false;
            HasReaction = true;
            _apMoveHistory.Clear();
        }

        public void ResetUnifiedTurn(
            int baseAp = DefaultBaseApPerTurn,
            int equipApBonus = 0,
            int freeMoveTiles = 0,
            int guardApDiscount = 0,
            bool applyCarryover = true)
        {
            IsUnifiedApMode = true;
            IsDnd5eMode = false;
            IsSplitMpApMode = false;
            BaseApPerTurn = Math.Max(1, baseAp);
            EquipApBonus = Math.Max(0, equipApBonus);
            CarriedInAp = applyCarryover ? Math.Clamp(_carryoverBankAp, 0, DefaultMaxCarryoverAp) : 0;
            _carryoverBankAp = 0;
            MaxAp = BaseApPerTurn + EquipApBonus + CarriedInAp;
            CurrentAp = MaxAp;
            FreeMoveTilesMax = Math.Max(0, freeMoveTiles);
            FreeMoveTilesRemaining = FreeMoveTilesMax;
            GuardApDiscount = Math.Max(0, guardApDiscount);
            BonusActionGuardEnabled = false;
            HasUsedDash = false;
            MovementLocked = false;
            _undoLockedByAction = false;
            HasReaction = true;
            _apMoveHistory.Clear();
        }

        public bool HasSlot(CombatActionSlot slot)
        {
            return slot switch
            {
                CombatActionSlot.MainAction => HasMainAction,
                CombatActionSlot.BonusAction => HasBonusAction,
                CombatActionSlot.Reaction => HasReaction,
                CombatActionSlot.Move => MoveRemaining > 0,
                _ => false
            };
        }

        public bool CommitSlot(CombatActionSlot slot, bool lockMoveUndo = true)
        {
            switch (slot)
            {
                case CombatActionSlot.MainAction:
                    if (!UsesApForActions)
                    {
                        if (!_legacyHasStandardAction)
                        {
                            return false;
                        }

                        _legacyHasStandardAction = false;
                        if (lockMoveUndo)
                        {
                            _undoLockedByAction = true;
                            _apMoveHistory.Clear();
                        }

                        return true;
                    }

                    return SpendAp(StandardWeaponApCost, lockMoveUndo);

                case CombatActionSlot.BonusAction:
                    if (!UsesApForActions)
                    {
                        if (!_legacyHasSwiftAction)
                        {
                            return false;
                        }

                        _legacyHasSwiftAction = false;
                        if (lockMoveUndo)
                        {
                            _undoLockedByAction = true;
                            _apMoveHistory.Clear();
                        }

                        return true;
                    }

                    return SpendAp(DefaultShoveApCost, lockMoveUndo);

                case CombatActionSlot.Reaction:
                    return CommitReaction();

                default:
                    return false;
            }
        }

        public bool PerformDash(int baseMoveBudget)
        {
            if (!CanPerformDash)
            {
                return false;
            }

            if (IsSplitMpApMode)
            {
                if (!SpendAp(DefaultDashApCost, lockMoveUndo: true))
                {
                    return false;
                }
            }
            else
            {
                _legacyHasStandardAction = false;
                _undoLockedByAction = true;
                _apMoveHistory.Clear();
            }

            HasUsedDash = true;
            int bonusMove = Math.Max(1, baseMoveBudget);
            _legacyMoveBudget += bonusMove;
            _legacyMoveRemaining += bonusMove;
            return true;
        }

        public bool HasApFor(int apCost)
        {
            if (apCost <= 0)
            {
                return true;
            }

            return UsesApForActions
                ? CurrentAp >= apCost
                : (apCost <= 2 ? (_legacyHasSwiftAction || _legacyHasStandardAction) : _legacyHasStandardAction);
        }

        public bool SpendAp(int apCost, bool lockMoveUndo = true)
        {
            if (!UsesApForActions)
            {
                return apCost <= 2 ? CommitSwiftAction() : CommitStandardAction();
            }

            if (apCost < 0 || CurrentAp < apCost)
            {
                return false;
            }

            CurrentAp -= apCost;
            if (lockMoveUndo)
            {
                _undoLockedByAction = true;
                _apMoveHistory.Clear();
            }

            return true;
        }

        public void BankCarryoverForEndTurn(bool allowCarryover = true)
        {
            if (IsUnifiedApMode)
            {
                _carryoverBankAp = allowCarryover ? Math.Min(DefaultMaxCarryoverAp, Math.Max(0, CurrentAp)) : 0;
            }
        }

        public void ZeroApForIncapacitatedTurn()
        {
            if (IsUnifiedApMode)
            {
                CurrentAp = 0;
                FreeMoveTilesRemaining = 0;
                MovementLocked = true;
                _carryoverBankAp = 0;
                _apMoveHistory.Clear();
            }
            else
            {
                CurrentAp = 0;
                _legacyMoveRemaining = 0;
                _legacyHasStandardAction = false;
                _legacyHasSwiftAction = false;
                MovementLocked = true;
                HasReaction = false;
                _apMoveHistory.Clear();
            }
        }

        public void LockMovementForTurn()
        {
            MovementLocked = true;
            if (!IsUnifiedApMode)
            {
                _legacyMoveRemaining = 0;
            }

            _apMoveHistory.Clear();
        }

        public bool TryStep(int cost = 1)
        {
            if (cost <= 0 || MovementLocked)
            {
                return false;
            }

            if (!IsUnifiedApMode)
            {
                if (_legacyMoveRemaining < cost)
                {
                    return false;
                }

                _legacyMoveRemaining -= cost;
                _apMoveHistory.Push((cost, 0));
                return true;
            }

            if (FreeMoveTilesRemaining > 0)
            {
                FreeMoveTilesRemaining--;
                _apMoveHistory.Push((0, 1));
                return true;
            }

            if (CurrentAp < cost)
            {
                return false;
            }

            CurrentAp -= cost;
            _apMoveHistory.Push((cost, 0));
            return true;
        }

        public void ConsumeAllMovement()
        {
            LockMovementForTurn();
        }

        public bool UndoMove()
        {
            if (!CanUndoMove)
            {
                return false;
            }

            (int apCost, int freeUsed) = _apMoveHistory.Pop();
            if (!IsUnifiedApMode)
            {
                _legacyMoveRemaining += apCost;
                return true;
            }

            CurrentAp += apCost;
            FreeMoveTilesRemaining += freeUsed;
            return true;
        }

        public bool CommitStandardAction()
        {
            if (!IsUnifiedApMode)
            {
                if (!_legacyHasStandardAction)
                {
                    return false;
                }

                _legacyHasStandardAction = false;
                _undoLockedByAction = true;
                _apMoveHistory.Clear();
                return true;
            }

            return SpendAp(StandardWeaponApCost, lockMoveUndo: true);
        }

        public bool CommitSwiftAction()
        {
            if (!IsUnifiedApMode)
            {
                if (!_legacyHasSwiftAction)
                {
                    return false;
                }

                _legacyHasSwiftAction = false;
                return true;
            }

            return SpendAp(DefaultShoveApCost, lockMoveUndo: false);
        }

        public bool CommitReaction()
        {
            if (!HasReaction)
            {
                return false;
            }

            HasReaction = false;
            return true;
        }

        public void ResetReaction()
        {
            HasReaction = true;
        }
    }

    public readonly struct HitPreview
    {
        public HitPreview(
            int hitChancePercent,
            int uncappedHitScore,
            int grazeThresholdPercent,
            int critThresholdPercent,
            bool targetIsHelpless)
            : this(hitChancePercent, uncappedHitScore, grazeThresholdPercent, critThresholdPercent, targetIsHelpless, 0, Array.Empty<string>())
        {
        }

        public HitPreview(
            int hitChancePercent,
            int uncappedHitScore,
            int grazeThresholdPercent,
            int critThresholdPercent,
            bool targetIsHelpless,
            int elevationModifierPercent,
            IReadOnlyList<string>? modifiers = null)
        {
            HitChancePercent = hitChancePercent;
            UncappedHitScore = uncappedHitScore;
            GrazeThresholdPercent = grazeThresholdPercent;
            CritThresholdPercent = critThresholdPercent;
            TargetIsHelpless = targetIsHelpless;
            ElevationModifierPercent = elevationModifierPercent;
            Modifiers = modifiers ?? Array.Empty<string>();
        }

        public int HitChancePercent { get; }
        public int UncappedHitScore { get; }
        public int GrazeThresholdPercent { get; }
        public int CritThresholdPercent { get; }
        public bool TargetIsHelpless { get; }
        public int ElevationModifierPercent { get; }
        public IReadOnlyList<string> Modifiers { get; }
    }

    public enum AttackOutcomeKind
    {
        Miss = 0,
        Graze = 1,
        Hit = 2,
        CriticalHit = 3
    }

    public sealed class AttackOutcome
    {
        public AttackOutcome(
            AttackOutcomeKind kind,
            int roll,
            int hitChancePercent,
            int baseDamageRolled,
            int damageDealt)
        {
            Kind = kind;
            Roll = roll;
            HitChancePercent = hitChancePercent;
            BaseDamageRolled = baseDamageRolled;
            DamageDealt = damageDealt;
        }

        public AttackOutcomeKind Kind { get; }
        public int Roll { get; }
        public int HitChancePercent { get; }
        public int BaseDamageRolled { get; }
        public int DamageDealt { get; }
        public bool IsHit => Kind != AttackOutcomeKind.Miss;
    }

    public static class CombatRules
    {
        public static HitPreview CalculateHitPreview(
            int attackerToHit,
            int defenderAc,
            bool isFlanking = false,
            int equippedWeaponBonus = 0,
            bool defenderIsEvil = false,
            ConditionTracker? attackerConditions = null,
            ConditionTracker? defenderConditions = null,
            RulesetMode mode = RulesetMode.ModernBalanced,
            int attackerElevation = 0,
            int defenderElevation = 0,
            bool weaponBonusAlreadyIncluded = false,
            bool defenderIsProne = false,
            bool attackerIsProne = false,
            int attackerCritBonusPct = 0)
        {
            int weaponHitBonus = weaponBonusAlreadyIncluded ? 0 : (5 * equippedWeaponBonus);
            int score = 50 + attackerToHit + (isFlanking ? 20 : 0) + weaponHitBonus - defenderAc;
            int elevationMod = 0;
            List<string>? modifiers = null;

            if (attackerElevation > defenderElevation)
            {
                elevationMod = 15;
                score += 15;
                modifiers ??= new List<string>();
                modifiers.Add("High Ground +15%");
            }
            else if (attackerElevation < defenderElevation)
            {
                elevationMod = -15;
                score -= 15;
                modifiers ??= new List<string>();
                modifiers.Add("Low Ground -15%");
            }

            if (defenderIsProne)
            {
                score += 15;
                modifiers ??= new List<string>();
                modifiers.Add("Target Prone +15%");
            }

            if (attackerIsProne)
            {
                score -= 15;
                modifiers ??= new List<string>();
                modifiers.Add("Attacker Prone -15%");
            }

            if (attackerConditions != null)
            {
                if (attackerConditions.Has(CharConditionId.Strong))
                {
                    score += 15;
                }

                if (attackerConditions.Has(CharConditionId.Slow))
                {
                    score -= 15;
                }

                if (attackerConditions.Has(CharConditionId.Blind))
                {
                    score -= 15;
                }

                if (attackerConditions.Has(CharConditionId.Confused))
                {
                    score -= 10;
                }

                if (attackerConditions.Has(CharConditionId.MagicAura))
                {
                    score += 5;
                }

                if (attackerConditions.Has(CharConditionId.Cursed))
                {
                    score -= 5;
                }

                if (defenderIsEvil && attackerConditions.Has(CharConditionId.ProtectionFromEvil))
                {
                    score += 10;
                }
            }

            bool targetIsHelpless = false;
            if (defenderConditions != null)
            {
                targetIsHelpless = defenderConditions.Has(CharConditionId.Helpless);

                if (defenderConditions.Has(CharConditionId.Slow))
                {
                    score += 15;
                }

                if (defenderConditions.Has(CharConditionId.Blind))
                {
                    score += 15;
                }

                if (defenderConditions.Has(CharConditionId.Confused))
                {
                    score += 10;
                }

                if (defenderConditions.Has(CharConditionId.MagicAura))
                {
                    score -= 5;
                }

                if (defenderConditions.Has(CharConditionId.Cursed))
                {
                    score += 5;
                }

                if (defenderConditions.Has(CharConditionId.Invisible))
                {
                    score -= 10;
                }
            }

            int clamped = Math.Clamp(score, 5, 95);
            int critThreshold = mode == RulesetMode.ModernBalanced
                ? Math.Clamp(5 + Math.Max(0, attackerCritBonusPct), 5, 50)
                : 0;
            int grazeThreshold = mode == RulesetMode.ModernBalanced
                ? Math.Min(95, clamped + 10)
                : clamped;

            return new HitPreview(
                hitChancePercent: clamped,
                uncappedHitScore: score,
                grazeThresholdPercent: grazeThreshold,
                critThresholdPercent: critThreshold,
                targetIsHelpless: targetIsHelpless,
                elevationModifierPercent: elevationMod,
                modifiers: modifiers);
        }

        public static AttackOutcome ResolveMeleeAttack(
            IDiceRoller roller,
            DiceSpec damageDice,
            int attackerToHit,
            int attackerDamageBonus,
            int defenderAc,
            bool isFlanking = false,
            int equippedWeaponBonus = 0,
            bool defenderIsEvil = false,
            ConditionTracker? attackerConditions = null,
            ConditionTracker? defenderConditions = null,
            RulesetMode mode = RulesetMode.ModernBalanced,
            int attackerElevation = 0,
            int defenderElevation = 0,
            bool weaponBonusAlreadyIncluded = false,
            bool defenderIsProne = false,
            bool attackerIsProne = false,
            int attackerCritBonusPct = 0,
            RollAdvantageState advantageState = RollAdvantageState.Normal)
        {
            if (roller == null)
            {
                throw new ArgumentNullException(nameof(roller));
            }

            HitPreview preview = CalculateHitPreview(
                attackerToHit: attackerToHit,
                defenderAc: defenderAc,
                isFlanking: isFlanking,
                equippedWeaponBonus: equippedWeaponBonus,
                defenderIsEvil: defenderIsEvil,
                attackerConditions: attackerConditions,
                defenderConditions: defenderConditions,
                mode: mode,
                attackerElevation: attackerElevation,
                defenderElevation: defenderElevation,
                weaponBonusAlreadyIncluded: weaponBonusAlreadyIncluded,
                defenderIsProne: defenderIsProne,
                attackerIsProne: attackerIsProne,
                attackerCritBonusPct: attackerCritBonusPct);

            int roll = RollAdvantageHelper.RollD100RollUnderWithAdvantage(roller, advantageState, out _, out _);
            AttackOutcomeKind kind;

            if (preview.TargetIsHelpless)
            {
                kind = mode == RulesetMode.ModernBalanced && roll <= preview.CritThresholdPercent
                    ? AttackOutcomeKind.CriticalHit
                    : AttackOutcomeKind.Hit;
            }
            else if (mode == RulesetMode.ModernBalanced && roll <= preview.CritThresholdPercent)
            {
                kind = AttackOutcomeKind.CriticalHit;
            }
            else if (roll <= preview.HitChancePercent)
            {
                kind = AttackOutcomeKind.Hit;
            }
            else if (mode == RulesetMode.ModernBalanced && roll <= preview.GrazeThresholdPercent)
            {
                kind = AttackOutcomeKind.Graze;
            }
            else
            {
                return new AttackOutcome(
                    kind: AttackOutcomeKind.Miss,
                    roll: roll,
                    hitChancePercent: preview.HitChancePercent,
                    baseDamageRolled: 0,
                    damageDealt: 0);
            }

            int strongBonus = attackerConditions != null && attackerConditions.Has(CharConditionId.Strong) ? 3 : 0;
            int rawDamage = Math.Max(1, damageDice.Roll(roller) + attackerDamageBonus + strongBonus);

            int finalDamage = kind switch
            {
                AttackOutcomeKind.CriticalHit => rawDamage * 2,
                AttackOutcomeKind.Graze => Math.Max(1, rawDamage / 2),
                _ => rawDamage
            };

            return new AttackOutcome(
                kind: kind,
                roll: roll,
                hitChancePercent: preview.HitChancePercent,
                baseDamageRolled: rawDamage,
                damageDealt: finalDamage);
        }
    }
}
