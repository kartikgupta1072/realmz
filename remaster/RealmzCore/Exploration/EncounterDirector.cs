using System;
using System.Collections.Generic;
using System.Globalization;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Rules;

namespace RealmzCore.Exploration
{
    public enum WorldTimePhase
    {
        Dawn = 0,
        Day = 1,
        Dusk = 2,
        Night = 3
    }

    public enum EncounterBiomeKind
    {
        TownSafe = 0,
        Road = 1,
        Riverbank = 2,
        DeepForest = 3,
        Ruins = 4,
        DungeonApproach = 5,
        WildMeadow = 6,
        Dungeon = 7
    }

    public enum EncounterThreatTier
    {
        Safe = 0,
        Low = 1,
        Moderate = 2,
        High = 3,
        DarkDanger = 4
    }

    public readonly struct WorldClock : IEquatable<WorldClock>
    {
        public const int MinutesPerDay = 1440;
        public const int MinutesPerStep = 2;
        public const int FullRestMinutes = 480;
        public const double DefaultStartMinutes = 480.0; // Day 1 - 08:00 AM (Morning)

        // Legacy 72-turn constants preserved for backwards source compatibility.
        public const int TurnsPerPhase = 18;
        public const int PhasesPerDay = 4;
        public const int TurnsPerDay = TurnsPerPhase * PhasesPerDay; // 72 turns per full day
        public const int DefaultStartTurns = TurnsPerPhase; // 18 -> maps to 08:00 AM Morning

        public WorldClock(double totalMinutes)
        {
            TotalMinutes = double.IsNaN(totalMinutes) || double.IsInfinity(totalMinutes)
                ? 0.0
                : Math.Max(0.0, totalMinutes);
        }

        public WorldClock(int totalTurns)
        {
            TotalMinutes = LegacyTurnsToMinutes(totalTurns);
        }

        public double TotalMinutes { get; }
        public double TimeOfDayMinutes => TotalMinutes % MinutesPerDay;
        public float NormalizedTimeOfDay => (float)(TimeOfDayMinutes / MinutesPerDay);
        public int DayNumber => ((int)Math.Floor(TotalMinutes) / MinutesPerDay) + 1;
        public int Hour24 => ((int)Math.Floor(TimeOfDayMinutes) / 60) % 24;
        public int MinuteOfHour => (int)Math.Floor(TimeOfDayMinutes) % 60;
        public int TotalTurns => (int)Math.Floor(TotalMinutes / MinutesPerStep);

        public WorldTimePhase Phase
        {
            get
            {
                double m = TimeOfDayMinutes;
                if (m >= 330.0 && m < 450.0) return WorldTimePhase.Dawn;
                if (m >= 450.0 && m < 1050.0) return WorldTimePhase.Day;
                if (m >= 1050.0 && m < 1170.0) return WorldTimePhase.Dusk;
                return WorldTimePhase.Night;
            }
        }

        public int PhaseIndex => (int)Phase;
        public bool IsNight => Phase == WorldTimePhase.Night;

        public float PhaseProgress01
        {
            get
            {
                double m = TimeOfDayMinutes;
                if (m >= 330.0 && m < 450.0)
                {
                    return (float)((m - 330.0) / 120.0);
                }

                if (m >= 450.0 && m < 1050.0)
                {
                    return (float)((m - 450.0) / 600.0);
                }

                if (m >= 1050.0 && m < 1170.0)
                {
                    return (float)((m - 1050.0) / 120.0);
                }

                double nightElapsed = m >= 1170.0 ? m - 1170.0 : m + 270.0;
                return (float)(nightElapsed / 600.0);
            }
        }

        public int TurnInPhase
        {
            get
            {
                double m = TimeOfDayMinutes;
                double elapsed;
                if (m >= 330.0 && m < 450.0) elapsed = m - 330.0;
                else if (m >= 450.0 && m < 1050.0) elapsed = m - 450.0;
                else if (m >= 1050.0 && m < 1170.0) elapsed = m - 1050.0;
                else elapsed = m >= 1170.0 ? m - 1170.0 : m + 270.0;
                return (int)Math.Floor(elapsed / MinutesPerStep);
            }
        }

        public int MinutesUntilNextPhase
        {
            get
            {
                double m = TimeOfDayMinutes;
                double remaining;
                if (m >= 330.0 && m < 450.0)
                {
                    remaining = 450.0 - m;
                }
                else if (m >= 450.0 && m < 1050.0)
                {
                    remaining = 1050.0 - m;
                }
                else if (m >= 1050.0 && m < 1170.0)
                {
                    remaining = 1170.0 - m;
                }
                else
                {
                    remaining = m >= 1170.0 ? (1770.0 - m) : (330.0 - m);
                }

                return Math.Max(1, (int)Math.Ceiling(remaining));
            }
        }

        public int TurnsUntilNextPhase => Math.Max(1, (int)Math.Ceiling(MinutesUntilNextPhase / (double)MinutesPerStep));
        public int NextPhaseStartTurn => TotalTurns + TurnsUntilNextPhase;

        public string PhaseLabel
        {
            get
            {
                switch (Phase)
                {
                    case WorldTimePhase.Dawn: return "Dawn";
                    case WorldTimePhase.Day: return "Day";
                    case WorldTimePhase.Dusk: return "Dusk";
                    case WorldTimePhase.Night: return "Night";
                    default: return "Day";
                }
            }
        }

        public string SubPhaseLabel
        {
            get
            {
                double m = TimeOfDayMinutes;
                if (m >= 330.0 && m < 450.0) return "Dawn";
                if (m >= 450.0 && m < 690.0) return "Morning";
                if (m >= 690.0 && m < 870.0) return "Noon";
                if (m >= 870.0 && m < 1050.0) return "Afternoon";
                if (m >= 1050.0 && m < 1170.0) return "Dusk";
                return "Night";
            }
        }

        public string FormattedTime12h =>
            new DateTime(2000, 1, 1, Hour24, MinuteOfHour, 0).ToString("hh:mm tt", CultureInfo.InvariantCulture);

        public string DisplayLabel => $"Day {DayNumber} - {FormattedTime12h} ({SubPhaseLabel})";

        public static WorldClock FromMinutes(double totalMinutes) => new WorldClock(totalMinutes);
        public static WorldClock FromTurns(int totalTurns) => new WorldClock(totalTurns);

        public static double LegacyTurnsToMinutes(int turns)
        {
            int clamped = Math.Max(0, turns);
            int day = clamped / TurnsPerDay;
            int t = clamped % TurnsPerDay;

            double dayMinutes;
            if (t < TurnsPerPhase)
            {
                dayMinutes = 330.0 + (t / (double)TurnsPerPhase) * 120.0;
            }
            else if (t < TurnsPerPhase * 2)
            {
                dayMinutes = 480.0 + ((t - TurnsPerPhase) / (double)TurnsPerPhase) * 570.0;
            }
            else if (t < TurnsPerPhase * 3)
            {
                dayMinutes = 1050.0 + ((t - (TurnsPerPhase * 2)) / (double)TurnsPerPhase) * 120.0;
            }
            else
            {
                dayMinutes = 1170.0 + ((t - (TurnsPerPhase * 3)) / (double)TurnsPerPhase) * 269.0;
            }

            return (day * (double)MinutesPerDay) + dayMinutes;
        }

        public bool Equals(WorldClock other) => Math.Abs(TotalMinutes - other.TotalMinutes) < 0.0001;
        public override bool Equals(object? obj) => obj is WorldClock other && Equals(other);
        public override int GetHashCode() => Math.Round(TotalMinutes, 4).GetHashCode();
        public static bool operator ==(WorldClock left, WorldClock right) => left.Equals(right);
        public static bool operator !=(WorldClock left, WorldClock right) => !left.Equals(right);
        public override string ToString() => DisplayLabel;
    }

    public sealed class EncounterGeographyInfo
    {
        public EncounterGeographyInfo(
            EncounterBiomeKind biomeKind,
            string locationLabel,
            string tileTerrainLabel,
            int distanceToWater,
            bool isOnRoad,
            bool isCobblestone,
            bool isDeepForest,
            bool isNearRuins,
            bool isNearDungeonEntrance,
            bool isTownSafeZone,
            double probabilityMultiplier)
        {
            BiomeKind = biomeKind;
            LocationLabel = locationLabel ?? string.Empty;
            TileTerrainLabel = tileTerrainLabel ?? string.Empty;
            DistanceToWater = distanceToWater;
            IsOnRoad = isOnRoad;
            IsCobblestone = isCobblestone;
            IsDeepForest = isDeepForest;
            IsNearRuins = isNearRuins;
            IsNearDungeonEntrance = isNearDungeonEntrance;
            IsTownSafeZone = isTownSafeZone;
            ProbabilityMultiplier = probabilityMultiplier;
        }

        public EncounterBiomeKind BiomeKind { get; }
        public string LocationLabel { get; }
        public string TileTerrainLabel { get; }
        public int DistanceToWater { get; }
        public bool IsNearWater => DistanceToWater <= 3;
        public bool IsOnRoad { get; }
        public bool IsCobblestone { get; }
        public bool IsDeepForest { get; }
        public bool IsNearRuins { get; }
        public bool IsNearDungeonEntrance { get; }
        public bool IsTownSafeZone { get; }
        public double ProbabilityMultiplier { get; }
    }

    public sealed class EncounterThreatAssessment
    {
        public EncounterThreatAssessment(
            EncounterThreatTier tier,
            string label,
            string reason,
            int effectiveChanceBasisPoints,
            bool isInGracePeriod,
            int graceStepsRemaining,
            bool hasLitTorch,
            bool isDarkWithoutTorch)
        {
            Tier = tier;
            Label = label ?? "SAFE";
            Reason = reason ?? string.Empty;
            EffectiveChanceBasisPoints = effectiveChanceBasisPoints;
            IsInGracePeriod = isInGracePeriod;
            GraceStepsRemaining = graceStepsRemaining;
            HasLitTorch = hasLitTorch;
            IsDarkWithoutTorch = isDarkWithoutTorch;
        }

        public EncounterThreatTier Tier { get; }
        public string Label { get; }
        public string Reason { get; }
        public int EffectiveChanceBasisPoints { get; }
        public double EffectiveChancePercent => EffectiveChanceBasisPoints / 100.0;
        public bool IsInGracePeriod { get; }
        public int GraceStepsRemaining { get; }
        public bool HasLitTorch { get; }
        public bool IsDarkWithoutTorch { get; }
    }

    public sealed class EncounterUnitCount
    {
        public EncounterUnitCount(MonsterDefinition definition, int count)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Count = Math.Max(1, count);
        }

        public MonsterDefinition Definition { get; }
        public int Count { get; }
    }

    public sealed class EncounterScoutingReport
    {
        public EncounterScoutingReport(
            EncounterGeographyInfo geography,
            WorldClock clock,
            EncounterThreatTier threatTier,
            EncounterZoneData? triggeredZone,
            string zoneKey,
            bool isLandmark,
            bool isAmbushedInDark,
            bool canAmbush,
            bool canEvade,
            int evadeChancePct,
            int ambushChancePct,
            string leadScoutName,
            int partyScoutScore,
            int enemyAlertScore,
            int rewardBonusPct,
            bool hasLitTorch,
            int torchTurnsRemaining,
            IReadOnlyList<MonsterDefinition> monsters,
            IReadOnlyList<EncounterUnitCount> monsterCounts)
        {
            Geography = geography ?? throw new ArgumentNullException(nameof(geography));
            Clock = clock;
            ThreatTier = threatTier;
            TriggeredZone = triggeredZone;
            ZoneKey = zoneKey ?? string.Empty;
            IsLandmark = isLandmark;
            IsAmbushedInDark = isAmbushedInDark;
            CanAmbush = canAmbush;
            CanEvade = canEvade;
            EvadeChancePct = evadeChancePct;
            AmbushChancePct = ambushChancePct;
            LeadScoutName = leadScoutName ?? "Party";
            PartyScoutScore = partyScoutScore;
            EnemyAlertScore = enemyAlertScore;
            RewardBonusPct = rewardBonusPct;
            HasLitTorch = hasLitTorch;
            TorchTurnsRemaining = torchTurnsRemaining;
            Monsters = monsters ?? Array.Empty<MonsterDefinition>();
            MonsterCounts = monsterCounts ?? Array.Empty<EncounterUnitCount>();
        }

        public EncounterGeographyInfo Geography { get; }
        public WorldClock Clock { get; }
        public EncounterThreatTier ThreatTier { get; }
        public EncounterZoneData? TriggeredZone { get; }
        public string ZoneKey { get; }
        public bool IsLandmark { get; }
        public bool IsAmbushedInDark { get; }
        public bool CanAmbush { get; }
        public bool CanEvade { get; }
        public int EvadeChancePct { get; }
        public int AmbushChancePct { get; }
        public string LeadScoutName { get; }
        public int PartyScoutScore { get; }
        public int EnemyAlertScore { get; }
        public int RewardBonusPct { get; }
        public bool HasLitTorch { get; }
        public int TorchTurnsRemaining { get; }
        public IReadOnlyList<MonsterDefinition> Monsters { get; }
        public IReadOnlyList<EncounterUnitCount> MonsterCounts { get; }

        public CombatInitiativeSurpriseMode ResolveEngageSurpriseMode()
        {
            if (IsAmbushedInDark)
            {
                return CombatInitiativeSurpriseMode.EnemySurprisedParty;
            }

            if (CanAmbush)
            {
                return CombatInitiativeSurpriseMode.PartyAmbush;
            }

            return CombatInitiativeSurpriseMode.None;
        }
    }

    /// <summary>
    /// Tactical Survival RPG encounter director combining basis-point zone odds, exact geography,
    /// water proximity, Day/Night survival cycle, torchlight darkness penalties, and pre-battle scouting.
    /// </summary>
    public static class EncounterDirector
    {
        public const int MaxBasisPoints = 10000;
        public const int LandmarkMaxArea = 150;
        public const int LandmarkGuaranteedBasisPoints = 1000;
        public const int NightRewardBonusPercent = 25;
        public const double NightEncounterMultiplier = 1.40;

        public static bool IsLandmarkZone(TileMapData? map, EncounterZoneData? zone)
        {
            if (zone == null)
            {
                return false;
            }

            int width = Math.Max(1, (zone.Right - zone.Left) + 1);
            int height = Math.Max(1, (zone.Bottom - zone.Top) + 1);
            int area = width * height;
            return area <= LandmarkMaxArea || zone.Percent >= LandmarkGuaranteedBasisPoints;
        }

        public static bool IsZoneInBywaterTownLimits(TileMapData map, GridCoord pos)
        {
            if (map == null || map.IsDungeon)
            {
                return false;
            }

            if (!string.Equals(map.MapId, "land_0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Full 90x90 Bywater walled compound is (0..11, 0..22) plus town POIs outside Zone 3 Haunted Quarters.
            if (map.Width >= 90 && map.Height >= 90)
            {
                if (pos.X <= 11 && pos.Y <= 22)
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetDistanceToNearestWater(TileMapData map, GridCoord pos, int maxRadius = 3)
        {
            if (map == null || !map.InBounds(pos))
            {
                return int.MaxValue;
            }

            int best = int.MaxValue;
            for (int dy = -maxRadius; dy <= maxRadius; dy++)
            {
                for (int dx = -maxRadius; dx <= maxRadius; dx++)
                {
                    int nx = pos.X + dx;
                    int ny = pos.Y + dy;
                    if (!map.InBounds(nx, ny))
                    {
                        continue;
                    }

                    TileRule rule = map.GetTileRule(map.GetTile(nx, ny));
                    if (rule.NeedsBoat)
                    {
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (d < best)
                        {
                            best = d;
                        }
                    }
                }
            }

            return best;
        }

        public static EncounterGeographyInfo EvaluateGeography(
            TileMapData map,
            GridCoord pos,
            bool isInSafeZone)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (map.IsDungeon)
            {
                int waterDistDungeon = GetDistanceToNearestWater(map, pos, 3);
                string dungeonLabel = waterDistDungeon <= 3
                    ? "Bywater Cavern - Subterranean Stream"
                    : (pos.Y < 32 ? "Bywater Cavern - Upper Crypt" : "Bywater Cavern - Deep Halls");
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.Dungeon,
                    dungeonLabel,
                    "Cavern Stone",
                    waterDistDungeon,
                    isOnRoad: false,
                    isCobblestone: false,
                    isDeepForest: false,
                    isNearRuins: true,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: false,
                    probabilityMultiplier: 1.0);
            }

            int rawTile = map.InBounds(pos) ? map.GetTile(pos.X, pos.Y) : 0;
            TileRule rule = map.GetTileRule(rawTile);
            int decodedId = LandMapData.DecodeTerrainTileIndex(rawTile) + 1;

            bool isCobblestone = decodedId >= 132 && decodedId <= 146;
            bool isPathTile = isCobblestone ||
                              rule.IsPath ||
                              (rawTile >= 0 && (rawTile & 0x2000) != 0) ||
                              decodedId == 159 ||
                              decodedId == 160 ||
                              decodedId == 165;

            int waterDist = GetDistanceToNearestWater(map, pos, 3);
            bool nearWater = waterDist <= 3;

            bool nearDungeonEntrance = false;
            for (int i = 0; i < map.Portals.Count; i++)
            {
                MapPortal portal = map.Portals[i];
                if (!string.IsNullOrWhiteSpace(portal.DestMapId) &&
                    portal.DestMapId.StartsWith("dungeon", StringComparison.OrdinalIgnoreCase) &&
                    pos.ChebyshevDistance(portal.Coord) <= 5)
                {
                    nearDungeonEntrance = true;
                    break;
                }
            }

            bool inHauntedQuarters = string.Equals(map.MapId, "land_0", StringComparison.OrdinalIgnoreCase) &&
                                     map.Width >= 90 &&
                                     pos.X >= 12 && pos.X <= 22 &&
                                     pos.Y >= 13 && pos.Y <= 18;
            bool nearRuins = inHauntedQuarters || IsNearOutdoorRuins(map, pos, 3);

            int forestNearbyCount = CountNearbyForestTiles(map, pos, 1);
            bool isDeepForest = rule.IsForest || forestNearbyCount >= 3;

            if (isInSafeZone || IsZoneInBywaterTownLimits(map, pos))
            {
                string townLoc = isCobblestone ? "Bywater Town Square" : "City of Bywater";
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.TownSafe,
                    townLoc,
                    isCobblestone ? "Cobblestone" : "Town Grounds",
                    waterDist,
                    isOnRoad: isPathTile,
                    isCobblestone: isCobblestone,
                    isDeepForest: false,
                    isNearRuins: false,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: true,
                    probabilityMultiplier: 0.0);
            }

            if (nearDungeonEntrance)
            {
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.DungeonApproach,
                    "Dungeon Approach",
                    isPathTile ? "Stone Trail" : "Craggy Pass",
                    waterDist,
                    isOnRoad: isPathTile,
                    isCobblestone: isCobblestone,
                    isDeepForest: isDeepForest,
                    isNearRuins: true,
                    isNearDungeonEntrance: true,
                    isTownSafeZone: false,
                    probabilityMultiplier: 1.35);
            }

            if (nearRuins)
            {
                string ruinsLabel = inHauntedQuarters ? "Haunted Quarters" : "Ancient Ruins";
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.Ruins,
                    ruinsLabel,
                    isCobblestone ? "Cracked Cobbles" : "Ruined Flagstone",
                    waterDist,
                    isOnRoad: isPathTile,
                    isCobblestone: isCobblestone,
                    isDeepForest: isDeepForest,
                    isNearRuins: true,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: false,
                    probabilityMultiplier: 1.35);
            }

            if (isPathTile)
            {
                string roadLabel;
                if (waterDist <= 2)
                {
                    roadLabel = pos.X >= 40 ? "Bywater East Bridge" : "River Crossing";
                }
                else if (isCobblestone)
                {
                    roadLabel = "Old King's Road";
                }
                else
                {
                    roadLabel = pos.X >= 45 ? "Eastern Trade Road" : "Old King's Road";
                }

                return new EncounterGeographyInfo(
                    EncounterBiomeKind.Road,
                    roadLabel,
                    isCobblestone ? "Cobblestone Road" : "Dirt Road",
                    waterDist,
                    isOnRoad: true,
                    isCobblestone: isCobblestone,
                    isDeepForest: false,
                    isNearRuins: false,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: false,
                    probabilityMultiplier: 0.50);
            }

            if (nearWater)
            {
                string riverLabel = pos.X >= 40 ? "Bywater East Riverbank" : "Bywater Riverbank";
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.Riverbank,
                    riverLabel,
                    waterDist == 0 ? "Shallow Water" : "Marshy Riverbank",
                    waterDist,
                    isOnRoad: false,
                    isCobblestone: false,
                    isDeepForest: isDeepForest,
                    isNearRuins: false,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: false,
                    probabilityMultiplier: 1.15);
            }

            if (isDeepForest)
            {
                string forestLabel = pos.Y >= 35 ? "Whispering Woods" : "Thornwood Thicket";
                return new EncounterGeographyInfo(
                    EncounterBiomeKind.DeepForest,
                    forestLabel,
                    "Deep Forest",
                    waterDist,
                    isOnRoad: false,
                    isCobblestone: false,
                    isDeepForest: true,
                    isNearRuins: false,
                    isNearDungeonEntrance: false,
                    isTownSafeZone: false,
                    probabilityMultiplier: 1.50);
            }

            string meadowLabel = pos.X >= 50
                ? (pos.Y < 35 ? "Eastern Borderlands" : "Outland Downs")
                : "Bywater Outskirts";
            return new EncounterGeographyInfo(
                EncounterBiomeKind.WildMeadow,
                meadowLabel,
                "Open Wilds",
                waterDist,
                isOnRoad: false,
                isCobblestone: false,
                isDeepForest: false,
                isNearRuins: false,
                isNearDungeonEntrance: false,
                isTownSafeZone: false,
                probabilityMultiplier: 1.00);
        }

        public static int GetEffectiveBasisPoints(
            EncounterZoneData zone,
            EncounterGeographyInfo geography,
            WorldClock clock,
            bool isDungeon)
        {
            if (zone == null || zone.Percent <= 0 || geography == null)
            {
                return 0;
            }

            if (geography.IsTownSafeZone || geography.BiomeKind == EncounterBiomeKind.TownSafe)
            {
                return 0;
            }

            // Guaranteed test/landmark triggers (>= 10000 bp) remain 100% deterministic.
            if (zone.Percent >= MaxBasisPoints)
            {
                return MaxBasisPoints;
            }

            double mult = geography.ProbabilityMultiplier;
            if (!isDungeon && clock.IsNight)
            {
                mult *= NightEncounterMultiplier;
            }

            int scaled = (int)Math.Round(zone.Percent * mult, MidpointRounding.AwayFromZero);
            return Math.Clamp(scaled, 1, MaxBasisPoints);
        }

        public static EncounterThreatAssessment ComputeThreatAssessment(
            TileMapData map,
            GridCoord pos,
            PartyState party,
            WorldClock clock,
            bool isInSafeZone,
            int graceStepsRemaining,
            IReadOnlyList<EncounterZoneData> zonesAtTile,
            Func<EncounterZoneData, bool> isZoneEligible)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            EncounterGeographyInfo geo = EvaluateGeography(map, pos, isInSafeZone);
            bool hasLitTorch = party != null && party.TorchTurnsRemaining > 0;
            bool inDarkness = (map.IsDungeon || clock.IsNight) && !hasLitTorch;

            int maxBps = 0;
            if (zonesAtTile != null)
            {
                for (int i = 0; i < zonesAtTile.Count; i++)
                {
                    EncounterZoneData z = zonesAtTile[i];
                    if (isZoneEligible != null && !isZoneEligible(z))
                    {
                        continue;
                    }

                    int bps = GetEffectiveBasisPoints(z, geo, clock, map.IsDungeon);
                    if (bps > maxBps)
                    {
                        maxBps = bps;
                    }
                }
            }

            if (geo.IsTownSafeZone && !map.IsDungeon)
            {
                return new EncounterThreatAssessment(
                    EncounterThreatTier.Safe,
                    "SAFE",
                    "Inside Bywater city protection (0% ambush risk).",
                    effectiveChanceBasisPoints: 0,
                    isInGracePeriod: graceStepsRemaining > 0,
                    graceStepsRemaining: graceStepsRemaining,
                    hasLitTorch: hasLitTorch,
                    isDarkWithoutTorch: false);
            }

            if (inDarkness)
            {
                string darkReason = map.IsDungeon
                    ? "Cavern darkness without a lit torch: ambushes inflict Surprised!"
                    : "Pitch-black night without a lit torch: ambushes inflict Surprised!";
                return new EncounterThreatAssessment(
                    EncounterThreatTier.DarkDanger,
                    "DARK DANGER!",
                    darkReason,
                    effectiveChanceBasisPoints: graceStepsRemaining > 0 ? 0 : maxBps,
                    isInGracePeriod: graceStepsRemaining > 0,
                    graceStepsRemaining: graceStepsRemaining,
                    hasLitTorch: false,
                    isDarkWithoutTorch: true);
            }

            if (graceStepsRemaining > 0)
            {
                return new EncounterThreatAssessment(
                    EncounterThreatTier.Low,
                    "LOW",
                    $"Post-battle grace active ({graceStepsRemaining} safe step(s)).",
                    effectiveChanceBasisPoints: 0,
                    isInGracePeriod: true,
                    graceStepsRemaining: graceStepsRemaining,
                    hasLitTorch: hasLitTorch,
                    isDarkWithoutTorch: false);
            }

            if (maxBps <= 0)
            {
                EncounterThreatTier fallbackTier = geo.BiomeKind == EncounterBiomeKind.Road
                    ? EncounterThreatTier.Low
                    : (clock.IsNight ? EncounterThreatTier.Moderate : EncounterThreatTier.Low);
                return new EncounterThreatAssessment(
                    fallbackTier,
                    fallbackTier == EncounterThreatTier.Moderate ? "MODERATE" : "LOW",
                    "Region patrol cleared or quiet.",
                    effectiveChanceBasisPoints: 0,
                    isInGracePeriod: false,
                    graceStepsRemaining: 0,
                    hasLitTorch: hasLitTorch,
                    isDarkWithoutTorch: false);
            }

            if (maxBps >= 350 || geo.BiomeKind == EncounterBiomeKind.DeepForest || geo.BiomeKind == EncounterBiomeKind.DungeonApproach || clock.IsNight)
            {
                string tierLabel = maxBps >= 400 ? "HIGH" : "MODERATE";
                EncounterThreatTier tier = maxBps >= 400 ? EncounterThreatTier.High : EncounterThreatTier.Moderate;
                return new EncounterThreatAssessment(
                    tier,
                    tierLabel,
                    $"{geo.LocationLabel} ({maxBps / 100.0:0.0}%/step)",
                    effectiveChanceBasisPoints: maxBps,
                    isInGracePeriod: false,
                    graceStepsRemaining: 0,
                    hasLitTorch: hasLitTorch,
                    isDarkWithoutTorch: false);
            }

            if (maxBps >= 150)
            {
                return new EncounterThreatAssessment(
                    EncounterThreatTier.Moderate,
                    "MODERATE",
                    $"{geo.LocationLabel} ({maxBps / 100.0:0.0}%/step)",
                    effectiveChanceBasisPoints: maxBps,
                    isInGracePeriod: false,
                    graceStepsRemaining: 0,
                    hasLitTorch: hasLitTorch,
                    isDarkWithoutTorch: false);
            }

            return new EncounterThreatAssessment(
                EncounterThreatTier.Low,
                "LOW",
                $"{geo.LocationLabel} ({maxBps / 100.0:0.0}%/step)",
                effectiveChanceBasisPoints: maxBps,
                isInGracePeriod: false,
                graceStepsRemaining: 0,
                hasLitTorch: hasLitTorch,
                isDarkWithoutTorch: false);
        }

        public static List<MonsterDefinition> BuildEcologyMonsterPack(
            TileMapData map,
            GridCoord pos,
            EncounterZoneData? zone,
            EncounterGeographyInfo geo,
            WorldClock clock,
            IReadOnlyList<MonsterDefinition> catalog,
            IDiceRoller? roller)
        {
            if (catalog == null || catalog.Count == 0)
            {
                return new List<MonsterDefinition>();
            }

            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x45434F4C4F4759UL);
            Dictionary<string, int> weightsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void AddWeight(string id, int w)
            {
                if (w <= 0 || FindMonsterById(catalog, id) == null)
                {
                    return;
                }

                weightsById[id] = weightsById.TryGetValue(id, out int cur) ? cur + w : w;
            }

            // 1. Baseline zone monster roster from data
            if (zone != null && zone.MonsterIndices.Count > 0)
            {
                for (int i = 0; i < zone.MonsterIndices.Count; i++)
                {
                    MonsterDefinition? zMon = FindMonsterByIndex(catalog, zone.MonsterIndices[i]);
                    if (zMon != null)
                    {
                        AddWeight(zMon.Id, 28);
                    }
                }
            }

            // 2. Geography & Biome ecology overlays
            switch (geo.BiomeKind)
            {
                case EncounterBiomeKind.Riverbank:
                    AddWeight("giant_frog", 70);
                    AddWeight("goblin", 24);
                    AddWeight("goblin_archer", 16);
                    AddWeight("giant_spider", 12);
                    break;

                case EncounterBiomeKind.Road:
                    AddWeight("goblin", 46);
                    AddWeight("goblin_archer", 38);
                    AddWeight("goblin_champion", 24);
                    if (pos.X >= 45)
                    {
                        AddWeight("orc_warrior", 22);
                    }
                    break;

                case EncounterBiomeKind.DeepForest:
                    AddWeight("giant_spider", 40);
                    AddWeight("orc_warrior", 32);
                    AddWeight("orc_archer", 28);
                    AddWeight("goblin_shaman", 18);
                    AddWeight("giant_rat", 16);
                    break;

                case EncounterBiomeKind.Ruins:
                case EncounterBiomeKind.DungeonApproach:
                    AddWeight("skeletal_warrior", 52);
                    AddWeight("zombie", 32);
                    AddWeight("orc_warrior", 22);
                    AddWeight("goblin_champion", 18);
                    break;

                case EncounterBiomeKind.Dungeon:
                    AddWeight("skeletal_warrior", 36);
                    AddWeight("zombie", 26);
                    AddWeight("monstrous_rat", 28);
                    AddWeight("giant_rat", 24);
                    AddWeight("giant_spider", 20);
                    break;

                default:
                    AddWeight("goblin", 32);
                    AddWeight("goblin_archer", 22);
                    AddWeight("giant_rat", 20);
                    AddWeight("orc_warrior", 18);
                    break;
            }

            // 3. Night ecology shift (outdoor undead & stalkers)
            if (!map.IsDungeon && clock.IsNight)
            {
                AddWeight("skeletal_warrior", 34);
                AddWeight("zombie", 24);
                AddWeight("giant_spider", 18);
            }

            if (weightsById.Count == 0)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    weightsById[catalog[i].Id] = 10;
                }
            }

            List<(MonsterDefinition Def, int Weight)> pool = new List<(MonsterDefinition, int)>(weightsById.Count);
            int totalWeight = 0;
            foreach (KeyValuePair<string, int> kvp in weightsById)
            {
                MonsterDefinition? def = FindMonsterById(catalog, kvp.Key);
                if (def != null && kvp.Value > 0)
                {
                    pool.Add((def, kvp.Value));
                    totalWeight += kvp.Value;
                }
            }

            int targetCount = geo.BiomeKind == EncounterBiomeKind.DeepForest ||
                              geo.BiomeKind == EncounterBiomeKind.DungeonApproach ||
                              (!map.IsDungeon && clock.IsNight)
                ? 4
                : 3;
            if (zone != null && zone.Percent >= 400)
            {
                targetCount = 4;
            }

            List<MonsterDefinition> pack = new List<MonsterDefinition>(targetCount);

            // Guarantee signature biome unit in slot 0 for crystal-clear ecology identity.
            MonsterDefinition? signature = null;
            if (geo.BiomeKind == EncounterBiomeKind.Riverbank)
            {
                signature = FindMonsterById(catalog, "giant_frog");
            }
            else if (geo.BiomeKind == EncounterBiomeKind.Ruins || geo.BiomeKind == EncounterBiomeKind.DungeonApproach)
            {
                signature = FindMonsterById(catalog, "skeletal_warrior");
            }
            else if (geo.BiomeKind == EncounterBiomeKind.Road)
            {
                signature = FindMonsterById(catalog, "goblin");
            }
            else if (geo.BiomeKind == EncounterBiomeKind.DeepForest)
            {
                signature = FindMonsterById(catalog, "orc_warrior") ?? FindMonsterById(catalog, "giant_spider");
            }

            if (signature != null)
            {
                pack.Add(signature);
                if (geo.BiomeKind == EncounterBiomeKind.Riverbank && targetCount >= 3)
                {
                    pack.Add(signature);
                }
            }

            while (pack.Count < targetCount && totalWeight > 0)
            {
                int roll = rng.NextInt(1, totalWeight + 1);
                int accum = 0;
                MonsterDefinition? chosen = pool[0].Def;
                for (int i = 0; i < pool.Count; i++)
                {
                    accum += pool[i].Weight;
                    if (roll <= accum)
                    {
                        chosen = pool[i].Def;
                        break;
                    }
                }

                if (chosen != null)
                {
                    pack.Add(chosen);
                }
            }

            return pack;
        }

        public static EncounterScoutingReport EvaluateScouting(
            TileMapData map,
            GridCoord pos,
            PartyState party,
            WorldClock clock,
            EncounterZoneData? zone,
            string zoneKey,
            bool isLandmark,
            IReadOnlyList<MonsterDefinition> monsters,
            IDiceRoller? roller,
            bool isInSafeZone = false)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (party == null) throw new ArgumentNullException(nameof(party));

            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x53434F5554UL);
            EncounterGeographyInfo geo = EvaluateGeography(map, pos, isInSafeZone);
            bool hasLitTorch = party.TorchTurnsRemaining > 0;
            bool isAmbushedInDark = (map.IsDungeon || clock.IsNight) && !hasLitTorch;

            int bestScoutScore = 0;
            int secondaryBonus = 0;
            bool hasRanger = false;
            bool hasRogue = false;
            int livingCount = 0;
            int sumAgility = 0;
            int sumLuck = 0;
            string leadScoutName = "Vanguard";

            for (int i = 0; i < party.Members.Count; i++)
            {
                PartyMemberState m = party.Members[i];
                if (m == null || !m.IsAlive)
                {
                    continue;
                }

                livingCount++;
                sumAgility += m.Agility;
                sumLuck += m.Luck;

                string arch = (m.Archetype ?? string.Empty).Trim().ToLowerInvariant();
                bool isRangerRole = arch == "ranger" || arch == "archer" ||
                                    string.Equals(m.Name, "Kaelen", StringComparison.OrdinalIgnoreCase);
                bool isRogueRole = arch == "rogue" || arch == "thief" ||
                                   string.Equals(m.Name, "Vex", StringComparison.OrdinalIgnoreCase);

                int memberScoutScore = m.Agility + (m.Luck / 2) + ((isRangerRole || isRogueRole) ? 4 : 0);
                if (isRangerRole) hasRanger = true;
                if (isRogueRole) hasRogue = true;

                if (memberScoutScore > bestScoutScore)
                {
                    bestScoutScore = memberScoutScore;
                    leadScoutName = m.Name;
                }
            }

            if (hasRanger && hasRogue)
            {
                secondaryBonus = 3;
                leadScoutName = "Kaelen & Vex";
            }

            double avgAgility = livingCount > 0 ? sumAgility / (double)livingCount : 10.0;
            double avgLuck = livingCount > 0 ? sumLuck / (double)livingCount : 10.0;
            int partyScoutScore = bestScoutScore + secondaryBonus + (int)Math.Round((avgLuck - 10.0) * 0.5, MidpointRounding.AwayFromZero);

            int enemyCount = monsters != null ? monsters.Count : 0;
            double sumEnemyDex = 0.0;
            Dictionary<string, int> countLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<EncounterUnitCount> grouped = new List<EncounterUnitCount>();
            Dictionary<string, int> groupIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterDefinition def = monsters[i];
                    if (def == null) continue;
                    sumEnemyDex += def.Dexterity;
                    if (groupIndexById.TryGetValue(def.Id, out int idx))
                    {
                        EncounterUnitCount prev = grouped[idx];
                        grouped[idx] = new EncounterUnitCount(prev.Definition, prev.Count + 1);
                    }
                    else
                    {
                        groupIndexById[def.Id] = grouped.Count;
                        grouped.Add(new EncounterUnitCount(def, 1));
                    }
                }
            }

            double avgEnemyDex = enemyCount > 0 ? sumEnemyDex / enemyCount : 12.0;
            int enemyAlertScore = (int)Math.Round(avgEnemyDex, MidpointRounding.AwayFromZero)
                                  + 6
                                  + (clock.IsNight ? 3 : 0)
                                  + (map.IsDungeon ? 2 : 0)
                                  + (geo.BiomeKind == EncounterBiomeKind.DeepForest ? 2 : 0);

            int ambushChancePct = Math.Clamp(40 + (partyScoutScore - enemyAlertScore) * 3, 15, 85);
            int evadeChancePct = Math.Clamp(
                45
                + (int)Math.Round((avgAgility - avgEnemyDex) * 4.0, MidpointRounding.AwayFromZero)
                + (int)Math.Round((avgLuck - 10.0) * 2.0, MidpointRounding.AwayFromZero)
                + ((hasRanger || hasRogue) ? 10 : 0)
                - (clock.IsNight ? 10 : 0)
                - (map.IsDungeon ? 10 : 0),
                15,
                90);

            bool canAmbush = false;
            bool canEvade = !isAmbushedInDark;
            if (isAmbushedInDark)
            {
                evadeChancePct = 0;
                ambushChancePct = 0;
            }
            else
            {
                int scoutRoll = rng.NextInt(1, 101);
                canAmbush = scoutRoll <= ambushChancePct;
            }

            EncounterThreatTier tier;
            if (isAmbushedInDark)
            {
                tier = EncounterThreatTier.DarkDanger;
            }
            else
            {
                double zoneBps = zone != null ? Math.Max(0, zone.Percent) : 0.0;
                double timeMult = (!map.IsDungeon && clock.IsNight) ? NightEncounterMultiplier : 1.0;
                int effectiveBps = (int)Math.Round(zoneBps * geo.ProbabilityMultiplier * timeMult, MidpointRounding.AwayFromZero);

                if (effectiveBps >= 400 || geo.BiomeKind == EncounterBiomeKind.DungeonApproach || geo.BiomeKind == EncounterBiomeKind.DeepForest || clock.IsNight)
                {
                    tier = effectiveBps >= 400 ? EncounterThreatTier.High : EncounterThreatTier.Moderate;
                }
                else if (effectiveBps >= 150)
                {
                    tier = EncounterThreatTier.Moderate;
                }
                else
                {
                    tier = EncounterThreatTier.Low;
                }
            }

            int rewardBonusPct = (!map.IsDungeon && clock.IsNight) ? NightRewardBonusPercent : 0;

            return new EncounterScoutingReport(
                geography: geo,
                clock: clock,
                threatTier: tier,
                triggeredZone: zone,
                zoneKey: zoneKey,
                isLandmark: isLandmark,
                isAmbushedInDark: isAmbushedInDark,
                canAmbush: canAmbush,
                canEvade: canEvade,
                evadeChancePct: evadeChancePct,
                ambushChancePct: ambushChancePct,
                leadScoutName: leadScoutName,
                partyScoutScore: partyScoutScore,
                enemyAlertScore: enemyAlertScore,
                rewardBonusPct: rewardBonusPct,
                hasLitTorch: hasLitTorch,
                torchTurnsRemaining: party.TorchTurnsRemaining,
                monsters: monsters ?? Array.Empty<MonsterDefinition>(),
                monsterCounts: grouped);
        }

        public static bool RollEvade(EncounterScoutingReport report, IDiceRoller? roller)
        {
            if (report == null || !report.CanEvade || report.IsAmbushedInDark || report.EvadeChancePct <= 0)
            {
                return false;
            }

            IDiceRoller rng = roller ?? new DeterministicDiceRoller(0x4556414445UL);
            return rng.NextInt(1, 101) <= report.EvadeChancePct;
        }

        public static CombatArenaArchetype ResolveArenaArchetype(EncounterGeographyInfo? geo, IDiceRoller? roller = null)
        {
            if (geo == null)
            {
                return CombatArenaArchetype.RoadAmbush;
            }

            if (geo.BiomeKind == EncounterBiomeKind.Dungeon)
            {
                return CombatArenaArchetype.DungeonChamber;
            }

            if (geo.BiomeKind == EncounterBiomeKind.Riverbank ||
                (geo.IsNearWater && !geo.IsOnRoad))
            {
                return CombatArenaArchetype.RiverbankCrossing;
            }

            if (geo.BiomeKind == EncounterBiomeKind.Road ||
                geo.IsOnRoad ||
                geo.BiomeKind == EncounterBiomeKind.TownSafe)
            {
                return CombatArenaArchetype.RoadAmbush;
            }

            if (geo.BiomeKind == EncounterBiomeKind.Ruins ||
                geo.BiomeKind == EncounterBiomeKind.DungeonApproach)
            {
                return CombatArenaArchetype.RuinsOutskirts;
            }

            if (geo.BiomeKind == EncounterBiomeKind.DeepForest || geo.IsDeepForest)
            {
                return CombatArenaArchetype.DeepWoods;
            }

            int hash = Math.Abs((geo.LocationLabel ?? string.Empty).GetHashCode() + (geo.TileTerrainLabel ?? string.Empty).GetHashCode());
            int pick = roller != null
                ? Math.Abs(roller.NextInt(0, 3))
                : (hash % 3);
            return pick switch
            {
                0 => CombatArenaArchetype.DeepWoods,
                1 => CombatArenaArchetype.RoadAmbush,
                _ => CombatArenaArchetype.RuinsOutskirts
            };
        }

        public static void ApplyBiomeArenaFeatures(
            CombatGrid grid,
            EncounterGeographyInfo? geo,
            IReadOnlyCollection<GridCoord>? reservedCoords,
            IDiceRoller? roller,
            WeatherState weather = WeatherState.Clear)
        {
            if (grid == null)
            {
                return;
            }

            CombatArenaArchetype archetype = ResolveArenaArchetype(geo, roller);
            ApplyArenaArchetype(grid, archetype, reservedCoords, roller, geo, weather);
        }

        public static CombatArenaLayout ApplyArenaArchetype(
            CombatGrid grid,
            CombatArenaArchetype archetype,
            IReadOnlyCollection<GridCoord>? reservedCoords,
            IDiceRoller? roller,
            EncounterGeographyInfo? geo = null,
            WeatherState weather = WeatherState.Clear)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            HashSet<GridCoord> reserved = reservedCoords != null
                ? new HashSet<GridCoord>(reservedCoords)
                : new HashSet<GridCoord>();

            int w = grid.Width;
            int h = grid.Height;
            int midX = w / 2;
            int midY = h / 2;
            ulong seedVal = DeriveArenaSeed(geo, archetype, roller);
            int variant = (int)(seedVal % 3UL);

            CombatArenaFeature[,] mapFeatures = new CombatArenaFeature[w, h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    CombatGridCell cell = grid.GetCell(x, y);
                    mapFeatures[x, y] = new CombatArenaFeature(x, y)
                    {
                        BlocksMovement = cell.IsBlocked,
                        BlocksLos = cell.BlocksLos,
                        ProvidesCover = cell.ProvidesCover,
                        ElevationTier = cell.ElevationTier,
                        MoveCost = Math.Max(1, cell.MoveCost),
                        Surface = cell.Surface,
                        SurfaceRounds = cell.SurfaceRoundsRemaining
                    };
                }
            }

            bool IsOpenFeatureSlot(int x, int y)
            {
                if (!grid.InBounds(x, y))
                {
                    return false;
                }
                if (reserved.Contains(new GridCoord(x, y)))
                {
                    return false;
                }
                return x >= 1 && x <= w - 2 && y >= 1 && y <= h - 2;
            }

            // Preserve perimeter corridor walls on Dungeon slices while clearing messy exploration clutter inside central lanes.
            int clearMinX = Math.Min(3, midX - 2);
            int clearMaxX = Math.Max(clearMinX, w - 4);
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = clearMinX; x <= clearMaxX; x++)
                {
                    if (!reserved.Contains(new GridCoord(x, y)))
                    {
                        ResetFeatureOpen(grid, mapFeatures, x, y, 0, 1);
                    }
                }
            }

            switch (archetype)
            {
                case CombatArenaArchetype.RoadAmbush:
                    BuildRoadAmbushTemplate(grid, mapFeatures, w, h, variant, reserved, IsOpenFeatureSlot);
                    break;
                case CombatArenaArchetype.RiverbankCrossing:
                    BuildRiverbankCrossingTemplate(grid, mapFeatures, w, h, variant, reserved, IsOpenFeatureSlot);
                    break;
                case CombatArenaArchetype.DeepWoods:
                    BuildDeepWoodsTemplate(grid, mapFeatures, w, h, variant, reserved, IsOpenFeatureSlot);
                    break;
                case CombatArenaArchetype.RuinsOutskirts:
                    BuildRuinsOutskirtsTemplate(grid, mapFeatures, w, h, variant, reserved, IsOpenFeatureSlot);
                    break;
                case CombatArenaArchetype.DungeonChamber:
                    BuildDungeonChamberTemplate(grid, mapFeatures, w, h, variant, reserved, IsOpenFeatureSlot);
                    break;
            }

            EnforceSpawnsAndMultiLaneBfs(grid, mapFeatures, w, h, reserved, out int verifiedLanes, out bool allConnected);

            if (weather != WeatherState.Clear && archetype != CombatArenaArchetype.DungeonChamber)
            {
                ApplyWeatherPuddles(mapFeatures, w, h, reserved, seedVal, weather);
            }

            List<CombatArenaFeature> featureList = new List<CombatArenaFeature>(w * h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    CombatArenaFeature f = mapFeatures[x, y];
                    grid.SetFeatureCell(
                        x,
                        y,
                        f.BlocksMovement,
                        f.BlocksLos,
                        f.ProvidesCover,
                        f.ElevationTier,
                        f.MoveCost,
                        f.Surface,
                        f.SurfaceRounds);
                    if (f.ObstacleKind != CombatArenaObstacleKind.None ||
                        f.ElevationTier != 0 ||
                        f.Surface != SurfaceType.None ||
                        f.MoveCost > 1 ||
                        f.ProvidesCover ||
                        f.IsRoadTile ||
                        f.IsMudBank ||
                        f.IsBridgeTile ||
                        f.BlocksMovement)
                    {
                        featureList.Add(f);
                    }
                }
            }

            CombatArenaLayout layout = new CombatArenaLayout(archetype, seedVal, verifiedLanes, allConnected, featureList);
            grid.SetArenaLayout(layout);
            return layout;
        }

        private static ulong DeriveArenaSeed(EncounterGeographyInfo? geo, CombatArenaArchetype archetype, IDiceRoller? roller)
        {
            ulong h = ((ulong)((int)archetype + 1)) * 0x9E3779B97F4A7C15UL;
            if (geo != null)
            {
                int labelHash = (geo.LocationLabel ?? string.Empty).GetHashCode() ^
                                ((geo.TileTerrainLabel ?? string.Empty).GetHashCode() * 31) ^
                                ((int)geo.BiomeKind * 397);
                h ^= unchecked((ulong)(uint)labelHash) * 73856093UL;
            }
            if (roller != null)
            {
                ulong rVal = (ulong)(uint)Math.Abs(roller.NextInt(1, 100000));
                h ^= rVal * 2654435761UL;
            }
            return h;
        }

        private static void ResetFeatureOpen(CombatGrid grid, CombatArenaFeature[,] map, int x, int y, short elev, int moveCost)
        {
            if (!grid.InBounds(x, y)) return;
            CombatArenaFeature f = map[x, y];
            f.ObstacleKind = CombatArenaObstacleKind.None;
            f.BlocksMovement = false;
            f.BlocksLos = false;
            f.ProvidesCover = false;
            f.ElevationTier = elev;
            f.MoveCost = Math.Max(1, moveCost);
            f.Surface = SurfaceType.None;
            f.SurfaceRounds = -1;
            f.IsMudBank = false;
        }

        private static void SetObstacleFeature(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int x,
            int y,
            CombatArenaObstacleKind kind,
            bool blocksMove,
            bool blocksLos,
            bool providesCover,
            int moveCost = 1,
            short elevationTier = 0,
            SurfaceType surface = SurfaceType.None)
        {
            if (!grid.InBounds(x, y)) return;
            CombatArenaFeature f = map[x, y];
            f.ObstacleKind = kind;
            f.BlocksMovement = blocksMove;
            f.BlocksLos = blocksLos;
            f.ProvidesCover = providesCover;
            f.MoveCost = Math.Max(1, moveCost);
            f.ElevationTier = elevationTier;
            f.Surface = surface;
            f.SurfaceRounds = surface == SurfaceType.None ? 0 : -1;
            f.BlocksFoliage = blocksMove || providesCover;
        }

        private static void BuildRoadAmbushTemplate(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            int variant,
            HashSet<GridCoord> reserved,
            Func<int, int, bool> isOpen)
        {
            int midX = w / 2;
            int roadY0 = Math.Clamp((h / 2) - 1 + (variant == 1 ? -1 : 0), 2, Math.Max(2, h - 4));
            int roadY1 = Math.Min(h - 3, roadY0 + 1);

            for (int x = 0; x < w; x++)
            {
                for (int ry = roadY0; ry <= roadY1; ry++)
                {
                    if (!reserved.Contains(new GridCoord(x, ry)))
                    {
                        ResetFeatureOpen(grid, map, x, ry, 0, 1);
                    }
                    map[x, ry].IsRoadTile = true;
                    map[x, ry].BlocksFoliage = true;
                }
            }

            // Hedgerow / stone fence + roadside boulders flanking road while keeping side-trail openings at flanks
            int northShoulderY = Math.Max(1, roadY0 - 1);
            int southShoulderY = Math.Min(h - 2, roadY1 + 1);
            (int x, int y, CombatArenaObstacleKind kind)[] shoulders =
            {
                (Math.Max(2, midX - 2), northShoulderY, CombatArenaObstacleKind.StoneFence),
                (Math.Min(w - 3, midX + 2), northShoulderY, CombatArenaObstacleKind.BoulderRock),
                (Math.Max(2, midX - 2), southShoulderY, CombatArenaObstacleKind.BoulderRock),
                (Math.Min(w - 3, midX + 2), southShoulderY, CombatArenaObstacleKind.StoneFence)
            };
            for (int i = 0; i < shoulders.Length; i++)
            {
                var (ox, oy, okind) = shoulders[i];
                if (isOpen(ox, oy))
                {
                    SetObstacleFeature(grid, map, ox, oy, okind, blocksMove: true, blocksLos: true, providesCover: false, moveCost: 1);
                }
            }

            // Overturned Supply Wagon & Crate Cover creating central road bottleneck
            int wagonX = Math.Clamp(midX - (variant == 2 ? 0 : 1), 3, w - 4);
            if (isOpen(wagonX, roadY0))
            {
                SetObstacleFeature(grid, map, wagonX, roadY0, CombatArenaObstacleKind.SupplyWagon, blocksMove: true, blocksLos: true, providesCover: false, moveCost: 1);
            }

            int crateX = Math.Clamp(midX + 1, 3, w - 4);
            if (isOpen(crateX, roadY1))
            {
                SetObstacleFeature(grid, map, crateX, roadY1, CombatArenaObstacleKind.BarrelsCrates, blocksMove: false, blocksLos: false, providesCover: true, moveCost: 2);
            }
            if (isOpen(Math.Max(3, wagonX - 1), roadY1))
            {
                SetObstacleFeature(grid, map, Math.Max(3, wagonX - 1), roadY1, CombatArenaObstacleKind.BarrelsCrates, blocksMove: false, blocksLos: false, providesCover: true, moveCost: 2);
            }

            // Elevated roadside embankment ridges (north and south flanks)
            for (int x = Math.Max(3, midX - 2); x <= Math.Min(w - 4, midX); x++)
            {
                int ridgeY = 1;
                if (isOpen(x, ridgeY) && !map[x, ridgeY].BlocksMovement)
                {
                    map[x, ridgeY].ElevationTier = 1;
                }
            }
            for (int x = Math.Min(w - 4, midX); x <= Math.Min(w - 3, midX + 2); x++)
            {
                int ridgeY = Math.Max(southShoulderY + 1, h - 2);
                if (isOpen(x, ridgeY) && !map[x, ridgeY].BlocksMovement)
                {
                    map[x, ridgeY].ElevationTier = 1;
                }
            }

            // Lit Brazier + spilled Oil slick near the road bottleneck for Fire combos
            int brazierX = Math.Clamp(midX, 3, w - 4);
            int brazierY = northShoulderY;
            if (isOpen(brazierX, brazierY) && !map[brazierX, brazierY].BlocksMovement)
            {
                SetObstacleFeature(grid, map, brazierX, brazierY, CombatArenaObstacleKind.LitBrazier, blocksMove: true, blocksLos: false, providesCover: false, moveCost: 1);
            }

            GridCoord[] oilSpots =
            {
                new GridCoord(Math.Clamp(brazierX, 3, w - 4), roadY1),
                new GridCoord(Math.Clamp(brazierX + 1, 3, w - 4), roadY0)
            };
            for (int i = 0; i < oilSpots.Length; i++)
            {
                GridCoord c = oilSpots[i];
                if (isOpen(c.X, c.Y) && !map[c.X, c.Y].BlocksMovement && map[c.X, c.Y].Surface == SurfaceType.None)
                {
                    map[c.X, c.Y].Surface = SurfaceType.Oil;
                    map[c.X, c.Y].SurfaceRounds = -1;
                }
            }

            // Explosive OilKegCluster and flammable HayBale in open non-reserved slots without touching existing features
            GridCoord[] kegCandidates =
            {
                new GridCoord(Math.Clamp(brazierX - 1, 3, w - 4), northShoulderY),
                new GridCoord(Math.Clamp(brazierX + 1, 3, w - 4), northShoulderY),
                new GridCoord(Math.Clamp(midX, 3, w - 4), southShoulderY),
                new GridCoord(Math.Clamp(midX - 1, 3, w - 4), southShoulderY),
                new GridCoord(Math.Clamp(midX + 1, 3, w - 4), southShoulderY)
            };
            bool placedKeg = PlaceFirstOpenPropSlot(
                grid,
                map,
                w,
                h,
                kegCandidates,
                isOpen,
                CombatArenaObstacleKind.OilKegCluster,
                blocksMove: true,
                blocksLos: false,
                providesCover: true,
                moveCost: 1);
            if (!placedKeg)
            {
                PlaceFirstInteriorFallbackProp(
                    grid,
                    map,
                    w,
                    h,
                    isOpen,
                    CombatArenaObstacleKind.OilKegCluster,
                    blocksMove: true,
                    blocksLos: false,
                    providesCover: true,
                    moveCost: 1);
            }

            GridCoord[] hayCandidates =
            {
                new GridCoord(Math.Clamp(midX + 1, 3, w - 4), southShoulderY),
                new GridCoord(Math.Clamp(midX - 1, 3, w - 4), southShoulderY),
                new GridCoord(Math.Clamp(midX + 2, 3, w - 4), roadY0),
                new GridCoord(Math.Clamp(midX - 2, 3, w - 4), roadY1)
            };
            bool placedHay = PlaceFirstOpenPropSlot(
                grid,
                map,
                w,
                h,
                hayCandidates,
                isOpen,
                CombatArenaObstacleKind.HayBale,
                blocksMove: false,
                blocksLos: false,
                providesCover: true,
                moveCost: 2);
            if (!placedHay)
            {
                PlaceFirstInteriorFallbackProp(
                    grid,
                    map,
                    w,
                    h,
                    isOpen,
                    CombatArenaObstacleKind.HayBale,
                    blocksMove: false,
                    blocksLos: false,
                    providesCover: true,
                    moveCost: 2);
            }
        }

        private static void BuildRiverbankCrossingTemplate(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            int variant,
            HashSet<GridCoord> reserved,
            Func<int, int, bool> isOpen)
        {
            int midX = w / 2;
            int upperBridgeY = Math.Clamp(2 + (variant == 1 ? 1 : 0), 1, Math.Max(2, (h / 2) - 1));
            int lowerFordY = Math.Clamp(h - 3 - (variant == 2 ? 1 : 0), upperBridgeY + 3, h - 2);

            for (int y = 0; y < h; y++)
            {
                int meander = ((y + variant) % 4 == 1) ? 0 : (((y + variant) % 4 == 2) ? -1 : 0);
                int streamStartX = Math.Clamp(midX - 1 + meander, 3, Math.Max(3, w - 5));
                int streamEndX = Math.Min(w - 3, streamStartX + 1);
                bool isCrossingRow = (y == upperBridgeY || y == lowerFordY);

                for (int x = streamStartX; x <= streamEndX; x++)
                {
                    if (reserved.Contains(new GridCoord(x, y)))
                    {
                        continue;
                    }

                    ResetFeatureOpen(grid, map, x, y, 0, isCrossingRow ? 1 : 2);
                    if (isCrossingRow)
                    {
                        map[x, y].ObstacleKind = CombatArenaObstacleKind.BridgePlank;
                        map[x, y].IsBridgeTile = true;
                        map[x, y].Surface = SurfaceType.None;
                        map[x, y].MoveCost = 1;
                    }
                    else
                    {
                        map[x, y].Surface = SurfaceType.Water;
                        map[x, y].SurfaceRounds = -1;
                        map[x, y].MoveCost = 2;
                    }
                    map[x, y].BlocksFoliage = true;
                }
            }

            // Ensure mud / reed slow zones immediately bordering water banks (leaving bridge approach tips clean)
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 2; x < w - 2; x++)
                {
                    CombatArenaFeature f = map[x, y];
                    if (f.BlocksMovement || f.Surface == SurfaceType.Water || f.IsBridgeTile || reserved.Contains(new GridCoord(x, y)))
                    {
                        continue;
                    }

                    bool adjWater =
                        (x > 0 && map[x - 1, y].Surface == SurfaceType.Water) ||
                        (x + 1 < w && map[x + 1, y].Surface == SurfaceType.Water);
                    bool adjBridge =
                        (x > 0 && map[x - 1, y].IsBridgeTile) ||
                        (x + 1 < w && map[x + 1, y].IsBridgeTile);
                    if (adjWater && !adjBridge)
                    {
                        f.IsMudBank = true;
                        f.MoveCost = 2;
                    }
                }
            }

            // Raised High-Ground rocky banks overlooking the stream on both sides
            GridCoord[] bluffs =
            {
                new GridCoord(2, 1), new GridCoord(3, 1), new GridCoord(3, 2),
                new GridCoord(w - 4, h - 3), new GridCoord(w - 3, h - 2), new GridCoord(w - 4, h - 2)
            };
            for (int i = 0; i < bluffs.Length; i++)
            {
                GridCoord b = bluffs[i];
                if (isOpen(b.X, b.Y) && map[b.X, b.Y].Surface == SurfaceType.None && !map[b.X, b.Y].IsBridgeTile && !map[b.X, b.Y].BlocksMovement)
                {
                    map[b.X, b.Y].ElevationTier = 1;
                    map[b.X, b.Y].IsMudBank = false;
                    map[b.X, b.Y].MoveCost = 1;
                }
            }

            // Flanking bank boulders & brush thickets
            (int x, int y, CombatArenaObstacleKind kind)[] covers =
            {
                (midX - 2, Math.Max(1, upperBridgeY - 1), CombatArenaObstacleKind.BoulderRock),
                (midX + 2, Math.Min(h - 2, lowerFordY + 1), CombatArenaObstacleKind.BoulderRock),
                (midX - 2, lowerFordY - 1, CombatArenaObstacleKind.BrushThicket),
                (midX + 2, upperBridgeY + 1, CombatArenaObstacleKind.BrushThicket)
            };
            for (int i = 0; i < covers.Length; i++)
            {
                var (cx, cy, okind) = covers[i];
                if (isOpen(cx, cy) && map[cx, cy].Surface == SurfaceType.None && !map[cx, cy].IsBridgeTile && !map[cx, cy].BlocksMovement)
                {
                    bool blocks = okind == CombatArenaObstacleKind.BoulderRock;
                    SetObstacleFeature(grid, map, cx, cy, okind, blocksMove: blocks, blocksLos: blocks, providesCover: !blocks, moveCost: blocks ? 1 : 2, elevationTier: map[cx, cy].ElevationTier);
                }
            }
        }

        private static void BuildDeepWoodsTemplate(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            int variant,
            HashSet<GridCoord> reserved,
            Func<int, int, bool> isOpen)
        {
            int midX = w / 2;
            int midY = h / 2;

            // Staggered ancient tree trunks & forest boulders blocking straight ranged LOS
            (int x, int y, CombatArenaObstacleKind kind)[] pillars =
            {
                (midX - 2, 2, CombatArenaObstacleKind.TreeTrunk),
                (midX - 1, midY + (variant == 1 ? 0 : 1), CombatArenaObstacleKind.BoulderRock),
                (midX + 1, 2 + (variant == 2 ? 1 : 0), CombatArenaObstacleKind.TreeTrunk),
                (midX + 2, midY + 1, CombatArenaObstacleKind.TreeTrunk),
                (midX - 2, h - 2, CombatArenaObstacleKind.TreeTrunk),
                (midX + 1, h - 3, CombatArenaObstacleKind.BoulderRock)
            };
            for (int i = 0; i < pillars.Length; i++)
            {
                var (px, py, pkind) = pillars[i];
                if (isOpen(px, py))
                {
                    SetObstacleFeature(grid, map, px, py, pkind, blocksMove: true, blocksLos: true, providesCover: false, moveCost: 1);
                }
            }

            // Mossy elevated root / cliff ridge in north-central & south flanks
            GridCoord[] ridges =
            {
                new GridCoord(midX - 1, 1), new GridCoord(midX, 1), new GridCoord(midX, 2),
                new GridCoord(midX - 1, h - 2), new GridCoord(midX, h - 2)
            };
            for (int i = 0; i < ridges.Length; i++)
            {
                GridCoord r = ridges[i];
                if (isOpen(r.X, r.Y) && !map[r.X, r.Y].BlocksMovement)
                {
                    map[r.X, r.Y].ElevationTier = 1;
                }
            }

            // Flanking brush cover pockets along twisting ambush trails
            GridCoord[] thickets =
            {
                new GridCoord(midX - 2, midY - 1),
                new GridCoord(midX, midY),
                new GridCoord(midX + 2, midY - 1),
                new GridCoord(midX - 1, 3),
                new GridCoord(midX + 2, h - 3)
            };
            for (int i = 0; i < thickets.Length; i++)
            {
                GridCoord c = thickets[i];
                if (isOpen(c.X, c.Y) && !map[c.X, c.Y].BlocksMovement)
                {
                    SetObstacleFeature(grid, map, c.X, c.Y, CombatArenaObstacleKind.BrushThicket, blocksMove: false, blocksLos: false, providesCover: true, moveCost: 2, elevationTier: map[c.X, c.Y].ElevationTier);
                }
            }
        }

        private static void BuildRuinsOutskirtsTemplate(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            int variant,
            HashSet<GridCoord> reserved,
            Func<int, int, bool> isOpen)
        {
            int midX = w / 2;
            int midY = h / 2;

            // Angled crumbled ruined wall segments framing doorway choke & defensive bays
            (int x, int y, CombatArenaObstacleKind kind)[] walls =
            {
                (midX - 2, 2, CombatArenaObstacleKind.RuinedWall),
                (midX - 2, 3, CombatArenaObstacleKind.RuinedWall),
                (midX - 2, h - 3, CombatArenaObstacleKind.RuinedWall),
                (midX + 2, 2 + (variant == 1 ? 1 : 0), CombatArenaObstacleKind.RuinedWall),
                (midX + 2, h - 4, CombatArenaObstacleKind.RuinedWall),
                (midX + 2, h - 3, CombatArenaObstacleKind.RuinedWall)
            };
            for (int i = 0; i < walls.Length; i++)
            {
                var (wx, wy, wkind) = walls[i];
                if (isOpen(wx, wy))
                {
                    SetObstacleFeature(grid, map, wx, wy, wkind, blocksMove: true, blocksLos: true, providesCover: false, moveCost: 1);
                }
            }

            // Stepped ruined courtyard dais overlooking central breach
            GridCoord[] dais =
            {
                new GridCoord(midX - 1, midY - 1),
                new GridCoord(midX, midY - 1),
                new GridCoord(midX, midY),
                new GridCoord(midX - 1, 1)
            };
            for (int i = 0; i < dais.Length; i++)
            {
                GridCoord d = dais[i];
                if (isOpen(d.X, d.Y) && !map[d.X, d.Y].BlocksMovement)
                {
                    map[d.X, d.Y].ElevationTier = 1;
                    map[d.X, d.Y].IsRoadTile = true;
                }
            }

            // Standing braziers & ruined pillar cover
            if (isOpen(midX - 1, 2) && !map[midX - 1, 2].BlocksMovement)
            {
                SetObstacleFeature(grid, map, midX - 1, 2, CombatArenaObstacleKind.LitBrazier, blocksMove: true, blocksLos: false, providesCover: false);
            }
            if (isOpen(midX + 1, h - 3) && !map[midX + 1, h - 3].BlocksMovement)
            {
                SetObstacleFeature(grid, map, midX + 1, h - 3, CombatArenaObstacleKind.LitBrazier, blocksMove: true, blocksLos: false, providesCover: false);
            }
            if (isOpen(midX + 1, midY + 1) && !map[midX + 1, midY + 1].BlocksMovement)
            {
                SetObstacleFeature(grid, map, midX + 1, midY + 1, CombatArenaObstacleKind.BarrelsCrates, blocksMove: false, blocksLos: false, providesCover: true, moveCost: 2);
            }

            // Courtyard Oil and Poison puddles near doorway choke & flank bay
            GridCoord[] oilTiles = { new GridCoord(midX - 1, 3), new GridCoord(midX, 2) };
            for (int i = 0; i < oilTiles.Length; i++)
            {
                GridCoord o = oilTiles[i];
                if (isOpen(o.X, o.Y) && !map[o.X, o.Y].BlocksMovement && map[o.X, o.Y].Surface == SurfaceType.None)
                {
                    map[o.X, o.Y].Surface = SurfaceType.Oil;
                    map[o.X, o.Y].SurfaceRounds = -1;
                }
            }

            GridCoord[] poisonTiles = { new GridCoord(midX, h - 2), new GridCoord(midX + 1, h - 2) };
            for (int i = 0; i < poisonTiles.Length; i++)
            {
                GridCoord p = poisonTiles[i];
                if (isOpen(p.X, p.Y) && !map[p.X, p.Y].BlocksMovement && map[p.X, p.Y].Surface == SurfaceType.None)
                {
                    map[p.X, p.Y].Surface = SurfaceType.Poison;
                    map[p.X, p.Y].SurfaceRounds = -1;
                }
            }

            GridCoord[] ruinsKegCandidates =
            {
                new GridCoord(Math.Clamp(midX + 1, 2, w - 3), 3),
                new GridCoord(Math.Clamp(midX, 2, w - 3), 3),
                new GridCoord(Math.Clamp(midX - 1, 2, w - 3), Math.Max(2, h - 3))
            };
            PlaceFirstOpenPropSlot(
                grid,
                map,
                w,
                h,
                ruinsKegCandidates,
                isOpen,
                CombatArenaObstacleKind.OilKegCluster,
                blocksMove: true,
                blocksLos: false,
                providesCover: true,
                moveCost: 1);
        }

        private static bool CanPlacePropAt(CombatArenaFeature[,] map, int x, int y, Func<int, int, bool> isOpen)
        {
            if (!isOpen(x, y))
            {
                return false;
            }

            CombatArenaFeature f = map[x, y];
            return !f.BlocksMovement &&
                   !f.ProvidesCover &&
                   f.ObstacleKind == CombatArenaObstacleKind.None &&
                   f.Surface == SurfaceType.None &&
                   f.ElevationTier == 0 &&
                   !f.IsBridgeTile;
        }

        private static bool PlaceFirstOpenPropSlot(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            GridCoord[] candidates,
            Func<int, int, bool> isOpen,
            CombatArenaObstacleKind kind,
            bool blocksMove,
            bool blocksLos,
            bool providesCover,
            int moveCost)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                GridCoord c = candidates[i];
                if (c.X >= 1 && c.X <= w - 2 && c.Y >= 1 && c.Y <= h - 2 && CanPlacePropAt(map, c.X, c.Y, isOpen))
                {
                    SetObstacleFeature(grid, map, c.X, c.Y, kind, blocksMove, blocksLos, providesCover, moveCost);
                    return true;
                }
            }

            return false;
        }

        private static bool PlaceFirstInteriorFallbackProp(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            Func<int, int, bool> isOpen,
            CombatArenaObstacleKind kind,
            bool blocksMove,
            bool blocksLos,
            bool providesCover,
            int moveCost)
        {
            int minX = Math.Min(3, Math.Max(1, w / 3));
            int maxX = Math.Max(minX, w - 4);
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (CanPlacePropAt(map, x, y, isOpen))
                    {
                        SetObstacleFeature(grid, map, x, y, kind, blocksMove, blocksLos, providesCover, moveCost);
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ApplyWeatherPuddles(
            CombatArenaFeature[,] map,
            int w,
            int h,
            HashSet<GridCoord> reserved,
            ulong seedVal,
            WeatherState weather)
        {
            if (weather == WeatherState.Clear)
            {
                return;
            }

            List<GridCoord> eligible = new List<GridCoord>(w * h);
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    GridCoord coord = new GridCoord(x, y);
                    if (reserved.Contains(coord))
                    {
                        continue;
                    }

                    CombatArenaFeature f = map[x, y];
                    if (f.BlocksMovement ||
                        f.Surface != SurfaceType.None ||
                        f.ObstacleKind != CombatArenaObstacleKind.None ||
                        f.IsBridgeTile)
                    {
                        continue;
                    }

                    eligible.Add(coord);
                }
            }

            if (eligible.Count == 0)
            {
                return;
            }

            int targetCount = Math.Max(1, (int)Math.Round(eligible.Count * 0.15, MidpointRounding.AwayFromZero));
            ulong state = seedVal ^ ((ulong)((int)weather + 1) * 0x9E3779B97F4A7C15UL);
            for (int i = eligible.Count - 1; i > 0; i--)
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                int j = (int)(z % (ulong)(i + 1));
                GridCoord tmp = eligible[i];
                eligible[i] = eligible[j];
                eligible[j] = tmp;
            }

            int count = Math.Min(targetCount, eligible.Count);
            for (int i = 0; i < count; i++)
            {
                GridCoord c = eligible[i];
                CombatArenaFeature f = map[c.X, c.Y];
                f.Surface = SurfaceType.Water;
                f.SurfaceRounds = -1;
                f.MoveCost = Math.Max(f.MoveCost, 2);
            }
        }

        private static void BuildDungeonChamberTemplate(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            int variant,
            HashSet<GridCoord> reserved,
            Func<int, int, bool> isOpen)
        {
            int midX = w / 2;
            int midY = h / 2;

            // Vaulted entry arch walls + symmetric stone pillar colonnade blocking straight ranged sightlines
            (int x, int y, CombatArenaObstacleKind kind)[] structures =
            {
                (midX - 3, 2, CombatArenaObstacleKind.RuinedWall),
                (midX - 3, h - 3, CombatArenaObstacleKind.RuinedWall),
                (midX - 1, 2, CombatArenaObstacleKind.StonePillar),
                (midX - 1, h - 3, CombatArenaObstacleKind.StonePillar),
                (midX + 2, 2, CombatArenaObstacleKind.StonePillar),
                (midX + 2, h - 3, CombatArenaObstacleKind.StonePillar)
            };
            for (int i = 0; i < structures.Length; i++)
            {
                var (sx, sy, skind) = structures[i];
                if (isOpen(sx, sy))
                {
                    SetObstacleFeature(grid, map, sx, sy, skind, blocksMove: true, blocksLos: true, providesCover: false);
                }
            }

            // Raised stone altar dais overlooking chamber nave
            GridCoord[] altar =
            {
                new GridCoord(midX + 1, midY - 1),
                new GridCoord(midX + 2, midY - 1),
                new GridCoord(midX + 1, midY),
                new GridCoord(midX + 2, midY)
            };
            for (int i = 0; i < altar.Length; i++)
            {
                GridCoord a = altar[i];
                if (isOpen(a.X, a.Y) && !map[a.X, a.Y].BlocksMovement)
                {
                    map[a.X, a.Y].ElevationTier = 1;
                }
            }

            // Wall sconces / braziers flanking nave & altar steps
            GridCoord[] sconces =
            {
                new GridCoord(midX, 1),
                new GridCoord(midX, h - 2)
            };
            for (int i = 0; i < sconces.Length; i++)
            {
                GridCoord s = sconces[i];
                if (isOpen(s.X, s.Y) && !map[s.X, s.Y].BlocksMovement)
                {
                    SetObstacleFeature(grid, map, s.X, s.Y, i == 0 ? CombatArenaObstacleKind.WallSconce : CombatArenaObstacleKind.LitBrazier, blocksMove: true, blocksLos: false, providesCover: false);
                }
            }

            // Hazardous IronGrate drainage floor tiles carrying Oil / Water / Ice
            (int x, int y, SurfaceType surf)[] grates =
            {
                (midX - 2, midY - 1, SurfaceType.Oil),
                (midX - 2, midY, SurfaceType.Oil),
                (midX, midY - 1, SurfaceType.Water),
                (midX, midY + 1, variant == 2 ? SurfaceType.Ice : SurfaceType.Water)
            };
            for (int i = 0; i < grates.Length; i++)
            {
                var (gx, gy, gsurf) = grates[i];
                if (isOpen(gx, gy) && !map[gx, gy].BlocksMovement)
                {
                    map[gx, gy].ObstacleKind = CombatArenaObstacleKind.IronGrate;
                    map[gx, gy].Surface = gsurf;
                    map[gx, gy].SurfaceRounds = -1;
                    map[gx, gy].MoveCost = gsurf == SurfaceType.Ice ? 1 : 2;
                    map[gx, gy].BlocksFoliage = true;
                }
            }
        }

        private static void EnforceSpawnsAndMultiLaneBfs(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            HashSet<GridCoord> reserved,
            out int verifiedLaneCount,
            out bool allConnected)
        {
            // Ensure all reserved spawn tiles are strictly walkable
            foreach (GridCoord rc in reserved)
            {
                if (grid.InBounds(rc.X, rc.Y) && map[rc.X, rc.Y].BlocksMovement)
                {
                    ResetFeatureOpen(grid, map, rc.X, rc.Y, 0, 1);
                }
            }

            List<GridCoord> westAnchors = new List<GridCoord>();
            List<GridCoord> eastAnchors = new List<GridCoord>();
            int midX = w / 2;
            foreach (GridCoord rc in reserved)
            {
                if (rc.X < midX) westAnchors.Add(rc);
                else eastAnchors.Add(rc);
            }
            if (westAnchors.Count == 0) westAnchors.Add(new GridCoord(Math.Min(1, w - 1), h / 2));
            if (eastAnchors.Count == 0) eastAnchors.Add(new GridCoord(Math.Max(0, w - 2), h / 2));

            // 1. Connect any isolated reserved spawn or east/west groups
            for (int i = 0; i < westAnchors.Count; i++)
            {
                GridCoord wa = westAnchors[i];
                if (!HasBfsPath(map, w, h, wa, eastAnchors[0], null))
                {
                    CarveWalkableCorridor(grid, map, w, h, wa, eastAnchors[0]);
                }
            }
            for (int i = 0; i < eastAnchors.Count; i++)
            {
                GridCoord ea = eastAnchors[i];
                if (!HasBfsPath(map, w, h, westAnchors[0], ea, null))
                {
                    CarveWalkableCorridor(grid, map, w, h, westAnchors[0], ea);
                }
            }

            // 2. Verify >= 2 distinct central crossing lanes (disjoint on interior cols x in [2..w-3])
            allConnected = true;
            verifiedLaneCount = 1;
            if (TryFindBfsPath(map, w, h, westAnchors[0], eastAnchors[0], null, out List<GridCoord> laneA))
            {
                HashSet<GridCoord> blockedFirstLane = new HashSet<GridCoord>();
                for (int i = 0; i < laneA.Count; i++)
                {
                    GridCoord step = laneA[i];
                    if (step.X >= 2 && step.X <= w - 3 && !reserved.Contains(step))
                    {
                        blockedFirstLane.Add(step);
                    }
                }

                if (TryFindBfsPath(map, w, h, westAnchors[0], eastAnchors[0], blockedFirstLane, out _))
                {
                    verifiedLaneCount = 2;
                }
                else
                {
                    // Carve a guaranteed secondary flank lane at alternate row (top or bottom row inside arena)
                    int laneRowA = laneA[laneA.Count / 2].Y;
                    int altY = laneRowA < h / 2 ? Math.Max(1, h - 2) : 1;
                    GridCoord startAlt = new GridCoord(westAnchors[0].X, altY);
                    GridCoord endAlt = new GridCoord(eastAnchors[0].X, altY);
                    CarveWalkableCorridor(grid, map, w, h, westAnchors[0], startAlt);
                    CarveWalkableCorridor(grid, map, w, h, startAlt, endAlt);
                    CarveWalkableCorridor(grid, map, w, h, endAlt, eastAnchors[0]);

                    if (TryFindBfsPath(map, w, h, westAnchors[0], eastAnchors[0], blockedFirstLane, out _))
                    {
                        verifiedLaneCount = 2;
                    }
                }
            }
            else
            {
                allConnected = false;
            }
        }

        private static bool HasBfsPath(
            CombatArenaFeature[,] map,
            int w,
            int h,
            GridCoord start,
            GridCoord goal,
            HashSet<GridCoord>? extraForbidden)
        {
            return TryFindBfsPath(map, w, h, start, goal, extraForbidden, out _);
        }

        private static bool TryFindBfsPath(
            CombatArenaFeature[,] map,
            int w,
            int h,
            GridCoord start,
            GridCoord goal,
            HashSet<GridCoord>? extraForbidden,
            out List<GridCoord> path)
        {
            path = new List<GridCoord>();
            if (start.X < 0 || start.X >= w || start.Y < 0 || start.Y >= h ||
                goal.X < 0 || goal.X >= w || goal.Y < 0 || goal.Y >= h ||
                map[start.X, start.Y].BlocksMovement || map[goal.X, goal.Y].BlocksMovement)
            {
                return false;
            }

            Dictionary<GridCoord, GridCoord> parent = new Dictionary<GridCoord, GridCoord>();
            Queue<GridCoord> q = new Queue<GridCoord>();
            parent[start] = start;
            q.Enqueue(start);

            int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };

            while (q.Count > 0)
            {
                GridCoord cur = q.Dequeue();
                if (cur.Equals(goal))
                {
                    GridCoord p = cur;
                    while (!p.Equals(start))
                    {
                        path.Add(p);
                        p = parent[p];
                    }
                    path.Add(start);
                    path.Reverse();
                    return true;
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = cur.X + dxs[d];
                    int ny = cur.Y + dys[d];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    GridCoord next = new GridCoord(nx, ny);
                    if (parent.ContainsKey(next)) continue;
                    if (map[nx, ny].BlocksMovement) continue;
                    if (extraForbidden != null && extraForbidden.Contains(next)) continue;
                    if (Math.Abs(map[nx, ny].ElevationTier - map[cur.X, cur.Y].ElevationTier) > 1) continue;
                    if (d >= 4 && (map[cur.X + dxs[d], cur.Y].BlocksMovement || map[cur.X, cur.Y + dys[d]].BlocksMovement)) continue;

                    parent[next] = cur;
                    q.Enqueue(next);
                }
            }

            return false;
        }

        private static void CarveWalkableCorridor(
            CombatGrid grid,
            CombatArenaFeature[,] map,
            int w,
            int h,
            GridCoord from,
            GridCoord to)
        {
            int cx = from.X;
            int cy = from.Y;
            int guard = w * h * 2;
            while ((cx != to.X || cy != to.Y) && guard-- > 0)
            {
                if (map[cx, cy].BlocksMovement)
                {
                    ResetFeatureOpen(grid, map, cx, cy, 0, 1);
                }
                if (cx != to.X) cx += cx < to.X ? 1 : -1;
                else if (cy != to.Y) cy += cy < to.Y ? 1 : -1;
            }
            if (map[to.X, to.Y].BlocksMovement)
            {
                ResetFeatureOpen(grid, map, to.X, to.Y, 0, 1);
            }
        }

        private static bool IsNearOutdoorRuins(TileMapData map, GridCoord pos, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = pos.X + dx;
                    int ny = pos.Y + dy;
                    if (!map.InBounds(nx, ny) || (nx <= 32 && ny <= 32))
                    {
                        continue;
                    }

                    int decodedId = LandMapData.DecodeTerrainTileIndex(map.GetTile(nx, ny)) + 1;
                    if (decodedId >= 181 && decodedId <= 185)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CountNearbyForestTiles(TileMapData map, GridCoord pos, int radius)
        {
            int count = 0;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = pos.X + dx;
                    int ny = pos.Y + dy;
                    if (map.InBounds(nx, ny) && map.GetTileRule(map.GetTile(nx, ny)).IsForest)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static MonsterDefinition? FindMonsterById(IReadOnlyList<MonsterDefinition> catalog, string id)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                if (string.Equals(catalog[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return catalog[i];
                }
            }

            return null;
        }

        private static MonsterDefinition? FindMonsterByIndex(IReadOnlyList<MonsterDefinition> catalog, int index)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Index == index)
                {
                    return catalog[i];
                }
            }

            return null;
        }
    }
}
