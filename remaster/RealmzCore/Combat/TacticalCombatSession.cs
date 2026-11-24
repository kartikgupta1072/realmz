using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public sealed class TacticalCombatSession
    {
        private enum ElementKind
        {
            None = 0,
            Heat = 1,
            Cold = 2,
            Electric = 3,
            Chemical = 4,
            Mental = 5,
            Charm = 6,
            Magic = 7
        }

        private readonly Dictionary<string, CombatantState> _combatantsById =
            new Dictionary<string, CombatantState>(StringComparer.Ordinal);
        private readonly List<CombatantState> _combatants = new List<CombatantState>();
        private readonly Dictionary<int, SpellDefinition> _spells;
        private readonly Dictionary<string, SpellDefinition> _spellsByKey =
            new Dictionary<string, SpellDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ItemDefinition> _items;
        private readonly List<string> _roundQueue = new List<string>();
        private readonly List<CombatLogEntry> _log = new List<CombatLogEntry>();
        private readonly List<int> _collectedLootItemIds = new List<int>();
        private readonly List<SurfaceChangeEvent> _lastSurfaceChanges = new List<SurfaceChangeEvent>();
        private readonly AStarPathfinder _pathfinder;
        private bool _victoryRewardsGranted;
        private bool _partyRewardsApplied;

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
            _pathfinder = new AStarPathfinder(Math.Max(grid.Width, 16), Math.Max(grid.Height, 16));
            RoundNumber = 1;
            TurnNumber = 1;

            RebuildRoundQueue();
            StartCurrentActorTurn();
            CheckOutcome();
        }

        public event Action<IReadOnlyList<SurfaceChangeEvent>>? SurfaceChanged;

        public CombatGrid Grid { get; }
        public IDiceRoller Roller { get; }
        public RulesetMode Mode { get; }
        public PartyState? Party { get; }
        public IReadOnlyList<CombatantState> Combatants => _combatants;
        public IReadOnlyDictionary<int, SpellDefinition> Spells => _spells;
        public IReadOnlyDictionary<int, ItemDefinition> Items => _items;
        public IReadOnlyList<CombatLogEntry> Log => _log;
        public IReadOnlyList<int> CollectedLootItemIds => _collectedLootItemIds;
        public IReadOnlyList<SurfaceChangeEvent> LastSurfaceChanges => _lastSurfaceChanges;
        public int RoundNumber { get; private set; }
        public int TurnNumber { get; private set; }
        public CombatOutcome Outcome { get; private set; }
        public int TotalXpEarned { get; private set; }
        public int TotalGoldLoot { get; private set; }
        public int VictoryLevelsGained { get; private set; }
        public bool VictoryRewardsGranted => _victoryRewardsGranted;
        public CombatVictorySummary? LastVictorySummary { get; private set; }

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

        public IReadOnlyList<CombatGridCell> GetActiveSurfaceCells()
        {
            return Grid.GetActiveSurfaceCells();
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
                if (c.Side == moverSide || !c.IsConscious || !c.Economy.HasReaction)
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
                !unit.Economy.HasReaction ||
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
                if (enemy == null || !enemy.Economy.HasReaction)
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
            return GetReachableTilesInternal(combatantId, ignoreMoveRemaining: false);
        }

        internal IReadOnlyList<ReachableTileInfo> GetReachableTilesInternal(string combatantId, bool ignoreMoveRemaining)
        {
            if (!_combatantsById.TryGetValue(combatantId, out CombatantState? unit) || !unit.IsConscious)
            {
                return Array.Empty<ReachableTileInfo>();
            }

            int budget = ignoreMoveRemaining ? unit.EffectiveMovement : unit.Economy.MoveRemaining;
            if (budget <= 0)
            {
                return Array.Empty<ReachableTileInfo>();
            }

            Dictionary<GridCoord, ReachableNode> nodes = _pathfinder.FindReachableTiles(
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
            return CheckLineOfSight(from, to, includeSteamObscuration: true);
        }

        public LosResult CheckLineOfSight(GridCoord from, GridCoord to, bool includeSteamObscuration)
        {
            List<GridCoord> rayCells = TraceBresenham(from, to);
            bool partialCover = false;
            bool steamObscured = false;

            if (includeSteamObscuration &&
                (Grid.GetSurface(from.X, from.Y) == SurfaceType.SteamCloud ||
                 Grid.GetSurface(to.X, to.Y) == SurfaceType.SteamCloud))
            {
                partialCover = true;
                steamObscured = true;
            }

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

                    if (includeSteamObscuration && Grid.GetSurface(cell.X, cell.Y) == SurfaceType.SteamCloud)
                    {
                        return new LosResult(LosKind.Blocked, 0, cell, rayCells, isSteamObscured: true);
                    }

                    if (Grid.ProvidesCover(cell.X, cell.Y) || IsOccupiedByLiving(cell.X, cell.Y))
                    {
                        partialCover = true;
                    }
                }

                prev = cell;
            }

            if (steamObscured)
            {
                return new LosResult(LosKind.PartialCover, 20, null, rayCells, isSteamObscured: true);
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
            return GetSpellAoeFootprint(spellIdOrKey, targetCenter, casterId, fromPos: null);
        }

        public SpellAoeFootprint GetSpellAoeFootprint(
            string spellIdOrKey,
            GridCoord targetCenter,
            string casterId,
            GridCoord? fromPos)
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
                    Array.Empty<GridCoord>(),
                    hasLineOfSight: false);
            }

            GridCoord casterPos = fromPos ?? caster.Position;
            int casterElev = Grid.GetElevation(casterPos.X, casterPos.Y);
            int targetElev = Grid.InBounds(targetCenter) ? Grid.GetElevation(targetCenter.X, targetCenter.Y) : 0;
            int effectiveRange = spell.Range > 0 && casterElev > targetElev
                ? spell.Range + 1
                : Math.Max(0, spell.Range);

            bool inRange = Grid.InBounds(targetCenter) &&
                           casterPos.ChebyshevDistance(targetCenter) <= effectiveRange;
            bool hasLos = true;

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
                affectedTiles.Add(casterPos);
                allyIds.Add(caster.Id);
            }
            else
            {
                if (!Grid.InBounds(targetCenter) || Grid.BlocksLos(targetCenter.X, targetCenter.Y))
                {
                    hasLos = false;
                    if (Grid.InBounds(targetCenter))
                    {
                        blockedTiles.Add(targetCenter);
                    }
                }
                else
                {
                    LosResult centerLos = CheckLineOfSight(casterPos, targetCenter, includeSteamObscuration: false);
                    if (centerLos.Kind == LosKind.Blocked)
                    {
                        hasLos = false;
                        if (centerLos.BlockedBy.HasValue)
                        {
                            blockedTiles.Add(centerLos.BlockedBy.Value);
                        }
                    }
                }

                if (hasLos)
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

                        if (tile != targetCenter)
                        {
                            LosResult blastLos = CheckLineOfSight(targetCenter, tile, includeSteamObscuration: false);
                            if (blastLos.Kind == LosKind.Blocked)
                            {
                                blockedTiles.Add(tile);
                                continue;
                            }
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
            }

            return new SpellAoeFootprint(
                inRange,
                targetCenter,
                affectedTiles,
                new List<string>(allyIds),
                new List<string>(enemyIds),
                blockedTiles,
                hasLineOfSight: hasLos);
        }

        public TacticalAttackPreview PreviewMeleeAttack(string attackerId, string defenderId, GridCoord? fromPos = null)
        {
            return PreviewMeleeAttackInternal(attackerId, defenderId, fromPos, ignoreActionBudget: false);
        }

        internal TacticalAttackPreview PreviewMeleeAttackInternal(
            string attackerId,
            string defenderId,
            GridCoord? fromPos,
            bool ignoreActionBudget)
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
            int attElev = Grid.GetElevation(pos.X, pos.Y);
            int defElev = Grid.GetElevation(defender.Position.X, defender.Position.Y);
            int unappliedWeaponBonus = attacker.WeaponBonusIncludedInToHit ? 0 : attacker.WeaponBonus;

            HitPreview hit = CombatRules.CalculateHitPreview(
                attackerToHit: attacker.EffectiveToHit,
                defenderAc: defender.EffectiveAc,
                isFlanking: flanking,
                equippedWeaponBonus: unappliedWeaponBonus,
                defenderIsEvil: defender.IsEvil,
                attackerConditions: attacker.Conditions,
                defenderConditions: defender.Conditions,
                mode: Mode,
                attackerElevation: attElev,
                defenderElevation: defElev,
                weaponBonusAlreadyIncluded: attacker.WeaponBonusIncludedInToHit,
                defenderIsProne: defender.IsProne,
                attackerIsProne: attacker.IsProne,
                attackerCritBonusPct: attacker.CritBonusPct);

            ComputeDamageRange(attacker.WeaponDice, attacker.DamageBonus, attacker.Conditions, out int minDmg, out int maxDmg);
            List<string> tags = CollectConditionTags(attacker, defender, flanking, los: null, hit.Modifiers);

            bool provokesAoO = false;
            if (fromPos.HasValue && fromPos.Value != attacker.Position)
            {
                provokesAoO = WouldStepProvokeAoO(attackerId, attacker.Position, fromPos.Value);
            }

            bool hasAction = ignoreActionBudget || attacker.Economy.HasStandardAction;
            bool canExecute = adjacent && attacker.Side != defender.Side && hasAction;
            string reason = !adjacent
                ? "Target is out of melee reach."
                : !hasAction
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
                conditionTags: tags,
                elevationModifierPct: hit.ElevationModifierPercent);
        }

        public TacticalAttackPreview PreviewRangedAttack(string attackerId, string defenderId, GridCoord? fromPos = null)
        {
            return PreviewRangedAttackInternal(attackerId, defenderId, fromPos, ignoreActionBudget: false);
        }

        internal TacticalAttackPreview PreviewRangedAttackInternal(
            string attackerId,
            string defenderId,
            GridCoord? fromPos,
            bool ignoreActionBudget)
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
            int attElev = Grid.GetElevation(pos.X, pos.Y);
            int defElev = Grid.GetElevation(defender.Position.X, defender.Position.Y);
            int effectiveRange = hasRanged
                ? attacker.RangedRange + (attElev > defElev ? 1 : 0)
                : 0;

            LosResult los = CheckLineOfSight(pos, defender.Position, includeSteamObscuration: true);

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
                mode: Mode,
                attackerElevation: attElev,
                defenderElevation: defElev,
                weaponBonusAlreadyIncluded: true,
                defenderIsProne: false,
                attackerIsProne: attacker.IsProne,
                attackerCritBonusPct: attacker.CritBonusPct);

            string diceStr = hasRanged ? attacker.RangedDice : "1d4";
            ComputeDamageRange(diceStr, 0, null, out int minDmg, out int maxDmg);
            List<string> tags = CollectConditionTags(attacker, defender, isFlanking: false, los, hit.Modifiers);

            bool inRange = hasRanged && dist > 0 && dist <= effectiveRange;
            bool losClear = los.Kind != LosKind.Blocked;
            bool hasAction = ignoreActionBudget || attacker.Economy.HasStandardAction;
            bool canExecute = inRange && losClear && attacker.Side != defender.Side && hasAction;
            string reason = !hasRanged
                ? "No ranged weapon equipped."
                : !inRange
                    ? "Target out of ranged weapon range."
                    : !losClear
                        ? "Line of sight is blocked."
                        : !hasAction
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
                conditionTags: tags,
                elevationModifierPct: hit.ElevationModifierPercent);
        }

        public TacticalSpellPreview PreviewSpell(string casterId, string spellIdOrKey, GridCoord targetCenter)
        {
            return PreviewSpellInternal(casterId, spellIdOrKey, targetCenter, fromPos: null, ignoreActionBudget: false);
        }

        internal TacticalSpellPreview PreviewSpellInternal(
            string casterId,
            string spellIdOrKey,
            GridCoord targetCenter,
            GridCoord? fromPos,
            bool ignoreActionBudget)
        {
            CombatantState? caster = GetCombatant(casterId);
            SpellDefinition? spell = ResolveSpell(spellIdOrKey);
            if (caster == null || spell == null)
            {
                return new TacticalSpellPreview(
                    false,
                    "Caster or spell not found.",
                    0, 0, 0, 0,
                    new SpellAoeFootprint(false, targetCenter, Array.Empty<GridCoord>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<GridCoord>(), hasLineOfSight: false),
                    Array.Empty<string>());
            }

            GridCoord casterPos = fromPos ?? caster.Position;
            SpellAoeFootprint footprint = GetSpellAoeFootprint(spellIdOrKey, targetCenter, casterId, casterPos);
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

            List<string> tags = new List<string>();
            if (spell.AppliedCondition.HasValue)
            {
                tags.Add(ConditionEffects.GetCode(spell.AppliedCondition.Value));
            }

            int casterElev = Grid.GetElevation(casterPos.X, casterPos.Y);
            int targetElev = Grid.InBounds(targetCenter) ? Grid.GetElevation(targetCenter.X, targetCenter.Y) : 0;
            if (spell.Range > 0 && casterElev > targetElev)
            {
                tags.Add("High Ground +1 Range");
            }

            bool anyImmune = false;
            if (isHostile && footprint.AffectedEnemyIds.Count > 0)
            {
                int totalSave = 0;
                for (int i = 0; i < footprint.AffectedEnemyIds.Count; i++)
                {
                    CombatantState? enemy = GetCombatant(footprint.AffectedEnemyIds[i]);
                    if (enemy != null)
                    {
                        if (IsTargetImmuneToSpell(enemy, spell))
                        {
                            anyImmune = true;
                        }

                        totalSave += ComputeSpellSaveChance(enemy, spell);
                    }
                }

                saveChancePct = totalSave / footprint.AffectedEnemyIds.Count;
            }

            if (anyImmune)
            {
                tags.Add("IMMUNE");
            }

            bool silenced = caster.Conditions.Has(CharConditionId.Silenced);
            bool enoughSp = caster.Sp >= spell.SpCost;
            bool hasAction = ignoreActionBudget || caster.Economy.HasStandardAction;
            bool canExecute = caster.IsConscious && !silenced && enoughSp && hasAction && footprint.InRange && footprint.HasLineOfSight;

            string reason = silenced
                ? "Caster is silenced."
                : !enoughSp
                    ? "Insufficient SP."
                    : !footprint.InRange
                        ? "Target is out of spell range."
                        : !footprint.HasLineOfSight
                            ? "Target is blocked by wall or obstacle (no line of sight)."
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

        public TacticalShovePreview PreviewShove(string actorId, string targetId, GridCoord? fromPos = null)
        {
            CombatantState? actor = GetCombatant(actorId);
            CombatantState? target = GetCombatant(targetId);
            if (actor == null || target == null || !actor.IsConscious || !target.IsAlive || actor.Id == target.Id)
            {
                return new TacticalShovePreview(
                    false,
                    "Actor or target unavailable for Shove.",
                    0,
                    new GridCoord(0, 0),
                    target?.Position ?? new GridCoord(0, 0),
                    false,
                    null,
                    false,
                    0,
                    SurfaceType.None,
                    false);
            }

            GridCoord pos = fromPos ?? actor.Position;
            int dx = Math.Sign(target.Position.X - pos.X);
            int dy = Math.Sign(target.Position.Y - pos.Y);
            GridCoord pushDir = new GridCoord(dx, dy);
            GridCoord dest = new GridCoord(target.Position.X + dx, target.Position.Y + dy);

            bool adjacent = pos.ChebyshevDistance(target.Position) == 1;
            bool hasSwift = actor.Economy.HasSwiftAction;
            bool isAlly = actor.Side == target.Side;

            bool hitsWall = !Grid.InBounds(dest) || !Grid.IsWalkable(dest.X, dest.Y);
            string? collidingUnitId = null;
            if (!hitsWall)
            {
                CombatantState? occupant = GetLivingUnitAt(dest, excludeId: target.Id);
                if (occupant != null)
                {
                    collidingUnitId = occupant.Id;
                }
            }

            int startElev = Grid.GetElevation(target.Position.X, target.Position.Y);
            int destElev = !hitsWall && collidingUnitId == null ? Grid.GetElevation(dest.X, dest.Y) : startElev;
            int elevDrop = Math.Max(0, startElev - destElev);
            bool pushesOffLedge = !hitsWall && collidingUnitId == null && elevDrop > 0;
            SurfaceType destSurface = Grid.InBounds(dest) ? Grid.GetSurface(dest.X, dest.Y) : SurfaceType.None;

            int successPct = isAlly
                ? 100
                : ComputeShoveSuccessChance(actor, target);

            bool canExecute = adjacent && hasSwift;
            string reason = !adjacent
                ? "Target is not adjacent for Shove."
                : !hasSwift
                    ? "No swift action remaining."
                    : string.Empty;

            return new TacticalShovePreview(
                canExecute: canExecute,
                reason: reason,
                successChancePct: successPct,
                pushDirection: pushDir,
                destination: dest,
                hitsWallOrBounds: hitsWall,
                collidingUnitId: collidingUnitId,
                pushesOffLedge: pushesOffLedge,
                fallElevationDrop: elevDrop,
                destinationSurface: destSurface,
                isAllyShove: isAlly);
        }

        public CombatActionResult TryShove(string targetId)
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            CombatantState? target = GetCombatant(targetId);
            if (actor == null || target == null)
            {
                return new CombatActionResult(false, "Invalid actor or target.");
            }

            TacticalShovePreview preview = PreviewShove(actor.Id, target.Id);
            if (!preview.CanExecute)
            {
                return new CombatActionResult(false, preview.Reason);
            }

            actor.Economy.CommitSwiftAction();
            actor.HasTriggeredIrreversibleEvent = true;

            if (!preview.IsAllyShove)
            {
                int roll = Roller.NextInt(1, 101);
                if (roll > preview.SuccessChancePct)
                {
                    string resistMsg = $"{actor.Name} tries to shove {target.Name} ({preview.SuccessChancePct}%), but {target.Name} holds firm (roll {roll}).";
                    AppendLog(actor.Id, actor.Name, "Shove", resistMsg, new[] { target.Id });
                    return new CombatActionResult(true, resistMsg);
                }
            }

            int totalDamage = 0;
            List<string> notes = new List<string>();
            List<string> loggedTargets = new List<string> { target.Id };

            if (preview.HitsWallOrBounds)
            {
                int crush = Math.Max(1, DiceSpec.Parse("1d4").Roll(Roller));
                target.Hp = Math.Max(0, target.Hp - crush);
                totalDamage += crush;
                notes.Add($"slams into wall for {crush} crush damage");
            }
            else if (!string.IsNullOrEmpty(preview.CollidingUnitId))
            {
                CombatantState? other = GetCombatant(preview.CollidingUnitId!);
                int targetCrush = Math.Max(1, DiceSpec.Parse("1d4").Roll(Roller));
                target.Hp = Math.Max(0, target.Hp - targetCrush);
                totalDamage += targetCrush;

                if (other != null && other.IsAlive)
                {
                    int otherCrush = Math.Max(1, DiceSpec.Parse("1d4").Roll(Roller));
                    other.Hp = Math.Max(0, other.Hp - otherCrush);
                    totalDamage += otherCrush;
                    loggedTargets.Add(other.Id);
                    notes.Add($"collides into {other.Name} ({targetCrush}/{otherCrush} crush damage)");
                }
                else
                {
                    notes.Add($"takes {targetCrush} collision damage");
                }
            }
            else
            {
                // Forced displacement never provokes AoOs.
                target.Position = preview.Destination;
                notes.Add($"is shoved to {target.Position}");

                if (preview.PushesOffLedge && target.IsAlive)
                {
                    int fallDmg = 0;
                    DiceSpec d6 = DiceSpec.Parse("1d6");
                    for (int tier = 0; tier < preview.FallElevationDrop; tier++)
                    {
                        fallDmg += Math.Max(1, d6.Roll(Roller));
                    }

                    target.Hp = Math.Max(0, target.Hp - fallDmg);
                    totalDamage += fallDmg;
                    if (target.IsAlive)
                    {
                        target.IsProne = true;
                        target.PronePendingMovePenalty = true;
                    }

                    notes.Add($"falls off ledge ({fallDmg} fall dmg, Prone)");
                }

                if (target.IsAlive)
                {
                    int surfaceDmg = ApplyTileSurfaceHazard(target, target.Position, isStepEnter: true);
                    totalDamage += surfaceDmg;
                    if (surfaceDmg > 0)
                    {
                        notes.Add($"takes {surfaceDmg} surface damage");
                    }
                    else if (target.IsProne && preview.DestinationSurface == SurfaceType.Ice)
                    {
                        notes.Add("slips Prone on Ice");
                    }
                }
            }

            string msg = $"{actor.Name} shoves {target.Name}: {string.Join(", ", notes)}.";
            AppendLog(actor.Id, actor.Name, "Shove", msg, loggedTargets, damageDealt: totalDamage);
            CheckOutcome();

            return new CombatActionResult(true, msg, damageDealt: totalDamage);
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

            PathResult path = _pathfinder.FindPath(
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
            int totalDamage = 0;
            List<AttackOutcome> aooOutcomes = new List<AttackOutcome>();

            for (int i = 0; i < path.Steps.Count; i++)
            {
                if (!ResolveMovementStep(actor, path.Steps[i], ref aooTriggered, ref totalDamage, aooOutcomes))
                {
                    if (!actor.IsConscious)
                    {
                        CheckOutcome();
                        return new CombatActionResult(
                            true,
                            $"{actor.Name} was stopped at {actor.Position}.",
                            aooOutcomes,
                            damageDealt: totalDamage,
                            aooTriggered: aooTriggered);
                    }

                    break;
                }
            }

            AppendLog(
                actor.Id,
                actor.Name,
                "Move",
                $"{actor.Name} moves to {actor.Position}.",
                Array.Empty<string>(),
                damageDealt: 0);

            CheckOutcome();
            return new CombatActionResult(
                true,
                $"{actor.Name} moved to {actor.Position}.",
                aooOutcomes,
                damageDealt: totalDamage,
                aooTriggered: aooTriggered);
        }

        private bool ResolveMovementStep(
            CombatantState actor,
            GridCoord next,
            ref bool aooTriggered,
            ref int totalDamage,
            List<AttackOutcome> aooOutcomes)
        {
            if (WouldStepProvokeAoO(actor.Id, actor.Position, next, out List<string> provokers))
            {
                for (int p = 0; p < provokers.Count; p++)
                {
                    CombatantState? enemy = GetCombatant(provokers[p]);
                    if (enemy == null || !enemy.IsConscious || !enemy.Economy.HasReaction || !actor.IsAlive)
                    {
                        continue;
                    }

                    if (!enemy.Economy.CommitReaction())
                    {
                        continue;
                    }

                    actor.HasTriggeredIrreversibleEvent = true;
                    aooTriggered = true;

                    AttackOutcome aoo = ResolveSingleMeleeSwing(enemy, actor, isFlanking: false);
                    aooOutcomes.Add(aoo);
                    totalDamage += aoo.DamageDealt;
                    AppendLog(
                        enemy.Id,
                        enemy.Name,
                        "AoO",
                        $"{enemy.Name} makes an Opportunity Attack on {actor.Name}: {aoo.Kind} ({aoo.DamageDealt} dmg).",
                        new[] { actor.Id },
                        damageDealt: aoo.DamageDealt);

                    if (!actor.IsConscious)
                    {
                        return false;
                    }
                }
            }

            int stepCost = Grid.GetMoveCost(next.X, next.Y);
            if (!actor.Economy.TryStep(stepCost))
            {
                return false;
            }

            actor.Position = next;

            bool wasProneBefore = actor.IsProne;
            int surfaceDamage = ApplyTileSurfaceHazard(actor, next, isStepEnter: true);
            totalDamage += surfaceDamage;

            if (!actor.IsConscious || (!wasProneBefore && actor.IsProne))
            {
                return false;
            }

            return true;
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

            int attElev = Grid.GetElevation(actor.Position.X, actor.Position.Y);
            int defElev = Grid.GetElevation(target.Position.X, target.Position.Y);

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
                mode: Mode,
                attackerElevation: attElev,
                defenderElevation: defElev,
                weaponBonusAlreadyIncluded: true,
                defenderIsProne: false,
                attackerIsProne: actor.IsProne,
                attackerCritBonusPct: actor.CritBonusPct);

            if (outcome.DamageDealt > 0)
            {
                bool wasAlive = target.IsAlive;
                target.Hp = Math.Max(0, target.Hp - outcome.DamageDealt);
                actor.CombatDamageDealt += outcome.DamageDealt;
                if (wasAlive && !target.IsAlive)
                {
                    actor.CombatKills++;
                }
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
                    // Friendly-fire AoE spells also hit allies caught in the blast.
                    targetIds.AddRange(preview.AoeFootprint.AffectedAllyIds);
                }
            }

            int totalDamage = 0;
            int totalHealing = 0;
            List<string> immuneTargetNames = new List<string>();
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
                    int heal = (effectSpec.HasValue ? Math.Max(1, effectSpec.Value.Roll(Roller)) : 5) + caster.DivineHealingBonus;
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + heal);
                    int actualHeal = target.Hp - before;
                    totalHealing += actualHeal;
                    caster.CombatHealingDone += actualHeal;
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
                    if (IsTargetImmuneToSpell(target, spell))
                    {
                        immuneTargetNames.Add(target.Name);
                        string dmgLabel = string.IsNullOrWhiteSpace(spell.DamageType) ? "Spell" : spell.DamageType;
                        AppendLog(
                            caster.Id,
                            caster.Name,
                            "SpellImmune",
                            $"{target.Name} is immune to {dmgLabel} ({spell.Name}).",
                            new[] { target.Id });
                        continue;
                    }

                    int saveChance = ComputeSpellSaveChance(target, spell, caster.SpellHitDcBonusPct);
                    bool saved = saveChance > 0 && Roller.NextInt(1, 101) <= saveChance;

                    if (effectSpec.HasValue)
                    {
                        int dmg = Math.Max(1, effectSpec.Value.Roll(Roller) + caster.ArcaneSpellPowerBonus);
                        if (saved)
                        {
                            dmg /= 2;
                        }

                        if ((string.Equals(spell.DamageType, "Heat", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(spell.DamageType, "Fire", StringComparison.OrdinalIgnoreCase)) &&
                            target.Conditions.Has(CharConditionId.FireProtection))
                        {
                            dmg = Math.Max(1, dmg / 2);
                        }

                        bool wasAlive = target.IsAlive;
                        target.Hp = Math.Max(0, target.Hp - dmg);
                        totalDamage += dmg;
                        caster.CombatDamageDealt += dmg;
                        if (wasAlive && !target.IsAlive)
                        {
                            caster.CombatKills++;
                        }
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

            if (!isHeal && !isBuff && !isCure && preview.AoeFootprint.AffectedTiles.Count > 0)
            {
                int surfaceComboDamage = ApplyElementToTiles(
                    preview.AoeFootprint.AffectedTiles,
                    spell.DamageType,
                    spell.Key,
                    spell);
                totalDamage += surfaceComboDamage;
            }

            string immuneSuffix = immuneTargetNames.Count > 0
                ? $" [Immune: {string.Join(", ", immuneTargetNames)}]"
                : string.Empty;
            string msg = $"{caster.Name} casts {spell.Name} ({totalDamage} dmg, {totalHealing} heal).{immuneSuffix}";
            AppendLog(caster.Id, caster.Name, "CastSpell", msg, targetIds, damageDealt: totalDamage, healingDone: totalHealing);
            CheckOutcome();

            return new CombatActionResult(true, msg, damageDealt: totalDamage, healingDone: totalHealing);
        }

        public int ApplySurfaceInteraction(
            IReadOnlyList<GridCoord> tiles,
            string elementType,
            string cause = "Environment")
        {
            if (tiles == null || tiles.Count == 0)
            {
                return 0;
            }

            int dmg = ApplyElementToTiles(tiles, elementType, cause, spell: null);
            CheckOutcome();
            return dmg;
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
                if (!Party.TryConsumeItemCharge(itemId, out _, _items))
                {
                    return new CombatActionResult(false, $"{item.Name} is not available in party stash.");
                }
            }

            actor.Economy.CommitSwiftAction();
            actor.HasTriggeredIrreversibleEvent = true;

            CombatantState target = !string.IsNullOrWhiteSpace(targetId)
                ? (GetCombatant(targetId!) ?? actor)
                : actor;

            int healed = 0;
            string nameLow = (item.Name ?? string.Empty).ToLowerInvariant();
            string msg;
            if (item.Id == 603 || nameLow.Contains("heat resist"))
            {
                target.Conditions.Apply(CharConditionId.FireProtection, magnitude: 1, turnsRemaining: 10, baseRecoveryChancePercent: 0, canRecoverBySave: false);
                msg = $"{actor.Name} uses {item.Name} on {target.Name} (+Fire Protection).";
            }
            else if (item.Id == 723 || nameLow.Contains("refresh"))
            {
                int spGain = Math.Min(target.MaxSp - target.Sp, 8);
                target.Sp += Math.Max(0, spGain);
                target.ExhaustionStacks = 0;
                if (!string.IsNullOrWhiteSpace(item.HealDice) && DiceSpec.TryParse(item.HealDice, out DiceSpec rSpec))
                {
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + Math.Max(1, rSpec.Roll(Roller)));
                    healed = target.Hp - before;
                }
                msg = $"{actor.Name} uses {item.Name} on {target.Name} (+{healed} HP, +{spGain} SP, refreshed).";
            }
            else
            {
                if (item.Id == 602 || nameLow.Contains("poison"))
                {
                    target.Conditions.Remove(CharConditionId.Poisoned);
                    target.Conditions.Remove(CharConditionId.Diseased);
                }

                if (!string.IsNullOrWhiteSpace(item.HealDice) && DiceSpec.TryParse(item.HealDice, out DiceSpec spec))
                {
                    int roll = Math.Max(1, spec.Roll(Roller));
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + roll);
                    healed = target.Hp - before;
                }
                msg = $"{actor.Name} uses {item.Name} on {target.Name} (+{healed} HP).";
            }

            actor.CombatHealingDone += healed;
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

                if (actor.IsProne && !actor.PronePendingMovePenalty)
                {
                    actor.IsProne = false;
                    AppendLog(actor.Id, actor.Name, "ProneEnd", $"{actor.Name} stands up from Prone.");
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
                TickSurfacesOnNewRound();
                RebuildRoundQueue();
            }

            TurnNumber++;
            StartCurrentActorTurn();
            CheckOutcome();
        }

        public CombatActionResult ExecuteMonsterAiTurn()
        {
            return MonsterAi.ExecuteTurn(this);
        }

        public CombatActionResult ExecutePartyAiTurn()
        {
            return PartyAi.ExecuteTurn(this);
        }

        public CombatActionResult StepAutoCombatTurn()
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new CombatActionResult(false, "Combat has ended.");
            }

            CombatantState? actor = CurrentActor;
            if (actor == null)
            {
                return new CombatActionResult(false, "No current actor.");
            }

            return actor.Side == CombatSide.Hero
                ? PartyAi.ExecuteTurn(this)
                : MonsterAi.ExecuteTurn(this);
        }

        public bool ShouldOfferQuickResolve()
        {
            return EvaluateQuickResolve().CanQuickResolve;
        }

        public QuickResolveSummary EvaluateQuickResolve()
        {
            if (Outcome != CombatOutcome.InProgress)
            {
                return new QuickResolveSummary(false, 0, 0, 0, "Combat is not in progress.");
            }

            return EvaluateQuickResolve(_combatants);
        }

        public static bool ShouldOfferQuickResolve(IReadOnlyList<CombatantState> combatants)
        {
            return EvaluateQuickResolve(combatants).CanQuickResolve;
        }

        public static QuickResolveSummary EvaluateQuickResolve(IReadOnlyList<CombatantState> combatants)
        {
            if (combatants == null || combatants.Count == 0)
            {
                return new QuickResolveSummary(false, 0, 0, 0, "No combatants.");
            }

            int livingHeroes = 0;
            int livingMonsters = 0;
            double partyPower = 0;
            double enemyPower = 0;
            int lowestHeroMaxHp = int.MaxValue;
            int highestEnemyBurst = 0;

            for (int i = 0; i < combatants.Count; i++)
            {
                CombatantState c = combatants[i];
                if (c == null || !c.IsAlive)
                {
                    continue;
                }

                double score = ComputeCombatantPowerScore(c);
                if (c.Side == CombatSide.Hero)
                {
                    livingHeroes++;
                    partyPower += score;
                    if (c.MaxHp < lowestHeroMaxHp)
                    {
                        lowestHeroMaxHp = c.MaxHp;
                    }
                }
                else
                {
                    livingMonsters++;
                    enemyPower += score;
                    int burst = ComputeMaxBurstDamage(c);
                    if (burst > highestEnemyBurst)
                    {
                        highestEnemyBurst = burst;
                    }
                }
            }

            if (livingHeroes == 0 || livingMonsters == 0)
            {
                return new QuickResolveSummary(
                    false,
                    Math.Round(partyPower, 2),
                    Math.Round(enemyPower, 2),
                    0,
                    "Both sides must have at least one living combatant.");
            }

            double safeEnemyPower = Math.Max(1.0, enemyPower);
            double ratio = partyPower / safeEnemyPower;
            bool burstDanger = (highestEnemyBurst * 100L) >= (lowestHeroMaxHp * 55L);
            bool dominant = partyPower >= (safeEnemyPower * 2.1);
            bool overwhelming = partyPower >= (safeEnemyPower * 2.6);
            bool canResolve = overwhelming || (dominant && !burstDanger);

            string reason = canResolve
                ? $"Party advantage {ratio:F2}x qualifies for Quick Resolve."
                : !dominant
                    ? $"Encounter too close for Quick Resolve ({ratio:F2}x < 2.10x)."
                    : $"Enemy burst ({highestEnemyBurst}) is too dangerous for hero MaxHP ({lowestHeroMaxHp}) at {ratio:F2}x.";

            return new QuickResolveSummary(
                canResolve,
                Math.Round(partyPower, 2),
                Math.Round(enemyPower, 2),
                Math.Round(ratio, 2),
                reason);
        }

        public CombatOutcome QuickResolveCombat(int maxTurns = 240)
        {
            int limit = Math.Max(1, maxTurns);
            for (int i = 0; i < limit && Outcome == CombatOutcome.InProgress; i++)
            {
                CombatActionResult step = StepAutoCombatTurn();
                if (!step.Success && Outcome == CombatOutcome.InProgress)
                {
                    break;
                }
            }

            return Outcome;
        }

        private static double ComputeCombatantPowerScore(CombatantState c)
        {
            double meleeAvg = DiceSpec.TryParse(c.WeaponDice, out DiceSpec weaponSpec)
                ? Math.Max(1.0, weaponSpec.ExpectedAverage + Math.Max(0, c.DamageBonus))
                : 2.5;
            double meleeDpr = Math.Max(1, c.NumAttacks) * meleeAvg;

            double rangedAvg = (c.RangedRange > 1 &&
                                !string.IsNullOrWhiteSpace(c.RangedDice) &&
                                DiceSpec.TryParse(c.RangedDice, out DiceSpec rangedSpec))
                ? Math.Max(1.0, rangedSpec.ExpectedAverage)
                : 0.0;

            double offenseAvg = Math.Max(meleeDpr, rangedAvg);
            int bestSkill = Math.Max(c.EffectiveToHit, c.RangedRange > 1 ? c.EffectiveMissileSkill : c.EffectiveToHit);
            double hitFactor = Math.Clamp(0.45 + (bestSkill * 0.015), 0.35, 1.35);
            double defenseFactor = Math.Clamp(0.75 + (c.EffectiveAc * 0.03), 0.60, 1.65);

            double hpValue = c.Hp * defenseFactor;
            double offenseValue = offenseAvg * hitFactor * 3.0;
            double spValue = Math.Min(60, c.MaxSp) * 0.5;
            return Math.Max(1.0, hpValue + offenseValue + spValue);
        }

        private static int ComputeMaxBurstDamage(CombatantState c)
        {
            int meleeSingleMax = DiceSpec.TryParse(c.WeaponDice, out DiceSpec weaponSpec)
                ? Math.Max(1, weaponSpec.Max + Math.Max(0, c.DamageBonus))
                : 4;
            int meleeBurst = Math.Max(1, c.NumAttacks) * meleeSingleMax;

            int rangedBurst = (c.RangedRange > 1 &&
                               !string.IsNullOrWhiteSpace(c.RangedDice) &&
                               DiceSpec.TryParse(c.RangedDice, out DiceSpec rangedSpec))
                ? Math.Max(0, rangedSpec.Max)
                : 0;

            return Math.Max(meleeBurst, rangedBurst);
        }

        public EnemyIntentPrediction PredictNextIntent(string? actorId = null)
        {
            return MonsterAi.PredictNextIntent(this, actorId);
        }

        public void GrantVictoryRewardsToParty(PartyState party)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (Outcome != CombatOutcome.Victory || _partyRewardsApplied)
            {
                return;
            }

            _partyRewardsApplied = true;

            int baseGold = TotalGoldLoot;
            int luckBonusGold = baseGold > 0
                ? (int)Math.Round(baseGold * (party.GetGoldLootBonusPercent() / 100.0), MidpointRounding.AwayFromZero)
                : 0;
            int grantedGold = baseGold + luckBonusGold;
            TotalGoldLoot = grantedGold;
            if (grantedGold > 0)
            {
                party.Gold += grantedGold;
            }

            List<LootStackSummary> lootStacks = new List<LootStackSummary>();
            for (int i = 0; i < _collectedLootItemIds.Count; i++)
            {
                int itemId = _collectedLootItemIds[i];
                ItemDefinition? def = _items.TryGetValue(itemId, out ItemDefinition? foundDef) ? foundDef : null;
                int charges = def != null ? def.Charges : 0;
                party.AddItemToStash(itemId, 1, isIdentified: true, charges: charges);

                int normalizedChg = Math.Max(0, charges);
                bool stacked = false;
                for (int s = 0; s < lootStacks.Count; s++)
                {
                    if (lootStacks[s].ItemId == itemId && lootStacks[s].RemainingCharges == normalizedChg)
                    {
                        LootStackSummary prev = lootStacks[s];
                        lootStacks[s] = new LootStackSummary(
                            prev.ItemId,
                            prev.Name,
                            prev.Category,
                            prev.Slot,
                            prev.Quantity + 1,
                            prev.RemainingCharges,
                            prev.BasePrice,
                            prev.IsJunk);
                        stacked = true;
                        break;
                    }
                }

                if (!stacked)
                {
                    lootStacks.Add(new LootStackSummary(
                        itemId,
                        def != null ? def.Name : $"Item #{itemId}",
                        def != null ? def.Category : "Supply",
                        def != null ? def.Slot : string.Empty,
                        1,
                        normalizedChg,
                        def != null ? def.BasePrice : 1,
                        def != null && def.IsJunk));
                }
            }

            List<HeroVictorySummary> heroSummaries = new List<HeroVictorySummary>(party.Members.Count);
            if (party.Members.Count > 0)
            {
                bool[] aliveFlags = new bool[party.Members.Count];
                int totalWeights = 0;
                int livingCount = 0;
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState member = party.Members[i];
                    CombatantState? combatant = GetCombatant(member.Id);
                    bool alive = combatant != null ? combatant.IsAlive : member.IsAlive;
                    aliveFlags[i] = alive;
                    if (alive)
                    {
                        livingCount++;
                        totalWeights += 2;
                    }
                    else
                    {
                        totalWeights += 1;
                    }
                }

                int[] xpShares = new int[party.Members.Count];
                if (TotalXpEarned > 0 && totalWeights > 0)
                {
                    if (livingCount == party.Members.Count || livingCount == 0)
                    {
                        int equalShare = TotalXpEarned / party.Members.Count;
                        int equalRem = TotalXpEarned % party.Members.Count;
                        for (int i = 0; i < party.Members.Count; i++)
                        {
                            xpShares[i] = equalShare + (i < equalRem ? 1 : 0);
                        }
                    }
                    else
                    {
                        int assigned = 0;
                        for (int i = 0; i < party.Members.Count; i++)
                        {
                            int w = aliveFlags[i] ? 2 : 1;
                            xpShares[i] = (TotalXpEarned * w) / totalWeights;
                            assigned += xpShares[i];
                        }

                        int rem = TotalXpEarned - assigned;
                        for (int i = 0; i < party.Members.Count && rem > 0; i++)
                        {
                            if (aliveFlags[i])
                            {
                                xpShares[i]++;
                                rem--;
                            }
                        }
                        for (int i = 0; i < party.Members.Count && rem > 0; i++)
                        {
                            xpShares[i]++;
                            rem--;
                        }
                    }
                }

                int levelsGained = 0;
                int mvpIdx = -1;
                int bestMvpScore = 0;
                for (int i = 0; i < party.Members.Count; i++)
                {
                    PartyMemberState member = party.Members[i];
                    CombatantState? combatant = GetCombatant(member.Id);
                    int share = xpShares[i];
                    int levelBefore = member.Level;
                    int xpBefore = member.Xp;
                    bool readyToLevel;
                    if (party.DeferLevelUp)
                    {
                        member.AwardXp(share);
                        readyToLevel = member.HasPendingLevelUp;
                    }
                    else
                    {
                        levelsGained += member.AddXp(share);
                        readyToLevel = member.HasPendingLevelUp;
                    }

                    int kills = combatant != null ? combatant.CombatKills : 0;
                    int dmg = combatant != null ? combatant.CombatDamageDealt : 0;
                    int heal = combatant != null ? combatant.CombatHealingDone : 0;
                    int mvpScore = dmg + (kills * 10) + heal;
                    if (mvpScore > bestMvpScore)
                    {
                        bestMvpScore = mvpScore;
                        mvpIdx = i;
                    }

                    int curFloor = PartyMemberState.GetXpRequiredForLevel(member.Level);
                    int nextTarget = PartyMemberState.GetXpRequiredForLevel(Math.Min(PartyMemberState.MaxLevel, member.Level + 1));

                    heroSummaries.Add(new HeroVictorySummary(
                        member.Id,
                        member.Name,
                        member.Archetype,
                        member.IconId,
                        member.PortraitId,
                        levelBefore,
                        member.Level,
                        xpBefore,
                        member.Xp,
                        share,
                        curFloor,
                        nextTarget,
                        readyToLevel,
                        member.PendingLevelUps,
                        wasFallenRevived: !aliveFlags[i],
                        kills,
                        dmg,
                        heal,
                        isMvp: false));
                }

                if (mvpIdx >= 0 && mvpIdx < heroSummaries.Count)
                {
                    heroSummaries[mvpIdx].IsMvp = true;
                }

                VictoryLevelsGained = levelsGained;
            }

            LastVictorySummary = new CombatVictorySummary(
                TotalXpEarned,
                baseGold,
                luckBonusGold,
                grantedGold,
                party.Gold,
                RoundNumber,
                heroSummaries,
                lootStacks);
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

        internal static bool IsTargetImmuneToSpell(CombatantState target, SpellDefinition spell)
        {
            if (!TryGetImmunityIndex(spell.DamageType, out int index))
            {
                return false;
            }

            return index >= 0 && index < target.SpellImmunities.Count && target.SpellImmunities[index];
        }

        internal CombatantState? GetLivingUnitAt(GridCoord coord, string? excludeId = null)
        {
            for (int i = 0; i < _combatants.Count; i++)
            {
                CombatantState c = _combatants[i];
                if (c.IsAlive && c.Id != excludeId && c.Position == coord)
                {
                    return c;
                }
            }

            return null;
        }

        private AttackOutcome ResolveSingleMeleeSwing(CombatantState attacker, CombatantState defender, bool isFlanking)
        {
            DiceSpec weapon = DiceSpec.TryParse(attacker.WeaponDice, out DiceSpec parsed)
                ? parsed
                : DiceSpec.Parse("1d4");

            int attElev = Grid.GetElevation(attacker.Position.X, attacker.Position.Y);
            int defElev = Grid.GetElevation(defender.Position.X, defender.Position.Y);
            int unappliedWeaponBonus = attacker.WeaponBonusIncludedInToHit ? 0 : attacker.WeaponBonus;

            AttackOutcome outcome = CombatRules.ResolveMeleeAttack(
                Roller,
                weapon,
                attackerToHit: attacker.EffectiveToHit,
                attackerDamageBonus: attacker.DamageBonus,
                defenderAc: defender.EffectiveAc,
                isFlanking: isFlanking,
                equippedWeaponBonus: unappliedWeaponBonus,
                defenderIsEvil: defender.IsEvil,
                attackerConditions: attacker.Conditions,
                defenderConditions: defender.Conditions,
                mode: Mode,
                attackerElevation: attElev,
                defenderElevation: defElev,
                weaponBonusAlreadyIncluded: attacker.WeaponBonusIncludedInToHit,
                defenderIsProne: defender.IsProne,
                attackerIsProne: attacker.IsProne,
                attackerCritBonusPct: attacker.CritBonusPct);

            if (outcome.DamageDealt > 0)
            {
                bool wasAlive = defender.IsAlive;
                defender.Hp = Math.Max(0, defender.Hp - outcome.DamageDealt);
                attacker.CombatDamageDealt += outcome.DamageDealt;
                if (wasAlive && !defender.IsAlive)
                {
                    attacker.CombatKills++;
                }
            }

            return outcome;
        }

        private int ApplyElementToTiles(
            IReadOnlyList<GridCoord> tiles,
            string damageType,
            string cause,
            SpellDefinition? spell)
        {
            ElementKind element = ParseElementKind(damageType);
            SurfaceType createdSurface = SurfaceType.None;
            int createdRounds = 0;
            if (spell != null &&
                !string.IsNullOrWhiteSpace(spell.CreatedSurface) &&
                Enum.TryParse<SurfaceType>(spell.CreatedSurface, ignoreCase: true, out SurfaceType parsedSurf) &&
                parsedSurf != SurfaceType.None)
            {
                createdSurface = parsedSurf;
                createdRounds = spell.CreatedSurfaceRounds != 0
                    ? spell.CreatedSurfaceRounds
                    : (createdSurface == SurfaceType.Water || createdSurface == SurfaceType.Oil ? -1 : 2);
            }

            if (element == ElementKind.None && createdSurface == SurfaceType.None)
            {
                return 0;
            }

            int totalBonusDamage = 0;
            HashSet<string> reactionDamagedUnits = new HashSet<string>(StringComparer.Ordinal);
            List<SurfaceChangeEvent> changes = new List<SurfaceChangeEvent>();

            for (int i = 0; i < tiles.Count; i++)
            {
                GridCoord tile = tiles[i];
                if (!Grid.TryGetCell(tile.X, tile.Y, out CombatGridCell? cell) || cell == null || cell.IsBlocked)
                {
                    continue;
                }

                if (element == ElementKind.Heat)
                {
                    if (cell.Surface == SurfaceType.Oil)
                    {
                        List<GridCoord> oilCluster = CollectContiguousSurface(tile, SurfaceType.Oil);
                        for (int c = 0; c < oilCluster.Count; c++)
                        {
                            GridCoord oc = oilCluster[c];
                            CombatGridCell oilCell = Grid.GetCell(oc);
                            oilCell.BaseSurface = SurfaceType.None;
                            RecordSurfaceChange(oilCell, SurfaceType.Fire, 2, "OilIgnited", changes);

                            CombatantState? victim = GetLivingUnitAt(oc);
                            if (victim != null && reactionDamagedUnits.Add(victim.Id))
                            {
                                int burst = RollElementalDamage(victim, "1d6", ElementKind.Heat);
                                totalBonusDamage += burst;
                                AppendLog(
                                    victim.Id,
                                    victim.Name,
                                    "OilIgnition",
                                    $"{victim.Name} is caught in Oil ignition burst ({burst} Heat dmg).",
                                    new[] { victim.Id },
                                    damageDealt: burst);
                            }
                        }
                    }
                    else if (cell.Surface == SurfaceType.Ice)
                    {
                        cell.BaseSurface = SurfaceType.Water;
                        RecordSurfaceChange(cell, SurfaceType.Water, -1, "IceMelted", changes);
                        AppendLog(string.Empty, "Surface", "SurfaceReaction", $"Ice at {tile} melts into Water.");
                    }
                    else if (cell.Surface == SurfaceType.Water || cell.Surface == SurfaceType.ElectrifiedWater)
                    {
                        cell.BaseSurface = SurfaceType.None;
                        RecordSurfaceChange(cell, SurfaceType.SteamCloud, 2, "WaterVaporized", changes);
                        AppendLog(string.Empty, "Surface", "SurfaceReaction", $"Water at {tile} vaporizes into SteamCloud.");
                    }
                    else if (cell.Surface == SurfaceType.Poison)
                    {
                        cell.BaseSurface = SurfaceType.None;
                        RecordSurfaceChange(cell, SurfaceType.Fire, 1, "PoisonIgnited", changes);
                    }
                }
                else if (element == ElementKind.Cold)
                {
                    if (cell.Surface == SurfaceType.Water || cell.Surface == SurfaceType.ElectrifiedWater)
                    {
                        cell.BaseSurface = SurfaceType.Water;
                        RecordSurfaceChange(cell, SurfaceType.Ice, 3, "WaterFrozen", changes);
                        CombatantState? occupant = GetLivingUnitAt(tile);
                        if (occupant != null)
                        {
                            TryIceSlipSave(occupant, stopMovement: false);
                        }
                    }
                    else if (cell.Surface == SurfaceType.Fire)
                    {
                        RecordSurfaceChange(cell, SurfaceType.SteamCloud, 2, "FireExtinguished", changes);
                    }
                }
                else if (element == ElementKind.Electric)
                {
                    if (cell.Surface == SurfaceType.Water)
                    {
                        List<GridCoord> waterCluster = CollectContiguousSurface(tile, SurfaceType.Water);
                        for (int w = 0; w < waterCluster.Count; w++)
                        {
                            GridCoord wc = waterCluster[w];
                            CombatGridCell waterCell = Grid.GetCell(wc);
                            waterCell.BaseSurface = SurfaceType.Water;
                            RecordSurfaceChange(waterCell, SurfaceType.ElectrifiedWater, 2, "WaterElectrified", changes);

                            CombatantState? victim = GetLivingUnitAt(wc);
                            if (victim != null && reactionDamagedUnits.Add(victim.Id))
                            {
                                int shock = RollElementalDamage(victim, "1d4", ElementKind.Electric);
                                totalBonusDamage += shock;
                                AppendLog(
                                    victim.Id,
                                    victim.Name,
                                    "ElectrifiedWater",
                                    $"{victim.Name} is shocked by ElectrifiedWater ({shock} Electrical dmg).",
                                    new[] { victim.Id },
                                    damageDealt: shock);
                            }
                        }
                    }
                }

                if (createdSurface != SurfaceType.None && cell.Surface == SurfaceType.None)
                {
                    if (createdRounds < 0 && (createdSurface == SurfaceType.Water || createdSurface == SurfaceType.Oil))
                    {
                        cell.BaseSurface = createdSurface;
                    }

                    RecordSurfaceChange(cell, createdSurface, createdRounds, string.IsNullOrEmpty(cause) ? "SpellSurface" : cause, changes);
                }
            }

            NotifySurfaceChanges(changes);
            return totalBonusDamage;
        }

        private List<GridCoord> CollectContiguousSurface(GridCoord seed, SurfaceType targetSurface)
        {
            List<GridCoord> cluster = new List<GridCoord>();
            if (!Grid.InBounds(seed) || Grid.GetSurface(seed.X, seed.Y) != targetSurface)
            {
                return cluster;
            }

            HashSet<GridCoord> visited = new HashSet<GridCoord> { seed };
            Queue<GridCoord> queue = new Queue<GridCoord>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                GridCoord cur = queue.Dequeue();
                cluster.Add(cur);

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        GridCoord next = new GridCoord(cur.X + dx, cur.Y + dy);
                        if (!Grid.InBounds(next) || visited.Contains(next))
                        {
                            continue;
                        }

                        CombatGridCell cell = Grid.GetCell(next);
                        if (!cell.IsBlocked && cell.Surface == targetSurface)
                        {
                            visited.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            return cluster;
        }

        private int ApplyTileSurfaceHazard(CombatantState unit, GridCoord coord, bool isStepEnter)
        {
            if (!unit.IsAlive || !Grid.TryGetCell(coord.X, coord.Y, out CombatGridCell? cell) || cell == null)
            {
                return 0;
            }

            switch (cell.Surface)
            {
                case SurfaceType.Fire:
                {
                    int dmg = RollElementalDamage(unit, "1d4", ElementKind.Heat);
                    if (dmg > 0)
                    {
                        unit.HasTriggeredIrreversibleEvent = true;
                        AppendLog(
                            unit.Id,
                            unit.Name,
                            "SurfaceBurn",
                            $"{unit.Name} takes {dmg} Heat damage from Fire surface.",
                            new[] { unit.Id },
                            damageDealt: dmg);
                    }

                    return dmg;
                }

                case SurfaceType.ElectrifiedWater:
                {
                    int dmg = RollElementalDamage(unit, "1d4", ElementKind.Electric);
                    if (dmg > 0)
                    {
                        unit.HasTriggeredIrreversibleEvent = true;
                        AppendLog(
                            unit.Id,
                            unit.Name,
                            "SurfaceShock",
                            $"{unit.Name} takes {dmg} Electrical damage from ElectrifiedWater.",
                            new[] { unit.Id },
                            damageDealt: dmg);
                    }

                    return dmg;
                }

                case SurfaceType.Poison:
                {
                    if (!IsImmuneToElement(unit, ElementKind.Chemical))
                    {
                        int dmg = RollElementalDamage(unit, "1d4", ElementKind.Chemical);
                        unit.Conditions.Apply(
                            CharConditionId.Poisoned,
                            magnitude: 2,
                            turnsRemaining: 2,
                            baseRecoveryChancePercent: 25,
                            canRecoverBySave: true,
                            escalationStepPercent: 10);
                        unit.HasTriggeredIrreversibleEvent = true;
                        AppendLog(
                            unit.Id,
                            unit.Name,
                            "SurfacePoison",
                            $"{unit.Name} suffers Poison surface ({dmg} dmg, Poisoned).",
                            new[] { unit.Id },
                            damageDealt: dmg);
                        return dmg;
                    }

                    return 0;
                }

                case SurfaceType.Ice:
                {
                    if (isStepEnter)
                    {
                        TryIceSlipSave(unit, stopMovement: ReferenceEquals(unit, CurrentActor));
                    }

                    return 0;
                }

                default:
                    return 0;
            }
        }

        private void TryIceSlipSave(CombatantState unit, bool stopMovement)
        {
            if (!unit.IsAlive || unit.IsProne)
            {
                return;
            }

            int dodgeScore = Math.Clamp(25 + (unit.Dodge * 2) + ((unit.EffectiveDexterity - 10) * 3), 10, 90);
            int roll = Roller.NextInt(1, 101);
            if (roll > dodgeScore)
            {
                unit.IsProne = true;
                unit.PronePendingMovePenalty = true;
                unit.HasTriggeredIrreversibleEvent = true;
                if (stopMovement)
                {
                    unit.Economy.ConsumeAllMovement();
                }

                AppendLog(
                    unit.Id,
                    unit.Name,
                    "IceSlip",
                    $"{unit.Name} slips on Ice (roll {roll} > save {dodgeScore}%) and is knocked Prone!",
                    new[] { unit.Id });
            }
            else
            {
                AppendLog(
                    unit.Id,
                    unit.Name,
                    "IceSave",
                    $"{unit.Name} keeps balance on Ice (roll {roll} <= {dodgeScore}%).",
                    new[] { unit.Id });
            }
        }

        private int RollElementalDamage(CombatantState target, string diceExpr, ElementKind element)
        {
            if (!target.IsAlive || IsImmuneToElement(target, element))
            {
                return 0;
            }

            int dmg = Math.Max(1, DiceSpec.Parse(diceExpr).Roll(Roller));
            bool protectedCond = element switch
            {
                ElementKind.Heat => target.Conditions.Has(CharConditionId.FireProtection),
                ElementKind.Cold => target.Conditions.Has(CharConditionId.ColdProtection),
                ElementKind.Electric => target.Conditions.Has(CharConditionId.ElectricalProtection),
                ElementKind.Chemical => target.Conditions.Has(CharConditionId.ChemicalProtection),
                _ => false
            };

            if (protectedCond)
            {
                dmg = Math.Max(1, dmg / 2);
            }

            target.Hp = Math.Max(0, target.Hp - dmg);
            return dmg;
        }

        private static bool IsImmuneToElement(CombatantState target, ElementKind element)
        {
            int idx = element switch
            {
                ElementKind.Charm => 0,
                ElementKind.Heat => 1,
                ElementKind.Cold => 2,
                ElementKind.Electric => 3,
                ElementKind.Chemical => 4,
                ElementKind.Mental => 5,
                _ => -1
            };

            return idx >= 0 && idx < target.SpellImmunities.Count && target.SpellImmunities[idx];
        }

        private void TickSurfacesOnNewRound()
        {
            List<SurfaceChangeEvent> changes = new List<SurfaceChangeEvent>();
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    CombatGridCell cell = Grid.GetCell(x, y);
                    if (cell.Surface == SurfaceType.None || cell.SurfaceRoundsRemaining <= 0)
                    {
                        continue;
                    }

                    cell.SurfaceRoundsRemaining--;
                    if (cell.SurfaceRoundsRemaining == 0)
                    {
                        SurfaceType reverted = cell.BaseSurface == SurfaceType.Water
                            ? SurfaceType.Water
                            : SurfaceType.None;
                        int nextRounds = reverted == SurfaceType.Water ? -1 : 0;
                        RecordSurfaceChange(cell, reverted, nextRounds, "RoundExpired", changes);
                    }
                }
            }

            NotifySurfaceChanges(changes);
        }

        private static void RecordSurfaceChange(
            CombatGridCell cell,
            SurfaceType newSurface,
            int roundsRemaining,
            string cause,
            List<SurfaceChangeEvent> changes)
        {
            SurfaceType prev = cell.Surface;
            if (prev == newSurface && cell.SurfaceRoundsRemaining == roundsRemaining)
            {
                return;
            }

            cell.Surface = newSurface;
            cell.SurfaceRoundsRemaining = newSurface == SurfaceType.None ? 0 : roundsRemaining;
            changes.Add(new SurfaceChangeEvent(cell.Coord, prev, newSurface, cell.SurfaceRoundsRemaining, cause));
        }

        private void NotifySurfaceChanges(List<SurfaceChangeEvent> changes)
        {
            _lastSurfaceChanges.Clear();
            if (changes.Count == 0)
            {
                return;
            }

            _lastSurfaceChanges.AddRange(changes);
            SurfaceChanged?.Invoke(changes);
        }

        private int ComputeShoveSuccessChance(CombatantState actor, CombatantState target)
        {
            int actorScore = actor.Brawn + (actor.EffectiveToHit / 10);
            int targetScore = target.Brawn + (target.Dodge / 10);
            return Math.Clamp(55 + ((actorScore - targetScore) * 5), 15, 95);
        }

        private int ComputeSpellSaveChance(CombatantState target, SpellDefinition spell, int casterDcBonusPct = 0)
        {
            if (string.Equals(spell.SaveKind, "None", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            int saveBonus = 0;
            if (target.Saves.Count > 0)
            {
                int idx = GetSaveIndex(spell.SaveKind);
                saveBonus = idx < target.Saves.Count ? target.Saves[idx] : target.Saves[0];
            }

            bool isConditionOrPoisonSave = string.Equals(spell.SaveKind, "Chemical", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(spell.SaveKind, "Poison", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(spell.SaveKind, "Mental", StringComparison.OrdinalIgnoreCase);
            int vitBonus = isConditionOrPoisonSave ? target.ConditionSaveBonusPct : 0;
            int total = Math.Max(0, target.MagicResistance) + Math.Max(0, saveBonus) + vitBonus - casterDcBonusPct;
            return Math.Clamp(total, 0, 95);
        }

        private static int GetSaveIndex(string saveKind)
        {
            if (string.IsNullOrWhiteSpace(saveKind))
            {
                return 5;
            }

            switch (saveKind.Trim().ToLowerInvariant())
            {
                case "heat":
                case "fire":
                    return 0;
                case "cold":
                case "ice":
                case "frost":
                    return 1;
                case "electrical":
                case "electric":
                case "shock":
                case "lightning":
                    return 2;
                case "chemical":
                case "poison":
                case "acid":
                    return 3;
                case "mental":
                case "mind":
                case "psychic":
                    return 4;
                default:
                    return 5;
            }
        }

        private static ElementKind ParseElementKind(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
            {
                return ElementKind.None;
            }

            switch (damageType.Trim().ToLowerInvariant())
            {
                case "heat":
                case "fire":
                    return ElementKind.Heat;
                case "cold":
                case "ice":
                case "frost":
                    return ElementKind.Cold;
                case "electrical":
                case "electric":
                case "shock":
                case "lightning":
                    return ElementKind.Electric;
                case "chemical":
                case "poison":
                case "acid":
                    return ElementKind.Chemical;
                case "mental":
                case "mind":
                case "psychic":
                    return ElementKind.Mental;
                case "charm":
                    return ElementKind.Charm;
                case "magic":
                    return ElementKind.Magic;
                default:
                    return ElementKind.None;
            }
        }

        private static bool TryGetImmunityIndex(string damageType, out int index)
        {
            ElementKind kind = ParseElementKind(damageType);
            switch (kind)
            {
                case ElementKind.Charm:
                    index = 0;
                    return true;
                case ElementKind.Heat:
                    index = 1;
                    return true;
                case ElementKind.Cold:
                    index = 2;
                    return true;
                case ElementKind.Electric:
                    index = 3;
                    return true;
                case ElementKind.Chemical:
                    index = 4;
                    return true;
                case ElementKind.Mental:
                    index = 5;
                    return true;
                default:
                    index = -1;
                    return false;
            }
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

            if (actor.IsProne && actor.PronePendingMovePenalty)
            {
                actor.Economy.ConsumeAllMovement();
                actor.PronePendingMovePenalty = false;
                AppendLog(
                    actor.Id,
                    actor.Name,
                    "Prone",
                    $"{actor.Name} is Prone and loses movement while rising.",
                    new[] { actor.Id });
            }

            if (actor.IsAlive)
            {
                ApplyTileSurfaceHazard(actor, actor.Position, isStepEnter: false);
            }
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
                if (!_victoryRewardsGranted)
                {
                    _victoryRewardsGranted = true;
                    int xp = 0;
                    int gold = 0;
                    _collectedLootItemIds.Clear();

                    for (int i = 0; i < _combatants.Count; i++)
                    {
                        CombatantState c = _combatants[i];
                        if (c.Side != CombatSide.Monster)
                        {
                            continue;
                        }

                        xp += c.XpReward;
                        gold += c.GoldReward;
                        for (int l = 0; l < c.LootItemIds.Count; l++)
                        {
                            int lootId = c.LootItemIds[l];
                            if (lootId > 0)
                            {
                                _collectedLootItemIds.Add(lootId);
                            }
                        }
                    }

                    TotalXpEarned = xp;
                    TotalGoldLoot = gold;

                    if (Party != null)
                    {
                        GrantVictoryRewardsToParty(Party);
                    }
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
            LosResult? los,
            IReadOnlyList<string>? hitModifiers = null)
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

            if (defender.IsProne || attacker.IsProne)
            {
                tags.Add("PRONE");
            }

            if (los != null && los.IsSteamObscured)
            {
                tags.Add("STEAM_OBSCURED -20%");
            }
            else if (los != null && los.Kind == LosKind.PartialCover)
            {
                tags.Add("PARTIAL_COVER");
            }

            if (hitModifiers != null)
            {
                for (int i = 0; i < hitModifiers.Count; i++)
                {
                    if (!tags.Contains(hitModifiers[i]))
                    {
                        tags.Add(hitModifiers[i]);
                    }
                }
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

    public sealed class QuickResolveSummary
    {
        public QuickResolveSummary(
            bool canQuickResolve,
            double partyPowerScore,
            double enemyPowerScore,
            double powerRatio,
            string reason)
        {
            CanQuickResolve = canQuickResolve;
            PartyPowerScore = partyPowerScore;
            EnemyPowerScore = enemyPowerScore;
            PowerRatio = powerRatio;
            Reason = reason ?? string.Empty;
        }

        public bool CanQuickResolve { get; }
        public double PartyPowerScore { get; }
        public double EnemyPowerScore { get; }
        public double PowerRatio { get; }
        public string Reason { get; }
    }
}
