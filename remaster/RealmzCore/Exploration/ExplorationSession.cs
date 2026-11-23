using System;
using System.Collections.Generic;
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
            int? triggeredBattleId = null)
        {
            MovedSteps = movedSteps;
            FinalPosition = finalPosition;
            HaltReason = haltReason;
            PathTaken = pathTaken ?? Array.Empty<GridCoord>();
            Portal = portal;
            PointOfInterest = pointOfInterest;
            TriggeredZone = triggeredZone;
            TriggeredBattleId = triggeredBattleId;
        }

        public int MovedSteps { get; }
        public GridCoord FinalPosition { get; }
        public ExplorationHaltReason HaltReason { get; }
        public IReadOnlyList<GridCoord> PathTaken { get; }
        public MapPortal? Portal { get; }
        public MapPointOfInterest? PointOfInterest { get; }
        public EncounterZoneData? TriggeredZone { get; }
        public int? TriggeredBattleId { get; }
    }

    public sealed class ExplorationSession
    {
        private readonly Dictionary<string, TileMapData> _maps;
        private readonly IDiceRoller _roller;

        public ExplorationSession(
            IReadOnlyDictionary<string, TileMapData> maps,
            string initialMapId,
            PartyState party,
            IDiceRoller? roller = null,
            GridCoord? startPosition = null)
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
            Position = startPosition ?? initialMap.StartSpawn;
            Facing = "South";
        }

        public IReadOnlyDictionary<string, TileMapData> Maps => _maps;
        public TileMapData CurrentMap { get; private set; }
        public PartyState Party { get; }
        public GridCoord Position { get; private set; }
        public string Facing { get; private set; }
        public int TurnCount { get; private set; }

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

            PathResult path = AStarPathfinder.FindPath(
                CurrentMap,
                Position,
                target,
                allowDiagonal: true,
                hasBoat: Party.HasBoat);

            if (!path.Found || path.Steps.Count == 0)
            {
                return new ExplorationMoveResult(
                    movedSteps: 0,
                    finalPosition: Position,
                    haltReason: ExplorationHaltReason.Blocked,
                    pathTaken: Array.Empty<GridCoord>());
            }

            List<GridCoord> taken = new List<GridCoord>(path.Steps.Count);

            for (int i = 0; i < path.Steps.Count; i++)
            {
                GridCoord step = path.Steps[i];
                UpdateFacing(Position, step);
                Position = step;
                taken.Add(step);
                TurnCount++;

                if (Party.TorchTurnsRemaining > 0)
                {
                    Party.TorchTurnsRemaining--;
                }

                if (!autoHaltOnEvents)
                {
                    continue;
                }

                MapPortal? portal = CurrentMap.GetPortalAt(step.X, step.Y);
                if (portal != null)
                {
                    return new ExplorationMoveResult(
                        movedSteps: taken.Count,
                        finalPosition: Position,
                        haltReason: ExplorationHaltReason.Portal,
                        pathTaken: taken,
                        portal: portal);
                }

                MapPointOfInterest? poi = CurrentMap.GetPointOfInterestAt(step.X, step.Y);
                if (poi != null)
                {
                    return new ExplorationMoveResult(
                        movedSteps: taken.Count,
                        finalPosition: Position,
                        haltReason: ExplorationHaltReason.PointOfInterest,
                        pathTaken: taken,
                        pointOfInterest: poi);
                }

                IReadOnlyList<EncounterZoneData> zones = CurrentMap.GetZonesAt(step.X, step.Y);
                for (int z = 0; z < zones.Count; z++)
                {
                    EncounterZoneData zone = zones[z];
                    if (zone.Percent <= 0)
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

                        return new ExplorationMoveResult(
                            movedSteps: taken.Count,
                            finalPosition: Position,
                            haltReason: ExplorationHaltReason.EncounterZone,
                            pathTaken: taken,
                            triggeredZone: zone,
                            triggeredBattleId: battleId);
                    }
                }
            }

            return new ExplorationMoveResult(
                movedSteps: taken.Count,
                finalPosition: Position,
                haltReason: ExplorationHaltReason.Arrived,
                pathTaken: taken);
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
