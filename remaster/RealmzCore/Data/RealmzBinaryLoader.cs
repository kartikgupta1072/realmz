using System;
using System.Collections.Generic;
using RealmzCore.Serialization;

namespace RealmzCore.Data
{
    /// <summary>
    /// Binary and JSON loaders matching C structs in structs.h (68k 2-byte alignment, big-endian).
    /// </summary>
    public static class RealmzBinaryLoader
    {
        public const int CharacterRecordSize = 872;
        public const int LandMapWidth = LandMapData.DefaultWidth;
        public const int LandMapHeight = LandMapData.DefaultHeight;
        public const int LandMapRecordSize = LandMapWidth * LandMapHeight * 2; // 16,200 bytes
        public const int RandLevelRecordSize = 644;
        public const int MonsterRecordSize = 210;

        public static HeroCharacterData LoadCharacterFromBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < CharacterRecordSize)
            {
                throw new ArgumentException(
                    $"Expected at least {CharacterRecordSize} bytes for struct character, got {data.Length}.",
                    nameof(data));
            }

            BigEndianReader reader = new BigEndianReader(data, 0, CharacterRecordSize);

            reader.Seek(4);
            int toHit = reader.ReadInt16();
            int dodge = reader.ReadInt16();
            int missile = reader.ReadInt16();

            reader.Seek(14);
            int normAttacks = reader.ReadInt16();

            reader.Seek(30);
            int attackBonus = reader.ReadInt16();

            reader.Seek(44);
            int armorClass = reader.ReadInt16();
            int damage = reader.ReadInt16();
            int raceId = reader.ReadInt16();
            int casteId = reader.ReadInt16();
            reader.Skip(2); // spellcastertype
            int gender = reader.ReadInt16();
            int level = reader.ReadInt16();
            int movement = reader.ReadInt16();
            int movementMax = reader.ReadInt16();

            reader.Seek(78);
            int stamina = reader.ReadInt16();
            int staminaMax = reader.ReadInt16();
            int portraitId = reader.ReadInt16();
            int iconId = reader.ReadInt16();
            int spellPoints = reader.ReadInt16();
            int spellPointsMax = reader.ReadInt16();

            reader.Seek(496);
            int experience = reader.ReadInt32();

            reader.Seek(517);
            int strength = reader.ReadByte();
            int intelligence = reader.ReadByte();
            int wisdom = reader.ReadByte();
            int dexterity = reader.ReadByte();
            int constitution = reader.ReadByte();
            int luck = reader.ReadByte();

            reader.Seek(607);
            string name = reader.ReadMacRomanFixedString(30);

            double attacksPerRound = (normAttacks + attackBonus) / 2.0;

            return new HeroCharacterData(
                name: name,
                level: level,
                raceId: raceId,
                casteId: casteId,
                gender: gender,
                stamina: stamina,
                staminaMax: staminaMax,
                spellPoints: spellPoints,
                spellPointsMax: spellPointsMax,
                armorClass: armorClass,
                toHit: toHit,
                dodge: dodge,
                missile: missile,
                damage: damage,
                movement: movement,
                movementMax: movementMax,
                attacksPerRound: attacksPerRound,
                portraitId: portraitId,
                iconId: iconId,
                strength: strength,
                intelligence: intelligence,
                wisdom: wisdom,
                dexterity: dexterity,
                constitution: constitution,
                luck: luck,
                experience: experience);
        }

        public static LandMapData LoadLandMapFromDataLd(
            byte[] dataLd,
            int mapIndex,
            byte[]? dataRd = null,
            int startX = 0,
            int startY = 0)
        {
            if (dataLd == null)
            {
                throw new ArgumentNullException(nameof(dataLd));
            }

            if (mapIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapIndex));
            }

            int mapOffset = mapIndex * LandMapRecordSize;
            if (mapOffset + LandMapRecordSize > dataLd.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mapIndex),
                    $"Map index {mapIndex} exceeds Data LD length {dataLd.Length}.");
            }

            BigEndianReader ldReader = new BigEndianReader(dataLd, mapOffset, LandMapRecordSize);
            int[] tiles = new int[LandMapWidth * LandMapHeight];

            // Data LD stores field[x][y] in column-major order (x * 90 + y).
            // Convert to row-major (y * 90 + x) for LandMapData.GetTile(x, y).
            for (int x = 0; x < LandMapWidth; x++)
            {
                for (int y = 0; y < LandMapHeight; y++)
                {
                    tiles[y * LandMapWidth + x] = ldReader.ReadInt16();
                }
            }

            int landLookId = 0;
            bool isDark = false;
            bool useLineOfSight = false;

            if (dataRd != null)
            {
                int rdOffset = mapIndex * RandLevelRecordSize;
                if (rdOffset + RandLevelRecordSize > dataRd.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mapIndex),
                        $"Map index {mapIndex} exceeds Data RD length {dataRd.Length}.");
                }

                BigEndianReader rdReader = new BigEndianReader(dataRd, rdOffset, RandLevelRecordSize);
                rdReader.Seek(520);
                landLookId = rdReader.ReadByte();
                isDark = rdReader.ReadBool();
                useLineOfSight = rdReader.ReadBool();
            }

            return new LandMapData(
                width: LandMapWidth,
                height: LandMapHeight,
                tiles: tiles,
                landLookId: landLookId,
                isDark: isDark,
                useLineOfSight: useLineOfSight,
                startX: startX,
                startY: startY);
        }

        public static IReadOnlyList<MonsterTemplateData> LoadMonstersFromDataMd(byte[] dataMd)
        {
            if (dataMd == null)
            {
                throw new ArgumentNullException(nameof(dataMd));
            }

            if (dataMd.Length % MonsterRecordSize != 0)
            {
                throw new ArgumentException(
                    $"Data MD length {dataMd.Length} is not a multiple of {MonsterRecordSize} bytes.",
                    nameof(dataMd));
            }

            int count = dataMd.Length / MonsterRecordSize;
            MonsterTemplateData[] monsters = new MonsterTemplateData[count];

            for (int i = 0; i < count; i++)
            {
                int recordOffset = i * MonsterRecordSize;
                BigEndianReader reader = new BigEndianReader(dataMd, recordOffset, MonsterRecordSize);

                int hitDice = reader.ReadByte();
                int bonus = reader.ReadByte();
                int dexterity = reader.ReadByte();
                reader.Skip(1); // name index byte
                int movementMax = reader.ReadByte();

                int armorClass = reader.ReadSByte();
                int magicResistance = reader.ReadSByte();
                reader.Skip(1); // dist
                reader.Skip(1); // traiter
                int size = reader.ReadSByte();

                bool[] monsterTypes = new bool[8];
                for (int t = 0; t < 8; t++)
                {
                    monsterTypes[t] = reader.ReadBool();
                }

                int numAttacks = reader.ReadByte();

                reader.Seek(46);
                int[] saves = new int[6];
                for (int s = 0; s < 6; s++)
                {
                    saves[s] = reader.ReadSByte();
                }

                bool[] spellImmunities = new bool[6];
                for (int s = 0; s < 6; s++)
                {
                    spellImmunities[s] = reader.ReadBool();
                }

                reader.Seek(98);
                int iconId = reader.ReadInt16();
                reader.Skip(2); // spellpoints
                int experienceValue = reader.ReadInt16();
                int stamina = reader.ReadInt16();
                int staminaMax = reader.ReadInt16();

                reader.Seek(170);
                string name = reader.ReadMacRomanFixedString(40);

                monsters[i] = new MonsterTemplateData(
                    index: i,
                    name: name,
                    hitDice: hitDice,
                    bonus: bonus,
                    dexterity: dexterity,
                    armorClass: armorClass,
                    magicResistance: magicResistance,
                    movementMax: movementMax,
                    size: size,
                    monsterTypes: monsterTypes,
                    numAttacks: numAttacks,
                    saves: saves,
                    spellImmunities: spellImmunities,
                    iconId: iconId,
                    stamina: stamina,
                    staminaMax: staminaMax,
                    experienceValue: experienceValue);
            }

            return monsters;
        }

        public static LandMapData LoadLandMapFromJson(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json));

            int width = GetIntAny(root, LandMapWidth, "width", "Width");
            int height = GetIntAny(root, LandMapHeight, "height", "Height");
            int landLookId = GetIntAny(root, 0, "landLookId", "land_look_id", "landlook", "LandLookId");
            bool isDark = GetBoolAny(root, false, "isDark", "is_dark", "isdark", "IsDark");
            bool useLineOfSight = GetBoolAny(root, false, "useLineOfSight", "use_line_of_sight", "uselos", "UseLineOfSight");
            int startX = GetIntAny(root, 0, "startX", "start_x", "startx", "StartX");
            int startY = GetIntAny(root, 0, "startY", "start_y", "starty", "StartY");
            if (TryGetAny(root, out object? rawStartPos, "start_position", "startPosition") && rawStartPos != null)
            {
                IReadOnlyDictionary<string, object?> startObj = MiniJson.AsObject(rawStartPos);
                startX = GetIntAny(startObj, startX, "map0_start_x", "look_x", "startX", "x");
                startY = GetIntAny(startObj, startY, "map0_start_y", "look_y", "startY", "y");
            }

            if (!TryGetAny(root, out object? rawTiles, "tiles_yx", "tiles", "Tiles", "grid", "Grid"))
            {
                throw new FormatException("Land map JSON is missing 'tiles_yx' or 'tiles' array.");
            }

            IReadOnlyList<object?> tilesArray = MiniJson.AsArray(rawTiles);
            int[] tiles = new int[width * height];

            if (tilesArray.Count == height && height > 0 && tilesArray[0] is IReadOnlyList<object?>)
            {
                // Support 2D array [y][x].
                for (int y = 0; y < height; y++)
                {
                    IReadOnlyList<object?> row = MiniJson.AsArray(tilesArray[y]);
                    if (row.Count != width)
                    {
                        throw new FormatException($"Expected row {y} length {width}, got {row.Count}.");
                    }

                    for (int x = 0; x < width; x++)
                    {
                        tiles[y * width + x] = MiniJson.ToInt32(row[x], $"tiles[{y}][{x}]");
                    }
                }
            }
            else
            {
                if (tilesArray.Count != width * height)
                {
                    throw new FormatException(
                        $"Expected {width * height} entries in 'tiles', got {tilesArray.Count}.");
                }

                for (int i = 0; i < tiles.Length; i++)
                {
                    tiles[i] = MiniJson.ToInt32(tilesArray[i], $"tiles[{i}]");
                }
            }

            return new LandMapData(
                width: width,
                height: height,
                tiles: tiles,
                landLookId: landLookId,
                isDark: isDark,
                useLineOfSight: useLineOfSight,
                startX: startX,
                startY: startY);
        }

        private static bool TryGetAny(
            IReadOnlyDictionary<string, object?> obj,
            out object? value,
            params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static int GetIntAny(
            IReadOnlyDictionary<string, object?> obj,
            int defaultValue,
            params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    return MiniJson.ToInt32(raw, keys[i]);
                }
            }

            return defaultValue;
        }

        private static bool GetBoolAny(
            IReadOnlyDictionary<string, object?> obj,
            bool defaultValue,
            params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    if (raw is bool b)
                    {
                        return b;
                    }

                    if (raw is long l)
                    {
                        return l != 0;
                    }

                    throw new FormatException($"JSON field '{keys[i]}' is not a boolean.");
                }
            }

            return defaultValue;
        }
    }
}
