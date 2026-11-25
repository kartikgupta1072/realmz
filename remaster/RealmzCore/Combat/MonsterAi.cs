using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public static class MonsterAi
    {
        private sealed class AiDecision
        {
            public AiDecision(
                CombatIntentActionType actionKind,
                string actionType,
                GridCoord targetCoord,
                string targetId,
                GridCoord? moveCoord = null,
                string spellKey = "")
            {
                ActionKind = actionKind;
                ActionType = actionType;
                TargetCoord = targetCoord;
                TargetId = targetId;
                MoveCoord = moveCoord;
                SpellKey = spellKey;
            }

            public CombatIntentActionType ActionKind { get; }
            public string ActionType { get; }
            public GridCoord TargetCoord { get; }
            public string TargetId { get; }
            public GridCoord? MoveCoord { get; }
            public string SpellKey { get; }
        }

        public static EnemyIntentPrediction PredictNextIntent(TacticalCombatSession session, string? actorId = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            CombatantState? actor = string.IsNullOrWhiteSpace(actorId)
                ? session.CurrentActor
                : session.GetCombatant(actorId!);

            if (actor == null || !actor.IsAlive || session.Outcome != CombatOutcome.InProgress)
            {
                return new EnemyIntentPrediction(
                    actor?.Id ?? string.Empty,
                    actor?.Position ?? new GridCoord(0, 0),
                    "Hold",
                    CombatIntentActionType.Hold,
                    string.Empty,
                    actor?.Position ?? new GridCoord(0, 0),
                    hasPlannedMove: false);
            }

            AiDecision decision = ChooseNextDecision(session, actor, dryRun: true);
            return new EnemyIntentPrediction(
                actorId: actor.Id,
                targetCoord: decision.TargetCoord,
                actionType: decision.ActionType,
                actionKind: decision.ActionKind,
                targetId: decision.TargetId,
                plannedMoveCoord: decision.MoveCoord ?? actor.Position,
                hasPlannedMove: decision.MoveCoord.HasValue && decision.MoveCoord.Value != actor.Position,
                spellKey: decision.SpellKey);
        }

        public static CombatActionResult ExecuteTurn(TacticalCombatSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (session.Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = session.CurrentActor;
            if (actor == null)
            {
                return new CombatActionResult(false, "No current actor.");
            }

            if (!actor.IsConscious)
            {
                session.AppendLog(actor.Id, actor.Name, "Skip", $"{actor.Name} is incapacitated and cannot act.");
                session.EndTurn();
                return new CombatActionResult(true, $"{actor.Name} is incapacitated.");
            }

            List<CombatantState> enemies = CollectLivingBySide(
                session,
                actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);
            if (enemies.Count == 0)
            {
                session.EndTurn();
                return new CombatActionResult(true, $"{actor.Name} has no targets.");
            }

            CombatActionResult finalResult = new CombatActionResult(true, $"{actor.Name} holds position.");
            bool tookAttackOrCast = false;
            const int maxSubActions = 5;

            for (int step = 0; step < maxSubActions; step++)
            {
                if (session.Outcome != CombatOutcome.InProgress ||
                    !string.Equals(session.CurrentActorId, actor.Id, StringComparison.Ordinal) ||
                    !actor.IsConscious ||
                    actor.Economy.CurrentAp <= 0 ||
                    actor.IsGuarding)
                {
                    break;
                }

                enemies = CollectLivingBySide(
                    session,
                    actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);
                if (enemies.Count == 0)
                {
                    break;
                }

                int apBefore = actor.Economy.CurrentAp;
                int freeBefore = actor.Economy.FreeMoveTilesRemaining;

                // If already attacked/cast this turn and only <= 2 AP remains (cannot afford another attack/cast without wasting carryover), bank it cleanly.
                if (tookAttackOrCast)
                {
                    bool canFollowUpCast = TryFindBestSpell(session, actor, actor.Position, ignoreActionBudget: false, out _, out _, out _, out _, out _);
                    bool canFollowUpRanged = actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice) &&
                                             PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _) != null;
                    bool canFollowUpMelee = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _) != null;
                    if (!canFollowUpCast && !canFollowUpRanged && !canFollowUpMelee)
                    {
                        break;
                    }
                }

                AiDecision decision = ChooseNextDecision(session, actor, dryRun: false);

                if (decision.ActionKind == CombatIntentActionType.Flee)
                {
                    if (decision.MoveCoord.HasValue && decision.MoveCoord.Value != actor.Position)
                    {
                        finalResult = session.TryMoveTo(decision.MoveCoord.Value);
                    }
                    break;
                }

                // If already acted and holding with <= 2 AP out of immediate strike/cast range, bank AP for next turn.
                if (tookAttackOrCast &&
                    decision.ActionKind == CombatIntentActionType.Hold &&
                    actor.Economy.CurrentAp <= CombatActionEconomy.DefaultMaxCarryoverAp)
                {
                    break;
                }

                bool moved = false;
                if (decision.MoveCoord.HasValue && decision.MoveCoord.Value != actor.Position)
                {
                    CombatActionResult moveRes = session.TryMoveTo(decision.MoveCoord.Value);
                    moved = moveRes.Success;
                    if (moved)
                    {
                        finalResult = moveRes;
                    }

                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        break;
                    }
                }

                CombatActionResult? actionResult = null;
                if ((decision.ActionKind == CombatIntentActionType.CastSpell ||
                     decision.ActionKind == CombatIntentActionType.MoveAndCast ||
                     decision.ActionKind == CombatIntentActionType.HealAlly) &&
                    !string.IsNullOrEmpty(decision.SpellKey))
                {
                    TacticalSpellPreview postMoveSpell = session.PreviewSpell(actor.Id, decision.SpellKey, decision.TargetCoord);
                    if (postMoveSpell.CanExecute)
                    {
                        actionResult = session.CastSpell(decision.SpellKey, decision.TargetCoord);
                    }
                }

                if (actionResult == null &&
                    TryFindBestSpell(session, actor, actor.Position, ignoreActionBudget: false, out string? fallbackSpellKey, out GridCoord fallbackSpellCenter, out _, out _, out _) &&
                    fallbackSpellKey != null)
                {
                    actionResult = session.CastSpell(fallbackSpellKey, fallbackSpellCenter);
                }

                if (actionResult == null &&
                    (decision.ActionKind == CombatIntentActionType.RangedAttack ||
                     decision.ActionKind == CombatIntentActionType.MoveAndRanged) &&
                    !string.IsNullOrEmpty(decision.TargetId) &&
                    session.PreviewRangedAttack(actor.Id, decision.TargetId).CanExecute)
                {
                    actionResult = session.ExecuteRangedAttack(decision.TargetId);
                }

                if (actionResult == null &&
                    actor.RangedRange > 1 &&
                    !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    CombatantState? bestRanged = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _);
                    if (bestRanged != null)
                    {
                        actionResult = session.ExecuteRangedAttack(bestRanged.Id);
                    }
                }

                if (actionResult == null)
                {
                    CombatantState? meleeTarget = !string.IsNullOrEmpty(decision.TargetId) &&
                                                  decision.ActionKind == CombatIntentActionType.MeleeAttack
                        ? session.GetCombatant(decision.TargetId)
                        : null;
                    if (meleeTarget == null || !session.PreviewMeleeAttack(actor.Id, meleeTarget.Id).CanExecute)
                    {
                        meleeTarget = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _);
                    }

                    if (meleeTarget != null)
                    {
                        actionResult = session.ExecuteMeleeAttack(meleeTarget.Id);
                    }
                }

                if (actionResult != null && actionResult.Success)
                {
                    finalResult = actionResult;
                    tookAttackOrCast = true;
                    continue;
                }

                if (!tookAttackOrCast && actor.Economy.HasApFor(actor.Economy.EffectiveGuardApCost))
                {
                    finalResult = session.GuardOverwatch();
                    break;
                }

                if (moved && actor.Economy.CurrentAp != apBefore)
                {
                    continue;
                }

                if (actor.Economy.CurrentAp == apBefore && actor.Economy.FreeMoveTilesRemaining == freeBefore)
                {
                    break;
                }
            }

            if (session.Outcome == CombatOutcome.InProgress &&
                string.Equals(session.CurrentActorId, actor.Id, StringComparison.Ordinal))
            {
                session.EndTurn();
            }

            return finalResult;
        }

        private static AiDecision ChooseNextDecision(
            TacticalCombatSession session,
            CombatantState actor,
            bool dryRun)
        {
            if (!actor.IsConscious)
            {
                return new AiDecision(CombatIntentActionType.Hold, "Hold", actor.Position, string.Empty);
            }

            List<CombatantState> allies = CollectLivingBySide(session, actor.Side);
            List<CombatantState> enemies = CollectLivingBySide(
                session,
                actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);

            if (enemies.Count == 0)
            {
                return new AiDecision(CombatIntentActionType.Hold, "Hold", actor.Position, string.Empty);
            }

            bool ignoreBudget = dryRun;
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: dryRun);

            // 1. Panicked units flee away from enemies.
            if (actor.Conditions.Has(CharConditionId.RunsAway))
            {
                GridCoord? fleeCoord = PickBestFleeCoord(reachable, enemies);
                return new AiDecision(
                    CombatIntentActionType.Flee,
                    "Flee",
                    fleeCoord ?? actor.Position,
                    string.Empty,
                    fleeCoord);
            }

            // 2. Check if already adjacent to any living enemy.
            CombatantState? adjacentMelee = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreBudget, out int meleeScore);
            bool isAdjacentToEnemy = adjacentMelee != null;

            // 3. Check spellcasting from current position (including ground centroid AoEs).
            if (TryFindBestSpell(
                    session,
                    actor,
                    actor.Position,
                    ignoreBudget,
                    out string? spellKeyHere,
                    out GridCoord spellCenterHere,
                    out string spellTargetHere,
                    out bool isHealHere,
                    out int spellScoreHere) &&
                spellKeyHere != null)
            {
                // If ranged-preferring caster is adjacent and can kite safely first, check kiting below!
                if (!actor.PrefersRanged || !isAdjacentToEnemy || isHealHere)
                {
                    return new AiDecision(
                        isHealHere ? CombatIntentActionType.HealAlly : CombatIntentActionType.CastSpell,
                        isHealHere ? "HealAlly" : "CastSpell",
                        spellCenterHere,
                        spellTargetHere,
                        moveCoord: null,
                        spellKey: spellKeyHere);
                }

                // If adjacent caster cannot find a better kite retreat, cast right here.
                if (!TryFindBestKiteDecision(session, actor, reachable, enemies, ignoreBudget, out AiDecision? kiteCasterDecision))
                {
                    return new AiDecision(
                        CombatIntentActionType.CastSpell,
                        "CastSpell",
                        spellCenterHere,
                        spellTargetHere,
                        moveCoord: null,
                        spellKey: spellKeyHere);
                }

                // Compare casting immediately vs kiting + casting/shooting.
                if (spellScoreHere >= 150)
                {
                    return new AiDecision(
                        CombatIntentActionType.CastSpell,
                        "CastSpell",
                        spellCenterHere,
                        spellTargetHere,
                        moveCoord: null,
                        spellKey: spellKeyHere);
                }

                return kiteCasterDecision!;
            }

            // 4. Ranged/caster kiting when pinned adjacent to an enemy.
            if (actor.PrefersRanged && isAdjacentToEnemy)
            {
                if (TryFindBestKiteDecision(session, actor, reachable, enemies, ignoreBudget, out AiDecision? kiteDecision))
                {
                    return kiteDecision!;
                }
            }

            // 5. Standing melee attack if already engaged (and not a kiting ranged unit that retreated).
            if (adjacentMelee != null && !actor.PrefersRanged)
            {
                return new AiDecision(
                    CombatIntentActionType.MeleeAttack,
                    "MeleeAttack",
                    adjacentMelee.Position,
                    adjacentMelee.Id);
            }

            // 6. Standing ranged shot if in range & clear/partial LOS.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                CombatantState? rangedHere = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreBudget, out _);
                if (rangedHere != null && !isAdjacentToEnemy)
                {
                    return new AiDecision(
                        CombatIntentActionType.RangedAttack,
                        "RangedAttack",
                        rangedHere.Position,
                        rangedHere.Id);
                }
            }

            // 7. Move-then-cast for casters starting out of range/LOS.
            if (actor.SpellIds.Count > 0 && actor.Sp > 0 && !actor.Conditions.Has(CharConditionId.Silenced))
            {
                if (TryFindBestMoveAndCast(session, actor, reachable, out AiDecision? moveCast))
                {
                    return moveCast!;
                }
            }

            // 8. Move-then-shoot for ranged units out of range or blocked by wall.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                if (TryFindBestMoveAndRanged(session, actor, reachable, enemies, out AiDecision? moveRanged))
                {
                    return moveRanged!;
                }
            }

            // 9. If adjacent melee fallback exists (e.g. archer pinned with no kite exit), swing!
            if (adjacentMelee != null)
            {
                return new AiDecision(
                    CombatIntentActionType.MeleeAttack,
                    "MeleeAttack",
                    adjacentMelee.Position,
                    adjacentMelee.Id);
            }

            // 10. Move toward best melee engagement tile (preferring flanking, High Ground, no AoO, safe surface).
            if (TryPickBestApproachTile(session, actor, reachable, enemies, out ReachableTileInfo? attackTile, out CombatantState? attackTarget, out ReachableTileInfo? approachTile))
            {
                if (attackTile != null && attackTarget != null)
                {
                    return new AiDecision(
                        CombatIntentActionType.MoveAndAttack,
                        "MoveAndAttack",
                        attackTarget.Position,
                        attackTarget.Id,
                        attackTile.Coord);
                }

                if (approachTile != null)
                {
                    CombatantState? nearest = FindNearestEnemy(approachTile.Coord, enemies);
                    return new AiDecision(
                        CombatIntentActionType.Guard,
                        "MoveAndGuard",
                        nearest?.Position ?? approachTile.Coord,
                        nearest?.Id ?? string.Empty,
                        approachTile.Coord);
                }
            }

            return new AiDecision(CombatIntentActionType.Guard, "Guard", actor.Position, string.Empty);
        }

        private static bool TryFindBestSpell(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            bool ignoreActionBudget,
            out string? bestSpellKey,
            out GridCoord bestCenter,
            out string bestTargetId,
            out bool isHeal,
            out int bestScore)
        {
            bestSpellKey = null;
            bestCenter = fromPos;
            bestTargetId = string.Empty;
            isHeal = false;
            bestScore = int.MinValue;

            if (actor.SpellIds.Count == 0 || actor.Sp <= 0 || actor.Conditions.Has(CharConditionId.Silenced))
            {
                return false;
            }

            List<CombatantState> allies = CollectLivingBySide(session, actor.Side);
            List<CombatantState> enemies = CollectLivingBySide(
                session,
                actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);

            HashSet<GridCoord> candidateCenters = BuildCandidateSpellCenters(session.Grid, enemies);

            for (int i = 0; i < actor.SpellIds.Count; i++)
            {
                int spellId = actor.SpellIds[i];
                if (!session.Spells.TryGetValue(spellId, out SpellDefinition? spell) || actor.Sp < spell.SpCost)
                {
                    continue;
                }

                string spellKey = !string.IsNullOrWhiteSpace(spell.Key) ? spell.Key : spell.Id.ToString();
                if (string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase))
                {
                    CombatantState? injured = null;
                    for (int a = 0; a < allies.Count; a++)
                    {
                        CombatantState ally = allies[a];
                        if (ally.Hp * 100 <= ally.MaxHp * 65 && (injured == null || ally.Hp < injured.Hp))
                        {
                            injured = ally;
                        }
                    }

                    if (injured != null)
                    {
                        TacticalSpellPreview preview = session.PreviewSpellInternal(
                            actor.Id,
                            spellKey,
                            injured.Position,
                            fromPos,
                            ignoreActionBudget);
                        if (preview.CanExecute)
                        {
                            int score = 120 + (100 - ((injured.Hp * 100) / Math.Max(1, injured.MaxHp)));
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestSpellKey = spellKey;
                                bestCenter = injured.Position;
                                bestTargetId = injured.Id;
                                isHeal = true;
                            }
                        }
                    }
                }
                else if (!string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (GridCoord center in candidateCenters)
                    {
                        TacticalSpellPreview preview = session.PreviewSpellInternal(
                            actor.Id,
                            spellKey,
                            center,
                            fromPos,
                            ignoreActionBudget);
                        if (!preview.CanExecute || preview.AoeFootprint.AffectedAllyIds.Count > 0)
                        {
                            continue;
                        }

                        int nonImmuneHits = 0;
                        int lowHpBonus = 0;
                        string primaryTargetId = string.Empty;
                        int lowestHp = int.MaxValue;

                        for (int e = 0; e < preview.AoeFootprint.AffectedEnemyIds.Count; e++)
                        {
                            CombatantState? victim = session.GetCombatant(preview.AoeFootprint.AffectedEnemyIds[e]);
                            if (victim == null || !victim.IsAlive || TacticalCombatSession.IsTargetImmuneToSpell(victim, spell))
                            {
                                continue;
                            }

                            nonImmuneHits++;
                            lowHpBonus += Math.Max(0, 30 - victim.Hp);
                            if (victim.Hp < lowestHp)
                            {
                                lowestHp = victim.Hp;
                                primaryTargetId = victim.Id;
                            }
                        }

                        if (nonImmuneHits == 0)
                        {
                            continue;
                        }

                        int comboBonus = ScoreSpellSurfaceCombo(session.Grid, preview.AoeFootprint.AffectedTiles, spell.DamageType);
                        bool directOnUnit = session.GetLivingUnitAt(center) != null;
                        int score = (nonImmuneHits * 100) + lowHpBonus + comboBonus + (directOnUnit ? 5 : 0);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSpellKey = spellKey;
                            bestCenter = center;
                            bestTargetId = primaryTargetId;
                            isHeal = false;
                        }
                    }
                }
            }

            return bestSpellKey != null;
        }

        private static bool TryFindBestKiteDecision(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            bool ignoreBudget,
            out AiDecision? bestKite)
        {
            bestKite = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                if (minDist < 2)
                {
                    continue;
                }

                int surfacePenalty = GetSurfaceRiskPenalty(session.Grid, tile.Coord);
                int elevBonus = session.Grid.GetElevation(tile.Coord.X, tile.Coord.Y) * 15;
                int aooPenalty = tile.ProvokesAoO ? -25 : 20;

                if (actor.SpellIds.Count > 0 && actor.Sp > 0 && !actor.Conditions.Has(CharConditionId.Silenced))
                {
                    if (TryFindBestSpell(
                            session,
                            actor,
                            tile.Coord,
                            ignoreActionBudget: true,
                            out string? spellKey,
                            out GridCoord spellCenter,
                            out string targetId,
                            out bool isHeal,
                            out int spellScore) &&
                        !isHeal &&
                        spellKey != null)
                    {
                        if (!ignoreBudget)
                        {
                            TacticalSpellPreview spCheck = session.PreviewSpellInternal(actor.Id, spellKey, spellCenter, tile.Coord, ignoreActionBudget: true);
                            if (tile.PathCost + spCheck.ApCost > actor.Economy.CurrentAp)
                            {
                                continue;
                            }
                        }
                        int totalScore = spellScore + elevBonus + aooPenalty - surfacePenalty - tile.PathCost;
                        if (totalScore > bestScore)
                        {
                            bestScore = totalScore;
                            bestKite = new AiDecision(
                                CombatIntentActionType.MoveAndCast,
                                "MoveAndCast",
                                spellCenter,
                                targetId,
                                tile.Coord,
                                spellKey);
                        }
                    }
                }

                if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    CombatantState? rangedTarget = PickBestRangedTarget(
                        session,
                        actor,
                        tile.Coord,
                        enemies,
                        ignoreActionBudget: true,
                        out int rangedScore);
                    if (rangedTarget != null && (ignoreBudget || tile.PathCost + actor.RangedApCost <= actor.Economy.CurrentAp))
                    {
                        int totalScore = rangedScore + elevBonus + aooPenalty - surfacePenalty - tile.PathCost;
                        if (totalScore > bestScore)
                        {
                            bestScore = totalScore;
                            bestKite = new AiDecision(
                                CombatIntentActionType.MoveAndRanged,
                                "MoveAndRanged",
                                rangedTarget.Position,
                                rangedTarget.Id,
                                tile.Coord);
                        }
                    }
                }
            }

            return bestKite != null;
        }

        private static bool TryFindBestMoveAndCast(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            out AiDecision? bestDecision)
        {
            bestDecision = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                if (TryFindBestSpell(
                        session,
                        actor,
                        tile.Coord,
                        ignoreActionBudget: true,
                        out string? spellKey,
                        out GridCoord spellCenter,
                        out string targetId,
                        out bool isHeal,
                        out int spellScore) &&
                    spellKey != null)
                {
                    int elevBonus = session.Grid.GetElevation(tile.Coord.X, tile.Coord.Y) * 15;
                    int aooPenalty = tile.ProvokesAoO ? -30 : 15;
                    int surfacePenalty = GetSurfaceRiskPenalty(session.Grid, tile.Coord);
                    int score = spellScore + elevBonus + aooPenalty - surfacePenalty - tile.PathCost;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDecision = new AiDecision(
                            isHeal ? CombatIntentActionType.HealAlly : CombatIntentActionType.MoveAndCast,
                            isHeal ? "MoveAndHeal" : "MoveAndCast",
                            spellCenter,
                            targetId,
                            tile.Coord,
                            spellKey);
                    }
                }
            }

            return bestDecision != null;
        }

        private static bool TryFindBestMoveAndRanged(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            out AiDecision? bestDecision)
        {
            bestDecision = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                CombatantState? target = PickBestRangedTarget(
                    session,
                    actor,
                    tile.Coord,
                    enemies,
                    ignoreActionBudget: true,
                    out int rangedScore);
                if (target == null)
                {
                    continue;
                }

                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                int safeDistBonus = minDist >= 2 ? 35 : -20;
                int elevBonus = session.Grid.GetElevation(tile.Coord.X, tile.Coord.Y) * 15;
                int aooPenalty = tile.ProvokesAoO ? -30 : 15;
                int surfacePenalty = GetSurfaceRiskPenalty(session.Grid, tile.Coord);
                int score = rangedScore + safeDistBonus + elevBonus + aooPenalty - surfacePenalty - tile.PathCost;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDecision = new AiDecision(
                        CombatIntentActionType.MoveAndRanged,
                        "MoveAndRanged",
                        target.Position,
                        target.Id,
                        tile.Coord);
                }
            }

            return bestDecision != null;
        }

        private static bool TryPickBestApproachTile(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            out ReachableTileInfo? bestAttackTile,
            out CombatantState? bestAttackTarget,
            out ReachableTileInfo? bestApproach)
        {
            bestAttackTile = null;
            bestAttackTarget = null;
            bestApproach = null;

            if (reachable.Count == 0)
            {
                return false;
            }

            int bestAttackScore = int.MinValue;
            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int surfacePenalty = GetSurfaceRiskPenalty(session.Grid, tile.Coord);
                for (int e = 0; e < enemies.Count; e++)
                {
                    CombatantState enemy = enemies[e];
                    if (!enemy.IsAlive || tile.Coord.ChebyshevDistance(enemy.Position) != 1)
                    {
                        continue;
                    }

                    bool flanking = session.IsFlanking(actor.Id, enemy.Id, tile.Coord);
                    int elevDiff = session.Grid.GetElevation(tile.Coord.X, tile.Coord.Y) -
                                   session.Grid.GetElevation(enemy.Position.X, enemy.Position.Y);
                    int score = (flanking ? 50 : 0) +
                                (elevDiff > 0 ? 20 : 0) +
                                (enemy.IsProne ? 20 : 0) +
                                (tile.ProvokesAoO ? -30 : 20) +
                                (100 - Math.Min(99, enemy.Hp)) -
                                surfacePenalty -
                                tile.PathCost;
                    if (score > bestAttackScore)
                    {
                        bestAttackScore = score;
                        bestAttackTile = tile;
                        bestAttackTarget = enemy;
                    }
                }
            }

            if (bestAttackTile != null)
            {
                return true;
            }

            int bestDist = int.MaxValue;
            int bestPenalty = int.MaxValue;
            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minEnemyDist = int.MaxValue;
                for (int e = 0; e < enemies.Count; e++)
                {
                    if (!enemies[e].IsAlive)
                    {
                        continue;
                    }

                    int d = tile.Coord.ManhattanDistance(enemies[e].Position);
                    if (d < minEnemyDist)
                    {
                        minEnemyDist = d;
                    }
                }

                int penalty = (tile.ProvokesAoO ? 20 : 0) + GetSurfaceRiskPenalty(session.Grid, tile.Coord);
                if (minEnemyDist < bestDist ||
                    (minEnemyDist == bestDist && penalty < bestPenalty) ||
                    (minEnemyDist == bestDist && penalty == bestPenalty && tile.PathCost < (bestApproach?.PathCost ?? int.MaxValue)))
                {
                    bestDist = minEnemyDist;
                    bestPenalty = penalty;
                    bestApproach = tile;
                }
            }

            if (bestApproach != null)
            {
                int currentNearest = int.MaxValue;
                for (int e = 0; e < enemies.Count; e++)
                {
                    if (enemies[e].IsAlive)
                    {
                        currentNearest = Math.Min(currentNearest, actor.Position.ManhattanDistance(enemies[e].Position));
                    }
                }

                if (bestDist < currentNearest)
                {
                    return true;
                }

                bestApproach = null;
            }

            return false;
        }

        private static CombatantState? PickBestMeleeTarget(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            List<CombatantState> enemies,
            bool ignoreActionBudget,
            out int bestScore)
        {
            CombatantState? best = null;
            bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive || fromPos.ChebyshevDistance(enemy.Position) != 1)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewMeleeAttackInternal(
                    actor.Id,
                    enemy.Id,
                    fromPos,
                    ignoreActionBudget);
                if (!preview.CanExecute)
                {
                    continue;
                }

                int score = preview.HitChancePct +
                            (preview.IsFlanking ? 25 : 0) +
                            (enemy.IsProne ? 20 : 0) +
                            (100 - Math.Min(99, enemy.Hp));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }

            return best;
        }

        private static CombatantState? PickBestRangedTarget(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            List<CombatantState> enemies,
            bool ignoreActionBudget,
            out int bestScore)
        {
            CombatantState? best = null;
            bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewRangedAttackInternal(
                    actor.Id,
                    enemy.Id,
                    fromPos,
                    ignoreActionBudget);
                if (!preview.CanExecute)
                {
                    continue;
                }

                int score = preview.HitChancePct + (100 - Math.Min(99, enemy.Hp));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }

            return best;
        }

        private static GridCoord? PickBestFleeCoord(
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies)
        {
            if (reachable.Count == 0)
            {
                return null;
            }

            ReachableTileInfo? best = null;
            int bestMinDist = -1;
            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minD = MinChebyshevDistance(tile.Coord, enemies);
                if (minD > bestMinDist)
                {
                    bestMinDist = minD;
                    best = tile;
                }
            }

            return best?.Coord;
        }

        private static HashSet<GridCoord> BuildCandidateSpellCenters(CombatGrid grid, List<CombatantState> enemies)
        {
            HashSet<GridCoord> centers = new HashSet<GridCoord>();
            for (int e = 0; e < enemies.Count; e++)
            {
                GridCoord pos = enemies[e].Position;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int cx = pos.X + dx;
                        int cy = pos.Y + dy;
                        if (grid.InBounds(cx, cy) && !grid.BlocksLos(cx, cy))
                        {
                            centers.Add(new GridCoord(cx, cy));
                        }
                    }
                }
            }

            return centers;
        }

        private static int ScoreSpellSurfaceCombo(
            CombatGrid grid,
            IReadOnlyList<GridCoord> tiles,
            string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
            {
                return 0;
            }

            string dt = damageType.Trim().ToLowerInvariant();
            bool isHeat = dt == "heat" || dt == "fire";
            bool isCold = dt == "cold" || dt == "ice" || dt == "frost";
            bool isElec = dt == "electrical" || dt == "electric" || dt == "shock" || dt == "lightning";

            int bonus = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                SurfaceType surf = grid.GetSurface(tiles[i].X, tiles[i].Y);
                if (isHeat && surf == SurfaceType.Oil)
                {
                    bonus += 25;
                }
                else if (isCold && surf == SurfaceType.Water)
                {
                    bonus += 15;
                }
                else if (isElec && surf == SurfaceType.Water)
                {
                    bonus += 20;
                }
            }

            return bonus;
        }

        private static int GetSurfaceRiskPenalty(CombatGrid grid, GridCoord coord)
        {
            return grid.GetSurface(coord.X, coord.Y) switch
            {
                SurfaceType.Fire => 40,
                SurfaceType.ElectrifiedWater => 35,
                SurfaceType.Poison => 25,
                SurfaceType.Ice => 10,
                _ => 0
            };
        }

        private static int MinChebyshevDistance(GridCoord pos, List<CombatantState> units)
        {
            int minD = int.MaxValue;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsAlive)
                {
                    int d = pos.ChebyshevDistance(units[i].Position);
                    if (d < minD)
                    {
                        minD = d;
                    }
                }
            }

            return minD;
        }

        private static CombatantState? FindNearestEnemy(GridCoord pos, List<CombatantState> enemies)
        {
            CombatantState? best = null;
            int bestD = int.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].IsAlive)
                {
                    continue;
                }

                int d = pos.ManhattanDistance(enemies[i].Position);
                if (d < bestD)
                {
                    bestD = d;
                    best = enemies[i];
                }
            }

            return best;
        }

        private static List<CombatantState> CollectLivingBySide(TacticalCombatSession session, CombatSide side)
        {
            List<CombatantState> list = new List<CombatantState>();
            for (int i = 0; i < session.Combatants.Count; i++)
            {
                CombatantState c = session.Combatants[i];
                if (c.IsAlive && c.Side == side)
                {
                    list.Add(c);
                }
            }

            return list;
        }
    }
}
