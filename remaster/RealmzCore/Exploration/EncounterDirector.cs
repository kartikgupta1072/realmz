using System;
using System.Collections.Generic;
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
        public const int TurnsPerPhase = 18;
        public const int PhasesPerDay = 4;
        public const int TurnsPerDay = TurnsPerPhase * PhasesPerDay; // 72 turns per full day
        public const int DefaultStartTurns = TurnsPerPhase; // Morning Day 1 (Noon phase)

        public WorldClock(int totalTurns)
        {
            TotalTurns = Math.Max(0, totalTurns);
        }

        public int TotalTurns { get; }
        public int DayNumber => (TotalTurns / TurnsPerDay) + 1;
        public int PhaseIndex => (TotalTurns / TurnsPerPhase) % PhasesPerDay;
        public WorldTimePhase Phase => (WorldTimePhase)PhaseIndex;
        public int TurnInPhase => TotalTurns % TurnsPerPhase;
        public int TurnsUntilNextPhase => TurnsPerPhase - TurnInPhase;
        public int NextPhaseStartTurn => ((TotalTurns / TurnsPerPhase) + 1) * TurnsPerPhase;
        public float PhaseProgress01 => TurnInPhase / (float)TurnsPerPhase;
        public bool IsNight => Phase == WorldTimePhase.Night;

        public string PhaseLabel
        {
            get
            {
                switch (Phase)
                {
                    case WorldTimePhase.Dawn: return "Dawn";
                    case WorldTimePhase.Day: return "Noon";
                    case WorldTimePhase.Dusk: return "Dusk";
                    case WorldTimePhase.Night: return "Night";
                    default: return "Noon";
                }
            }
        }

        public string DisplayLabel => $"Day {DayNumber} - {PhaseLabel}";

        public static WorldClock FromTurns(int totalTurns) => new WorldClock(totalTurns);

        public bool Equals(WorldClock other) => TotalTurns == other.TotalTurns;
        public override bool Equals(object? obj) => obj is WorldClock other && Equals(other);
        public override int GetHashCode() => TotalTurns;
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

        public static void ApplyBiomeArenaFeatures(
            CombatGrid grid,
            EncounterGeographyInfo? geo,
            IReadOnlyCollection<GridCoord>? reservedCoords,
            IDiceRoller? roller)
        {
            if (grid == null || geo == null)
            {
                return;
            }

            HashSet<GridCoord> reserved = reservedCoords != null
                ? new HashSet<GridCoord>(reservedCoords)
                : new HashSet<GridCoord>();

            bool IsAvailableForFeature(int x, int y)
            {
                if (!grid.InBounds(x, y) || !grid.IsWalkable(x, y))
                {
                    return false;
                }

                GridCoord c = new GridCoord(x, y);
                if (reserved.Contains(c))
                {
                    return false;
                }

                return x >= 2 && x <= grid.Width - 3 && y >= 1 && y <= grid.Height - 2;
            }

            if (geo.IsNearWater || geo.BiomeKind == EncounterBiomeKind.Riverbank)
            {
                int waterCount = 0;
                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (grid.GetSurface(x, y) == SurfaceType.Water)
                        {
                            waterCount++;
                        }
                    }
                }

                if (waterCount < 4)
                {
                    int midX = grid.Width / 2;
                    int midY = grid.Height / 2;
                    GridCoord[] poolCandidates =
                    {
                        new GridCoord(midX - 1, midY),
                        new GridCoord(midX, midY),
                        new GridCoord(midX, midY - 1),
                        new GridCoord(midX + 1, midY),
                        new GridCoord(midX - 1, midY + 1),
                        new GridCoord(midX, midY + 1),
                        new GridCoord(midX + 1, midY - 1)
                    };

                    for (int i = 0; i < poolCandidates.Length && waterCount < 5; i++)
                    {
                        GridCoord c = poolCandidates[i];
                        if (IsAvailableForFeature(c.X, c.Y) && grid.GetSurface(c.X, c.Y) == SurfaceType.None)
                        {
                            grid.SetSurface(c.X, c.Y, SurfaceType.Water, -1);
                            grid.SetMoveCost(c.X, c.Y, 2);
                            waterCount++;
                        }
                    }
                }

                // Bias riverbank mud border tiles around water with slower footing (moveCost=2)
                for (int y = 1; y < grid.Height - 1; y++)
                {
                    for (int x = 2; x < grid.Width - 2; x++)
                    {
                        if (!grid.IsWalkable(x, y) || grid.GetSurface(x, y) != SurfaceType.None)
                        {
                            continue;
                        }

                        if (grid.GetSurface(x - 1, y) == SurfaceType.Water ||
                            grid.GetSurface(x + 1, y) == SurfaceType.Water ||
                            grid.GetSurface(x, y - 1) == SurfaceType.Water ||
                            grid.GetSurface(x, y + 1) == SurfaceType.Water)
                        {
                            grid.SetMoveCost(x, y, Math.Max(2, grid.GetMoveCost(x, y)));
                        }
                    }
                }
            }
            else if (geo.BiomeKind == EncounterBiomeKind.DeepForest)
            {
                int existingCover = 0;
                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (grid.ProvidesCover(x, y))
                        {
                            existingCover++;
                        }
                    }
                }

                int midX = grid.Width / 2;
                int midY = grid.Height / 2;
                GridCoord[] thicketSpots =
                {
                    new GridCoord(midX - 1, 2),
                    new GridCoord(midX + 1, 3),
                    new GridCoord(midX - 2, midY + 1),
                    new GridCoord(midX + 1, grid.Height - 3),
                    new GridCoord(midX, midY - 1),
                    new GridCoord(midX - 1, grid.Height - 2)
                };

                for (int i = 0; i < thicketSpots.Length && existingCover < 5; i++)
                {
                    GridCoord c = thicketSpots[i];
                    if (IsAvailableForFeature(c.X, c.Y) && !grid.ProvidesCover(c.X, c.Y))
                    {
                        grid.SetCover(c.X, c.Y, moveCost: 2, blocksLos: false);
                        existingCover++;
                    }
                }
            }
            else if (geo.BiomeKind == EncounterBiomeKind.Ruins || geo.BiomeKind == EncounterBiomeKind.DungeonApproach)
            {
                int midX = grid.Width / 2;
                GridCoord[] ruinPillars =
                {
                    new GridCoord(midX - 1, 2),
                    new GridCoord(midX + 1, grid.Height - 3)
                };

                for (int i = 0; i < ruinPillars.Length; i++)
                {
                    GridCoord c = ruinPillars[i];
                    if (IsAvailableForFeature(c.X, c.Y) && !grid.ProvidesCover(c.X, c.Y))
                    {
                        grid.SetCover(c.X, c.Y, moveCost: 2, blocksLos: false);
                    }
                }
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
