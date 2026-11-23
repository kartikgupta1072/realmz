using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public sealed class TacticalCombatSession
    {
        private readonly Dictionary<string, CombatantState> _combatantsById =
            new Dictionary<string, CombatantState>(StringComparer.Ordinal);
        private readonly List<CombatantState> _combatants = new List<CombatantState>();
        private readonly Dictionary<int, SpellDefinition> _spells;
        private readonly Dictionary<string, SpellDefinition> _spellsByKey =
            new Dictionary<string, SpellDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ItemDefinition> _items;
        private readonly List<string> _roundQueue = new List<string>();
        private readonly List<CombatLogEntry> _log = new List<CombatLogEntry>();

        public TacticalCombatSession(
            CombatGrid grid,
            IEnumerable<CombatantState> combatants,
            IReadOnlyDictionary<int, SpellDefinition>? spells = null,
            IReadOnlyDictionary<int, ItemDefinition>? items = null,
            IDiceRoller? roller = null,
            RulesetMode mode = RulesetMode.ModernBalanced,
            PartyState? party = null)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (combatants == null)
            {
                throw new ArgumentNullException(nameof(combatants));
            }

            foreach (CombatantState c in combatants)
            {
                if (c == null)
                {
                    continue;
                }

                _combatants.Add(c);
                _combatantsById[c.Id] = c;
            }

            _spells = spells != null
                ? new Dictionary<int, SpellDefinition>(spells)
                : new Dictionary<int, SpellDefinition>();
            foreach (SpellDefinition s in _spells.Values)
            {
                if (!string.IsNullOrWhiteSpace(s.Key))
                {
                    _spellsByKey[s.Key] = s;
                }
            }

            _items = items != null
                ? new Dictionary<int, ItemDefinition>(items)
                : new Dictionary<int, ItemDefinition>();
            Roller = roller ?? new DeterministicDiceRoller(0x434F4D424154UL);
            Mode = mode;
            Party = party;
            RoundNumber = 1;
            TurnNumber = 1;

            RebuildRoundQueue();
            StartCurrentActorTurn();
            CheckOutcome();
        }

        public CombatGrid Grid { get; }
        public IDiceRoller Roller { get; }
        public RulesetMode Mode { get; }
        public PartyState? Party { get; }
        public IReadOnlyList<CombatantState> Combatants => _combatants;
        public IReadOnlyDictionary<int, SpellDefinition> Spells => _spells;
        public IReadOnlyDictionary<int, ItemDefinition> Items => _items;
        public IReadOnlyList<CombatLogEntry> Log => _log;
        public int RoundNumber { get; private set; }
        public int TurnNumber { get; private set; }
        public CombatOutcome Outcome { get; private set; }
        public int TotalXpEarned { get; private set; }
        public int TotalGoldLoot { get; private set; }

        public CombatantState? CurrentActor =>
            _roundQueue.Count > 0 && _combatantsById.TryGetValue(_roundQueue[0], out CombatantState? actor)
                ? actor
                : null;

        public string CurrentActorId => CurrentActor?.Id ?? string.Empty;

        public bool CanUndoMove =>
            Outcome == CombatOutcome.InProgress &&
            CurrentActor != null &&
            CurrentActor.Economy.CanUndoMove &&
            CurrentActor.Economy.HasSwiftAction &&
            !CurrentActor.HasTriggeredIrreversibleEvent;

        public CombatantState? GetCombatant(string id)
        {
            return _combatantsById.TryGetValue(id, out CombatantState? unit) ? unit : null;
        }

        public IReadOnlyList<InitiativeEntry> GetInitiativeRibbon(int maxCount = 8)
        {
            if (maxCount <= 0)
            {
                return Array.Empty<InitiativeEntry>();
            }

            List<InitiativeEntry> ribbon = new List<InitiativeEntry>(maxCount);
            if (Outcome != CombatOutcome.InProgress)
            {
                return ribbon;
            }

            for (int i = 0; i < _roundQueue.Count && ribbon.Count < maxCount; i++)
            {
                if (_combatantsById.TryGetValue(_roundQueue[i], out CombatantState? c) && c.IsAlive)
                {
                    ribbon.Add(new InitiativeEntry(
                        c.Id,
                        c.Name,
                        c.Side,
                        RoundNumber,
                        c.EffectiveDexterity,
                        isCurrentTurn: ribbon.Count == 0));
                }
            }

            List<CombatantState> sortedLiving = GetSortedLivingCombatants();
            if (sortedLiving.Count == 0)
            {
                return ribbon;
            }

            int nextRound = RoundNumber + 1;
            while (ribbon.Count < maxCount)
            {
                for (int i = 0; i < sortedLiving.Count && ribbon.Count < maxCount; i++)
                {
                    CombatantState c = sortedLiving[i];
                    ribbon.Add(new InitiativeEntry(
                        c.Id,
                        c.Name,
                        c.Side,
                        nextRound,
                        c.EffectiveDexterity,
                        isCurrentTurn: false));
                }

                nextRound++;
            }

            return ribbon;
        }

        public IReadOnlyList<string> GetThreateningEnemyIds(GridCoord pos, CombatSide moverSide)
        {
            List<string> enemies = new List<string>();
            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState c = _combatants[i];
                if (c.Side == moverSide || !c.IsConscious)
                {
                    continue;
                }

                if (c.Conditions.Has(CharConditionId.RunsAway))
                {
                    continue;
                }

                if (c.Position != pos && c.Position.ChebyshevDistance(pos) <= 1)
                {
                    enemies.Add(c.Id);
                }
            }

            return enemies;
        }

        public IReadOnlyList<GridCoord> GetThreatenedTiles(string unitId)
        {
            if (!_combatantsById.TryGetValue(unitId, out CombatantState? unit) ||
                !unit.IsConscious ||
                unit.Conditions.Has(CharConditionId.RunsAway))
            {
                return Array.Empty<GridCoord>();
            }

            List<GridCoord> tiles = new List<GridCoord>(8);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int tx = unit.Position.X + dx;
                    int ty = unit.Position.Y + dy;
                    if (Grid.IsWalkable(tx, ty))
                    {
                        tiles.Add(new GridCoord(tx, ty));
                    }
                }
            }

            return tiles;
        }

        public bool WouldStepProvokeAoO(string moverId, GridCoord from, GridCoord to)
        {
            return WouldStepProvokeAoO(moverId, from, to, out _);
        }

        public bool WouldStepProvokeAoO(
            string moverId,
            GridCoord from,
            GridCoord to,
            out List<string> provokingEnemyIds)
        {
            provokingEnemyIds = new List<string>();
            if (!_combatantsById.TryGetValue(moverId, out CombatantState? mover))
            {
                return false;
            }

            if (mover.Conditions.Has(CharConditionId.Invisible))
            {
                return false;
            }

            IReadOnlyList<string> threatsAtFrom = GetThreateningEnemyIds(from, mover.Side);
            for (int i = 0; i < threatsAtFrom.Count; i++)
            {
                CombatantState? enemy = GetCombatant(threatsAtFrom[i]);
                if (enemy == null)
                {
                    continue;
                }

                // Leaving an engaged enemy's 3x3 threatened ring provokes AoO.
                if (enemy.Position.ChebyshevDistance(to) > 1)
                {
                    provokingEnemyIds.Add(enemy.Id);
                }
            }

            return provokingEnemyIds.Count > 0;
        }

        public IReadOnlyList<ReachableTileInfo> GetReachableTiles(string combatantId)
        {
            if (!_combatantsById.TryGetValue(combatantId, out CombatantState? unit) || !unit.IsConscious)
            {
                return Array.Empty<ReachableTileInfo>();
            }

            int budget = unit.Economy.MoveRemaining;
            if (budget <= 0)
            {
                return Array.Empty<ReachableTileInfo>();
            }

            Dictionary<GridCoord, ReachableNode> nodes = AStarPathfinder.FindReachableTiles(
                Grid.Width,
                Grid.Height,
                unit.Position,
                budget,
                isPassable: (x, y) => IsWalkableFor(unit, x, y),
                canStopOn: (x, y) => !IsOccupiedByLiving(x, y, excludeId: unit.Id),
                getMoveCost: (x, y) => Grid.GetMoveCost(x, y),
                allowDiagonal: true,
                useOctileCost: false);

            List<ReachableTileInfo> results = new List<ReachableTileInfo>(nodes.Count);
            foreach (ReachableNode node in nodes.Values)
            {
                bool provokes = false;
                HashSet<string> threats = new HashSet<string>(StringComparer.Ordinal);
                GridCoord prev = unit.Position;
                for (int i = 0; i < node.Path.Count; i++)
                {
                    GridCoord next = node.Path[i];
                    if (WouldStepProvokeAoO(unit.Id, prev, next, out List<string> stepThreats))
                    {
                        provokes = true;
                        for (int t = 0; t < stepThreats.Count; t++)
                        {
                            threats.Add(stepThreats[t]);
                        }
                    }

                    prev = next;
                }

                results.Add(new ReachableTileInfo(
                    node.Coord,
                    node.TotalCost,
                    node.Path,
                    provokes,
                    new List<string>(threats)));
            }

            results.Sort((a, b) =>
            {
                int cmp = a.PathCost.CompareTo(b.PathCost);
                if (cmp != 0) return cmp;
                cmp = a.Coord.Y.CompareTo(b.Coord.Y);
                return cmp != 0 ? cmp : a.Coord.X.CompareTo(b.Coord.X);
            });

            return results;
        }

        public LosResult CheckLineOfSight(GridCoord from, GridCoord to)
        {
            List<GridCoord> rayCells = TraceBresenham(from, to);
            bool partialCover = false;

            GridCoord prev = from;
            for (int i = 0; i < rayCells.Count; i++)
            {
                GridCoord cell = rayCells[i];
                bool isEndpoint = i == rayCells.Count - 1;

                // Check diagonal corner clipping against LOS-blocking walls.
                if (Math.Abs(cell.X - prev.X) == 1 && Math.Abs(cell.Y - prev.Y) == 1)
                {
                    bool blockA = Grid.BlocksLos(cell.X, prev.Y);
                    bool blockB = Grid.BlocksLos(prev.X, cell.Y);
                    if (blockA && blockB)
                    {
                        GridCoord blocker = new GridCoord(cell.X, prev.Y);
                        return new LosResult(LosKind.Blocked, 0, blocker, rayCells);
                    }

                    if (blockA || blockB)
                    {
                        partialCover = true;
                    }
                }

                if (!isEndpoint)
                {
                    if (Grid.BlocksLos(cell.X, cell.Y))
                    {
                        return new LosResult(LosKind.Blocked, 0, cell, rayCells);
                    }

                    if (Grid.ProvidesCover(cell.X, cell.Y) || IsOccupiedByLiving(cell.X, cell.Y))
                    {
                        partialCover = true;
                    }
                }

                prev = cell;
            }

            return partialCover
                ? new LosResult(LosKind.PartialCover, 15, null, rayCells)
                : new LosResult(LosKind.Clear, 0, null, rayCells);
        }

        public bool IsFlanking(string attackerId, string defenderId, GridCoord? fromPos = null)
        {
            CombatantState? attacker = GetCombatant(attackerId);
            CombatantState? defender = GetCombatant(defenderId);
            if (attacker == null || defender == null || !defender.IsAlive || attacker.Side == defender.Side)
            {
                return false;
            }

            GridCoord attPos = fromPos ?? attacker.Position;
            if (attPos.ChebyshevDistance(defender.Position) != 1)
            {
                return false;
            }

            int attDx = attPos.X - defender.Position.X;
            int attDy = attPos.Y - defender.Position.Y;

            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState ally = _combatants[i];
                if (ally.Id == attacker.Id || ally.Side != attacker.Side || !ally.IsConscious)
                {
                    continue;
                }

                if (ally.Position.ChebyshevDistance(defender.Position) > 1)
                {
                    continue;
                }

                int allyDx = ally.Position.X - defender.Position.X;
                int allyDy = ally.Position.Y - defender.Position.Y;
                int dot = (allyDx * attDx) + (allyDy * attDy);
                if (dot <= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public SpellAoeFootprint GetSpellAoeFootprint(string spellIdOrKey, GridCoord targetCenter, string casterId)
        {
            CombatantState? caster = GetCombatant(casterId);
            SpellDefinition? spell = ResolveSpell(spellIdOrKey);
            if (caster == null || spell == null)
            {
                return new SpellAoeFootprint(
                    false,
                    targetCenter,
                    Array.Empty<GridCoord>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<GridCoord>());
            }

            bool inRange = Grid.InBounds(targetCenter) &&
                           caster.Position.ChebyshevDistance(targetCenter) <= Math.Max(0, spell.Range);

            List<GridCoord> affectedTiles = new List<GridCoord>();
            List<GridCoord> blockedTiles = new List<GridCoord>();
            HashSet<string> allyIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> enemyIds = new HashSet<string>(StringComparer.Ordinal);

            string mode = spell.TargetMode.Trim();
            if (string.Equals(mode, "AllEnemies", StringComparison.OrdinalIgnoreCase))
            {
                inRange = true;
                for (int i = 0; i < _combatants.Count; i++)
                {
                    CombatantState c = _combatants[i];
                    if (c.IsAlive && c.Side != caster.Side)
                    {
                        affectedTiles.Add(c.Position);
                        enemyIds.Add(c.Id);
                    }
                }
            }
            else if (string.Equals(mode, "AllAllies", StringComparison.OrdinalIgnoreCase))
            {
                inRange = true;
                for (int i = 0; i < _combatants.Count; i++)
                {
                    CombatantState c = _combatants[i];
                    if (c.IsAlive && c.Side == caster.Side)
                    {
                        affectedTiles.Add(c.Position);
                        allyIds.Add(c.Id);
                    }
                }
            }
            else if (string.Equals(mode, "Self", StringComparison.OrdinalIgnoreCase))
            {
                inRange = true;
                affectedTiles.Add(caster.Position);
                allyIds.Add(caster.Id);
            }
            else
            {
                IReadOnlyList<GridCoord> offsets = spell.AoeOffsets.Count > 0
                    ? spell.AoeOffsets
                    : new[] { new GridCoord(0, 0) };

                HashSet<GridCoord> seen = new HashSet<GridCoord>();
                for (int i = 0; i < offsets.Count; i++)
                {
                    GridCoord tile = new GridCoord(targetCenter.X + offsets[i].X, targetCenter.Y + offsets[i].Y);
                    if (!Grid.InBounds(tile) || !seen.Add(tile))
                    {
                        continue;
                    }

                    if (Grid.BlocksLos(tile.X, tile.Y))
                    {
                        blockedTiles.Add(tile);
                        continue;
                    }

                    affectedTiles.Add(tile);
                    for (int u = 0; u < _combatants.Count; u++)
                    {
                        CombatantState c = _combatants[u];
                        if (!c.IsAlive || c.Position != tile)
                        {
                            continue;
                        }

                        if (c.Side == caster.Side)
                        {
                            allyIds.Add(c.Id);
                        }
                        else
                        {
                            enemyIds.Add(c.Id);
                        }
                    }
                }
            }

            return new SpellAoeFootprint(
                inRange,
                targetCenter,
                affectedTiles,
                new List<string>(allyIds),
                new List<string>(enemyIds),
                blockedTiles);
        }

        public TacticalAttackPreview PreviewMeleeAttack(string attackerId, string defenderId, GridCoord? fromPos = null)
        {
            CombatantState? attacker = GetCombatant(attackerId);
            CombatantState? defender = GetCombatant(defenderId);
            if (attacker == null || defender == null || !attacker.IsConscious || !defender.IsAlive)
            {
                return new TacticalAttackPreview(
                    false,
                    "Attacker or defender unavailable.",
                    0, 0, 0, 0, 0, 0, false, false, null, 0, Array.Empty<string>());
            }

            GridCoord pos = fromPos ?? attacker.Position;
            bool adjacent = pos.ChebyshevDistance(defender.Position) == 1;
            bool flanking = adjacent && IsFlanking(attackerId, defenderId, pos);

            HitPreview hit = CombatRules.CalculateHitPreview(
                attackerToHit: attacker.EffectiveToHit,
                defenderAc: defender.EffectiveAc,
                isFlanking: flanking,
                equippedWeaponBonus: attacker.WeaponBonus,
                defenderIsEvil: defender.IsEvil,
                attackerConditions: attacker.Conditions,
                defenderConditions: defender.Conditions,
                mode: Mode);

            ComputeDamageRange(attacker.WeaponDice, attacker.DamageBonus, attacker.Conditions, out int minDmg, out int maxDmg);
            List<string> tags = CollectConditionTags(attacker, defender, flanking, los: null);

            bool provokesAoO = false;
            if (fromPos.HasValue && fromPos.Value != attacker.Position)
            {
                provokesAoO = WouldStepProvokeAoO(attackerId, attacker.Position, fromPos.Value);
            }

            bool canExecute = adjacent && attacker.Side != defender.Side && attacker.Economy.HasStandardAction;
            string reason = !adjacent
                ? "Target is out of melee reach."
                : !attacker.Economy.HasStandardAction
                    ? "No standard action remaining."
                    : string.Empty;

            return new TacticalAttackPreview(
                canExecute: canExecute,
                reason: reason,
                hitChancePct: hit.HitChancePercent,
                uncappedHitScore: hit.UncappedHitScore,
                critChancePct: hit.CritThresholdPercent,
                grazeChancePct: hit.GrazeThresholdPercent,
                minDamage: minDmg,
                maxDamage: maxDmg,
                isFlanking: flanking,
                provokesAoO: provokesAoO,
                losResult: null,
                defenderAc: defender.EffectiveAc,
                conditionTags: tags);
        }

        public TacticalAttackPreview PreviewRangedAttack(string attackerId, string defenderId, GridCoord? fromPos = null)
        {
            CombatantState? attacker = GetCombatant(attackerId);
            CombatantState? defender = GetCombatant(defenderId);
            if (attacker == null || defender == null || !attacker.IsConscious || !defender.IsAlive)
            {
                return new TacticalAttackPreview(
                    false,
                    "Attacker or defender unavailable.",
                    0, 0, 0, 0, 0, 0, false, false, null, 0, Array.Empty<string>());
            }

            GridCoord pos = fromPos ?? attacker.Position;
            int dist = pos.ChebyshevDistance(defender.Position);
            bool hasRanged = attacker.RangedRange > 0 && !string.IsNullOrWhiteSpace(attacker.RangedDice);
            LosResult los = CheckLineOfSight(pos, defender.Position);

            int missileToHit = attacker.EffectiveMissileSkill - los.CoverPenaltyPct;
            if (defender.Conditions.Has(CharConditionId.ShieldFromProjectiles))
            {
                missileToHit -= 15;
            }

            HitPreview hit = CombatRules.CalculateHitPreview(
                attackerToHit: missileToHit,
                defenderAc: defender.EffectiveAc,
                isFlanking: false,
                equippedWeaponBonus: 0,
                defenderIsEvil: defender.IsEvil,
                attackerConditions: attacker.Conditions,
                defenderConditions: defender.Conditions,
                mode: Mode);

            string diceStr = hasRanged ? attacker.RangedDice : "1d4";
            ComputeDamageRange(diceStr, 0, null, out int minDmg, out int maxDmg);
            List<string> tags = CollectConditionTags(attacker, defender, isFlanking: false, los);

            bool inRange = hasRanged && dist > 0 && dist <= attacker.RangedRange;
            bool losClear = los.Kind != LosKind.Blocked;
            bool canExecute = inRange && losClear && attacker.Side != defender.Side && attacker.Economy.HasStandardAction;
            string reason = !hasRanged
                ? "No ranged weapon equipped."
                : !inRange
                    ? "Target out of ranged weapon range."
                    : !losClear
                        ? "Line of sight is blocked."
                        : !attacker.Economy.HasStandardAction
                            ? "No standard action remaining."
                            : string.Empty;

            return new TacticalAttackPreview(
                canExecute: canExecute,
                reason: reason,
                hitChancePct: hit.HitChancePercent,
                uncappedHitScore: hit.UncappedHitScore,
                critChancePct: hit.CritThresholdPercent,
                grazeChancePct: hit.GrazeThresholdPercent,
                minDamage: minDmg,
                maxDamage: maxDmg,
                isFlanking: false,
                provokesAoO: false,
                losResult: los,
                defenderAc: defender.EffectiveAc,
                conditionTags: tags);
        }

        public TacticalSpellPreview PreviewSpell(string casterId, string spellIdOrKey, GridCoord targetCenter)
        {
            CombatantState? caster = GetCombatant(casterId);
            SpellDefinition? spell = ResolveSpell(spellIdOrKey);
            if (caster == null || spell == null)
            {
                return new TacticalSpellPreview(
                    false,
                    "Caster or spell not found.",
                    0, 0, 0, 0,
                    new SpellAoeFootprint(false, targetCenter, Array.Empty<GridCoord>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<GridCoord>()),
                    Array.Empty<string>());
            }

            SpellAoeFootprint footprint = GetSpellAoeFootprint(spellIdOrKey, targetCenter, casterId);
            int minAmount = 0;
            int maxAmount = 0;
            if (!string.IsNullOrWhiteSpace(spell.EffectDice) && DiceSpec.TryParse(spell.EffectDice, out DiceSpec spec))
            {
                minAmount = Math.Max(0, spec.Min);
                maxAmount = Math.Max(minAmount, spec.Max);
            }

            int saveChancePct = 0;
            bool isHostile = !string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase);
            if (isHostile && footprint.AffectedEnemyIds.Count > 0)
            {
                int totalSave = 0;
                for (int i = 0; i < footprint.AffectedEnemyIds.Count; i++)
                {
                    CombatantState? enemy = GetCombatant(footprint.AffectedEnemyIds[i]);
                    if (enemy != null)
                    {
                        totalSave += ComputeSpellSaveChance(enemy, spell);
                    }
                }

                saveChancePct = totalSave / footprint.AffectedEnemyIds.Count;
            }

            List<string> tags = new List<string>();
            if (spell.AppliedCondition.HasValue)
            {
                tags.Add(ConditionEffects.GetCode(spell.AppliedCondition.Value));
            }

            bool silenced = caster.Conditions.Has(CharConditionId.Silenced);
            bool enoughSp = caster.Sp >= spell.SpCost;
            bool hasAction = caster.Economy.HasStandardAction;
            bool canExecute = caster.IsConscious && !silenced && enoughSp && hasAction && footprint.InRange;

            string reason = silenced
                ? "Caster is silenced."
                : !enoughSp
                    ? "Insufficient SP."
                    : !footprint.InRange
                        ? "Target is out of spell range."
                        : !hasAction
                            ? "No standard action remaining."
                            : string.Empty;

            return new TacticalSpellPreview(
                canExecute: canExecute,
                reason: reason,
                spCost: spell.SpCost,
                minAmount: minAmount,
                maxAmount: maxAmount,
                saveChancePct: saveChancePct,
                aoeFootprint: footprint,
                conditionTags: tags);
        }

        public CombatActionResult TryMoveTo(GridCoord destination)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            if (actor == null || !actor.IsConscious)
            {
                return new CombatActionResult(false, "No conscious actor.");
            }

            if (destination == actor.Position)
            {
                return new CombatActionResult(true, $"{actor.Name} remains at {destination}.");
            }

            if (IsOccupiedByLiving(destination.X, destination.Y, excludeId: actor.Id))
            {
                return new CombatActionResult(false, "Destination tile is occupied.");
            }

            PathResult path = AStarPathfinder.FindPath(
                Grid.Width,
                Grid.Height,
                actor.Position,
                destination,
                isPassable: (x, y) => IsWalkableFor(actor, x, y),
                getMoveCost: (x, y) => Grid.GetMoveCost(x, y),
                allowDiagonal: true,
                maxCost: actor.Economy.MoveRemaining,
                useOctileCost: false);

            if (!path.Found || path.Steps.Count == 0)
            {
                return new CombatActionResult(false, "Destination is unreachable within remaining movement.");
            }

            bool aooTriggered = false;
            int aooDamage = 0;

            for (int i = 0; i < path.Steps.Count; i++)
            {
                GridCoord next = path.Steps[i];
                if (WouldStepProvokeAoO(actor.Id, actor.Position, next, out List<string> provokers))
                {
                    actor.HasTriggeredIrreversibleEvent = true;
                    aooTriggered = true;
                    for (int p = 0; p < provokers.Count; p++)
                    {
                        CombatantState? enemy = GetCombatant(provokers[p]);
                        if (enemy == null || !enemy.IsConscious || !actor.IsAlive)
                        {
                            continue;
                        }

                        AttackOutcome aoo = ResolveSingleMeleeSwing(enemy, actor, isFlanking: false);
                        aooDamage += aoo.DamageDealt;
                        AppendLog(
                            enemy.Id,
                            enemy.Name,
                            "AoO",
                            $"{enemy.Name} makes an Opportunity Attack on {actor.Name}: {aoo.Kind} ({aoo.DamageDealt} dmg).",
                            new[] { actor.Id },
                            damageDealt: aoo.DamageDealt);

                        if (!actor.IsConscious)
                        {
                            CheckOutcome();
                            return new CombatActionResult(
                                true,
                                $"{actor.Name} was stopped by an Opportunity Attack.",
                                new[] { aoo },
                                damageDealt: aooDamage,
                                aooTriggered: true);
                        }
                    }
                }

                int stepCost = Grid.GetMoveCost(next.X, next.Y);
                if (!actor.Economy.TryStep(stepCost))
                {
                    break;
                }

                actor.Position = next;
            }

            AppendLog(
                actor.Id,
                actor.Name,
                "Move",
                $"{actor.Name} moves to {actor.Position}.",
                Array.Empty<string>());

            return new CombatActionResult(
                true,
                $"{actor.Name} moved to {actor.Position}.",
                damageDealt: aooDamage,
                aooTriggered: aooTriggered);
        }

        public bool UndoMove()
        {
            if (!CanUndoMove || CurrentActor == null)
            {
                return false;
            }

            CombatantState actor = CurrentActor;
            actor.Position = actor.TurnStartPosition;
            while (actor.Economy.CanUndoMove)
            {
                actor.Economy.UndoMove();
            }

            AppendLog(actor.Id, actor.Name, "UndoMove", $"{actor.Name} undoes movement back to {actor.Position}.");
            return true;
        }

        public CombatActionResult ExecuteMeleeAttack(string targetId)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            CombatantState? target = GetCombatant(targetId);
            if (actor == null || target == null)
            {
                return new CombatActionResult(false, "Invalid attacker or target.");
            }

            TacticalAttackPreview preview = PreviewMeleeAttack(actor.Id, targetId);
            if (!preview.CanExecute)
            {
                return new CombatActionResult(false, preview.Reason);
            }

            actor.Economy.CommitStandardAction();
            actor.HasTriggeredIrreversibleEvent = true;

            List<AttackOutcome> outcomes = new List<AttackOutcome>();
            int totalDamage = 0;
            int swings = Math.Max(1, actor.NumAttacks);

            for (int i = 0; i < swings && target.IsAlive; i++)
            {
                AttackOutcome outcome = ResolveSingleMeleeSwing(actor, target, preview.IsFlanking);
                outcomes.Add(outcome);
                totalDamage += outcome.DamageDealt;
            }

            string msg = $"{actor.Name} attacks {target.Name} ({outcomes.Count} swing(s)) for {totalDamage} total damage.";
            AppendLog(actor.Id, actor.Name, "MeleeAttack", msg, new[] { target.Id }, damageDealt: totalDamage);
            CheckOutcome();

            return new CombatActionResult(true, msg, outcomes, damageDealt: totalDamage);
        }

        public CombatActionResult ExecuteRangedAttack(string targetId)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            CombatantState? target = GetCombatant(targetId);
            if (actor == null || target == null)
            {
                return new CombatActionResult(false, "Invalid attacker or target.");
            }

            TacticalAttackPreview preview = PreviewRangedAttack(actor.Id, targetId);
            if (!preview.CanExecute)
            {
                return new CombatActionResult(false, preview.Reason);
            }

            actor.Economy.CommitStandardAction();
            actor.HasTriggeredIrreversibleEvent = true;

            int coverPenalty = preview.LosResult != null ? preview.LosResult.CoverPenaltyPct : 0;
            int missileToHit = actor.EffectiveMissileSkill - coverPenalty;
            if (target.Conditions.Has(CharConditionId.ShieldFromProjectiles))
            {
                missileToHit -= 15;
            }

            DiceSpec dice = DiceSpec.TryParse(actor.RangedDice, out DiceSpec parsed)
                ? parsed
                : DiceSpec.Parse("1d4");

            AttackOutcome outcome = CombatRules.ResolveMeleeAttack(
                Roller,
                dice,
                attackerToHit: missileToHit,
                attackerDamageBonus: 0,
                defenderAc: target.EffectiveAc,
                isFlanking: false,
                equippedWeaponBonus: 0,
                defenderIsEvil: target.IsEvil,
                attackerConditions: actor.Conditions,
                defenderConditions: target.Conditions,
                mode: Mode);

            if (outcome.DamageDealt > 0)
            {
                target.Hp = Math.Max(0, target.Hp - outcome.DamageDealt);
            }

            string msg = $"{actor.Name} fires at {target.Name}: {outcome.Kind} ({outcome.DamageDealt} dmg).";
            AppendLog(actor.Id, actor.Name, "RangedAttack", msg, new[] { target.Id }, damageDealt: outcome.DamageDealt);
            CheckOutcome();

            return new CombatActionResult(true, msg, new[] { outcome }, damageDealt: outcome.DamageDealt);
        }

        public CombatActionResult CastSpell(string spellIdOrKey, GridCoord targetCenter)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? caster = CurrentActor;
            SpellDefinition? spell = ResolveSpell(spellIdOrKey);
            if (caster == null || spell == null)
            {
                return new CombatActionResult(false, "Caster or spell not found.");
            }

            TacticalSpellPreview preview = PreviewSpell(caster.Id, spellIdOrKey, targetCenter);
            if (!preview.CanExecute)
            {
                return new CombatActionResult(false, preview.Reason);
            }

            caster.Economy.CommitStandardAction();
            caster.HasTriggeredIrreversibleEvent = true;
            caster.Sp = Math.Max(0, caster.Sp - spell.SpCost);

            bool isHeal = string.Equals(spell.EffectKind, "Heal", StringComparison.OrdinalIgnoreCase);
            bool isBuff = string.Equals(spell.EffectKind, "Buff", StringComparison.OrdinalIgnoreCase);
            bool isCure = string.Equals(spell.EffectKind, "Cure", StringComparison.OrdinalIgnoreCase);

            List<string> targetIds = new List<string>();
            if (isHeal || isBuff || isCure)
            {
                targetIds.AddRange(preview.AoeFootprint.AffectedAllyIds);
                if (targetIds.Count == 0 && string.Equals(spell.TargetMode, "Single", StringComparison.OrdinalIgnoreCase))
                {
                    targetIds.AddRange(preview.AoeFootprint.AffectedEnemyIds);
                }
            }
            else
            {
                targetIds.AddRange(preview.AoeFootprint.AffectedEnemyIds);
                if (!string.Equals(spell.TargetMode, "AllEnemies", StringComparison.OrdinalIgnoreCase))
                {
                    // Friendly-fire AoE spells also hit allies caught in the blast!
                    targetIds.AddRange(preview.AoeFootprint.AffectedAllyIds);
                }
            }

            int totalDamage = 0;
            int totalHealing = 0;
            DiceSpec? effectSpec = null;
            if (!string.IsNullOrWhiteSpace(spell.EffectDice) && DiceSpec.TryParse(spell.EffectDice, out DiceSpec parsed))
            {
                effectSpec = parsed;
            }

            for (int i = 0; i < targetIds.Count; i++)
            {
                CombatantState? target = GetCombatant(targetIds[i]);
                if (target == null || !target.IsAlive)
                {
                    continue;
                }

                if (isHeal)
                {
                    int heal = effectSpec.HasValue ? Math.Max(1, effectSpec.Value.Roll(Roller)) : 5;
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + heal);
                    totalHealing += target.Hp - before;
                }
                else if (isCure)
                {
                    target.ExhaustionStacks = 0;
                    target.CurseStacks = 0;
                    target.Conditions.Remove(CharConditionId.Poisoned);
                    target.Conditions.Remove(CharConditionId.Diseased);
                    target.Conditions.Remove(CharConditionId.Cursed);
                    target.Conditions.Remove(CharConditionId.Helpless);
                    target.Conditions.Remove(CharConditionId.Confused);
                }
                else if (isBuff)
                {
                    if (spell.AppliedCondition.HasValue)
                    {
                        int duration = spell.ConditionDuration > 0 ? spell.ConditionDuration : 4;
                        target.Conditions.Apply(
                            spell.AppliedCondition.Value,
                            magnitude: 1,
                            turnsRemaining: duration,
                            baseRecoveryChancePercent: 0,
                            canRecoverBySave: false,
                            escalationStepPercent: 10);
                    }
                }
                else
                {
                    int saveChance = ComputeSpellSaveChance(target, spell);
                    bool saved = saveChance > 0 && Roller.NextInt(1, 101) <= saveChance;

                    if (effectSpec.HasValue)
                    {
                        int dmg = Math.Max(1, effectSpec.Value.Roll(Roller));
                        if (saved)
                        {
                            dmg /= 2;
                        }

                        target.Hp = Math.Max(0, target.Hp - dmg);
                        totalDamage += dmg;
                    }

                    if (!saved && spell.AppliedCondition.HasValue && target.IsAlive)
                    {
                        CharConditionId cond = spell.AppliedCondition.Value;
                        int step = ConditionEffects.IsCrowdControl(cond) ? 15 : 10;
                        int duration = spell.ConditionDuration > 0 ? spell.ConditionDuration : -1;
                        target.Conditions.Apply(
                            cond,
                            magnitude: 1,
                            turnsRemaining: duration,
                            baseRecoveryChancePercent: Math.Clamp(spell.BaseRecoveryPct, 0, 100),
                            canRecoverBySave: true,
                            escalationStepPercent: step);
                    }
                }
            }

            string msg = $"{caster.Name} casts {spell.Name} ({totalDamage} dmg, {totalHealing} heal).";
            AppendLog(caster.Id, caster.Name, "CastSpell", msg, targetIds, damageDealt: totalDamage, healingDone: totalHealing);
            CheckOutcome();

            return new CombatActionResult(true, msg, damageDealt: totalDamage, healingDone: totalHealing);
        }

        public CombatActionResult GuardOverwatch()
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            if (actor == null || !actor.IsConscious)
            {
                return new CombatActionResult(false, "No conscious actor.");
            }

            if (!actor.Economy.CommitStandardAction())
            {
                return new CombatActionResult(false, "No standard action remaining.");
            }

            actor.IsGuarding = true;
            actor.HasTriggeredIrreversibleEvent = true;
            string msg = $"{actor.Name} takes a defensive guard stance (+4 AC).";
            AppendLog(actor.Id, actor.Name, "Guard", msg);

            return new CombatActionResult(true, msg);
        }

        public CombatActionResult UseSwiftPotion(int itemId, string? targetId = null)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            if (actor == null || !actor.IsConscious)
            {
                return new CombatActionResult(false, "No conscious actor.");
            }

            if (!actor.Economy.HasSwiftAction)
            {
                return new CombatActionResult(false, "No swift action remaining.");
            }

            if (!_items.TryGetValue(itemId, out ItemDefinition? item))
            {
                return new CombatActionResult(false, $"Item {itemId} not found.");
            }

            if (Party != null)
            {
                int stashIdx = -1;
                for (int i = 0; i < Party.Stash.Count; i++)
                {
                    if (Party.Stash[i].ItemId == itemId && Party.Stash[i].Quantity > 0)
                    {
                        stashIdx = i;
                        break;
                    }
                }

                if (stashIdx < 0)
                {
                    return new CombatActionResult(false, $"{item.Name} is not available in party stash.");
                }

                Party.RemoveItemFromStash(stashIdx, 1);
            }

            actor.Economy.CommitSwiftAction();
            actor.HasTriggeredIrreversibleEvent = true;

            CombatantState target = !string.IsNullOrWhiteSpace(targetId)
                ? (GetCombatant(targetId!) ?? actor)
                : actor;

            int healed = 0;
            if (!string.IsNullOrWhiteSpace(item.HealDice) && DiceSpec.TryParse(item.HealDice, out DiceSpec spec))
            {
                int roll = Math.Max(1, spec.Roll(Roller));
                int before = target.Hp;
                target.Hp = Math.Min(target.MaxHp, target.Hp + roll);
                healed = target.Hp - before;
            }

            string msg = $"{actor.Name} uses {item.Name} on {target.Name} (+{healed} HP).";
            AppendLog(actor.Id, actor.Name, "SwiftPotion", msg, new[] { target.Id }, healingDone: healed);

            return new CombatActionResult(true, msg, healingDone: healed);
        }

        public void ApplyDrainOrAgeCurse(string targetId, int stacks = 1)
        {
            CombatantState? target = GetCombatant(targetId);
            if (target == null || stacks <= 0)
            {
                return;
            }

            if (Mode == RulesetMode.ModernBalanced)
            {
                target.CurseStacks += stacks;
                target.Conditions.Apply(
                    CharConditionId.Cursed,
                    magnitude: target.CurseStacks,
                    turnsRemaining: -1,
                    baseRecoveryChancePercent: 0,
                    canRecoverBySave: false,
                    escalationStepPercent: 10);

                AppendLog(
                    target.Id,
                    target.Name,
                    "Curse",
                    $"{target.Name} suffers {stacks} curable Curse stack(s) (-2 ToHit, -1 AC each).",
                    new[] { target.Id });
            }
            else
            {
                target.Ac -= stacks;
                target.ToHit -= 2 * stacks;
                target.MaxHp = Math.Max(1, target.MaxHp - (2 * stacks));
                target.Hp = Math.Min(target.Hp, target.MaxHp);

                AppendLog(
                    target.Id,
                    target.Name,
                    "Drain",
                    $"{target.Name} suffers permanent classic drain (-{2 * stacks} MaxHP/ToHit, -{stacks} AC).",
                    new[] { target.Id });
            }
        }

        public void EndTurn()
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            CombatantState? actor = CurrentActor;
            if (actor != null && actor.IsAlive)
            {
                if (actor.Conditions.Has(CharConditionId.Poisoned))
                {
                    int poisonDmg = Math.Max(1, actor.Conditions.GetMagnitude(CharConditionId.Poisoned));
                    actor.Hp = Math.Max(0, actor.Hp - poisonDmg);
                    AppendLog(actor.Id, actor.Name, "Poison", $"{actor.Name} takes {poisonDmg} poison damage.", new[] { actor.Id }, damageDealt: poisonDmg);
                }

                if (actor.IsAlive && actor.Conditions.Has(CharConditionId.Regenerating))
                {
                    int regen = Math.Max(1, actor.Conditions.GetMagnitude(CharConditionId.Regenerating));
                    int before = actor.Hp;
                    actor.Hp = Math.Min(actor.MaxHp, actor.Hp + regen);
                    int gained = actor.Hp - before;
                    if (gained > 0)
                    {
                        AppendLog(actor.Id, actor.Name, "Regen", $"{actor.Name} regenerates {gained} HP.", new[] { actor.Id }, healingDone: gained);
                    }
                }

                IReadOnlyList<CharConditionId> cleared = actor.Conditions.TickEndOfTurn(Roller, Mode);
                if (cleared.Count > 0)
                {
                    AppendLog(actor.Id, actor.Name, "ConditionEnd", $"{actor.Name} recovers from {string.Join(", ", cleared)}.");
                }
            }

            CheckOutcome();
            if (Outcome != CombatOutcome.InProgress)
            {
                return;
            }

            if (_roundQueue.Count > 0)
            {
                _roundQueue.RemoveAt(0);
            }

            while (_roundQueue.Count > 0)
            {
                CombatantState? next = GetCombatant(_roundQueue[0]);
                if (next != null && next.IsAlive)
                {
                    break;
                }

                _roundQueue.RemoveAt(0);
            }

            if (_roundQueue.Count == 0)
            {
                RoundNumber++;
                RebuildRoundQueue();
            }

            TurnNumber++;
            StartCurrentActorTurn();
        }

        public CombatActionResult ExecuteMonsterAiTurn()
        {
            return MonsterAi.ExecuteTurn(this);
        }

        internal void AppendLog(
            string actorId,
            string actorName,
            string actionType,
            string message,
            IReadOnlyList<string>? targetIds = null,
            int damageDealt = 0,
            int healingDone = 0)
        {
            _log.Add(new CombatLogEntry(
                TurnNumber,
                RoundNumber,
                actorId,
                actorName,
                actionType,
                message,
                targetIds,
                damageDealt,
                healingDone));
        }

        internal SpellDefinition? ResolveSpell(string spellIdOrKey)
        {
            if (string.IsNullOrWhiteSpace(spellIdOrKey))
            {
                return null;
            }

            if (int.TryParse(spellIdOrKey, out int numericId) && _spells.TryGetValue(numericId, out SpellDefinition? byId))
            {
                return byId;
            }

            if (_spellsByKey.TryGetValue(spellIdOrKey, out SpellDefinition? byKey))
            {
                return byKey;
            }

            foreach (SpellDefinition s in _spells.Values)
            {
                if (string.Equals(s.Name, spellIdOrKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Alias, spellIdOrKey, StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }

            return null;
        }

        private AttackOutcome ResolveSingleMeleeSwing(CombatantState attacker, CombatantState defender, bool isFlanking)
        {
            DiceSpec weapon = DiceSpec.TryParse(attacker.WeaponDice, out DiceSpec parsed)
                ? parsed
                : DiceSpec.Parse("1d4");

            AttackOutcome outcome = CombatRules.ResolveMeleeAttack(
                Roller,
                weapon,
                attackerToHit: attacker.EffectiveToHit,
                attackerDamageBonus: attacker.DamageBonus,
                defenderAc: defender.EffectiveAc,
                isFlanking: isFlanking,
                equippedWeaponBonus: attacker.WeaponBonus,
                defenderIsEvil: defender.IsEvil,
                attackerConditions: attacker.Conditions,
                defenderConditions: defender.Conditions,
                mode: Mode);

            if (outcome.DamageDealt > 0)
            {
                defender.Hp = Math.Max(0, defender.Hp - outcome.DamageDealt);
            }

            return outcome;
        }

        private int ComputeSpellSaveChance(CombatantState target, SpellDefinition spell)
        {
            int saveBonus = 0;
            if (target.Saves.Count > 0)
            {
                int idx = string.Equals(spell.SaveKind, "Mental", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
                saveBonus = idx < target.Saves.Count ? target.Saves[idx] : target.Saves[0];
            }

            int total = Math.Max(0, target.MagicResistance) + Math.Max(0, saveBonus);
            return Math.Clamp(total, 0, 95);
        }

        private void StartCurrentActorTurn()
        {
            CombatantState? actor = CurrentActor;
            if (actor == null)
            {
                return;
            }

            actor.Economy.ResetTurn(actor.EffectiveMovement);
            actor.TurnStartPosition = actor.Position;
            actor.HasTriggeredIrreversibleEvent = false;
            actor.IsGuarding = false;
        }

        private void RebuildRoundQueue()
        {
            _roundQueue.Clear();
            List<CombatantState> sorted = GetSortedLivingCombatants();
            for (int i = 0; i < sorted.Count; i++)
            {
                _roundQueue.Add(sorted[i].Id);
            }
        }

        private List<CombatantState> GetSortedLivingCombatants()
        {
            List<CombatantState> living = new List<CombatantState>();
            for (int i = 0; i < _combatants.Count; i++)
            {
                if (_combatants[i].IsAlive)
                {
                    living.Add(_combatants[i]);
                }
            }

            living.Sort((a, b) =>
            {
                int cmp = b.EffectiveDexterity.CompareTo(a.EffectiveDexterity);
                if (cmp != 0) return cmp;
                cmp = a.Side.CompareTo(b.Side);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            return living;
        }

        private void CheckOutcome()
        {
            bool anyHeroAlive = false;
            bool anyMonsterAlive = false;
            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState c = _combatants[i];
                if (!c.IsAlive)
                {
                    continue;
                }

                if (c.Side == CombatSide.Hero) anyHeroAlive = true;
                else anyMonsterAlive = true;
            }

            if (!anyHeroAlive)
            {
                Outcome = CombatOutcome.Defeat;
            }
            else if (!anyMonsterAlive)
            {
                Outcome = CombatOutcome.Victory;
                int xp = 0;
                int gold = 0;
                for (int i = 0; i < _combatants.Count; i++)
                {
                    CombatantState c = _combatants[i];
                    if (c.Side == CombatSide.Monster)
                    {
                        xp += c.XpReward;
                        gold += c.GoldReward;
                    }
                }

                TotalXpEarned = xp;
                TotalGoldLoot = gold;
                if (Party != null && gold > 0)
                {
                    Party.Gold += gold;
                }
            }
            else
            {
                Outcome = CombatOutcome.InProgress;
            }
        }

        private bool IsWalkableFor(CombatantState mover, int x, int y)
        {
            if (!Grid.IsWalkable(x, y))
            {
                return false;
            }

            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState c = _combatants[i];
                if (!c.IsAlive || c.Id == mover.Id)
                {
                    continue;
                }

                if (c.Position.X == x && c.Position.Y == y && c.Side != mover.Side)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsOccupiedByLiving(int x, int y, string? excludeId = null)
        {
            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState c = _combatants[i];
                if (!c.IsAlive || c.Id == excludeId)
                {
                    continue;
                }

                if (c.Position.X == x && c.Position.Y == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ComputeDamageRange(
            string diceText,
            int bonus,
            ConditionTracker? conditions,
            out int minDmg,
            out int maxDmg)
        {
            int strongBonus = conditions != null && conditions.Has(CharConditionId.Strong) ? 3 : 0;
            if (DiceSpec.TryParse(diceText, out DiceSpec spec))
            {
                minDmg = Math.Max(1, spec.Min + bonus + strongBonus);
                maxDmg = Math.Max(minDmg, spec.Max + bonus + strongBonus);
            }
            else
            {
                minDmg = Math.Max(1, 1 + bonus + strongBonus);
                maxDmg = Math.Max(minDmg, 4 + bonus + strongBonus);
            }
        }

        private static List<string> CollectConditionTags(
            CombatantState attacker,
            CombatantState defender,
            bool isFlanking,
            LosResult? los)
        {
            List<string> tags = new List<string>();
            if (isFlanking)
            {
                tags.Add("FLANKING");
            }

            if (defender.IsGuarding)
            {
                tags.Add("GUARDING");
            }

            if (los != null && los.Kind == LosKind.PartialCover)
            {
                tags.Add("PARTIAL_COVER");
            }

            IReadOnlyList<ConditionDisplayEffect> attEffects = attacker.Conditions.GetDisplayEffects();
            for (int i = 0; i < attEffects.Count; i++)
            {
                if (!tags.Contains(attEffects[i].Code))
                {
                    tags.Add(attEffects[i].Code);
                }
            }

            IReadOnlyList<ConditionDisplayEffect> defEffects = defender.Conditions.GetDisplayEffects();
            for (int i = 0; i < defEffects.Count; i++)
            {
                if (!tags.Contains(defEffects[i].Code))
                {
                    tags.Add(defEffects[i].Code);
                }
            }

            return tags;
        }

        private static List<GridCoord> TraceBresenham(GridCoord from, GridCoord to)
        {
            List<GridCoord> cells = new List<GridCoord>();
            int x0 = from.X;
            int y0 = from.Y;
            int x1 = to.X;
            int y1 = to.Y;

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (x0 != x1 || y0 != y1)
            {
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }

                cells.Add(new GridCoord(x0, y0));
            }

            return cells;
        }
    }
}
