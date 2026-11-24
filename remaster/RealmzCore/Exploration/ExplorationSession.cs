using System;
using System.Collections.Generic;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Rules;

namespace RealmzCore.Exploration
{
    public enum ExplorationHaltReason
    {
        None = 0,
        Arrived = 1,
        Blocked = 2,
        Portal = 3,
        PointOfInterest = 4,
        EncounterZone = 5
    }

    public sealed class ExplorationStepResult
    {
        public ExplorationStepResult(
            bool moved,
            GridCoord position,
            ExplorationHaltReason haltReason,
            MapPortal? portal = null,
            MapPointOfInterest? pointOfInterest = null,
            EncounterZoneData? triggeredZone = null,
            int? triggeredBattleId = null,
            int torchTurnsRemaining = 0,
            bool torchExpiredThisStep = false)
        {
            Moved = moved;
            Position = position;
            HaltReason = haltReason;
            Portal = portal;
            PointOfInterest = pointOfInterest;
            TriggeredZone = triggeredZone;
            TriggeredBattleId = triggeredBattleId;
            TorchTurnsRemaining = torchTurnsRemaining;
            TorchExpiredThisStep = torchExpiredThisStep;
        }

        public bool Moved { get; }
        public GridCoord Position { get; }
        public ExplorationHaltReason HaltReason { get; }
        public MapPortal? Portal { get; }
        public MapPointOfInterest? PointOfInterest { get; }
        public EncounterZoneData? TriggeredZone { get; }
        public int? TriggeredBattleId { get; }
        public int TorchTurnsRemaining { get; }
        public bool TorchExpiredThisStep { get; }
        public bool ShouldHalt => HaltReason != ExplorationHaltReason.None;
    }

    public sealed class ExplorationMoveResult
    {
        public ExplorationMoveResult(
            int movedSteps,
            GridCoord finalPosition,
            ExplorationHaltReason haltReason,
            IReadOnlyList<GridCoord> pathTaken,
            MapPortal? portal = null,
            MapPointOfInterest? pointOfInterest = null,
            EncounterZoneData? triggeredZone = null,
            int? triggeredBattleId = null,
            IReadOnlyList<ExplorationStepResult>? stepEvents = null)
        {
            MovedSteps = movedSteps;
            FinalPosition = finalPosition;
            HaltReason = haltReason;
            PathTaken = pathTaken ?? Array.Empty<GridCoord>();
            Portal = portal;
            PointOfInterest = pointOfInterest;
            TriggeredZone = triggeredZone;
            TriggeredBattleId = triggeredBattleId;
            StepEvents = stepEvents ?? Array.Empty<ExplorationStepResult>();
        }

        public int MovedSteps { get; }
        public GridCoord FinalPosition { get; }
        public ExplorationHaltReason HaltReason { get; }
        public IReadOnlyList<GridCoord> PathTaken { get; }
        public MapPortal? Portal { get; }
        public MapPointOfInterest? PointOfInterest { get; }
        public EncounterZoneData? TriggeredZone { get; }
        public int? TriggeredBattleId { get; }
        public IReadOnlyList<ExplorationStepResult> StepEvents { get; }
    }

    public sealed class ExplorationSession
    {
        private readonly Dictionary<string, TileMapData> _maps;
        private readonly IDiceRoller _roller;
        private readonly AStarPathfinder _pathfinder;
        private readonly HashSet<string> _clearedEncounterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ExplorationSession(
            IReadOnlyDictionary<string, TileMapData> maps,
            string initialMapId,
            PartyState party,
            IDiceRoller? roller = null,
            GridCoord? startPosition = null,
            string? initialFacing = null,
            int initialTurnCount = 0,
            IEnumerable<string>? clearedEncounterIds = null)
        {
            if (maps == null)
            {
                throw new ArgumentNullException(nameof(maps));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            _maps = new Dictionary<string, TileMapData>(maps, StringComparer.OrdinalIgnoreCase);
            if (!_maps.TryGetValue(initialMapId, out TileMapData? initialMap))
            {
                throw new KeyNotFoundException($"Map '{initialMapId}' not found.");
            }

            CurrentMap = initialMap;
            Party = party;
            _roller = roller ?? new DeterministicDiceRoller(0x5245414C4D5AUL);
            _pathfinder = new AStarPathfinder(
                Math.Max(90, initialMap.Width),
                Math.Max(90, initialMap.Height));
            Position = startPosition ?? initialMap.StartSpawn;
            Facing = string.IsNullOrWhiteSpace(initialFacing) ? "South" : initialFacing!;
            TurnCount = Math.Max(0, initialTurnCount);
            if (clearedEncounterIds != null)
            {
                foreach (string id in clearedEncounterIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _clearedEncounterIds.Add(id);
                    }
                }
            }
        }

        public IReadOnlyDictionary<string, TileMapData> Maps => _maps;
        public TileMapData CurrentMap { get; private set; }
        public PartyState Party { get; }
        public GridCoord Position { get; private set; }
        public string Facing { get; private set; }
        public int TurnCount { get; private set; }
        public IDiceRoller Roller => _roller;
        public IReadOnlyCollection<string> ClearedEncounterIds => _clearedEncounterIds;

        public static string GetEncounterZoneKey(string mapId, EncounterZoneData zone)
        {
            if (zone == null)
            {
                throw new ArgumentNullException(nameof(zone));
            }

            return $"{mapId}:zone:{zone.Slot}";
        }

        public void MarkEncounterCleared(string encounterId)
        {
            if (!string.IsNullOrWhiteSpace(encounterId))
            {
                _clearedEncounterIds.Add(encounterId);
            }
        }

        public bool IsEncounterCleared(string encounterId)
        {
            return !string.IsNullOrWhiteSpace(encounterId) && _clearedEncounterIds.Contains(encounterId);
        }

        public void RestorePersistenceState(int turnCount, IEnumerable<string>? clearedEncounterIds)
        {
            TurnCount = Math.Max(0, turnCount);
            _clearedEncounterIds.Clear();
            if (clearedEncounterIds != null)
            {
                foreach (string id in clearedEncounterIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _clearedEncounterIds.Add(id);
                    }
                }
            }
        }

        public PathResult PlanPath(GridCoord target, bool allowDiagonal = true)
        {
            if (target == Position)
            {
                return new PathResult(true, 0, Array.Empty<GridCoord>());
            }

            return _pathfinder.FindPathCore(
                CurrentMap,
                Position,
                target,
                allowDiagonal: allowDiagonal,
                hasBoat: Party.HasBoat);
        }

        public ExplorationStepResult StepOneTile(GridCoord nextTile, bool autoHaltOnEvents = true)
        {
            if (nextTile == Position)
            {
                return new ExplorationStepResult(
                    moved: false,
                    position: Position,
                    haltReason: ExplorationHaltReason.Arrived,
                    torchTurnsRemaining: Party.TorchTurnsRemaining);
            }

            if (!CurrentMap.InBounds(nextTile) ||
                Position.ChebyshevDistance(nextTile) != 1 ||
                !CurrentMap.IsPassable(nextTile.X, nextTile.Y, Party.HasBoat))
            {
                return new ExplorationStepResult(
                    moved: false,
                    position: Position,
                    haltReason: ExplorationHaltReason.Blocked,
                    torchTurnsRemaining: Party.TorchTurnsRemaining);
            }

            int dx = nextTile.X - Position.X;
            int dy = nextTile.Y - Position.Y;
            if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1 &&
                !CurrentMap.IsPassable(Position.X + dx, Position.Y, Party.HasBoat) &&
                !CurrentMap.IsPassable(Position.X, Position.Y + dy, Party.HasBoat))
            {
                return new ExplorationStepResult(
                    moved: false,
                    position: Position,
                    haltReason: ExplorationHaltReason.Blocked,
                    torchTurnsRemaining: Party.TorchTurnsRemaining);
            }

            UpdateFacing(Position, nextTile);
            Position = nextTile;
            TurnCount++;

            bool torchExpired = false;
            if (Party.TorchTurnsRemaining > 0)
            {
                Party.TorchTurnsRemaining--;
                torchExpired = Party.TorchTurnsRemaining == 0;
            }

            if (!autoHaltOnEvents)
            {
                return new ExplorationStepResult(
                    moved: true,
                    position: Position,
                    haltReason: ExplorationHaltReason.None,
                    torchTurnsRemaining: Party.TorchTurnsRemaining,
                    torchExpiredThisStep: torchExpired);
            }

            MapPortal? portal = CurrentMap.GetPortalAt(nextTile.X, nextTile.Y);
            if (portal != null)
            {
                return new ExplorationStepResult(
                    moved: true,
                    position: Position,
                    haltReason: ExplorationHaltReason.Portal,
                    portal: portal,
                    torchTurnsRemaining: Party.TorchTurnsRemaining,
                    torchExpiredThisStep: torchExpired);
            }

            MapPointOfInterest? poi = CurrentMap.GetPointOfInterestAt(nextTile.X, nextTile.Y);
            if (poi != null)
            {
                return new ExplorationStepResult(
                    moved: true,
                    position: Position,
                    haltReason: ExplorationHaltReason.PointOfInterest,
                    pointOfInterest: poi,
                    torchTurnsRemaining: Party.TorchTurnsRemaining,
                    torchExpiredThisStep: torchExpired);
            }

            IReadOnlyList<EncounterZoneData> zones = CurrentMap.GetZonesAt(nextTile.X, nextTile.Y);
            for (int z = 0; z < zones.Count; z++)
            {
                EncounterZoneData zone = zones[z];
                string encounterKey = GetEncounterZoneKey(CurrentMap.MapId, zone);
                if (zone.Percent <= 0 || _clearedEncounterIds.Contains(encounterKey))
                {
                    continue;
                }

                int roll = _roller.NextInt(1, 101);
                if (roll <= zone.Percent)
                {
                    int? battleId = null;
                    if (zone.BattleIds.Count > 0)
                    {
                        int pick = _roller.NextInt(0, zone.BattleIds.Count);
                        battleId = zone.BattleIds[pick];
                    }

                    return new ExplorationStepResult(
                        moved: true,
                        position: Position,
                        haltReason: ExplorationHaltReason.EncounterZone,
                        triggeredZone: zone,
                        triggeredBattleId: battleId,
                        torchTurnsRemaining: Party.TorchTurnsRemaining,
                        torchExpiredThisStep: torchExpired);
                }
            }

            return new ExplorationStepResult(
                moved: true,
                position: Position,
                haltReason: ExplorationHaltReason.None,
                torchTurnsRemaining: Party.TorchTurnsRemaining,
                torchExpiredThisStep: torchExpired);
        }

        public ExplorationMoveResult MoveAlongPath(GridCoord target, bool autoHaltOnEvents = true)
        {
            if (target == Position)
            {
                return new ExplorationMoveResult(
                    movedSteps: 0,
                    finalPosition: Position,
                    haltReason: ExplorationHaltReason.Arrived,
                    pathTaken: Array.Empty<GridCoord>());
            }

            PathResult path = PlanPath(target, allowDiagonal: true);

            if (!path.Found || path.Steps.Count == 0)
            {
                return new ExplorationMoveResult(
                    movedSteps: 0,
                    finalPosition: Position,
                    haltReason: ExplorationHaltReason.Blocked,
                    pathTaken: Array.Empty<GridCoord>());
            }

            List<GridCoord> taken = new List<GridCoord>(path.Steps.Count);
            List<ExplorationStepResult> stepEvents = new List<ExplorationStepResult>(path.Steps.Count);

            for (int i = 0; i < path.Steps.Count; i++)
            {
                ExplorationStepResult stepRes = StepOneTile(path.Steps[i], autoHaltOnEvents);
                if (!stepRes.Moved)
                {
                    return new ExplorationMoveResult(
                        movedSteps: taken.Count,
                        finalPosition: Position,
                        haltReason: stepRes.HaltReason == ExplorationHaltReason.None
                            ? ExplorationHaltReason.Blocked
                            : stepRes.HaltReason,
                        pathTaken: taken,
                        stepEvents: stepEvents);
                }

                taken.Add(stepRes.Position);
                stepEvents.Add(stepRes);

                if (stepRes.ShouldHalt)
                {
                    return new ExplorationMoveResult(
                        movedSteps: taken.Count,
                        finalPosition: Position,
                        haltReason: stepRes.HaltReason,
                        pathTaken: taken,
                        portal: stepRes.Portal,
                        pointOfInterest: stepRes.PointOfInterest,
                        triggeredZone: stepRes.TriggeredZone,
                        triggeredBattleId: stepRes.TriggeredBattleId,
                        stepEvents: stepEvents);
                }
            }

            return new ExplorationMoveResult(
                movedSteps: taken.Count,
                finalPosition: Position,
                haltReason: ExplorationHaltReason.Arrived,
                pathTaken: taken,
                stepEvents: stepEvents);
        }

        public bool UsePortalAtCurrentTile()
        {
            MapPortal? portal = CurrentMap.GetPortalAt(Position.X, Position.Y);
            if (portal == null)
            {
                return false;
            }

            if (!_maps.TryGetValue(portal.DestMapId, out TileMapData? destMap))
            {
                return false;
            }

            CurrentMap = destMap;
            Position = portal.DestCoord;
            if (!string.IsNullOrWhiteSpace(portal.DestFacing))
            {
                Facing = portal.DestFacing;
            }

            return true;
        }

        public void SyncAfterCombat(TacticalCombatSession combat, bool floorDeadToOne = false)
        {
            if (combat == null)
            {
                throw new ArgumentNullException(nameof(combat));
            }

            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState member = Party.Members[i];
                CombatantState? unit = combat.GetCombatant(member.Id);
                if (unit == null)
                {
                    continue;
                }

                member.Hp = floorDeadToOne
                    ? Math.Max(1, Math.Min(member.MaxHp, unit.Hp))
                    : Math.Clamp(unit.Hp, 0, member.MaxHp);
                member.Sp = Math.Clamp(unit.Sp, 0, member.MaxSp);
                member.ExhaustionStacks = unit.ExhaustionStacks;
                member.CurseStacks = unit.CurseStacks;
            }

            if (combat.Outcome == CombatOutcome.Victory)
            {
                combat.GrantVictoryRewardsToParty(Party);
            }
        }

        public bool TriggerTempleHeal(int goldCost = 25)
        {
            if (goldCost < 0 || Party.Gold < goldCost)
            {
                return false;
            }

            Party.Gold -= goldCost;

            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState m = Party.Members[i];
                m.Hp = m.MaxHp;
                m.ExhaustionStacks = 0;
                m.CurseStacks = 0;
                m.Conditions.Remove(CharConditionId.Poisoned);
                m.Conditions.Remove(CharConditionId.Diseased);
                m.Conditions.Remove(CharConditionId.Cursed);
                m.Conditions.Remove(CharConditionId.Blind);
                m.Conditions.Remove(CharConditionId.Helpless);
                m.Conditions.Remove(CharConditionId.TurnedToStone);
                m.Conditions.Remove(CharConditionId.Confused);
                m.Conditions.Remove(CharConditionId.Slow);
                m.Conditions.Remove(CharConditionId.Tangled);
                m.Conditions.Remove(CharConditionId.Silenced);
            }

            return true;
        }

        private void UpdateFacing(GridCoord from, GridCoord to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (dx > 0) Facing = "East";
                else if (dx < 0) Facing = "West";
            }
            else
            {
                if (dy > 0) Facing = "South";
                else if (dy < 0) Facing = "North";
            }
        }
    }
}
