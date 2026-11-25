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
        public const int DefaultPostCombatGraceSteps = 10;
        public const int DefaultStealthEvadeTurns = 3;
        public const int DefaultStealthEvadeGraceSteps = 6;

        private readonly Dictionary<string, TileMapData> _maps;
        private readonly Dictionary<string, FogOfWarMap> _fogMaps;
        private readonly IDiceRoller _roller;
        private readonly AStarPathfinder _pathfinder;
        private readonly HashSet<string> _clearedEncounterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _zoneCooldownUntilTurn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public ExplorationSession(
            IReadOnlyDictionary<string, TileMapData> maps,
            string initialMapId,
            PartyState party,
            IDiceRoller? roller = null,
            GridCoord? startPosition = null,
            string? initialFacing = null,
            int initialTurnCount = 0,
            IEnumerable<string>? clearedEncounterIds = null,
            int initialWorldClockTurns = -1,
            int initialPostCombatGraceSteps = 0,
            IEnumerable<KeyValuePair<string, int>>? initialZoneCooldowns = null,
            WeatherState initialWeather = WeatherState.Clear,
            BalanceOverrides? balanceOverrides = null,
            double initialWorldClockMinutes = -1.0)
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
            if (initialWorldClockMinutes >= 0.0)
            {
                WorldClockMinutes = initialWorldClockMinutes;
                WorldClockTurns = initialWorldClockTurns >= 0
                    ? initialWorldClockTurns
                    : (int)Math.Floor(initialWorldClockMinutes / WorldClock.MinutesPerStep);
            }
            else if (initialWorldClockTurns >= 0)
            {
                WorldClockTurns = initialWorldClockTurns;
                WorldClockMinutes = WorldClock.LegacyTurnsToMinutes(initialWorldClockTurns);
            }
            else
            {
                WorldClockTurns = WorldClock.DefaultStartTurns + TurnCount;
                WorldClockMinutes = WorldClock.DefaultStartMinutes + (TurnCount * WorldClock.MinutesPerStep);
            }
            PostCombatGraceStepsRemaining = Math.Max(0, initialPostCombatGraceSteps);
            Weather = initialWeather;
            BalanceOverrides = balanceOverrides ?? BalanceOverrides.CreateDefault();

            _fogMaps = new Dictionary<string, FogOfWarMap>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, TileMapData> kvp in _maps)
            {
                string mapKey = string.IsNullOrWhiteSpace(kvp.Value.MapId) ? kvp.Key : kvp.Value.MapId;
                _fogMaps[kvp.Key] = new FogOfWarMap(mapKey, kvp.Value.Width, kvp.Value.Height);
            }

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

            if (initialZoneCooldowns != null)
            {
                foreach (KeyValuePair<string, int> kvp in initialZoneCooldowns)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > WorldClockTurns)
                    {
                        _zoneCooldownUntilTurn[kvp.Key] = kvp.Value;
                    }
                }
            }

            RefreshFogOfWar();
        }

        public IReadOnlyDictionary<string, TileMapData> Maps => _maps;
        public TileMapData CurrentMap { get; private set; }
        public PartyState Party { get; }
        public GridCoord Position { get; private set; }
        public string Facing { get; private set; }
        public int TurnCount { get; private set; }
        public int WorldClockTurns { get; private set; }
        public double WorldClockMinutes { get; private set; }
        public WorldClock CurrentWorldClock => WorldClock.FromMinutes(WorldClockMinutes);
        public int PostCombatGraceStepsRemaining { get; private set; }
        public IDiceRoller Roller => _roller;
        public IReadOnlyCollection<string> ClearedEncounterIds => _clearedEncounterIds;
        public IReadOnlyDictionary<string, int> ZoneCooldownUntilTurns => _zoneCooldownUntilTurn;
        public FogOfWarMap FogOfWar => GetFogMap(CurrentMap.MapId);
        public IReadOnlyDictionary<string, FogOfWarMap> FogMaps => _fogMaps;
        public int CurrentVisionRadius => FogOfWar.CurrentVisionRadius;
        public WeatherState Weather { get; private set; }
        public BalanceOverrides BalanceOverrides { get; }

        public FogOfWarMap GetFogMap(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("Map ID cannot be null or whitespace.", nameof(mapId));
            }

            if (_fogMaps.TryGetValue(mapId, out FogOfWarMap? existing))
            {
                return existing;
            }

            if (_maps.TryGetValue(mapId, out TileMapData? mapData))
            {
                string resolvedId = string.IsNullOrWhiteSpace(mapData.MapId) ? mapId : mapData.MapId;
                FogOfWarMap created = new FogOfWarMap(resolvedId, mapData.Width, mapData.Height);
                _fogMaps[mapId] = created;
                return created;
            }

            throw new KeyNotFoundException($"Map '{mapId}' not found.");
        }

        public void SetWeather(WeatherState weather)
        {
            Weather = weather;
            RefreshFogOfWar();
        }

        public WeatherState RollWeatherShift()
        {
            int roll = _roller.NextInt(1, 101);
            WeatherState next;
            if (roll <= 65)
            {
                next = WeatherState.Clear;
            }
            else if (roll <= 90)
            {
                next = WeatherState.LightRain;
            }
            else
            {
                next = WeatherState.Storm;
            }

            Weather = next;
            return next;
        }

        public int ComputeVisibilityRadius()
        {
            VisionRadiusConfig radii = BalanceOverrides.Survival.VisionRadii;
            bool hasLitTorch = Party.TorchTurnsRemaining > 0;
            if (CurrentMap.IsDungeon)
            {
                return hasLitTorch ? radii.DungeonLitTorch : radii.DungeonUnlit;
            }

            WorldTimePhase phase = CurrentWorldClock.Phase;
            if (phase == WorldTimePhase.Night)
            {
                return hasLitTorch ? radii.NightLitTorch : radii.NightUnlit;
            }

            if (phase == WorldTimePhase.Dusk)
            {
                return hasLitTorch ? radii.DuskLitTorch : radii.DuskUnlit;
            }

            int radius = radii.DayBase;
            if (HasConsciousScout())
            {
                radius += radii.DayScoutBonus;
            }

            return radius;
        }

        public void RefreshFogOfWar()
        {
            int radius = ComputeVisibilityRadius();
            GetFogMap(CurrentMap.MapId).RecomputeVisibility(CurrentMap, Position, radius);
        }

        public bool TryLightTorchFromStash(
            out string feedbackMessage,
            IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (!Party.TryLightTorchFromStash(out feedbackMessage, catalog))
            {
                return false;
            }

            int configuredDuration = Math.Max(1, BalanceOverrides.Survival.TorchTurnDuration);
            if (configuredDuration != 30)
            {
                Party.TorchTurnsRemaining = Math.Max(1, Party.TorchTurnsRemaining + (configuredDuration - 30));
                feedbackMessage = $"Lit a Torch (+{configuredDuration} turns; {Party.TorchTurnsRemaining} active).";
            }

            RefreshFogOfWar();
            return true;
        }

        public void ExtinguishTorch()
        {
            Party.TorchTurnsRemaining = 0;
            RefreshFogOfWar();
        }

        public bool BuyFoodRations(int count = 1)
        {
            if (count <= 0)
            {
                return false;
            }

            int unitPrice = Math.Max(1, BalanceOverrides.Survival.RationPrice);
            long totalCost = (long)unitPrice * count;
            if (totalCost > Party.Gold)
            {
                return false;
            }

            Party.Gold -= (int)totalCost;
            Party.AddFoodRations(count);
            return true;
        }

        private bool HasConsciousScout()
        {
            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState member = Party.Members[i];
                if (!member.IsAlive ||
                    member.Conditions.Has(CharConditionId.Helpless) ||
                    member.Conditions.Has(CharConditionId.TurnedToStone))
                {
                    continue;
                }

                string arch = member.Archetype ?? string.Empty;
                string name = member.Name ?? string.Empty;
                string id = member.Id ?? string.Empty;
                if (arch.IndexOf("Ranger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    arch.IndexOf("Rogue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    arch.IndexOf("Archer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    arch.IndexOf("Thief", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Kaelen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Vex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Ranger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Rogue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Archer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Thief", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

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

        public bool IsEncounterZoneOnCooldown(string mapId, EncounterZoneData zone)
        {
            if (zone == null)
            {
                return false;
            }

            string key = GetEncounterZoneKey(mapId, zone);
            return _zoneCooldownUntilTurn.TryGetValue(key, out int untilTurn) && WorldClockTurns < untilTurn;
        }

        public void CompleteEncounterZone(EncounterZoneData? zone, int graceSteps = DefaultPostCombatGraceSteps)
        {
            GrantPostCombatGrace(graceSteps);

            if (zone == null)
            {
                return;
            }

            string key = GetEncounterZoneKey(CurrentMap.MapId, zone);
            if (EncounterDirector.IsLandmarkZone(CurrentMap, zone))
            {
                _clearedEncounterIds.Add(key);
                _zoneCooldownUntilTurn.Remove(key);
            }
            else
            {
                _zoneCooldownUntilTurn[key] = WorldClockTurns + Math.Max(1, CurrentWorldClock.TurnsUntilNextPhase);
            }
        }

        public void GrantPostCombatGrace(int graceSteps = DefaultPostCombatGraceSteps)
        {
            PostCombatGraceStepsRemaining = Math.Max(PostCombatGraceStepsRemaining, Math.Max(0, graceSteps));
        }

        public void ApplyStealthEvade(
            int turnsCost = DefaultStealthEvadeTurns,
            int graceSteps = DefaultStealthEvadeGraceSteps)
        {
            WorldTimePhase prevPhase = CurrentWorldClock.Phase;
            int addedTurns = Math.Max(0, turnsCost);
            TurnCount += addedTurns;
            WorldClockTurns += addedTurns;
            WorldClockMinutes += addedTurns * 5.0;
            PruneExpiredCooldowns(CurrentWorldClock.Phase != prevPhase);
            GrantPostCombatGrace(graceSteps);
            RefreshFogOfWar();
        }

        public bool AdvanceWorldClockMinutes(double deltaMinutes)
        {
            if (deltaMinutes <= 0.0 || double.IsNaN(deltaMinutes) || double.IsInfinity(deltaMinutes))
            {
                return false;
            }

            WorldTimePhase prevPhase = CurrentWorldClock.Phase;
            WorldClockMinutes += deltaMinutes;
            if (CurrentWorldClock.Phase != prevPhase)
            {
                PruneExpiredCooldowns(phaseChanged: true);
                RefreshFogOfWar();
                return true;
            }

            return false;
        }

        public void AdvanceWorldClockTurns(int turns)
        {
            if (turns <= 0)
            {
                return;
            }

            WorldTimePhase prevPhase = CurrentWorldClock.Phase;
            WorldClockTurns += turns;
            WorldClockMinutes += turns * 20.0;
            PruneExpiredCooldowns(CurrentWorldClock.Phase != prevPhase);
            RefreshFogOfWar();
        }

        public void RestorePersistenceState(
            int turnCount,
            IEnumerable<string>? clearedEncounterIds,
            int worldClockTurns = -1,
            int postCombatGraceSteps = 0,
            IEnumerable<KeyValuePair<string, int>>? zoneCooldowns = null,
            WeatherState? weather = null,
            double worldClockMinutes = -1.0)
        {
            TurnCount = Math.Max(0, turnCount);
            if (worldClockMinutes >= 0.0)
            {
                WorldClockMinutes = worldClockMinutes;
                WorldClockTurns = worldClockTurns >= 0
                    ? worldClockTurns
                    : (int)Math.Floor(worldClockMinutes / WorldClock.MinutesPerStep);
            }
            else if (worldClockTurns >= 0)
            {
                WorldClockTurns = worldClockTurns;
                WorldClockMinutes = WorldClock.LegacyTurnsToMinutes(worldClockTurns);
            }
            else
            {
                WorldClockTurns = WorldClock.DefaultStartTurns + TurnCount;
                WorldClockMinutes = WorldClock.DefaultStartMinutes + (TurnCount * WorldClock.MinutesPerStep);
            }
            PostCombatGraceStepsRemaining = Math.Max(0, postCombatGraceSteps);
            if (weather.HasValue)
            {
                Weather = weather.Value;
            }

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

            _zoneCooldownUntilTurn.Clear();
            if (zoneCooldowns != null)
            {
                foreach (KeyValuePair<string, int> kvp in zoneCooldowns)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > WorldClockTurns)
                    {
                        _zoneCooldownUntilTurn[kvp.Key] = kvp.Value;
                    }
                }
            }

            RefreshFogOfWar();
        }

        private void PruneExpiredCooldowns(bool phaseChanged = false)
        {
            if (_zoneCooldownUntilTurn.Count == 0)
            {
                return;
            }

            if (phaseChanged)
            {
                _zoneCooldownUntilTurn.Clear();
                return;
            }

            List<string>? expired = null;
            foreach (KeyValuePair<string, int> kvp in _zoneCooldownUntilTurn)
            {
                if (WorldClockTurns >= kvp.Value)
                {
                    expired ??= new List<string>();
                    expired.Add(kvp.Key);
                }
            }

            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    _zoneCooldownUntilTurn.Remove(expired[i]);
                }
            }
        }

        public void SetBlockedPropFootprints(IEnumerable<GridCoord>? coords)
        {
            CurrentMap.SetBlockedPropFootprints(coords);
        }

        public void SetBlockedPropFootprints(string mapId, IEnumerable<GridCoord>? coords)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("Map ID cannot be null or whitespace.", nameof(mapId));
            }

            if (!_maps.TryGetValue(mapId, out TileMapData? targetMap))
            {
                throw new KeyNotFoundException($"Map '{mapId}' not found.");
            }

            targetMap.SetBlockedPropFootprints(coords);
        }

        public void ClearBlockedPropFootprints()
        {
            CurrentMap.ClearBlockedPropFootprints();
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

            WorldTimePhase prevPhase = CurrentWorldClock.Phase;
            UpdateFacing(Position, nextTile);
            Position = nextTile;
            TurnCount++;
            WorldClockTurns++;
            WorldClockMinutes += WorldClock.MinutesPerStep;
            PruneExpiredCooldowns(CurrentWorldClock.Phase != prevPhase);

            bool inGraceCooldown = PostCombatGraceStepsRemaining > 0;
            if (inGraceCooldown)
            {
                PostCombatGraceStepsRemaining--;
            }

            bool torchExpired = false;
            if (Party.TorchTurnsRemaining > 0)
            {
                Party.TorchTurnsRemaining--;
                torchExpired = Party.TorchTurnsRemaining == 0;
            }

            RefreshFogOfWar();

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

            if (inGraceCooldown || EncounterDirector.IsZoneInBywaterTownLimits(CurrentMap, nextTile))
            {
                return new ExplorationStepResult(
                    moved: true,
                    position: Position,
                    haltReason: ExplorationHaltReason.None,
                    torchTurnsRemaining: Party.TorchTurnsRemaining,
                    torchExpiredThisStep: torchExpired);
            }

            EncounterGeographyInfo geo = EncounterDirector.EvaluateGeography(CurrentMap, nextTile, IsInSafeZone());
            if (!geo.IsTownSafeZone)
            {
                IReadOnlyList<EncounterZoneData> zones = CurrentMap.GetZonesAt(nextTile.X, nextTile.Y);
                for (int z = 0; z < zones.Count; z++)
                {
                    EncounterZoneData zone = zones[z];
                    if (!IsHostileZone(zone))
                    {
                        continue;
                    }

                    int effectiveBasisPoints = EncounterDirector.GetEffectiveBasisPoints(
                        zone,
                        geo,
                        CurrentWorldClock,
                        CurrentMap.IsDungeon);
                    if (effectiveBasisPoints <= 0)
                    {
                        continue;
                    }

                    int roll = _roller.NextInt(1, EncounterDirector.MaxBasisPoints + 1);
                    if (roll <= effectiveBasisPoints)
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

            if (!CurrentMap.IsDungeon && IsInSafeZone())
            {
                Party.QuickBandageCharges = PartyState.MaxQuickBandageCharges;
            }

            RefreshFogOfWar();
            return true;
        }

        private bool IsBaseHostileZone(EncounterZoneData zone)
        {
            if (zone == null || zone.Percent <= 0)
            {
                return false;
            }

            if (!CurrentMap.IsDungeon &&
                string.Equals(CurrentMap.MapId, "land_0", StringComparison.OrdinalIgnoreCase) &&
                CurrentMap.Width >= 90 &&
                (zone.Slot == 0 || zone.Slot == 8))
            {
                return false;
            }

            if (zone.MonsterIndices.Count > 0)
            {
                return true;
            }

            for (int i = 0; i < zone.BattleIds.Count; i++)
            {
                if (zone.BattleIds[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsHostileZone(EncounterZoneData zone)
        {
            if (!IsBaseHostileZone(zone))
            {
                return false;
            }

            string key = GetEncounterZoneKey(CurrentMap.MapId, zone);
            if (_clearedEncounterIds.Contains(key))
            {
                return false;
            }

            if (IsEncounterZoneOnCooldown(CurrentMap.MapId, zone))
            {
                return false;
            }

            return true;
        }

        public EncounterZoneData? GetUnclearedHostileZoneAtCurrentTile()
        {
            IReadOnlyList<EncounterZoneData> zones = CurrentMap.GetZonesAt(Position.X, Position.Y);
            for (int i = 0; i < zones.Count; i++)
            {
                if (IsHostileZone(zones[i]))
                {
                    return zones[i];
                }
            }
            return null;
        }

        public bool IsInUnclearedHostileZone()
        {
            return GetUnclearedHostileZoneAtCurrentTile() != null;
        }

        public bool IsInSafeZone()
        {
            if (CurrentMap.IsDungeon || IsInUnclearedHostileZone())
            {
                return false;
            }

            IReadOnlyList<EncounterZoneData> zonesHere = CurrentMap.GetZonesAt(Position.X, Position.Y);
            for (int i = 0; i < zonesHere.Count; i++)
            {
                if (IsBaseHostileZone(zonesHere[i]) && !_clearedEncounterIds.Contains(GetEncounterZoneKey(CurrentMap.MapId, zonesHere[i])))
                {
                    return false;
                }
            }

            if (!string.Equals(CurrentMap.MapId, "land_0", StringComparison.OrdinalIgnoreCase))
            {
                return CurrentMap.EncounterZones.Count == 0;
            }

            // Bywater Town Safe Zone covers the gated town compound and town POIs (outside Zone 3 haunted quarters).
            if (Position.X <= 11 && Position.Y <= 22)
            {
                return true;
            }

            if (CurrentMap.EncounterZones.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < CurrentMap.PointsOfInterest.Count; i++)
            {
                MapPointOfInterest poi = CurrentMap.PointsOfInterest[i];
                if (Position.ChebyshevDistance(poi.Coord) <= 6)
                {
                    return true;
                }
            }

            return false;
        }

        public EncounterGeographyInfo GetCurrentGeography()
        {
            return EncounterDirector.EvaluateGeography(CurrentMap, Position, IsInSafeZone());
        }

        public EncounterThreatAssessment GetCurrentThreatAssessment()
        {
            IReadOnlyList<EncounterZoneData> zones = CurrentMap.GetZonesAt(Position.X, Position.Y);
            return EncounterDirector.ComputeThreatAssessment(
                CurrentMap,
                Position,
                Party,
                CurrentWorldClock,
                IsInSafeZone(),
                PostCombatGraceStepsRemaining,
                zones,
                IsHostileZone);
        }

        public EncounterScoutingReport CreateScoutingReport(
            EncounterZoneData? zone,
            IReadOnlyList<MonsterDefinition> monsters)
        {
            string zoneKey = zone != null ? GetEncounterZoneKey(CurrentMap.MapId, zone) : string.Empty;
            bool isLandmark = EncounterDirector.IsLandmarkZone(CurrentMap, zone);
            return EncounterDirector.EvaluateScouting(
                CurrentMap,
                Position,
                Party,
                CurrentWorldClock,
                zone,
                zoneKey,
                isLandmark,
                monsters,
                _roller,
                IsInSafeZone());
        }

        public RestResult TryCampRest(
            CampMode mode,
            out EncounterZoneData? ambushZone,
            out int ambushBattleId)
        {
            ambushZone = null;
            ambushBattleId = 0;

            bool isSafeZone = IsInSafeZone();
            EncounterZoneData? hostileZone = GetUnclearedHostileZoneAtCurrentTile();
            bool inHostile = hostileZone != null;

            WorldTimePhase prevPhase = CurrentWorldClock.Phase;
            RestResult result = Party.Rest(mode, isSafeZone, inHostile, _roller);
            if (result.TurnsElapsed > 0)
            {
                TurnCount += result.TurnsElapsed;
                WorldClockTurns += result.TurnsElapsed;
                if (mode == CampMode.FullRest)
                {
                    WorldClockMinutes += result.Success
                        ? WorldClock.FullRestMinutes
                        : (result.Ambushed || result.TurnsElapsed == 4 ? 240.0 : result.TurnsElapsed * 60.0);
                }
                else
                {
                    WorldClockMinutes += result.TurnsElapsed == 1 ? 15.0 : result.TurnsElapsed * 15.0;
                }

                PruneExpiredCooldowns(CurrentWorldClock.Phase != prevPhase);
            }

            if (result.Ambushed && hostileZone != null)
            {
                ambushZone = hostileZone;
                if (hostileZone.BattleIds.Count == 1)
                {
                    ambushBattleId = hostileZone.BattleIds[0];
                }
                else if (hostileZone.BattleIds.Count > 1)
                {
                    ambushBattleId = hostileZone.BattleIds[_roller.NextInt(0, hostileZone.BattleIds.Count)];
                }
            }

            RefreshFogOfWar();
            return result;
        }

        public void SyncAfterCombat(TacticalCombatSession combat, bool floorDeadToOne = false)
        {
            if (combat == null)
            {
                throw new ArgumentNullException(nameof(combat));
            }

            GrantPostCombatGrace();

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

                SyncPersistentCondition(unit.Conditions, member.Conditions, CharConditionId.Poisoned);
                SyncPersistentCondition(unit.Conditions, member.Conditions, CharConditionId.Diseased);
                SyncPersistentCondition(unit.Conditions, member.Conditions, CharConditionId.Cursed);
            }

            if (combat.Outcome == CombatOutcome.Victory)
            {
                combat.GrantVictoryRewardsToParty(Party);
            }
        }

        private static void SyncPersistentCondition(
            ConditionTracker source,
            ConditionTracker target,
            CharConditionId conditionId)
        {
            if (source.Has(conditionId))
            {
                target.Apply(conditionId);
            }
            else
            {
                target.Remove(conditionId);
            }
        }

        private static readonly CharConditionId[] DetrimentalTempleConditions =
        {
            CharConditionId.Cursed,
            CharConditionId.Poisoned,
            CharConditionId.RunsAway,
            CharConditionId.Helpless,
            CharConditionId.Tangled,
            CharConditionId.Stupid,
            CharConditionId.Slow,
            CharConditionId.TurnedToStone,
            CharConditionId.Blind,
            CharConditionId.Diseased,
            CharConditionId.Confused,
            CharConditionId.EnergyDrain,
            CharConditionId.HinderedAttacks,
            CharConditionId.HinderedDefense,
            CharConditionId.Silenced
        };

        public bool TriggerTempleHeal(int goldCost = 25)
        {
            if (goldCost < 0 || Party.Gold < goldCost)
            {
                return false;
            }

            Party.Gold -= goldCost;
            Party.QuickBandageCharges = PartyState.MaxQuickBandageCharges;

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

        public bool TriggerTempleCureCurse(int goldCost = 20)
        {
            if (goldCost < 0 || Party.Gold < goldCost)
            {
                return false;
            }

            bool anyNeedsCure = false;
            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState m = Party.Members[i];
                if (m.Hp > 0 && (m.CurseStacks > 0 || m.ExhaustionStacks > 0 || m.Conditions.Count > 0))
                {
                    anyNeedsCure = true;
                    break;
                }
            }

            if (!anyNeedsCure)
            {
                return false;
            }

            Party.Gold -= goldCost;
            Party.QuickBandageCharges = PartyState.MaxQuickBandageCharges;

            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState m = Party.Members[i];
                if (m.Hp <= 0)
                {
                    continue;
                }

                m.ExhaustionStacks = 0;
                m.CurseStacks = 0;
                for (int c = 0; c < DetrimentalTempleConditions.Length; c++)
                {
                    m.Conditions.Remove(DetrimentalTempleConditions[c]);
                }
            }

            return true;
        }

        public int TriggerTempleRechargeItems(IReadOnlyDictionary<int, ItemDefinition> items, int goldCost = 30)
        {
            if (items == null)
            {
                return 0;
            }

            if (goldCost < 0)
            {
                return -1;
            }

            List<(InventoryItem Item, int MaxCharges)> eligible = new List<(InventoryItem, int)>();

            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMemberState m = Party.Members[i];
                foreach (InventoryItem equipped in m.Equipped.Values)
                {
                    if (items.TryGetValue(equipped.ItemId, out ItemDefinition? def) &&
                        def.Charges > 0 &&
                        equipped.RemainingCharges < def.Charges)
                    {
                        eligible.Add((equipped, def.Charges));
                    }
                }
            }

            for (int i = 0; i < Party.Stash.Count; i++)
            {
                InventoryItem stashItem = Party.Stash[i];
                if (items.TryGetValue(stashItem.ItemId, out ItemDefinition? def) &&
                    def.Charges > 0 &&
                    stashItem.RemainingCharges < def.Charges)
                {
                    eligible.Add((stashItem, def.Charges));
                }
            }

            if (eligible.Count == 0)
            {
                return 0;
            }

            if (Party.Gold < goldCost)
            {
                return -1;
            }

            Party.Gold -= goldCost;
            for (int i = 0; i < eligible.Count; i++)
            {
                eligible[i].Item.RemainingCharges = eligible[i].MaxCharges;
            }

            return eligible.Count;
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
