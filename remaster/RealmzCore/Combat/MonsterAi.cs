using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public static class MonsterAi
    {
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

            List<CombatantState> enemies = CollectLivingBySide(session, actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);
            if (enemies.Count == 0)
            {
                session.EndTurn();
                return new CombatActionResult(true, $"{actor.Name} has no targets.");
            }

            // 1. Panicked units flee away from enemies.
            if (actor.Conditions.Has(CharConditionId.RunsAway))
            {
                TryFlee(session, actor, enemies);
                session.EndTurn();
                return new CombatActionResult(true, $"{actor.Name} flees in panic.");
            }

            // 2. Try casting a beneficial heal or safe offensive/CC spell if available.
            if (TryBestSpell(session, actor, out CombatActionResult? spellResult))
            {
                session.EndTurn();
                return spellResult!;
            }

            // 3. Check if already in melee range of any enemy.
            CombatantState? adjacentTarget = PickBestMeleeTarget(session, actor, enemies);
            if (adjacentTarget != null)
            {
                CombatActionResult meleeRes = session.ExecuteMeleeAttack(adjacentTarget.Id);
                session.EndTurn();
                return meleeRes;
            }

            // 4. Check ranged attack if equipped and in LOS before stepping into danger.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                CombatantState? rangedTarget = PickBestRangedTarget(session, actor, enemies);
                if (rangedTarget != null)
                {
                    CombatActionResult rangedRes = session.ExecuteRangedAttack(rangedTarget.Id);
                    session.EndTurn();
                    return rangedRes;
                }
            }

            // 5. Move toward a melee engagement tile (preferring flanking & avoiding AoO), or step toward nearest enemy.
            bool moved = TryMoveToBestEngagement(session, actor, enemies);
            if (session.Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(true, $"{actor.Name} resolved turn.");
            }

            if (actor.IsConscious)
            {
                adjacentTarget = PickBestMeleeTarget(session, actor, enemies);
                if (adjacentTarget != null)
                {
                    CombatActionResult meleeAfterMove = session.ExecuteMeleeAttack(adjacentTarget.Id);
                    session.EndTurn();
                    return meleeAfterMove;
                }

                if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
                {
                    CombatantState? rangedAfterMove = PickBestRangedTarget(session, actor, enemies);
                    if (rangedAfterMove != null)
                    {
                        CombatActionResult rangedRes = session.ExecuteRangedAttack(rangedAfterMove.Id);
                        session.EndTurn();
                        return rangedRes;
                    }
                }

                if (actor.Economy.HasStandardAction)
                {
                    session.GuardOverwatch();
                }
            }

            session.EndTurn();
            return new CombatActionResult(true, moved ? $"{actor.Name} advances and guards." : $"{actor.Name} holds position.");
        }

        private static bool TryBestSpell(
            TacticalCombatSession session,
            CombatantState actor,
            out CombatActionResult? result)
        {
            result = null;
            if (actor.SpellIds.Count == 0 || actor.Sp <= 0 || actor.Conditions.Has(CharConditionId.Silenced))
            {
                return false;
            }

            List<CombatantState> allies = CollectLivingBySide(session, actor.Side);
            List<CombatantState> enemies = CollectLivingBySide(session, actor.Side == CombatSide.Monster ? CombatSide.Hero : CombatSide.Monster);

            for (int i = 0; i < actor.SpellIds.Count; i++)
            {
                int spellId = actor.SpellIds[i];
                if (!session.Spells.TryGetValue(spellId, out SpellDefinition? spell) || actor.Sp < spell.SpCost)
                {
                    continue;
                }

                string spellKey = spell.Id.ToString();
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
                        TacticalSpellPreview preview = session.PreviewSpell(actor.Id, spellKey, injured.Position);
                        if (preview.CanExecute)
                        {
                            result = session.CastSpell(spellKey, injured.Position);
                            return result.Success;
                        }
                    }
                }
                else if (!string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase))
                {
                    GridCoord? bestCenter = null;
                    int bestScore = 0;
                    for (int e = 0; e < enemies.Count; e++)
                    {
                        TacticalSpellPreview preview = session.PreviewSpell(actor.Id, spellKey, enemies[e].Position);
                        if (!preview.CanExecute)
                        {
                            continue;
                        }

                        // Avoid hitting allies with AoE.
                        if (preview.AoeFootprint.AffectedAllyIds.Count > 0)
                        {
                            continue;
                        }

                        int score = preview.AoeFootprint.AffectedEnemyIds.Count;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCenter = enemies[e].Position;
                        }
                    }

                    if (bestCenter.HasValue)
                    {
                        result = session.CastSpell(spellKey, bestCenter.Value);
                        return result.Success;
                    }
                }
            }

            return false;
        }

        private static CombatantState? PickBestMeleeTarget(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            CombatantState? best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive || actor.Position.ChebyshevDistance(enemy.Position) != 1)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewMeleeAttack(actor.Id, enemy.Id);
                if (!preview.CanExecute)
                {
                    continue;
                }

                int score = preview.HitChancePct + (preview.IsFlanking ? 25 : 0) + (100 - Math.Min(99, enemy.Hp));
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
            List<CombatantState> enemies)
        {
            CombatantState? best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < enemies.Count; i++)
            {
                CombatantState enemy = enemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                TacticalAttackPreview preview = session.PreviewRangedAttack(actor.Id, enemy.Id);
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

        private static bool TryMoveToBestEngagement(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTiles(actor.Id);
            if (reachable.Count == 0)
            {
                return false;
            }

            // First priority: reachable tile adjacent to an enemy (prefer no AoO, flanking, lowest target HP).
            ReachableTileInfo? bestAttackTile = null;
            int bestAttackScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                for (int e = 0; e < enemies.Count; e++)
                {
                    CombatantState enemy = enemies[e];
                    if (!enemy.IsAlive || tile.Coord.ChebyshevDistance(enemy.Position) != 1)
                    {
                        continue;
                    }

                    bool flanking = session.IsFlanking(actor.Id, enemy.Id, tile.Coord);
                    int score = (flanking ? 50 : 0) +
                                (tile.ProvokesAoO ? -30 : 20) +
                                (100 - Math.Min(99, enemy.Hp)) -
                                tile.PathCost;
                    if (score > bestAttackScore)
                    {
                        bestAttackScore = score;
                        bestAttackTile = tile;
                    }
                }
            }

            if (bestAttackTile != null)
            {
                return session.TryMoveTo(bestAttackTile.Coord).Success;
            }

            // Second priority: move as close as possible to the nearest living enemy without provoking unnecessary AoO.
            ReachableTileInfo? bestApproach = null;
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

                int aooPenalty = tile.ProvokesAoO ? 1 : 0;
                if (minEnemyDist < bestDist ||
                    (minEnemyDist == bestDist && aooPenalty < bestPenalty) ||
                    (minEnemyDist == bestDist && aooPenalty == bestPenalty && tile.PathCost < (bestApproach?.PathCost ?? int.MaxValue)))
                {
                    bestDist = minEnemyDist;
                    bestPenalty = aooPenalty;
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
                    return session.TryMoveTo(bestApproach.Coord).Success;
                }
            }

            return false;
        }

        private static void TryFlee(
            TacticalCombatSession session,
            CombatantState actor,
            List<CombatantState> enemies)
        {
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTiles(actor.Id);
            if (reachable.Count == 0)
            {
                return;
            }

            ReachableTileInfo? best = null;
            int bestMinDist = -1;
            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minD = int.MaxValue;
                for (int e = 0; e < enemies.Count; e++)
                {
                    if (enemies[e].IsAlive)
                    {
                        minD = Math.Min(minD, tile.Coord.ChebyshevDistance(enemies[e].Position));
                    }
                }

                if (minD > bestMinDist)
                {
                    bestMinDist = minD;
                    best = tile;
                }
            }

            if (best != null)
            {
                session.TryMoveTo(best.Coord);
            }
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
