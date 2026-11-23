using System;
using System.Collections.Generic;

namespace RealmzCore.Data
{
    public sealed class LandMapData
    {
        public const int DefaultWidth = 90;
        public const int DefaultHeight = 90;

        private readonly int[] _tiles;

        public LandMapData(
            int width,
            int height,
            int[] tiles,
            int landLookId = 0,
            bool isDark = false,
            bool useLineOfSight = false,
            int startX = 0,
            int startY = 0)
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

            Width = width;
            Height = height;
            _tiles = (int[])tiles.Clone();
            LandLookId = landLookId;
            IsDark = isDark;
            UseLineOfSight = useLineOfSight;
            StartX = startX;
            StartY = startY;
        }

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<int> Tiles => _tiles;
        public int LandLookId { get; }
        public bool IsDark { get; }
        public bool UseLineOfSight { get; }
        public int StartX { get; }
        public int StartY { get; }

        public int GetTile(int x, int y)
        {
            if (x < 0 || x >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return _tiles[y * Width + x];
        }

        public int[] CopyTilesArray()
        {
            return (int[])_tiles.Clone();
        }

        /// <summary>
        /// Decodes a raw map cell ID into a 0-based terrain sprite sheet index (0..199),
        /// following Realmz fastplot.c and centerpict.c conventions.
        /// Returns -1 for negative special icon IDs or non-terrain CIcon IDs (> 200).
        /// </summary>
        public static int DecodeTerrainTileIndex(int rawTileId)
        {
            if (rawTileId < 0)
            {
                return -1;
            }

            // Clear Mac 68k big-endian bit 1 (note marker, 0x4000) and bit 2 (path marker, 0x2000).
            int id = rawTileId & ~0x6000;

            // Strip up to three +1000 encounter/secret bump-up offsets as in centerpict.c.
            for (int i = 0; i < 3 && id > 999; i++)
            {
                id -= 1000;
            }

            if (id <= 0 || id > 200)
            {
                return -1;
            }

            return id - 1;
        }
    }

    public sealed class HeroCharacterData
    {
        public HeroCharacterData(
            string name,
            int level,
            int raceId,
            int casteId,
            int gender,
            int stamina,
            int staminaMax,
            int spellPoints,
            int spellPointsMax,
            int armorClass,
            int toHit,
            int dodge,
            int missile,
            int damage,
            int movement,
            int movementMax,
            double attacksPerRound,
            int portraitId,
            int iconId,
            int strength,
            int intelligence,
            int wisdom,
            int dexterity,
            int constitution,
            int luck,
            int experience)
        {
            Name = name ?? string.Empty;
            Level = level;
            RaceId = raceId;
            CasteId = casteId;
            Gender = gender;
            Stamina = stamina;
            StaminaMax = staminaMax;
            SpellPoints = spellPoints;
            SpellPointsMax = spellPointsMax;
            ArmorClass = armorClass;
            ToHit = toHit;
            Dodge = dodge;
            Missile = missile;
            Damage = damage;
            Movement = movement;
            MovementMax = movementMax;
            AttacksPerRound = attacksPerRound;
            PortraitId = portraitId;
            IconId = iconId;
            Strength = strength;
            Intelligence = intelligence;
            Wisdom = wisdom;
            Dexterity = dexterity;
            Constitution = constitution;
            Luck = luck;
            Experience = experience;
        }

        public string Name { get; }
        public int Level { get; }
        public int RaceId { get; }
        public int CasteId { get; }
        public int Gender { get; }
        public int Stamina { get; }
        public int StaminaMax { get; }
        public int SpellPoints { get; }
        public int SpellPointsMax { get; }
        public int ArmorClass { get; }
        public int ToHit { get; }
        public int Dodge { get; }
        public int Missile { get; }
        public int Damage { get; }
        public int Movement { get; }
        public int MovementMax { get; }
        public double AttacksPerRound { get; }
        public int PortraitId { get; }
        public int IconId { get; }
        public int Strength { get; }
        public int Intelligence { get; }
        public int Wisdom { get; }
        public int Dexterity { get; }
        public int Constitution { get; }
        public int Luck { get; }
        public int Experience { get; }
    }

    public sealed class MonsterTemplateData
    {
        private readonly bool[] _monsterTypes;
        private readonly int[] _saves;
        private readonly bool[] _spellImmunities;

        public MonsterTemplateData(
            int index,
            string name,
            int hitDice,
            int bonus,
            int dexterity,
            int armorClass,
            int magicResistance,
            int movementMax,
            int size,
            bool[] monsterTypes,
            int numAttacks,
            int[] saves,
            bool[] spellImmunities,
            int iconId,
            int stamina,
            int staminaMax,
            int experienceValue)
        {
            if (monsterTypes == null)
            {
                throw new ArgumentNullException(nameof(monsterTypes));
            }

            if (monsterTypes.Length != 8)
            {
                throw new ArgumentException("MonsterTypes must have length 8.", nameof(monsterTypes));
            }

            if (saves == null)
            {
                throw new ArgumentNullException(nameof(saves));
            }

            if (saves.Length != 6)
            {
                throw new ArgumentException("Saves must have length 6.", nameof(saves));
            }

            if (spellImmunities == null)
            {
                throw new ArgumentNullException(nameof(spellImmunities));
            }

            if (spellImmunities.Length != 6)
            {
                throw new ArgumentException("SpellImmunities must have length 6.", nameof(spellImmunities));
            }

            Index = index;
            Name = name ?? string.Empty;
            HitDice = hitDice;
            Bonus = bonus;
            Dexterity = dexterity;
            ArmorClass = armorClass;
            MagicResistance = magicResistance;
            MovementMax = movementMax;
            Size = size;
            _monsterTypes = (bool[])monsterTypes.Clone();
            NumAttacks = numAttacks;
            _saves = (int[])saves.Clone();
            _spellImmunities = (bool[])spellImmunities.Clone();
            IconId = iconId;
            Stamina = stamina;
            StaminaMax = staminaMax;
            ExperienceValue = experienceValue;
        }

        public int Index { get; }
        public string Name { get; }
        public int HitDice { get; }
        public int Bonus { get; }
        public int Dexterity { get; }
        public int ArmorClass { get; }
        public int MagicResistance { get; }
        public int MovementMax { get; }
        public int Size { get; }
        public IReadOnlyList<bool> MonsterTypes => _monsterTypes;
        public int NumAttacks { get; }
        public IReadOnlyList<int> Saves => _saves;
        public IReadOnlyList<bool> SpellImmunities => _spellImmunities;
        public int IconId { get; }
        public int Stamina { get; }
        public int StaminaMax { get; }
        public int ExperienceValue { get; }

        public bool IsEvil => _monsterTypes[4];
    }
}
