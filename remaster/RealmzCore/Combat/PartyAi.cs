using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public static class PartyAi
    {
        private sealed class SpellCandidate
        {
            public SpellCandidate(
                string spellKey,
                GridCoord center,
                string targetId,
                int score,
                bool isHeal,
                bool isCure,
                bool isBuff,
                int apCost = 3)
            {
                SpellIdOrKey = spellKey;
                SpellKey = spellKey;
                Center = center;
                TargetId = targetId;
                Score = score;
                IsHeal = isHeal;
                IsCure = isCure;
                IsBuff = isBuff;
                ApCost = apCost;
            }

            public string SpellIdOrKey { get; }
            public string SpellKey { get; }
            public GridCoord Center { get; }
            public string TargetId { get; }
            public int Score { get; }
            public bool IsHeal { get; }
            public bool IsCure { get; }
            public bool IsBuff { get; }
            public int ApCost { get; }
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
                FinishTurn(session, actor);
                return new CombatActionResult(true, $"{actor.Name} is incapacitated.");
            }

            List<CombatantState> enemies = CollectEnemies(session, actor);
            if (enemies.Count == 0)
            {
                FinishTurn(session, actor);
                return new CombatActionResult(true, $"{actor.Name} has no targets.");
            }

            if (actor.Conditions.Has(CharConditionId.RunsAway))
            {
                IReadOnlyList<ReachableTileInfo> fleeTiles = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: false);
                GridCoord? fleeCoord = PickBestFleeCoord(session.Grid, fleeTiles, enemies);
                if (fleeCoord.HasValue && fleeCoord.Value != actor.Position)
                {
                    session.TryMoveTo(fleeCoord.Value);
                }

                FinishTurn(session, actor);
                return new CombatActionResult(true, $"{actor.Name} flees in panic.");
            }

            CombatActionResult finalResult = new CombatActionResult(true, $"{actor.Name} resolves turn.");
            List<string> turnMessages = new List<string>();
            List<AttackOutcome> turnOutcomes = new List<AttackOutcome>();
            int totalTurnDmg = 0;
            int totalTurnHeal = 0;
            bool anyAoo = false;
            int totalTurnAp = 0;
            bool tookOffensiveOrMajorAction = false;
            const int maxSubActions = 6;

            void Accumulate(CombatActionResult r)
            {
                if (!string.IsNullOrWhiteSpace(r.Message))
                {
                    turnMessages.Add(r.Message);
                }
                if (r.AttackOutcomes.Count > 0)
                {
                    turnOutcomes.AddRange(r.AttackOutcomes);
                }
                totalTurnDmg += r.DamageDealt;
                totalTurnHeal += r.HealingDone;
                anyAoo |= r.AoOTriggered;
                totalTurnAp += r.ApSpent;
            }

            for (int step = 0; step < maxSubActions; step++)
            {
                if (session.Outcome != CombatOutcome.InProgress ||
                    !string.Equals(session.CurrentActorId, actor.Id, StringComparison.Ordinal) ||
                    !actor.IsConscious ||
                    (actor.Economy.UsesApForActions
                        ? (actor.Economy.CurrentAp < CombatActionEconomy.LightWeaponApCost && actor.Economy.MoveRemaining <= 0)
                        : (!actor.Economy.HasSlot(CombatActionSlot.MainAction) &&
                           !actor.Economy.HasSlot(CombatActionSlot.BonusAction) &&
                           actor.Economy.MoveRemaining <= 0)) ||
                    actor.IsGuarding)
                {
                    break;
                }

                int apBefore = actor.Economy.CurrentAp;
                int freeBefore = actor.Economy.FreeMoveTilesRemaining;
                int moveBefore = actor.Economy.MoveRemaining;
                bool mainBefore = actor.Economy.HasSlot(CombatActionSlot.MainAction);
                bool bonusBefore = actor.Economy.HasSlot(CombatActionSlot.BonusAction);

                // Priority 1: Cleric healing spells (<65% HP) and critical swift potion (<50% HP).
                if (TryExecutePriority1Healing(session, actor, out CombatActionResult healResult))
                {
                    finalResult = healResult;
                    Accumulate(healResult);
                    List<CombatantState> alliesAfterHeal = CollectLivingBySide(session, actor.Side);
                    bool anyStillLow = false;
                    for (int a = 0; a < alliesAfterHeal.Count; a++)
                    {
                        if (alliesAfterHeal[a].Hp * 100 < alliesAfterHeal[a].MaxHp * 65)
                        {
                            anyStillLow = true;
                            break;
                        }
                    }

                    bool outOfFollowUpHeal = actor.Economy.UsesApForActions
                        ? (!anyStillLow || actor.Economy.CurrentAp < 2)
                        : (!anyStillLow || (!actor.Economy.HasSlot(CombatActionSlot.MainAction) && !actor.Economy.HasSlot(CombatActionSlot.BonusAction)));
                    if (outOfFollowUpHeal)
                    {
                        break;
                    }

                    if (actor.Economy.CurrentAp == apBefore &&
                        actor.Economy.FreeMoveTilesRemaining == freeBefore &&
                        actor.Economy.MoveRemaining == moveBefore &&
                        actor.Economy.HasSlot(CombatActionSlot.MainAction) == mainBefore &&
                        actor.Economy.HasSlot(CombatActionSlot.BonusAction) == bonusBefore)
                    {
                        break;
                    }

                    continue;
                }

                if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                {
                    break;
                }

                enemies = CollectEnemies(session, actor);
                if (enemies.Count == 0)
                {
                    break;
                }

                // Priority 2: Shove hazard/ledge opportunity (Bonus Action).
                if (actor.Economy.HasSlot(CombatActionSlot.BonusAction) &&
                    TryOpportunisticSwiftShove(session, actor, enemies, out CombatActionResult shoveResult))
                {
                    finalResult = shoveResult;
                    Accumulate(shoveResult);
                    tookOffensiveOrMajorAction = true;
                    continue;
                }

                // Bank <= 2 AP cleanly in unified mode, follow up with multiple actions in Split MP/AP mode (spending AP down),
                // or stop cleanly in D&D 5e mode once Main Action is spent.
                if (tookOffensiveOrMajorAction &&
                    (actor.Economy.UsesApForActions
                        ? (actor.Economy.CurrentAp < Math.Min(actor.MeleeApCost, CombatActionEconomy.LightWeaponApCost) &&
                           (actor.RangedRange <= 1 || string.IsNullOrWhiteSpace(actor.RangedDice) || !actor.Economy.HasApFor(actor.RangedApCost)) &&
                           (actor.SpellIds.Count == 0 || actor.Sp <= 0))
                        : !actor.Economy.HasSlot(CombatActionSlot.MainAction)))
                {
                    if ((actor.Economy.IsDnd5eMode || actor.Economy.IsSplitMpApMode) &&
                        actor.Economy.HasApFor(actor.Economy.EffectiveGuardApCost) &&
                        !actor.IsGuarding &&
                        (actor.Economy.IsSplitMpApMode || (actor.Economy.BonusActionGuardEnabled && actor.Economy.HasSlot(CombatActionSlot.BonusAction))))
                    {
                        CombatActionResult bonusGuard = session.GuardOverwatch();
                        if (bonusGuard.Success)
                        {
                            finalResult = bonusGuard;
                            Accumulate(bonusGuard);
                        }
                    }

                    break;
                }

                CombatActionResult subResult;
                if (actor.SpellIds.Count > 0)
                {
                    subResult = ExecuteCasterPriority(session, actor, enemies);
                }
                else if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    subResult = ExecuteRangerPriority(session, actor, enemies);
                }
                else
                {
                    subResult = ExecuteMeleePriority(session, actor, enemies);
                }

                if (subResult.Success)
                {
                    finalResult = subResult;
                    Accumulate(subResult);
                    if (subResult.DamageDealt > 0 ||
                        subResult.HealingDone > 0 ||
                        subResult.AttackOutcomes.Count > 0 ||
                        subResult.ApSpent > 0 ||
                        actor.Economy.MoveRemaining != moveBefore ||
                        actor.Economy.HasSlot(CombatActionSlot.MainAction) != mainBefore ||
                        actor.Economy.HasSlot(CombatActionSlot.BonusAction) != bonusBefore)
                    {
                        tookOffensiveOrMajorAction = true;
                    }
                }

                if (actor.Economy.CurrentAp == apBefore &&
                    actor.Economy.FreeMoveTilesRemaining == freeBefore &&
                    actor.Economy.MoveRemaining == moveBefore &&
                    actor.Economy.HasSlot(CombatActionSlot.MainAction) == mainBefore &&
                    actor.Economy.HasSlot(CombatActionSlot.BonusAction) == bonusBefore)
                {
                    break;
                }
            }

            if (turnMessages.Count > 1)
            {
                finalResult = new CombatActionResult(
                    true,
                    string.Join(" | ", turnMessages),
                    turnOutcomes,
                    damageDealt: totalTurnDmg,
                    healingDone: totalTurnHeal,
                    aooTriggered: anyAoo,
                    apSpent: totalTurnAp);
            }

            FinishTurn(session, actor);
            return finalResult;
        }

        private static bool TryExecutePriority1Healing(
            TacticalCombatSession session,
            CombatantState actor,
            out CombatActionResult result)
        {
            result = new CombatActionResult(false, string.Empty);

            List<CombatantState> allies = CollectLivingBySide(session, actor.Side);
            List<CombatantState> injuredAllies = new List<CombatantState>();
            for (int i = 0; i < allies.Count; i++)
            {
                CombatantState ally = allies[i];
                if (ally.Hp * 100 < ally.MaxHp * 65)
                {
                    injuredAllies.Add(ally);
                }
            }

            if (injuredAllies.Count == 0)
            {
                return false;
            }

            SortByLowestHpRatio(injuredAllies);

            if (actor.SpellIds.Count > 0 && actor.Sp > 0 && !actor.Conditions.Has(CharConditionId.Silenced))
            {
                if (TryFindBestHealSpell(session, actor, actor.Position, injuredAllies, ignoreActionBudget: false, out SpellCandidate? directHeal) &&
                    directHeal != null)
                {
                    result = session.CastSpell(directHeal.SpellKey, directHeal.Center);
                    return result.Success;
                }

                IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: false);
                ReachableTileInfo? bestMoveTile = null;
                SpellCandidate? bestMoveHeal = null;
                int bestMoveScore = int.MinValue;

                for (int r = 0; r < reachable.Count; r++)
                {
                    ReachableTileInfo tile = reachable[r];
                    if (TryFindBestHealSpell(session, actor, tile.Coord, injuredAllies, ignoreActionBudget: true, out SpellCandidate? moveHeal) &&
                        moveHeal != null)
                    {
                        int score = moveHeal.Score +
                                    (IsHazardousSurface(session.Grid.GetSurface(tile.Coord)) ? -1000 : 0) +
                                    (tile.ProvokesAoO ? -35 : 15) -
                                    tile.PathCost;
                        if (score > bestMoveScore)
                        {
                            bestMoveScore = score;
                            bestMoveTile = tile;
                            bestMoveHeal = moveHeal;
                        }
                    }
                }

                if (bestMoveTile != null && bestMoveHeal != null)
                {
                    session.TryMoveTo(bestMoveTile.Coord);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        result = new CombatActionResult(true, $"{actor.Name} moved to heal.");
                        return true;
                    }

                    if (session.PreviewSpell(actor.Id, bestMoveHeal.SpellKey, bestMoveHeal.Center).CanExecute)
                    {
                        result = session.CastSpell(bestMoveHeal.SpellKey, bestMoveHeal.Center);
                        return result.Success;
                    }
                }
            }

            // Fallback potion (Bonus Action) when no heal spell was cast and an ally is critically injured (<50% HP).
            if (actor.Economy.HasSlot(CombatActionSlot.BonusAction) && session.Party != null)
            {
                CombatantState? criticalAlly = null;
                for (int i = 0; i < injuredAllies.Count; i++)
                {
                    CombatantState ally = injuredAllies[i];
                    if (ally.Hp * 100 < ally.MaxHp * 50)
                    {
                        criticalAlly = ally;
                        break;
                    }
                }

                if (criticalAlly != null && TryPickBestHealingItem(session, criticalAlly, out int itemId))
                {
                    session.UseSwiftPotion(itemId, criticalAlly.Id);
                }
            }

            return false;
        }

        private static bool TryOpportunisticSwiftShove(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies,
            out CombatActionResult result)
        {
            result = new CombatActionResult(false, string.Empty);
            if (!actor.Economy.HasSlot(CombatActionSlot.BonusAction))
            {
                return false;
            }

            CombatantState? bestTarget = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState foe = enemies[i];
                if (!foe.IsAlive || actor.Position.ChebyshevDistance(foe.Position) != 1)
                {
                    continue;
                }

                TacticalShovePreview preview = session.PreviewShove(actor.Id, foe.Id);
                if (!preview.CanExecute || preview.SuccessChancePct < 50)
                {
                    continue;
                }

                int score = ScoreQualifyingShove(preview);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = foe;
                }
            }

            if (bestTarget == null)
            {
                return false;
            }

            result = session.TryShove(bestTarget.Id);
            return result.Success;
        }

        private static int ScoreQualifyingShove(TacticalShovePreview preview)
        {
            bool isHazard = IsHazardousSurface(preview.DestinationSurface);
            bool isWater = preview.DestinationSurface == SurfaceType.Water;
            bool hasCollision = !string.IsNullOrEmpty(preview.CollidingUnitId);

            if (!preview.PushesOffLedge &&
                !isHazard &&
                !isWater &&
                !preview.HitsWallOrBounds &&
                !hasCollision)
            {
                return int.MinValue;
            }

            int score = preview.SuccessChancePct;
            if (preview.PushesOffLedge)
            {
                score += 120 + (preview.FallElevationDrop * 30);
            }

            if (preview.DestinationSurface == SurfaceType.Fire || preview.DestinationSurface == SurfaceType.ElectrifiedWater)
            {
                score += 95;
            }
            else if (preview.DestinationSurface == SurfaceType.Poison)
            {
                score += 75;
            }
            else if (isWater)
            {
                score += 45;
            }

            if (hasCollision)
            {
                score += 55;
            }

            if (preview.HitsWallOrBounds)
            {
                score += 40;
            }

            return score;
        }

        private static CombatActionResult ExecuteCasterPriority(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            bool isAdjacent = MinChebyshevDistance(actor.Position, enemies) <= 1;
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: false);

            if (actor.Sp > 0 && !actor.Conditions.Has(CharConditionId.Silenced))
            {
                SpellCandidate? castHere = TryFindBestCasterSpell(session, actor, actor.Position, enemies, ignoreActionBudget: false);

                // If adjacent to an enemy, check if kiting without AoO still yields an equal or better cast.
                if (isAdjacent)
                {
                    if (TryFindBestSafeSpellMoveTile(session, actor, reachable, enemies, requireNoAoO: true, requireDistAtLeast2: true, out ReachableTileInfo? kiteTile, out SpellCandidate? kiteSpell) &&
                        kiteTile != null &&
                        kiteSpell != null &&
                        (castHere == null || kiteSpell.Score >= castHere.Score - 20))
                    {
                        session.TryMoveTo(kiteTile.Coord);
                        if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                        {
                            return new CombatActionResult(true, $"{actor.Name} repositioned.");
                        }

                        if (session.PreviewSpell(actor.Id, kiteSpell.SpellKey, kiteSpell.Center).CanExecute)
                        {
                            return session.CastSpell(kiteSpell.SpellKey, kiteSpell.Center);
                        }
                    }
                }

                if (castHere != null)
                {
                    return session.CastSpell(castHere.SpellKey, castHere.Center);
                }

                // Move into range/LOS on a safe tile to cast.
                if (TryFindBestSafeSpellMoveTile(session, actor, reachable, enemies, requireNoAoO: false, requireDistAtLeast2: false, out ReachableTileInfo? moveTile, out SpellCandidate? moveSpell) &&
                    moveTile != null &&
                    moveSpell != null)
                {
                    session.TryMoveTo(moveTile.Coord);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return new CombatActionResult(true, $"{actor.Name} moved to cast.");
                    }

                    if (session.PreviewSpell(actor.Id, moveSpell.SpellKey, moveSpell.Center).CanExecute)
                    {
                        return session.CastSpell(moveSpell.SpellKey, moveSpell.Center);
                    }
                }
            }

            // No SP or no safe cast: kite away if adjacent to an enemy (preferring !ProvokesAoO).
            if (isAdjacent)
            {
                ReachableTileInfo? safeKite = PickBestKiteTile(session, actor, reachable, enemies, requireNoAoO: true);
                if (safeKite != null)
                {
                    session.TryMoveTo(safeKite.Coord);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return new CombatActionResult(true, $"{actor.Name} kited away.");
                    }

                    enemies = CollectEnemies(session, actor);
                    if (enemies.Count == 0)
                    {
                        return new CombatActionResult(true, $"{actor.Name} kited away.");
                    }

                    if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                    {
                        CombatantState? rangedAfterKite = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                        if (rangedAfterKite != null)
                        {
                            return session.ExecuteRangedAttack(rangedAfterKite.Id);
                        }
                    }

                    return session.GuardOverwatch();
                }

                // If no AoO-free kite tile exists, allow ranged kite or fallback melee/guard.
                if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    ReachableTileInfo? rangedKite = PickBestKiteTile(session, actor, reachable, enemies, requireNoAoO: false);
                    if (rangedKite != null)
                    {
                        CombatantState? targetFromKite = PickBestRangedTarget(session, actor, rangedKite.Coord, enemies, ignoreActionBudget: true, out _, out _);
                        if (targetFromKite != null)
                        {
                            session.TryMoveTo(rangedKite.Coord);
                            if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                            {
                                return new CombatActionResult(true, $"{actor.Name} kited.");
                            }

                            if (session.PreviewRangedAttack(actor.Id, targetFromKite.Id).CanExecute)
                            {
                                return session.ExecuteRangedAttack(targetFromKite.Id);
                            }
                        }
                    }
                }
            }

            // Fallback for out-of-SP / out-of-range caster: ranged shot if equipped, else melee or safe advance + guard.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                CombatantState? rangedHere = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                if (rangedHere != null)
                {
                    return session.ExecuteRangedAttack(rangedHere.Id);
                }
            }

            CombatantState? meleeHere = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false);
            if (meleeHere != null && actor.Sp <= 0)
            {
                return session.ExecuteMeleeAttack(meleeHere.Id);
            }

            // Move safely toward an enemy if out of range with SP remaining, otherwise guard.
            if (actor.Sp > 0 && !actor.Conditions.Has(CharConditionId.Silenced))
            {
                ReachableTileInfo? approach = PickBestSafeApproachTile(session, actor, reachable, enemies);
                if (approach != null)
                {
                    session.TryMoveTo(approach.Coord);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return new CombatActionResult(true, $"{actor.Name} advances.");
                    }
                }
            }

            if (meleeHere != null)
            {
                return session.ExecuteMeleeAttack(meleeHere.Id);
            }

            if (actor.Economy.HasSlot(actor.Economy.GuardSlot))
            {
                return session.GuardOverwatch();
            }

            return new CombatActionResult(false, $"{actor.Name} holds position.");
        }

        private static CombatActionResult ExecuteRangerPriority(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: false);
            bool isAdjacent = MinChebyshevDistance(actor.Position, enemies) <= 1;

            // Step 1: If adjacent to an enemy, kite to a safe tile (Dist >= 2, !ProvokesAoO, High Ground, safe surface) with LOS to shoot.
            if (isAdjacent)
            {
                if (TryFindBestRangedKiteTile(session, actor, reachable, enemies, out ReachableTileInfo? kiteTile) &&
                    kiteTile != null)
                {
                    session.TryMoveTo(kiteTile.Coord);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return new CombatActionResult(true, $"{actor.Name} kited away.");
                    }

                    enemies = CollectEnemies(session, actor);
                    if (enemies.Count == 0)
                    {
                        return new CombatActionResult(true, $"{actor.Name} kited away.");
                    }

                    CombatantState? postKiteTarget = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                    if (postKiteTarget != null)
                    {
                        return session.ExecuteRangedAttack(postKiteTarget.Id);
                    }
                }
            }

            // Step 2: Check current ranged shot and whether moving to High Ground (+15% elevation + clear LOS + no AoO + safe surface) improves it.
            CombatantState? currentTarget = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out TacticalAttackPreview? currentPreview);
            int currentElev = session.Grid.GetElevation(actor.Position);
            int currentElevMod = currentPreview?.ElevationModifierPct ?? int.MinValue;

            ReachableTileInfo? highGroundTile = null;
            int bestHighGroundScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                if (tile.ProvokesAoO || IsHazardousSurface(session.Grid.GetSurface(tile.Coord)))
                {
                    continue;
                }

                if (MinChebyshevDistance(tile.Coord, enemies) < 2)
                {
                    continue;
                }

                int tileElev = session.Grid.GetElevation(tile.Coord);
                if (tileElev <= currentElev)
                {
                    continue;
                }

                CombatantState? targetFromTile = PickBestRangedTarget(
                    session,
                    actor,
                    tile.Coord,
                    enemies,
                    ignoreActionBudget: true,
                    out int targetScore,
                    out TacticalAttackPreview? tilePreview);
                if (targetFromTile == null || tilePreview == null || tilePreview.LosResult?.Kind != LosKind.Clear)
                {
                    continue;
                }

                if (actor.Economy.IsUnifiedApMode
                    ? (tile.PathCost + actor.RangedApCost > actor.Economy.CurrentAp)
                    : (tile.PathCost > actor.Economy.MoveRemaining || !actor.Economy.HasApFor(actor.RangedApCost)))
                {
                    continue;
                }

                if (tilePreview.ElevationModifierPct >= 15 && tilePreview.ElevationModifierPct > currentElevMod)
                {
                    int score = targetScore + (tileElev * 40) - tile.PathCost;
                    if (score > bestHighGroundScore)
                    {
                        bestHighGroundScore = score;
                        highGroundTile = tile;
                    }
                }
            }

            if (highGroundTile != null)
            {
                session.TryMoveTo(highGroundTile.Coord);
                if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                {
                    return new CombatActionResult(true, $"{actor.Name} moved to High Ground.");
                }

                enemies = CollectEnemies(session, actor);
                if (enemies.Count == 0)
                {
                    return new CombatActionResult(true, $"{actor.Name} moved to High Ground.");
                }

                CombatantState? hgTarget = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                if (hgTarget != null)
                {
                    return session.ExecuteRangedAttack(hgTarget.Id);
                }
            }

            // Step 3: Shoot best target from current position if available.
            if (currentTarget != null)
            {
                return session.ExecuteRangedAttack(currentTarget.Id);
            }

            // Step 4: If no shot from current pos, move to a safe tile with a valid ranged shot.
            ReachableTileInfo? moveShootTile = null;
            int bestMoveShootScore = int.MinValue;
            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                CombatantState? target = PickBestRangedTarget(session, actor, tile.Coord, enemies, ignoreActionBudget: true, out int targetScore, out TacticalAttackPreview? preview);
                if (target == null || preview == null ||
                    (actor.Economy.IsUnifiedApMode
                        ? (tile.PathCost + actor.RangedApCost > actor.Economy.CurrentAp)
                        : (tile.PathCost > actor.Economy.MoveRemaining || !actor.Economy.HasApFor(actor.RangedApCost))))
                {
                    continue;
                }

                bool hazardous = IsHazardousSurface(session.Grid.GetSurface(tile.Coord));
                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                int score = targetScore +
                            (hazardous ? -1000 : 0) +
                            (tile.ProvokesAoO ? -35 : 20) +
                            (minDist >= 2 ? 35 : -20) +
                            (preview.ElevationModifierPct > 0 ? 30 : 0) -
                            tile.PathCost;
                if (score > bestMoveShootScore)
                {
                    bestMoveShootScore = score;
                    moveShootTile = tile;
                }
            }

            if (moveShootTile != null)
            {
                session.TryMoveTo(moveShootTile.Coord);
                if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                {
                    return new CombatActionResult(true, $"{actor.Name} repositioned to shoot.");
                }

                enemies = CollectEnemies(session, actor);
                CombatantState? targetAfterMove = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                if (targetAfterMove != null)
                {
                    return session.ExecuteRangedAttack(targetAfterMove.Id);
                }
            }

            // Step 5: Adjacent melee fallback or safe advance + GuardOverwatch.
            CombatantState? meleeFallback = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false);
            if (meleeFallback != null)
            {
                return session.ExecuteMeleeAttack(meleeFallback.Id);
            }

            ReachableTileInfo? approach = PickBestSafeApproachTile(session, actor, reachable, enemies);
            if (approach != null)
            {
                session.TryMoveTo(approach.Coord);
                if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                {
                    return new CombatActionResult(true, $"{actor.Name} advances.");
                }
            }

            if (actor.Economy.HasSlot(actor.Economy.GuardSlot))
            {
                return session.GuardOverwatch();
            }

            return new CombatActionResult(false, $"{actor.Name} holds position.");
        }

        private static CombatActionResult ExecuteMeleePriority(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTilesInternal(actor.Id, ignoreMoveRemaining: false);

            GridCoord? bestTile = null;
            CombatantState? bestTarget = null;
            int bestScore = int.MinValue;

            // Evaluate current position + all reachable tiles adjacent to any living enemy.
            ScoreMeleeCandidatesAtTile(
                session,
                actor,
                actor.Position,
                pathCost: 0,
                provokesAoO: false,
                isCurrentPos: true,
                enemies,
                ref bestTile,
                ref bestTarget,
                ref bestScore);

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                ScoreMeleeCandidatesAtTile(
                    session,
                    actor,
                    tile.Coord,
                    tile.PathCost,
                    tile.ProvokesAoO,
                    isCurrentPos: false,
                    enemies,
                    ref bestTile,
                    ref bestTarget,
                    ref bestScore);
            }

            if (bestTile.HasValue && bestTarget != null)
            {
                if (bestTile.Value != actor.Position)
                {
                    session.TryMoveTo(bestTile.Value);
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return new CombatActionResult(true, $"{actor.Name} engaged enemy.");
                    }

                    enemies = CollectEnemies(session, actor);
                    if (enemies.Count == 0)
                    {
                        return new CombatActionResult(true, $"{actor.Name} cleared enemies.");
                    }
                }

                // Check Shove opportunity (Bonus Action) after moving adjacent to an enemy.
                if (actor.Economy.HasSlot(CombatActionSlot.BonusAction) &&
                    TryOpportunisticSwiftShove(session, actor, enemies, out CombatActionResult shoveResult))
                {
                    if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                    {
                        return shoveResult;
                    }

                    enemies = CollectEnemies(session, actor);
                    if (enemies.Count == 0)
                    {
                        return shoveResult;
                    }
                }

                if (bestTarget.IsAlive && session.PreviewMeleeAttack(actor.Id, bestTarget.Id).CanExecute)
                {
                    return session.ExecuteMeleeAttack(bestTarget.Id);
                }

                CombatantState? fallbackMelee = PickBestMeleeTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false);
                if (fallbackMelee != null)
                {
                    return session.ExecuteMeleeAttack(fallbackMelee.Id);
                }

                if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    CombatantState? ranged = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                    if (ranged != null)
                    {
                        return session.ExecuteRangedAttack(ranged.Id);
                    }
                }

                if (actor.Economy.HasSlot(actor.Economy.GuardSlot))
                {
                    return session.GuardOverwatch();
                }

                return new CombatActionResult(false, $"{actor.Name} holds position.");
            }

            // No melee target reachable this turn: try ranged weapon if available, else safe approach (+ Dash if D&D 5e) + GuardOverwatch.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                CombatantState? rangedHere = PickBestRangedTarget(session, actor, actor.Position, enemies, ignoreActionBudget: false, out _, out _);
                if (rangedHere != null)
                {
                    return session.ExecuteRangedAttack(rangedHere.Id);
                }
            }

            ReachableTileInfo? approach = PickBestSafeApproachTile(session, actor, reachable, enemies);
            bool moved = false;
            if (approach != null)
            {
                moved = session.TryMoveTo(approach.Coord).Success;
                if (session.Outcome != CombatOutcome.InProgress || !actor.IsConscious)
                {
                    return new CombatActionResult(true, $"{actor.Name} advances.");
                }
            }

            if ((actor.Economy.IsDnd5eMode || actor.Economy.IsSplitMpApMode) &&
                actor.Economy.CanPerformDash &&
                MinChebyshevDistance(actor.Position, enemies) > 1)
            {
                CombatActionResult dashRes = session.ExecuteDash();
                if (dashRes.Success)
                {
                    return dashRes;
                }
            }

            if (actor.Economy.HasSlot(actor.Economy.GuardSlot))
            {
                return session.GuardOverwatch();
            }

            return new CombatActionResult(moved, moved ? $"{actor.Name} advances." : $"{actor.Name} holds position.");
        }

        private static void ScoreMeleeCandidatesAtTile(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord coord,
            int pathCost,
            bool provokesAoO,
            bool isCurrentPos,
            List<CombatantState> enemies,
            ref GridCoord? bestTile,
            ref CombatantState? bestTarget,
            ref int bestScore)
        {
            bool hazardous = IsHazardousSurface(session.Grid.GetSurface(coord));

            for (int e = 0; e < enemies.Count; e++)
            {
                CombatantState foe = enemies[e];
                if (!foe.IsAlive || coord.ChebyshevDistance(foe.Position) != 1)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewMeleeAttackInternal(
                    actor.Id,
                    foe.Id,
                    coord,
                    ignoreActionBudget: true);
                if (!preview.CanExecute)
                {
                    continue;
                }

                bool isKillingBlow = preview.MaxDamage >= foe.Hp || foe.Hp <= 4;
                int surfacePenalty = hazardous ? (isKillingBlow ? -50 : -1000) : 0;
                int aooPenalty = provokesAoO ? (isKillingBlow ? -10 : -35) : 0;
                bool isFlanking = session.IsFlanking(actor.Id, foe.Id, coord);
                bool isHighGround = session.Grid.GetElevation(coord) > session.Grid.GetElevation(foe.Position);

                int shoveSetupBonus = 0;
                if (actor.Economy.HasSlot(CombatActionSlot.BonusAction))
                {
                    TacticalShovePreview shovePreview = session.PreviewShove(actor.Id, foe.Id, coord);
                    if (shovePreview.CanExecute && shovePreview.SuccessChancePct >= 50)
                    {
                        if (shovePreview.PushesOffLedge || IsHazardousSurface(shovePreview.DestinationSurface))
                        {
                            shoveSetupBonus = 55;
                        }
                        else if (shovePreview.DestinationSurface == SurfaceType.Water || !string.IsNullOrEmpty(shovePreview.CollidingUnitId))
                        {
                            shoveSetupBonus = 35;
                        }
                        else if (shovePreview.HitsWallOrBounds)
                        {
                            shoveSetupBonus = 15;
                        }
                    }
                }

                int threatBonus = foe.SpellIds.Count > 0 ? 15 : 0;
                int targetFinishBonus = (isKillingBlow ? 80 : 0) + (100 - Math.Min(99, foe.Hp)) + threatBonus;
                int stayBonus = isCurrentPos ? 5 : 0;

                bool canAttackAfterMove = actor.Economy.IsUnifiedApMode
                    ? (actor.Economy.CurrentAp - pathCost >= actor.MeleeApCost)
                    : (pathCost <= actor.Economy.MoveRemaining && actor.Economy.HasApFor(actor.MeleeApCost));

                int score = preview.HitChancePct +
                            (preview.IsFlanking ? 30 : 0) +
                            (canAttackAfterMove ? 500 : -500) +
                            (isHighGround ? 25 : 0) +
                            (foe.IsProne ? 20 : 0) +
                            aooPenalty +
                            surfacePenalty +
                            shoveSetupBonus +
                            targetFinishBonus +
                            stayBonus -
                            pathCost;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = coord;
                    bestTarget = foe;
                }
            }
        }

        private static bool TryFindBestHealSpell(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            List<CombatantState> injuredAllies,
            bool ignoreActionBudget,
            out SpellCandidate? bestHeal)
        {
            bestHeal = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < actor.SpellIds.Count; i++)
            {
                if (!session.Spells.TryGetValue(actor.SpellIds[i], out SpellDefinition? spell) ||
                    actor.Sp < spell.SpCost ||
                    !string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string spellKey = !string.IsNullOrWhiteSpace(spell.Key) ? spell.Key : spell.Id.ToString();
                for (int a = 0; a < injuredAllies.Count; a++)
                {
                    CombatantState ally = injuredAllies[a];
                    TacticalSpellPreview preview = session.PreviewSpellInternal(
                        actor.Id,
                        spellKey,
                        ally.Position,
                        fromPos,
                        ignoreActionBudget);
                    if (!preview.CanExecute || preview.AoeFootprint.AffectedAllyIds.Count == 0)
                    {
                        continue;
                    }

                    int missingPct = 100 - ((ally.Hp * 100) / Math.Max(1, ally.MaxHp));
                    int multiBonus = preview.AoeFootprint.AffectedAllyIds.Count > 1 ? 25 : 0;
                    int score = 150 + missingPct + multiBonus + Math.Min(30, preview.MaxAmount);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHeal = new SpellCandidate(spellKey, ally.Position, ally.Id, score, isHeal: true, isCure: false, isBuff: false, apCost: preview.ApCost);
                    }
                }
            }

            return bestHeal != null;
        }

        private static SpellCandidate? TryFindBestCasterSpell(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            List<CombatantState> enemies,
            bool ignoreActionBudget)
        {
            if (actor.SpellIds.Count == 0 || actor.Sp <= 0 || actor.Conditions.Has(CharConditionId.Silenced))
            {
                return null;
            }

            List<CombatantState> allies = CollectLivingBySide(session, actor.Side);
            HashSet<GridCoord> aoeCenters = BuildCandidateSpellCenters(session.Grid, enemies);

            SpellCandidate? best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < actor.SpellIds.Count; i++)
            {
                if (!session.Spells.TryGetValue(actor.SpellIds[i], out SpellDefinition? spell) || actor.Sp < spell.SpCost)
                {
                    continue;
                }

                string spellKey = !string.IsNullOrWhiteSpace(spell.Key) ? spell.Key : spell.Id.ToString();
                if (string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase))
                {
                    for (int a = 0; a < allies.Count; a++)
                    {
                        CombatantState ally = allies[a];
                        int severity = GetAfflictionSeverity(ally);
                        if (severity <= 0)
                        {
                            continue;
                        }

                        TacticalSpellPreview preview = session.PreviewSpellInternal(actor.Id, spellKey, ally.Position, fromPos, ignoreActionBudget);
                        if (!preview.CanExecute)
                        {
                            continue;
                        }

                        int score = 100 + severity;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new SpellCandidate(spellKey, ally.Position, ally.Id, score, isHeal: false, isCure: true, isBuff: false, apCost: preview.ApCost);
                        }
                    }

                    continue;
                }

                if (string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase))
                {
                    if (!spell.AppliedCondition.HasValue)
                    {
                        continue;
                    }

                    for (int a = 0; a < allies.Count; a++)
                    {
                        CombatantState ally = allies[a];
                        if (ally.Conditions.Has(spell.AppliedCondition.Value))
                        {
                            continue;
                        }

                        if (string.Equals(spell.TargetMode, "Self", StringComparison.OrdinalIgnoreCase) && ally.Id != actor.Id)
                        {
                            continue;
                        }

                        TacticalSpellPreview preview = session.PreviewSpellInternal(actor.Id, spellKey, ally.Position, fromPos, ignoreActionBudget);
                        if (!preview.CanExecute || preview.AoeFootprint.AffectedAllyIds.Count == 0)
                        {
                            continue;
                        }

                        int unbuffedCount = 0;
                        for (int idIdx = 0; idIdx < preview.AoeFootprint.AffectedAllyIds.Count; idIdx++)
                        {
                            CombatantState? hitAlly = session.GetCombatant(preview.AoeFootprint.AffectedAllyIds[idIdx]);
                            if (hitAlly != null && hitAlly.IsAlive && !hitAlly.Conditions.Has(spell.AppliedCondition.Value))
                            {
                                unbuffedCount++;
                            }
                        }

                        if (unbuffedCount == 0)
                        {
                            continue;
                        }

                        int frontlineBonus = !ally.PrefersRanged ? 10 : 0;
                        int score = 45 + (unbuffedCount * 15) + frontlineBonus;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new SpellCandidate(spellKey, ally.Position, ally.Id, score, isHeal: false, isCure: false, isBuff: true, apCost: preview.ApCost);
                        }
                    }

                    continue;
                }

                // Hostile Damage / Condition spells: strictly enforce AffectedAllyIds.Count == 0!
                bool isAllEnemies = string.Equals(spell.TargetMode, "AllEnemies", StringComparison.OrdinalIgnoreCase);
                bool isAoe = isAllEnemies ||
                             spell.AoeOffsets.Count > 1 ||
                             string.Equals(spell.TargetMode, "AreaOfEffect", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(spell.TargetMode, "Area", StringComparison.OrdinalIgnoreCase);

                IEnumerable<GridCoord> candidateCenters;
                if (isAllEnemies)
                {
                    candidateCenters = enemies.Count > 0 ? new[] { enemies[0].Position } : Array.Empty<GridCoord>();
                }
                else if (isAoe)
                {
                    candidateCenters = aoeCenters;
                }
                else
                {
                    List<GridCoord> enemyCenters = new List<GridCoord>(enemies.Count);
                    for (int e = 0; e < enemies.Count; e++)
                    {
                        enemyCenters.Add(enemies[e].Position);
                    }

                    candidateCenters = enemyCenters;
                }

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
                    int killCount = 0;
                    string primaryTargetId = string.Empty;
                    int lowestHp = int.MaxValue;
                    int avgDmg = (preview.MinAmount + preview.MaxAmount) / 2;

                    for (int e = 0; e < preview.AoeFootprint.AffectedEnemyIds.Count; e++)
                    {
                        CombatantState? victim = session.GetCombatant(preview.AoeFootprint.AffectedEnemyIds[e]);
                        if (victim == null || !victim.IsAlive || TacticalCombatSession.IsTargetImmuneToSpell(victim, spell))
                        {
                            continue;
                        }

                        nonImmuneHits++;
                        lowHpBonus += Math.Max(0, 30 - victim.Hp);
                        if (avgDmg >= victim.Hp)
                        {
                            killCount++;
                        }

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
                    bool directOnEnemy = session.GetLivingUnitAt(center)?.Side != actor.Side && session.GetLivingUnitAt(center) != null;

                    int score;
                    if (nonImmuneHits >= 2)
                    {
                        score = (nonImmuneHits * 130) + (avgDmg * nonImmuneHits) + (killCount * 40) + lowHpBonus + comboBonus + (directOnEnemy ? 5 : 0);
                    }
                    else
                    {
                        int spPenalty = (isAoe && !isAllEnemies) ? spell.SpCost * 2 : spell.SpCost;
                        score = 80 + (avgDmg * 2) + (killCount * 50) + lowHpBonus + comboBonus - spPenalty + (directOnEnemy ? 5 : 0);
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new SpellCandidate(spellKey, center, primaryTargetId, score, isHeal: false, isCure: false, isBuff: false, apCost: preview.ApCost);
                    }
                }
            }

            return best;
        }

        private static bool TryFindBestSafeSpellMoveTile(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            bool requireNoAoO,
            bool requireDistAtLeast2,
            out ReachableTileInfo? bestTile,
            out SpellCandidate? bestSpell)
        {
            bestTile = null;
            bestSpell = null;
            int bestCombinedScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                if (requireNoAoO && tile.ProvokesAoO)
                {
                    continue;
                }

                if (requireDistAtLeast2 && MinChebyshevDistance(tile.Coord, enemies) < 2)
                {
                    continue;
                }

                if (IsHazardousSurface(session.Grid.GetSurface(tile.Coord)))
                {
                    continue;
                }

                SpellCandidate? candidate = TryFindBestCasterSpell(session, actor, tile.Coord, enemies, ignoreActionBudget: true);
                if (candidate == null ||
                    (actor.Economy.IsUnifiedApMode
                        ? (tile.PathCost + candidate.ApCost > actor.Economy.CurrentAp)
                        : (tile.PathCost > actor.Economy.MoveRemaining || !actor.Economy.HasApFor(candidate.ApCost))))
                {
                    continue;
                }

                int elevBonus = session.Grid.GetElevation(tile.Coord) * 15;
                int aooPenalty = tile.ProvokesAoO ? -35 : 20;
                int distBonus = MinChebyshevDistance(tile.Coord, enemies) >= 2 ? 15 : 0;
                int combined = candidate.Score + elevBonus + aooPenalty + distBonus - tile.PathCost;

                if (combined > bestCombinedScore)
                {
                    bestCombinedScore = combined;
                    bestTile = tile;
                    bestSpell = candidate;
                }
            }

            return bestTile != null && bestSpell != null;
        }

        private static bool TryFindBestRangedKiteTile(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            out ReachableTileInfo? bestTile)
        {
            bestTile = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                if (minDist < 2 ||
                    (actor.Economy.IsUnifiedApMode
                        ? (tile.PathCost + actor.RangedApCost > actor.Economy.CurrentAp)
                        : (tile.PathCost > actor.Economy.MoveRemaining || !actor.Economy.HasApFor(actor.RangedApCost))))
                {
                    continue;
                }

                bool hazardous = IsHazardousSurface(session.Grid.GetSurface(tile.Coord));
                CombatantState? target = PickBestRangedTarget(session, actor, tile.Coord, enemies, ignoreActionBudget: true, out int targetScore, out TacticalAttackPreview? preview);
                if (target == null || preview == null ||
                    (actor.Economy.IsUnifiedApMode
                        ? (tile.PathCost + actor.RangedApCost > actor.Economy.CurrentAp)
                        : (tile.PathCost > actor.Economy.MoveRemaining || !actor.Economy.HasApFor(actor.RangedApCost))))
                {
                    continue;
                }

                int elevBonus = preview.ElevationModifierPct > 0 ? 50 : session.Grid.GetElevation(tile.Coord) * 15;
                int aooScore = tile.ProvokesAoO ? -150 : 200;
                int hazardPenalty = hazardous ? -1000 : 0;
                int distScore = Math.Min(6, minDist) * 10;
                int score = targetScore + aooScore + hazardPenalty + elevBonus + distScore - tile.PathCost;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            return bestTile != null;
        }

        private static ReachableTileInfo? PickBestKiteTile(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies,
            bool requireNoAoO)
        {
            ReachableTileInfo? best = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                if (requireNoAoO && tile.ProvokesAoO)
                {
                    continue;
                }

                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                if (minDist < 2)
                {
                    continue;
                }

                bool hazardous = IsHazardousSurface(session.Grid.GetSurface(tile.Coord));
                bool leavesGuardAp = actor.Economy.IsUnifiedApMode
                    ? ((actor.Economy.CurrentAp - tile.PathCost) >= actor.Economy.EffectiveGuardApCost)
                    : (actor.Economy.IsSplitMpApMode
                        ? actor.Economy.HasApFor(actor.Economy.EffectiveGuardApCost)
                        : actor.Economy.HasSlot(actor.Economy.GuardSlot));
                int score = (leavesGuardAp ? 200 : 0) +
                            (minDist * 20) +
                            (session.Grid.GetElevation(tile.Coord) * 10) +
                            (tile.ProvokesAoO ? -150 : 100) +
                            (hazardous ? -1000 : 0) -
                            tile.PathCost;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }

            return best;
        }

        private static ReachableTileInfo? PickBestSafeApproachTile(
            TacticalCombatSession session,
            CombatantState actor,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies)
        {
            int currentNearest = MinManhattanDistance(actor.Position, enemies);
            ReachableTileInfo? bestSafe = null;
            int bestSafeScore = int.MinValue;

            ReachableTileInfo? bestAny = null;
            int bestAnyScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minDist = MinManhattanDistance(tile.Coord, enemies);
                if (minDist >= currentNearest)
                {
                    continue;
                }

                bool hazardous = IsHazardousSurface(session.Grid.GetSurface(tile.Coord));
                int progress = (currentNearest - minDist) * 40;
                int elevBonus = session.Grid.GetElevation(tile.Coord) * 10;
                int aooPenalty = tile.ProvokesAoO ? -40 : 10;
                int icePenalty = session.Grid.GetSurface(tile.Coord) == SurfaceType.Ice ? -10 : 0;
                int score = progress + elevBonus + aooPenalty + icePenalty - tile.PathCost;

                if (!hazardous && score > bestSafeScore)
                {
                    bestSafeScore = score;
                    bestSafe = tile;
                }

                int anyScore = score + (hazardous ? -1000 : 0);
                if (anyScore > bestAnyScore)
                {
                    bestAnyScore = anyScore;
                    bestAny = tile;
                }
            }

            return bestSafe ?? bestAny;
        }

        private static CombatantState? PickBestMeleeTarget(
            TacticalCombatSession session,
            CombatantState actor,
            GridCoord fromPos,
            List<CombatantState> enemies,
            bool ignoreActionBudget)
        {
            CombatantState? best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive || fromPos.ChebyshevDistance(enemy.Position) != 1)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewMeleeAttackInternal(actor.Id, enemy.Id, fromPos, ignoreActionBudget);
                if (!preview.CanExecute)
                {
                    continue;
                }

                bool killable = preview.MaxDamage >= enemy.Hp || enemy.Hp <= 4;
                int score = preview.HitChancePct +
                            (preview.IsFlanking ? 30 : 0) +
                            (enemy.IsProne ? 20 : 0) +
                            (killable ? 60 : 0) +
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
            out int bestScore,
            out TacticalAttackPreview? bestPreview)
        {
            CombatantState? best = null;
            bestScore = int.MinValue;
            bestPreview = null;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewRangedAttackInternal(actor.Id, enemy.Id, fromPos, ignoreActionBudget);
                if (!preview.CanExecute)
                {
                    continue;
                }

                bool killable = preview.MaxDamage >= enemy.Hp || enemy.Hp <= 4;
                int losBonus = preview.LosResult?.Kind == LosKind.Clear ? 30 : 0;
                int score = preview.HitChancePct +
                            losBonus +
                            (killable ? 100 : 0) +
                            (preview.ElevationModifierPct > 0 ? 15 : 0) +
                            (100 - Math.Min(99, enemy.Hp));

                if (score > bestScore)
                {
                    bestScore = score;
                    best = enemy;
                    bestPreview = preview;
                }
            }

            return best;
        }

        private static bool TryPickBestHealingItem(
            TacticalCombatSession session,
            CombatantState target,
            out int bestItemId)
        {
            bestItemId = 0;
            if (session.Party == null)
            {
                return false;
            }

            int missingHp = Math.Max(1, target.MaxHp - target.Hp);
            int bestScore = int.MinValue;

            for (int i = 0; i < session.Party.Stash.Count; i++)
            {
                InventoryItem entry = session.Party.Stash[i];
                if (entry.Quantity <= 0 || !session.Items.TryGetValue(entry.ItemId, out ItemDefinition? def) || def == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(def.HealDice) || !DiceSpec.TryParse(def.HealDice, out DiceSpec spec))
                {
                    continue;
                }

                int avgHeal = (int)Math.Round(spec.ExpectedAverage);
                int effectiveHeal = Math.Min(missingHp, avgHeal);
                int overhealWaste = Math.Max(0, avgHeal - missingHp);
                int score = (effectiveHeal * 10) - overhealWaste;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestItemId = entry.ItemId;
                }
            }

            return bestItemId != 0;
        }

        private static int GetAfflictionSeverity(CombatantState ally)
        {
            int score = 0;
            if (ally.Conditions.Has(CharConditionId.Helpless)) score += 45;
            if (ally.Conditions.Has(CharConditionId.Confused)) score += 40;
            if (ally.Conditions.Has(CharConditionId.Poisoned)) score += 30;
            if (ally.Conditions.Has(CharConditionId.Cursed) || ally.CurseStacks > 0) score += 25;
            if (ally.Conditions.Has(CharConditionId.Diseased)) score += 20;
            if (ally.ExhaustionStacks > 0) score += 15;
            return score;
        }

        private static GridCoord? PickBestFleeCoord(
            CombatGrid grid,
            IReadOnlyList<ReachableTileInfo> reachable,
            List<CombatantState> enemies)
        {
            ReachableTileInfo? best = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minDist = MinChebyshevDistance(tile.Coord, enemies);
                int hazardPenalty = IsHazardousSurface(grid.GetSurface(tile.Coord)) ? -1000 : 0;
                int score = (minDist * 20) + hazardPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
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

        private static bool IsHazardousSurface(SurfaceType surface)
        {
            return surface == SurfaceType.Fire ||
                   surface == SurfaceType.ElectrifiedWater ||
                   surface == SurfaceType.Poison;
        }

        private static void SortByLowestHpRatio(List<CombatantState> allies)
        {
            allies.Sort((a, b) =>
            {
                long ratioA = (a.Hp * 1000L) / Math.Max(1, a.MaxHp);
                long ratioB = (b.Hp * 1000L) / Math.Max(1, b.MaxHp);
                int cmp = ratioA.CompareTo(ratioB);
                if (cmp != 0) return cmp;
                cmp = a.Hp.CompareTo(b.Hp);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
            });
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

        private static int MinManhattanDistance(GridCoord pos, List<CombatantState> units)
        {
            int minD = int.MaxValue;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsAlive)
                {
                    int d = pos.ManhattanDistance(units[i].Position);
                    if (d < minD)
                    {
                        minD = d;
                    }
                }
            }

            return minD;
        }

        private static List<CombatantState> CollectEnemies(TacticalCombatSession session, CombatantState actor)
        {
            CombatSide enemySide = actor.Side == CombatSide.Hero ? CombatSide.Monster : CombatSide.Hero;
            return CollectLivingBySide(session, enemySide);
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

        private static void FinishTurn(TacticalCombatSession session, CombatantState actor)
        {
            if (session.Outcome == CombatOutcome.InProgress &&
                string.Equals(session.CurrentActorId, actor.Id, StringComparison.Ordinal))
            {
                session.EndTurn();
            }
        }
    }
}
