using System;
using System.Collections.Generic;
using RealmzCore.Rules;

namespace RealmzCore.Data
{
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public int ChebyshevDistance(GridCoord other)
        {
            return Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
        }

        public int ManhattanDistance(GridCoord other)
        {
            return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        }

        public bool Equals(GridCoord other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(GridCoord left, GridCoord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridCoord left, GridCoord right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }
    }

    public sealed class TileRule
    {
        public TileRule(
            int tileId,
            bool passable = true,
            int moveCost = 1,
            bool blocksLos = false,
            bool isForest = false,
            bool isPath = false,
            bool needsBoat = false,
            int soundId = 0)
        {
            TileId = tileId;
            Passable = passable;
            MoveCost = moveCost < 1 ? 1 : moveCost;
            BlocksLos = blocksLos;
            IsForest = isForest;
            IsPath = isPath;
            NeedsBoat = needsBoat;
            SoundId = soundId;
        }

        public int TileId { get; }
        public bool Passable { get; }
        public int MoveCost { get; }
        public bool BlocksLos { get; }
        public bool IsForest { get; }
        public bool IsPath { get; }
        public bool NeedsBoat { get; }
        public int SoundId { get; }
    }

    public sealed class MapPortal
    {
        public MapPortal(
            string id,
            int x,
            int y,
            string destMapId,
            int destX,
            int destY,
            string destFacing = "South",
            string label = "")
        {
            Id = id ?? string.Empty;
            X = x;
            Y = y;
            DestMapId = destMapId ?? string.Empty;
            DestX = destX;
            DestY = destY;
            DestFacing = destFacing ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public string Id { get; }
        public int X { get; }
        public int Y { get; }
        public string DestMapId { get; }
        public int DestX { get; }
        public int DestY { get; }
        public string DestFacing { get; }
        public string Label { get; }

        public GridCoord Coord => new GridCoord(X, Y);
        public GridCoord DestCoord => new GridCoord(DestX, DestY);
    }

    public sealed class MapPointOfInterest
    {
        public MapPointOfInterest(
            string id,
            string kind,
            int x,
            int y,
            string label = "",
            string shopId = "")
        {
            Id = id ?? string.Empty;
            Kind = kind ?? string.Empty;
            X = x;
            Y = y;
            Label = label ?? string.Empty;
            ShopId = shopId ?? string.Empty;
        }

        public string Id { get; }
        public string Kind { get; }
        public int X { get; }
        public int Y { get; }
        public string Label { get; }
        public string ShopId { get; }

        public GridCoord Coord => new GridCoord(X, Y);
    }

    public sealed class EncounterZoneData
    {
        private readonly int[] _battleIds;
        private readonly int[] _monsterIndices;

        public EncounterZoneData(
            int slot,
            int top,
            int left,
            int bottom,
            int right,
            int percent,
            IReadOnlyList<int>? battleIds = null,
            IReadOnlyList<int>? monsterIndices = null)
        {
            Slot = slot;
            Top = top;
            Left = left;
            Bottom = bottom;
            Right = right;
            Percent = percent;
            _battleIds = CopyArray(battleIds);
            _monsterIndices = CopyArray(monsterIndices);
        }

        public int Slot { get; }
        public int Top { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Percent { get; }
        public IReadOnlyList<int> BattleIds => _battleIds;
        public IReadOnlyList<int> MonsterIndices => _monsterIndices;

        public bool Contains(int x, int y)
        {
            return x >= Left && x <= Right && y >= Top && y <= Bottom;
        }

        public bool Contains(GridCoord coord)
        {
            return Contains(coord.X, coord.Y);
        }

        private static int[] CopyArray(IReadOnlyList<int>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<int>();
            }

            int[] copy = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }

    public sealed class TileMapData
    {
        private readonly int[] _tiles;
        private readonly Dictionary<int, TileRule> _tileRules;
        private readonly HashSet<int> _solidNegativeIds;
        private readonly MapPortal[] _portals;
        private readonly MapPointOfInterest[] _pointsOfInterest;
        private readonly EncounterZoneData[] _encounterZones;
        private readonly HashSet<GridCoord> _blockedPropTiles = new HashSet<GridCoord>();

        public TileMapData(
            string mapId,
            string scenario,
            int level,
            bool isDungeon,
            int width,
            int height,
            int[] tiles,
            GridCoord startSpawn,
            IReadOnlyDictionary<int, TileRule>? tileRules = null,
            IEnumerable<int>? solidNegativeIds = null,
            IEnumerable<MapPortal>? portals = null,
            IEnumerable<MapPointOfInterest>? pointsOfInterest = null,
            IEnumerable<EncounterZoneData>? encounterZones = null)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (tiles == null)
            {
                throw new ArgumentNullException(nameof(tiles));
            }

            if (tiles.Length != width * height)
            {
                throw new ArgumentException(
                    $"Expected {width * height} tiles for {width}x{height} map, got {tiles.Length}.",
                    nameof(tiles));
            }

            MapId = mapId ?? string.Empty;
            Scenario = scenario ?? string.Empty;
            Level = level;
            IsDungeon = isDungeon;
            Width = width;
            Height = height;
            _tiles = (int[])tiles.Clone();
            StartSpawn = startSpawn;

            _tileRules = new Dictionary<int, TileRule>();
            if (tileRules != null)
            {
                foreach (KeyValuePair<int, TileRule> kvp in tileRules)
                {
                    _tileRules[kvp.Key] = kvp.Value;
                }
            }

            _solidNegativeIds = new HashSet<int>();
            if (solidNegativeIds != null)
            {
                foreach (int id in solidNegativeIds)
                {
                    _solidNegativeIds.Add(id);
                }
            }

            _portals = portals != null ? new List<MapPortal>(portals).ToArray() : Array.Empty<MapPortal>();
            _pointsOfInterest = pointsOfInterest != null
                ? new List<MapPointOfInterest>(pointsOfInterest).ToArray()
                : Array.Empty<MapPointOfInterest>();
            _encounterZones = encounterZones != null
                ? new List<EncounterZoneData>(encounterZones).ToArray()
                : Array.Empty<EncounterZoneData>();
        }

        public string MapId { get; }
        public string Scenario { get; }
        public int Level { get; }
        public bool IsDungeon { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<int> Tiles => _tiles;
        public GridCoord StartSpawn { get; }
        public IReadOnlyList<MapPortal> Portals => _portals;
        public IReadOnlyList<MapPointOfInterest> PointsOfInterest => _pointsOfInterest;
        public IReadOnlyList<EncounterZoneData> EncounterZones => _encounterZones;
        public IReadOnlyDictionary<int, TileRule> TileRules => _tileRules;
        public int BlockedPropTileCount => _blockedPropTiles.Count;

        public void SetBlockedPropFootprints(IEnumerable<GridCoord>? coords)
        {
            _blockedPropTiles.Clear();
            if (coords == null)
            {
                return;
            }

            foreach (GridCoord coord in coords)
            {
                if (InBounds(coord))
                {
                    _blockedPropTiles.Add(coord);
                }
            }
        }

        public void ClearBlockedPropFootprints()
        {
            _blockedPropTiles.Clear();
        }

        public bool IsBlockedByProp(int x, int y) => _blockedPropTiles.Contains(new GridCoord(x, y));

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool InBounds(GridCoord coord)
        {
            return InBounds(coord.X, coord.Y);
        }

        public int GetTile(int x, int y)
        {
            if (!InBounds(x, y))
            {
                throw new ArgumentOutOfRangeException($"({x}, {y}) is outside {Width}x{Height} map.");
            }

            return _tiles[y * Width + x];
        }

        public TileRule GetTileRule(int rawTile)
        {
            // Direct match first (handles dungeon_0 bitfields and explicit raw overrides).
            if (_tileRules.TryGetValue(rawTile, out TileRule? directRule))
            {
                return directRule;
            }

            if (rawTile < 0)
            {
                int baseMag = rawTile > -1000 ? -rawTile : 0;
                bool isSolid = _solidNegativeIds.Contains(rawTile) ||
                               (baseMag > 0 && _solidNegativeIds.Contains(baseMag));
                return new TileRule(
                    tileId: rawTile,
                    passable: !isSolid,
                    moveCost: 1,
                    blocksLos: isSolid);
            }

            int decodedIdx = LandMapData.DecodeTerrainTileIndex(rawTile);
            if (decodedIdx >= 0)
            {
                int terrainId = decodedIdx + 1;
                if (_tileRules.TryGetValue(terrainId, out TileRule? terrainRule))
                {
                    return terrainRule;
                }

                bool hasPathBit = (rawTile & 0x2000) != 0;
                return new TileRule(
                    tileId: terrainId,
                    passable: true,
                    moveCost: 1,
                    blocksLos: false,
                    isForest: false,
                    isPath: hasPathBit);
            }

            return new TileRule(rawTile, passable: true, moveCost: 1);
        }

        public bool IsPassable(int x, int y, bool hasBoat = false)
        {
            if (!InBounds(x, y))
            {
                return false;
            }

            if (_blockedPropTiles.Contains(new GridCoord(x, y)))
            {
                return false;
            }

            TileRule rule = GetTileRule(GetTile(x, y));
            if (rule.NeedsBoat)
            {
                return hasBoat;
            }

            return rule.Passable;
        }

        public int GetMoveCost(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return int.MaxValue;
            }

            TileRule rule = GetTileRule(GetTile(x, y));
            return rule.MoveCost < 1 ? 1 : rule.MoveCost;
        }

        public bool BlocksLineOfSight(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return true;
            }

            return GetTileRule(GetTile(x, y)).BlocksLos;
        }

        public MapPortal? GetPortalAt(int x, int y)
        {
            for (int i = 0; i < _portals.Length; i++)
            {
                MapPortal portal = _portals[i];
                if (portal.X == x && portal.Y == y)
                {
                    return portal;
                }
            }

            return null;
        }

        public MapPointOfInterest? GetPointOfInterestAt(int x, int y)
        {
            for (int i = 0; i < _pointsOfInterest.Length; i++)
            {
                MapPointOfInterest poi = _pointsOfInterest[i];
                if (poi.X == x && poi.Y == y)
                {
                    return poi;
                }
            }

            return null;
        }

        public IReadOnlyList<EncounterZoneData> GetZonesAt(int x, int y)
        {
            List<EncounterZoneData> matches = new List<EncounterZoneData>();
            for (int i = 0; i < _encounterZones.Length; i++)
            {
                if (_encounterZones[i].Contains(x, y))
                {
                    matches.Add(_encounterZones[i]);
                }
            }

            return matches;
        }
    }

    public sealed class ItemDefinition
    {
        public ItemDefinition(
            int id,
            string name,
            string description = "",
            string category = "",
            string slot = "",
            string damageDice = "",
            int weaponBonus = 0,
            int armorClassBonus = 0,
            int movementMod = 0,
            int range = 1,
            int weight = 0,
            int basePrice = 0,
            int charges = 0,
            int? linkedSpellId = null,
            string healDice = "",
            bool isTwoHanded = false,
            bool isJunk = false)
        {
            Id = id;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Category = category ?? string.Empty;
            Slot = slot ?? string.Empty;
            DamageDice = damageDice ?? string.Empty;
            WeaponBonus = weaponBonus;
            ArmorClassBonus = armorClassBonus;
            MovementMod = movementMod;
            Range = range;
            Weight = weight;
            BasePrice = basePrice;
            Charges = charges;
            LinkedSpellId = linkedSpellId;
            HealDice = healDice ?? string.Empty;
            IsTwoHanded = isTwoHanded;
            IsJunk = isJunk;
        }

        public int Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }
        public string Slot { get; }
        public string DamageDice { get; }
        public int WeaponBonus { get; }
        public int ArmorClassBonus { get; }
        public int MovementMod { get; }
        public int Range { get; }
        public int Weight { get; }
        public int BasePrice { get; }
        public int Charges { get; }
        public int? LinkedSpellId { get; }
        public string HealDice { get; }
        public bool IsTwoHanded { get; }
        public bool IsJunk { get; }
    }

    public sealed class SpellAreaTemplate
    {
        private readonly bool[][] _grid7x7;
        private readonly GridCoord[] _offsets;

        public SpellAreaTemplate(int index, bool[][] grid7x7)
        {
            if (grid7x7 == null)
            {
                throw new ArgumentNullException(nameof(grid7x7));
            }

            Index = index;
            _grid7x7 = new bool[7][];
            List<GridCoord> offsets = new List<GridCoord>();

            for (int y = 0; y < 7; y++)
            {
                _grid7x7[y] = new bool[7];
                bool[]? srcRow = y < grid7x7.Length ? grid7x7[y] : null;
                for (int x = 0; x < 7; x++)
                {
                    bool active = srcRow != null && x < srcRow.Length && srcRow[x];
                    _grid7x7[y][x] = active;
                    if (active)
                    {
                        offsets.Add(new GridCoord(x - 3, y - 3));
                    }
                }
            }

            _offsets = offsets.ToArray();
        }

        public int Index { get; }
        public IReadOnlyList<IReadOnlyList<bool>> Grid7x7 => _grid7x7;
        public IReadOnlyList<GridCoord> Offsets => _offsets;
    }

    public sealed class SpellDefinition
    {
        private readonly GridCoord[] _aoeOffsets;

        public SpellDefinition(
            int id,
            string key,
            string name,
            string alias = "",
            string description = "",
            string school = "",
            int level = 1,
            int spCost = 0,
            int range = 0,
            string targetMode = "Single",
            int spellAreaIndex = -1,
            IReadOnlyList<GridCoord>? aoeOffsets = null,
            string effectKind = "Damage",
            string effectDice = "",
            string damageType = "",
            CharConditionId? appliedCondition = null,
            int conditionDuration = 0,
            int baseRecoveryPct = 20,
            string saveKind = "Magic",
            string createdSurface = "",
            int createdSurfaceRounds = 0)
        {
            Id = id;
            Key = key ?? string.Empty;
            Name = name ?? string.Empty;
            Alias = alias ?? string.Empty;
            Description = description ?? string.Empty;
            School = school ?? string.Empty;
            Level = level;
            SpCost = spCost;
            Range = range;
            TargetMode = targetMode ?? "Single";
            SpellAreaIndex = spellAreaIndex;

            if (aoeOffsets != null && aoeOffsets.Count > 0)
            {
                _aoeOffsets = new GridCoord[aoeOffsets.Count];
                for (int i = 0; i < aoeOffsets.Count; i++)
                {
                    _aoeOffsets[i] = aoeOffsets[i];
                }
            }
            else
            {
                _aoeOffsets = Array.Empty<GridCoord>();
            }

            EffectKind = effectKind ?? "Damage";
            EffectDice = effectDice ?? string.Empty;
            DamageType = damageType ?? string.Empty;
            AppliedCondition = appliedCondition;
            ConditionDuration = conditionDuration;
            BaseRecoveryPct = baseRecoveryPct;
            SaveKind = saveKind ?? "Magic";
            CreatedSurface = createdSurface ?? string.Empty;
            CreatedSurfaceRounds = createdSurfaceRounds;
        }

        public int Id { get; }
        public string Key { get; }
        public string Name { get; }
        public string Alias { get; }
        public string Description { get; }
        public string School { get; }
        public int Level { get; }
        public int SpCost { get; }
        public int Range { get; }
        public string TargetMode { get; }
        public int SpellAreaIndex { get; }
        public IReadOnlyList<GridCoord> AoeOffsets => _aoeOffsets;
        public string EffectKind { get; }
        public string EffectDice { get; }
        public string DamageType { get; }
        public CharConditionId? AppliedCondition { get; }
        public int ConditionDuration { get; }
        public int BaseRecoveryPct { get; }
        public string SaveKind { get; }
        public string CreatedSurface { get; }
        public int CreatedSurfaceRounds { get; }
    }

    public sealed class MonsterDefinition
    {
        private readonly int[] _spellIds;
        private readonly int[] _saves;
        private readonly bool[] _spellImmunities;
        private readonly int[] _lootItemIds;

        public MonsterDefinition(
            int index,
            string id,
            string name,
            int iconId = 0,
            int hitDice = 1,
            int hpBonus = 0,
            int hpMin = 1,
            int hpMax = 8,
            int hpDefault = 5,
            int ac = 10,
            int toHitBonus = 0,
            int dexterity = 10,
            int movementMax = 6,
            int size = 1,
            int numAttacks = 1,
            string attackDice = "1d4",
            int damageBonus = 0,
            int missileChancePct = 0,
            int castChancePct = 0,
            int rangedRange = 0,
            string rangedDice = "",
            IReadOnlyList<int>? spellIds = null,
            int magicResistance = 0,
            IReadOnlyList<int>? saves = null,
            IReadOnlyList<bool>? spellImmunities = null,
            bool isEvil = false,
            bool isUndead = false,
            int xpReward = 0,
            int goldMin = 0,
            int goldMax = 0,
            IReadOnlyList<int>? lootItemIds = null)
        {
            Index = index;
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            IconId = iconId;
            HitDice = hitDice;
            HpBonus = hpBonus;
            HpMin = hpMin;
            HpMax = hpMax;
            HpDefault = hpDefault;
            Ac = ac;
            ToHitBonus = toHitBonus;
            Dexterity = dexterity;
            MovementMax = movementMax;
            Size = size;
            NumAttacks = numAttacks;
            AttackDice = string.IsNullOrWhiteSpace(attackDice) ? "1d4" : attackDice;
            DamageBonus = damageBonus;
            MissileChancePct = missileChancePct;
            CastChancePct = castChancePct;
            RangedRange = rangedRange;
            RangedDice = rangedDice ?? string.Empty;
            _spellIds = CopyIntList(spellIds);
            MagicResistance = magicResistance;
            _saves = CopyFixedIntList(saves, 6);
            _spellImmunities = CopyFixedBoolList(spellImmunities, 6);
            IsEvil = isEvil;
            IsUndead = isUndead;
            XpReward = xpReward;
            GoldMin = goldMin;
            GoldMax = goldMax;
            _lootItemIds = CopyIntList(lootItemIds);
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public int IconId { get; }
        public int HitDice { get; }
        public int HpBonus { get; }
        public int HpMin { get; }
        public int HpMax { get; }
        public int HpDefault { get; }
        public int Ac { get; }
        public int ToHitBonus { get; }
        public int Dexterity { get; }
        public int MovementMax { get; }
        public int Size { get; }
        public int NumAttacks { get; }
        public string AttackDice { get; }
        public int DamageBonus { get; }
        public int MissileChancePct { get; }
        public int CastChancePct { get; }
        public int RangedRange { get; }
        public string RangedDice { get; }
        public IReadOnlyList<int> SpellIds => _spellIds;
        public int MagicResistance { get; }
        public IReadOnlyList<int> Saves => _saves;
        public IReadOnlyList<bool> SpellImmunities => _spellImmunities;
        public bool IsEvil { get; }
        public bool IsUndead { get; }
        public int XpReward { get; }
        public int GoldMin { get; }
        public int GoldMax { get; }
        public IReadOnlyList<int> LootItemIds => _lootItemIds;

        private static int[] CopyIntList(IReadOnlyList<int>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<int>();
            }

            int[] result = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        private static int[] CopyFixedIntList(IReadOnlyList<int>? source, int length)
        {
            int[] result = new int[length];
            if (source != null)
            {
                int count = Math.Min(length, source.Count);
                for (int i = 0; i < count; i++)
                {
                    result[i] = source[i];
                }
            }

            return result;
        }

        private static bool[] CopyFixedBoolList(IReadOnlyList<bool>? source, int length)
        {
            bool[] result = new bool[length];
            if (source != null)
            {
                int count = Math.Min(length, source.Count);
                for (int i = 0; i < count; i++)
                {
                    result[i] = source[i];
                }
            }

            return result;
        }
    }

    public sealed class HeroPresetData
    {
        private readonly int[] _equippedIds;
        private readonly int[] _knownSpellIds;

        public HeroPresetData(
            string id,
            string name,
            string archetype = "",
            int raceId = 0,
            string raceName = "",
            int casteId = 0,
            string casteName = "",
            int level = 1,
            int hp = 10,
            int maxHp = 10,
            int sp = 0,
            int maxSp = 0,
            int ac = 10,
            int toHit = 10,
            int dodge = 10,
            int missile = 0,
            int movementMax = 6,
            double attacksPerRound = 1.0,
            int iconId = 0,
            int portraitId = 0,
            int brawn = 10,
            int knowledge = 10,
            int judgment = 10,
            int agility = 10,
            int vitality = 10,
            int luck = 10,
            IReadOnlyList<int>? equippedIds = null,
            IReadOnlyList<int>? knownSpellIds = null,
            int xp = 0)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Archetype = archetype ?? string.Empty;
            RaceId = raceId;
            RaceName = raceName ?? string.Empty;
            CasteId = casteId;
            CasteName = casteName ?? string.Empty;
            Level = level;
            Xp = Math.Max(0, xp);
            Hp = hp;
            MaxHp = maxHp;
            Sp = sp;
            MaxSp = maxSp;
            Ac = ac;
            ToHit = toHit;
            Dodge = dodge;
            Missile = missile;
            MovementMax = movementMax;
            AttacksPerRound = attacksPerRound;
            IconId = iconId;
            PortraitId = portraitId;
            Brawn = brawn;
            Knowledge = knowledge;
            Judgment = judgment;
            Agility = agility;
            Vitality = vitality;
            Luck = luck;

            if (equippedIds != null && equippedIds.Count > 0)
            {
                _equippedIds = new int[equippedIds.Count];
                for (int i = 0; i < equippedIds.Count; i++)
                {
                    _equippedIds[i] = equippedIds[i];
                }
            }
            else
            {
                _equippedIds = Array.Empty<int>();
            }

            if (knownSpellIds != null && knownSpellIds.Count > 0)
            {
                _knownSpellIds = new int[knownSpellIds.Count];
                for (int i = 0; i < knownSpellIds.Count; i++)
                {
                    _knownSpellIds[i] = knownSpellIds[i];
                }
            }
            else
            {
                _knownSpellIds = Array.Empty<int>();
            }
        }

        public string Id { get; }
        public string Name { get; }
        public string Archetype { get; }
        public int RaceId { get; }
        public string RaceName { get; }
        public int CasteId { get; }
        public string CasteName { get; }
        public int Level { get; }
        public int Xp { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public int Sp { get; }
        public int MaxSp { get; }
        public int Ac { get; }
        public int ToHit { get; }
        public int Dodge { get; }
        public int Missile { get; }
        public int MovementMax { get; }
        public double AttacksPerRound { get; }
        public int IconId { get; }
        public int PortraitId { get; }
        public int Brawn { get; }
        public int Knowledge { get; }
        public int Judgment { get; }
        public int Agility { get; }
        public int Vitality { get; }
        public int Luck { get; }
        public IReadOnlyList<int> EquippedIds => _equippedIds;
        public IReadOnlyList<int> KnownSpellIds => _knownSpellIds;
    }

    public sealed class ShopStockItem
    {
        public ShopStockItem(int itemId, int price, int quantity = -1)
        {
            ItemId = itemId;
            Price = price;
            Quantity = quantity;
        }

        public int ItemId { get; }
        public int Price { get; }
        public int Quantity { get; }
    }

    public sealed class ShopDefinition
    {
        private readonly ShopStockItem[] _stock;

        public ShopDefinition(
            string shopId,
            string name,
            int inflationPct = 0,
            IReadOnlyList<ShopStockItem>? stock = null)
        {
            ShopId = shopId ?? string.Empty;
            Name = name ?? string.Empty;
            InflationPct = inflationPct;

            if (stock != null && stock.Count > 0)
            {
                _stock = new ShopStockItem[stock.Count];
                for (int i = 0; i < stock.Count; i++)
                {
                    _stock[i] = stock[i];
                }
            }
            else
            {
                _stock = Array.Empty<ShopStockItem>();
            }
        }

        public string ShopId { get; }
        public string Name { get; }
        public int InflationPct { get; }
        public IReadOnlyList<ShopStockItem> Stock => _stock;
    }

    public sealed class BywaterM1Bundle
    {
        private readonly Dictionary<string, TileMapData> _maps;
        private readonly List<HeroPresetData> _party;
        private readonly List<int> _initialStashItemIds;
        private readonly List<MonsterDefinition> _monsters;
        private readonly List<SpellAreaTemplate> _spellAreas;
        private readonly Dictionary<int, SpellDefinition> _spells;
        private readonly Dictionary<string, SpellDefinition> _spellsByKey;
        private readonly Dictionary<int, ItemDefinition> _items;
        private readonly Dictionary<string, ShopDefinition> _shops;

        public BywaterM1Bundle(
            IReadOnlyDictionary<string, TileMapData> maps,
            IReadOnlyList<HeroPresetData> party,
            IReadOnlyList<int> initialStashItemIds,
            int initialGold,
            IReadOnlyList<MonsterDefinition> monsters,
            IReadOnlyList<SpellAreaTemplate> spellAreas,
            IReadOnlyDictionary<int, SpellDefinition> spells,
            IReadOnlyDictionary<int, ItemDefinition> items,
            IReadOnlyDictionary<string, ShopDefinition> shops)
        {
            _maps = new Dictionary<string, TileMapData>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, TileMapData> kvp in maps)
            {
                _maps[kvp.Key] = kvp.Value;
            }

            _party = new List<HeroPresetData>(party);
            _initialStashItemIds = new List<int>(initialStashItemIds);
            InitialGold = initialGold;
            _monsters = new List<MonsterDefinition>(monsters);
            _spellAreas = new List<SpellAreaTemplate>(spellAreas);
            _spells = new Dictionary<int, SpellDefinition>(spells);
            _spellsByKey = new Dictionary<string, SpellDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (SpellDefinition spell in spells.Values)
            {
                if (!string.IsNullOrEmpty(spell.Key))
                {
                    _spellsByKey[spell.Key] = spell;
                }
            }

            _items = new Dictionary<int, ItemDefinition>(items);
            _shops = new Dictionary<string, ShopDefinition>(shops, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, TileMapData> Maps => _maps;
        public TileMapData? Land0 => _maps.TryGetValue("land_0", out TileMapData? m) ? m : null;
        public TileMapData? Dungeon0 => _maps.TryGetValue("dungeon_0", out TileMapData? m) ? m : null;
        public IReadOnlyList<HeroPresetData> Party => _party;
        public IReadOnlyList<int> InitialStashItemIds => _initialStashItemIds;
        public int InitialGold { get; }
        public IReadOnlyList<MonsterDefinition> Monsters => _monsters;
        public IReadOnlyList<SpellAreaTemplate> SpellAreas => _spellAreas;
        public IReadOnlyDictionary<int, SpellDefinition> Spells => _spells;
        public IReadOnlyDictionary<string, SpellDefinition> SpellsByKey => _spellsByKey;
        public IReadOnlyDictionary<int, ItemDefinition> Items => _items;
        public IReadOnlyDictionary<string, ShopDefinition> Shops => _shops;
    }
}
