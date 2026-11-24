using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RealmzCore.Rules;
using RealmzCore.Serialization;

namespace RealmzCore.Data
{
    /// <summary>
    /// MiniJson-based loaders for extracted Bywater M1 scenario JSON bundles.
    /// </summary>
    public static class BywaterJsonLoader
    {
        public static BywaterM1Bundle LoadFromDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                throw new ArgumentException("Directory path is required.", nameof(dir));
            }

            if (!Directory.Exists(dir))
            {
                throw new DirectoryNotFoundException($"Directory not found: {dir}");
            }

            string[] jsonFiles = Directory.GetFiles(dir, "*.json");
            Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, TileMapData> maps = new Dictionary<string, TileMapData>(StringComparer.OrdinalIgnoreCase);
            List<HeroPresetData> party = new List<HeroPresetData>();
            List<int> stash = new List<int>();
            int gold = 0;
            List<MonsterDefinition> monsters = new List<MonsterDefinition>();
            List<SpellAreaTemplate> spellAreas = new List<SpellAreaTemplate>();
            Dictionary<int, SpellDefinition> spells = new Dictionary<int, SpellDefinition>();
            Dictionary<int, ItemDefinition> items = new Dictionary<int, ItemDefinition>();
            Dictionary<string, ShopDefinition> shops = new Dictionary<string, ShopDefinition>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < jsonFiles.Length; i++)
            {
                string filePath = jsonFiles[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (string.Equals(fileName, "manifest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string json = File.ReadAllText(filePath);
                object? parsed = MiniJson.Parse(json);
                if (!(parsed is IReadOnlyDictionary<string, object?> root))
                {
                    continue;
                }

                if (root.ContainsKey("tiles_yx") || root.ContainsKey("tiles"))
                {
                    TileMapData map = LoadMapFromObject(root, fileName);
                    string key = string.IsNullOrEmpty(map.MapId) ? fileName : map.MapId;
                    maps[key] = map;
                }
                else if (root.ContainsKey("heroes") || root.ContainsKey("party"))
                {
                    party = LoadPartyFromObject(root, out stash, out gold);
                }
                else if (root.ContainsKey("monsters"))
                {
                    monsters = LoadMonstersFromObject(root);
                }
                else if (root.ContainsKey("spells") || root.ContainsKey("items") ||
                         root.ContainsKey("spell_areas") || root.ContainsKey("spellAreas") ||
                         root.ContainsKey("shops"))
                {
                    LoadSpellsAndItemsFromObject(root, out spellAreas, out spells, out items, out shops);
                }
            }

            return new BywaterM1Bundle(
                maps: maps,
                party: party,
                initialStashItemIds: stash,
                initialGold: gold,
                monsters: monsters,
                spellAreas: spellAreas,
                spells: spells,
                items: items,
                shops: shops);
        }

        public static TileMapData LoadMap(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json));
            return LoadMapFromObject(root, string.Empty);
        }

        public static List<HeroPresetData> LoadParty(string json, out List<int> stash, out int gold)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            object? parsed = MiniJson.Parse(json);
            if (parsed is IReadOnlyList<object?> arr)
            {
                stash = new List<int>();
                gold = 0;
                return ParseHeroList(arr);
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(parsed);
            return LoadPartyFromObject(root, out stash, out gold);
        }

        public static List<MonsterDefinition> LoadMonsters(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            object? parsed = MiniJson.Parse(json);
            if (parsed is IReadOnlyList<object?> arr)
            {
                return ParseMonsterList(arr);
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(parsed);
            return LoadMonstersFromObject(root);
        }

        public static void LoadSpellsAndItems(
            string json,
            out List<SpellAreaTemplate> spellAreas,
            out Dictionary<int, SpellDefinition> spells,
            out Dictionary<int, ItemDefinition> items,
            out Dictionary<string, ShopDefinition> shops)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json));
            LoadSpellsAndItemsFromObject(root, out spellAreas, out spells, out items, out shops);
        }

        private static TileMapData LoadMapFromObject(IReadOnlyDictionary<string, object?> root, string fallbackMapId)
        {
            string mapId = GetFirstString(root, fallbackMapId, "map_id", "mapId", "id");
            string scenario = GetFirstString(root, string.Empty, "scenario");
            int level = GetFirstInt(root, 0, "level");
            bool isDungeon = GetFirstBool(root, false, "is_dungeon", "isDungeon");
            if (string.IsNullOrEmpty(mapId))
            {
                mapId = isDungeon ? $"dungeon_{level}" : $"land_{level}";
            }

            int width = GetFirstInt(root, LandMapData.DefaultWidth, "width");
            int height = GetFirstInt(root, LandMapData.DefaultHeight, "height");
            int[] tiles = ParseTilesGrid(root, width, height);

            GridCoord startSpawn = ParseStartSpawn(root);
            Dictionary<int, TileRule> tileRules = ParseTileRules(root);
            List<int> solidNegativeIds = ParseIntArray(root, "solid_negative_ids", "solidNegativeIds");
            List<MapPortal> portals = ParsePortals(root);
            List<MapPointOfInterest> pois = ParsePointsOfInterest(root);
            List<EncounterZoneData> zones = ParseEncounterZones(root);

            return new TileMapData(
                mapId: mapId,
                scenario: scenario,
                level: level,
                isDungeon: isDungeon,
                width: width,
                height: height,
                tiles: tiles,
                startSpawn: startSpawn,
                tileRules: tileRules,
                solidNegativeIds: solidNegativeIds,
                portals: portals,
                pointsOfInterest: pois,
                encounterZones: zones);
        }

        private static int[] ParseTilesGrid(IReadOnlyDictionary<string, object?> root, int width, int height)
        {
            int[] tiles = new int[width * height];

            if (TryGetAny(root, out object? rawFlat, "tiles"))
            {
                IReadOnlyList<object?> flat = MiniJson.AsArray(rawFlat);
                if (flat.Count > 0 && flat[0] is IReadOnlyList<object?>)
                {
                    return Flatten2DTiles(flat, width, height);
                }

                if (flat.Count != width * height)
                {
                    throw new FormatException($"Expected {width * height} tiles, got {flat.Count}.");
                }

                for (int i = 0; i < flat.Count; i++)
                {
                    tiles[i] = MiniJson.ToInt32(flat[i], $"tiles[{i}]");
                }

                return tiles;
            }

            if (TryGetAny(root, out object? rawYx, "tiles_yx", "tilesYx"))
            {
                IReadOnlyList<object?> rows = MiniJson.AsArray(rawYx);
                return Flatten2DTiles(rows, width, height);
            }

            throw new FormatException("Map JSON missing 'tiles' or 'tiles_yx'.");
        }

        private static int[] Flatten2DTiles(IReadOnlyList<object?> rows, int width, int height)
        {
            if (rows.Count != height)
            {
                throw new FormatException($"Expected {height} rows in 2D tile grid, got {rows.Count}.");
            }

            int[] tiles = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                IReadOnlyList<object?> row = MiniJson.AsArray(rows[y]);
                if (row.Count != width)
                {
                    throw new FormatException($"Expected {width} columns in row {y}, got {row.Count}.");
                }

                for (int x = 0; x < width; x++)
                {
                    tiles[y * width + x] = MiniJson.ToInt32(row[x], $"tiles_yx[{y}][{x}]");
                }
            }

            return tiles;
        }

        private static GridCoord ParseStartSpawn(IReadOnlyDictionary<string, object?> root)
        {
            if (TryGetAny(root, out object? rawSpawn, "start_spawn", "startSpawn", "start_position", "startPosition") &&
                rawSpawn is IReadOnlyDictionary<string, object?> spawnObj)
            {
                int x = GetFirstInt(spawnObj, 0, "x", "map0_start_x", "startX", "start_x");
                int y = GetFirstInt(spawnObj, 0, "y", "map0_start_y", "startY", "start_y");
                return new GridCoord(x, y);
            }

            int rootX = GetFirstInt(root, 0, "start_x", "startX");
            int rootY = GetFirstInt(root, 0, "start_y", "startY");
            return new GridCoord(rootX, rootY);
        }

        private static Dictionary<int, TileRule> ParseTileRules(IReadOnlyDictionary<string, object?> root)
        {
            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule>();
            if (!TryGetAny(root, out object? rawRules, "tile_rules", "tileRules") || rawRules == null)
            {
                return rules;
            }

            if (rawRules is IReadOnlyDictionary<string, object?> dictRules)
            {
                foreach (KeyValuePair<string, object?> kvp in dictRules)
                {
                    if (!int.TryParse(kvp.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int keyId))
                    {
                        continue;
                    }

                    IReadOnlyDictionary<string, object?> ruleObj = MiniJson.AsObject(kvp.Value);
                    TileRule rule = ParseTileRuleObject(ruleObj, keyId);
                    rules[rule.TileId] = rule;
                }
            }
            else if (rawRules is IReadOnlyList<object?> listRules)
            {
                for (int i = 0; i < listRules.Count; i++)
                {
                    IReadOnlyDictionary<string, object?> ruleObj = MiniJson.AsObject(listRules[i]);
                    TileRule rule = ParseTileRuleObject(ruleObj, 0);
                    rules[rule.TileId] = rule;
                }
            }

            return rules;
        }

        private static TileRule ParseTileRuleObject(IReadOnlyDictionary<string, object?> obj, int fallbackTileId)
        {
            int tileId = GetFirstInt(obj, fallbackTileId, "tile_id", "tileId", "id");
            bool passable = GetFirstBool(obj, true, "passable", "walkable");
            int moveCost = GetFirstInt(obj, 1, "move_cost", "moveCost");
            bool blocksLos = GetFirstBool(obj, false, "blocks_los", "blocksLos");
            bool isForest = GetFirstBool(obj, false, "is_forest", "isForest");
            bool isPath = GetFirstBool(obj, false, "is_path", "isPath");
            bool needsBoat = GetFirstBool(obj, false, "needs_boat", "needsBoat");
            int soundId = GetFirstInt(obj, 0, "sound_id", "soundId");

            return new TileRule(
                tileId: tileId,
                passable: passable,
                moveCost: moveCost,
                blocksLos: blocksLos,
                isForest: isForest,
                isPath: isPath,
                needsBoat: needsBoat,
                soundId: soundId);
        }

        private static List<MapPortal> ParsePortals(IReadOnlyDictionary<string, object?> root)
        {
            List<MapPortal> result = new List<MapPortal>();
            if (!TryGetAny(root, out object? rawPortals, "portals") || rawPortals == null)
            {
                return result;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(rawPortals);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                result.Add(new MapPortal(
                    id: GetStringOrInt(obj, $"portal_{i}", "id", "portal_id", "portalId"),
                    x: GetFirstInt(obj, 0, "x"),
                    y: GetFirstInt(obj, 0, "y"),
                    destMapId: GetFirstString(obj, string.Empty, "dest_map_id", "destMapId", "target_map"),
                    destX: GetFirstInt(obj, 0, "dest_x", "destX", "target_x"),
                    destY: GetFirstInt(obj, 0, "dest_y", "destY", "target_y"),
                    destFacing: GetFirstString(obj, "South", "dest_facing", "destFacing", "facing"),
                    label: GetFirstString(obj, string.Empty, "label", "name")));
            }

            return result;
        }

        private static List<MapPointOfInterest> ParsePointsOfInterest(IReadOnlyDictionary<string, object?> root)
        {
            List<MapPointOfInterest> result = new List<MapPointOfInterest>();
            if (!TryGetAny(root, out object? rawPois, "points_of_interest", "pointsOfInterest", "pois") || rawPois == null)
            {
                return result;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(rawPois);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                result.Add(new MapPointOfInterest(
                    id: GetStringOrInt(obj, $"poi_{i}", "id", "poi_id", "poiId"),
                    kind: GetFirstString(obj, string.Empty, "kind", "type"),
                    x: GetFirstInt(obj, 0, "x"),
                    y: GetFirstInt(obj, 0, "y"),
                    label: GetFirstString(obj, string.Empty, "label", "name"),
                    shopId: GetStringOrInt(obj, string.Empty, "shop_id", "shopId")));
            }

            return result;
        }

        private static List<EncounterZoneData> ParseEncounterZones(IReadOnlyDictionary<string, object?> root)
        {
            List<EncounterZoneData> result = new List<EncounterZoneData>();
            if (!TryGetAny(root, out object? rawZones, "encounter_zones", "encounterZones") || rawZones == null)
            {
                return result;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(rawZones);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                int slot = GetFirstInt(obj, i, "slot");

                int top;
                int left;
                int bottom;
                int right;
                if (TryGetAny(obj, out object? rawRect, "rect") && rawRect is IReadOnlyDictionary<string, object?> rectObj)
                {
                    top = GetFirstInt(rectObj, 0, "top");
                    left = GetFirstInt(rectObj, 0, "left");
                    bottom = GetFirstInt(rectObj, 0, "bottom");
                    right = GetFirstInt(rectObj, 0, "right");
                }
                else
                {
                    top = GetFirstInt(obj, 0, "top");
                    left = GetFirstInt(obj, 0, "left");
                    bottom = GetFirstInt(obj, 0, "bottom");
                    right = GetFirstInt(obj, 0, "right");
                }

                int percent = GetFirstInt(obj, 0, "percent");
                List<int> battleIds = ParseIntArray(obj, "battle_ids", "battleIds");
                if (battleIds.Count == 0 && TryGetAny(obj, out object? rawRange, "battle_range", "battleRange") && rawRange != null)
                {
                    IReadOnlyList<object?> rangeArr = MiniJson.AsArray(rawRange);
                    if (rangeArr.Count >= 2)
                    {
                        int bMin = MiniJson.ToInt32(rangeArr[0], "battle_range[0]");
                        int bMax = MiniJson.ToInt32(rangeArr[1], "battle_range[1]");
                        if (bMax >= bMin && bMax - bMin <= 256)
                        {
                            for (int b = bMin; b <= bMax; b++)
                            {
                                battleIds.Add(b);
                            }
                        }
                    }
                }

                List<int> monsterIndices = ParseIntArray(obj, "monster_indices", "monsterIndices");
                result.Add(new EncounterZoneData(slot, top, left, bottom, right, percent, battleIds, monsterIndices));
            }

            return result;
        }

        private static List<HeroPresetData> LoadPartyFromObject(
            IReadOnlyDictionary<string, object?> root,
            out List<int> stash,
            out int gold)
        {
            stash = ParseIntArray(root, "stash", "stash_item_ids", "stashItemIds", "initial_stash", "initialStash");
            gold = GetFirstInt(root, 0, "gold", "initial_gold", "initialGold");

            if (TryGetAny(root, out object? rawHeroes, "heroes", "party") && rawHeroes != null)
            {
                return ParseHeroList(MiniJson.AsArray(rawHeroes));
            }

            return new List<HeroPresetData>();
        }

        private static List<HeroPresetData> ParseHeroList(IReadOnlyList<object?> arr)
        {
            List<HeroPresetData> heroes = new List<HeroPresetData>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                IReadOnlyDictionary<string, object?> attrs =
                    TryGetAny(obj, out object? rawAttrs, "attributes") && rawAttrs is IReadOnlyDictionary<string, object?> attrDict
                        ? attrDict
                        : obj;

                string name = GetFirstString(obj, $"Hero_{i}", "name", "file_name");
                string id = GetStringOrInt(obj, Slugify(name, $"hero_{i}"), "id", "hero_id", "heroId");
                int hp = GetFirstInt(obj, 10, "hp", "stamina");
                int maxHp = GetFirstInt(obj, hp, "max_hp", "maxHp", "stamina_max");
                int sp = GetFirstInt(obj, 0, "sp", "spell_points");
                int maxSp = GetFirstInt(obj, sp, "max_sp", "maxSp", "spell_points_max");

                double attacksPerRound = GetFirstDouble(obj, 1.0, "attacks_per_round", "attacksPerRound");
                if (!HasAnyKey(obj, "attacks_per_round", "attacksPerRound") && HasAnyKey(obj, "attacks"))
                {
                    int rawAttacks = GetFirstInt(obj, 2, "attacks");
                    attacksPerRound = rawAttacks > 0 ? rawAttacks / 2.0 : 1.0;
                }

                heroes.Add(new HeroPresetData(
                    id: id,
                    name: name,
                    archetype: GetFirstString(obj, string.Empty, "archetype", "role"),
                    raceId: GetFirstInt(obj, 0, "race_id", "raceId"),
                    raceName: GetFirstString(obj, string.Empty, "race_name", "raceName"),
                    casteId: GetFirstInt(obj, 0, "caste_id", "casteId"),
                    casteName: GetFirstString(obj, string.Empty, "caste_name", "casteName"),
                    level: GetFirstInt(obj, 1, "level"),
                    hp: hp,
                    maxHp: maxHp,
                    sp: sp,
                    maxSp: maxSp,
                    ac: GetFirstInt(obj, 10, "ac", "armor_class", "armorClass"),
                    toHit: GetFirstInt(obj, 10, "to_hit", "toHit", "tohit"),
                    dodge: GetFirstInt(obj, 10, "dodge"),
                    missile: GetFirstInt(obj, 0, "missile"),
                    movementMax: GetFirstInt(obj, 6, "movement_max", "movementMax", "movement"),
                    attacksPerRound: attacksPerRound,
                    iconId: GetFirstInt(obj, 0, "icon_id", "iconId"),
                    portraitId: GetFirstInt(obj, 0, "portrait_id", "portraitId", "pict_id"),
                    brawn: GetFirstInt(attrs, 10, "brawn", "strength"),
                    knowledge: GetFirstInt(attrs, 10, "knowledge", "intelligence"),
                    judgment: GetFirstInt(attrs, 10, "judgment", "wisdom"),
                    agility: GetFirstInt(attrs, 10, "agility", "dexterity"),
                    vitality: GetFirstInt(attrs, 10, "vitality", "constitution"),
                    luck: GetFirstInt(attrs, 10, "luck"),
                    equippedIds: ParseIntArray(obj, "equipped_ids", "equippedIds"),
                    knownSpellIds: ParseIntArray(obj, "known_spell_ids", "knownSpellIds"),
                    xp: GetFirstInt(obj, 0, "xp", "experience")));
            }

            return heroes;
        }

        private static List<MonsterDefinition> LoadMonstersFromObject(IReadOnlyDictionary<string, object?> root)
        {
            if (TryGetAny(root, out object? rawMonsters, "monsters") && rawMonsters != null)
            {
                return ParseMonsterList(MiniJson.AsArray(rawMonsters));
            }

            return new List<MonsterDefinition>();
        }

        private static List<MonsterDefinition> ParseMonsterList(IReadOnlyList<object?> arr)
        {
            List<MonsterDefinition> list = new List<MonsterDefinition>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                if (TryGetAny(obj, out object? validRaw, "is_valid") && validRaw is bool isValid && !isValid)
                {
                    continue;
                }

                int index = GetFirstInt(obj, i, "index");
                string name = GetFirstString(obj, $"Monster_{index}", "name");
                string id = GetStringOrInt(obj, Slugify(name, $"monster_{index}"), "id", "monster_id", "monsterId");
                int hd = GetFirstInt(obj, 1, "hit_dice", "hitDice", "hd");
                int hpBonus = GetFirstInt(obj, 0, "hp_bonus", "hpBonus", "bonus");
                int hpMin = GetFirstInt(obj, Math.Max(1, hd + hpBonus), "hp_min", "hpMin");
                int hpMax = GetFirstInt(obj, Math.Max(hpMin, (hd * 8) + hpBonus), "hp_max", "hpMax");
                int hpDefault = GetFirstInt(obj, (hpMin + hpMax) / 2, "hp_default", "hpDefault", "stamina_max", "stamina");
                if (hpDefault <= 0)
                {
                    hpDefault = Math.Max(1, (hpMin + hpMax) / 2);
                }

                List<int> saves = ParseIntArray(obj, "saves");
                List<bool> immunities = ParseBoolArray(obj, "spell_immunities", "spellImmunities");

                list.Add(new MonsterDefinition(
                    index: index,
                    id: id,
                    name: name,
                    iconId: GetFirstInt(obj, 0, "icon_id", "iconId"),
                    hitDice: hd,
                    hpBonus: hpBonus,
                    hpMin: hpMin,
                    hpMax: hpMax,
                    hpDefault: hpDefault,
                    ac: GetFirstInt(obj, 10, "ac", "armor_class", "armorClass"),
                    toHitBonus: GetFirstInt(obj, 0, "to_hit_bonus", "toHitBonus", "to_hit", "toHit"),
                    dexterity: GetFirstInt(obj, 10, "dexterity", "dx"),
                    movementMax: GetFirstInt(obj, 6, "movement_max", "movementMax", "movement"),
                    size: GetFirstInt(obj, 1, "size"),
                    numAttacks: Math.Max(1, GetFirstInt(obj, 1, "num_attacks", "numAttacks")),
                    attackDice: GetFirstString(obj, "1d4", "attack_dice", "attackDice"),
                    damageBonus: GetFirstInt(obj, 0, "damage_bonus", "damageBonus"),
                    missileChancePct: GetFirstInt(obj, 0, "missile_chance_pct", "missileChancePct"),
                    castChancePct: GetFirstInt(obj, 0, "cast_chance_pct", "castChancePct"),
                    rangedRange: GetFirstInt(obj, 0, "ranged_range", "rangedRange"),
                    rangedDice: GetFirstString(obj, string.Empty, "ranged_dice", "rangedDice"),
                    spellIds: ParseIntArray(obj, "spell_ids", "spellIds"),
                    magicResistance: GetFirstInt(obj, 0, "magic_resistance", "magicResistance"),
                    saves: saves,
                    spellImmunities: immunities,
                    isEvil: GetFirstBool(obj, false, "is_evil", "isEvil"),
                    isUndead: GetFirstBool(obj, false, "is_undead", "isUndead"),
                    xpReward: GetFirstInt(obj, 0, "xp_reward", "xpReward", "exp"),
                    goldMin: GetFirstInt(obj, 0, "gold_min", "goldMin"),
                    goldMax: GetFirstInt(obj, 0, "gold_max", "goldMax"),
                    lootItemIds: ParseIntArray(obj, "loot_item_ids", "lootItemIds")));
            }

            return list;
        }

        private static void LoadSpellsAndItemsFromObject(
            IReadOnlyDictionary<string, object?> root,
            out List<SpellAreaTemplate> spellAreas,
            out Dictionary<int, SpellDefinition> spells,
            out Dictionary<int, ItemDefinition> items,
            out Dictionary<string, ShopDefinition> shops)
        {
            spellAreas = new List<SpellAreaTemplate>();
            Dictionary<int, SpellAreaTemplate> areaByIndex = new Dictionary<int, SpellAreaTemplate>();
            if (TryGetAny(root, out object? rawAreas, "spell_areas", "spellAreas") && rawAreas != null)
            {
                IReadOnlyList<object?> arr = MiniJson.AsArray(rawAreas);
                for (int i = 0; i < arr.Count; i++)
                {
                    IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                    int index = GetFirstInt(obj, i, "index", "id");
                    bool[][] grid = ParseGrid7x7(obj);
                    SpellAreaTemplate tpl = new SpellAreaTemplate(index, grid);
                    spellAreas.Add(tpl);
                    areaByIndex[index] = tpl;
                }
            }

            spells = new Dictionary<int, SpellDefinition>();
            if (TryGetAny(root, out object? rawSpells, "spells") && rawSpells != null)
            {
                foreach (IReadOnlyDictionary<string, object?> obj in EnumerateObjects(rawSpells))
                {
                    int id = GetFirstInt(obj, spells.Count, "id", "spell_id", "spellId");
                    int areaIndex = GetFirstInt(obj, -1, "spell_area_index", "spellAreaIndex");
                    List<GridCoord> offsets = ParseCoordArray(obj, "aoe_offsets", "aoeOffsets");
                    if (offsets.Count == 0 && areaIndex >= 0 && areaByIndex.TryGetValue(areaIndex, out SpellAreaTemplate? tpl))
                    {
                        offsets.AddRange(tpl.Offsets);
                    }

                    CharConditionId? appliedCondition = ParseConditionId(obj, "applied_condition", "appliedCondition");

                    SpellDefinition spell = new SpellDefinition(
                        id: id,
                        key: GetFirstString(obj, string.Empty, "key"),
                        name: GetFirstString(obj, $"Spell_{id}", "name"),
                        alias: GetFirstString(obj, string.Empty, "alias"),
                        description: GetFirstString(obj, string.Empty, "description"),
                        school: GetFirstString(obj, string.Empty, "school"),
                        level: GetFirstInt(obj, 1, "level"),
                        spCost: GetFirstInt(obj, 0, "sp_cost", "spCost"),
                        range: GetFirstInt(obj, 0, "range"),
                        targetMode: GetFirstString(obj, "Single", "target_mode", "targetMode"),
                        spellAreaIndex: areaIndex,
                        aoeOffsets: offsets,
                        effectKind: GetFirstString(obj, "Damage", "effect_kind", "effectKind"),
                        effectDice: GetFirstString(obj, string.Empty, "effect_dice", "effectDice"),
                        damageType: GetFirstString(obj, string.Empty, "damage_type", "damageType"),
                        appliedCondition: appliedCondition,
                        conditionDuration: GetFirstInt(obj, 0, "condition_duration", "conditionDuration"),
                        baseRecoveryPct: GetFirstInt(obj, 20, "base_recovery_pct", "baseRecoveryPct"),
                        saveKind: GetFirstString(obj, "Magic", "save_kind", "saveKind"),
                        createdSurface: GetFirstString(obj, string.Empty, "created_surface", "createdSurface"),
                        createdSurfaceRounds: GetFirstInt(obj, 0, "created_surface_rounds", "createdSurfaceRounds", "surface_rounds", "surfaceRounds"));
                    spells[spell.Id] = spell;
                }
            }

            items = new Dictionary<int, ItemDefinition>();
            if (TryGetAny(root, out object? rawItems, "items") && rawItems != null)
            {
                foreach (IReadOnlyDictionary<string, object?> obj in EnumerateObjects(rawItems))
                {
                    int id = GetFirstInt(obj, items.Count, "id", "item_id", "itemId");
                    int? linkedSpellId = null;
                    if (TryGetAny(obj, out object? rawLinked, "linked_spell_id", "linkedSpellId") && rawLinked != null)
                    {
                        int parsedSpell = MiniJson.ToInt32(rawLinked, "linked_spell_id");
                        if (parsedSpell >= 0)
                        {
                            linkedSpellId = parsedSpell;
                        }
                    }

                    ItemDefinition item = new ItemDefinition(
                        id: id,
                        name: GetFirstString(obj, $"Item_{id}", "name"),
                        description: GetFirstString(obj, string.Empty, "description"),
                        category: GetFirstString(obj, string.Empty, "category"),
                        slot: GetFirstString(obj, string.Empty, "slot"),
                        damageDice: GetFirstString(obj, string.Empty, "damage_dice", "damageDice"),
                        weaponBonus: GetFirstInt(obj, 0, "weapon_bonus", "weaponBonus"),
                        armorClassBonus: GetFirstInt(obj, 0, "armor_class_bonus", "armorClassBonus", "ac_bonus", "acBonus"),
                        movementMod: GetFirstInt(obj, 0, "movement_mod", "movementMod"),
                        range: GetFirstInt(obj, 1, "range"),
                        weight: GetFirstInt(obj, 0, "weight"),
                        basePrice: GetFirstInt(obj, 0, "base_price", "basePrice", "price"),
                        charges: GetFirstInt(obj, 0, "charges"),
                        linkedSpellId: linkedSpellId,
                        healDice: GetFirstString(obj, string.Empty, "heal_dice", "healDice"),
                        isTwoHanded: GetFirstBool(obj, false, "is_two_handed", "isTwoHanded"),
                        isJunk: GetFirstBool(obj, false, "is_junk", "isJunk"));
                    items[item.Id] = item;
                }
            }

            shops = new Dictionary<string, ShopDefinition>(StringComparer.OrdinalIgnoreCase);
            if (TryGetAny(root, out object? rawShops, "shops") && rawShops != null)
            {
                foreach (IReadOnlyDictionary<string, object?> obj in EnumerateObjects(rawShops))
                {
                    string shopId = GetStringOrInt(obj, $"shop_{shops.Count}", "shop_id", "shopId", "id");
                    string name = GetFirstString(obj, shopId, "name");
                    int inflationPct = GetFirstInt(obj, 0, "inflation_pct", "inflationPct");
                    List<ShopStockItem> stock = new List<ShopStockItem>();
                    if (TryGetAny(obj, out object? rawStock, "stock") && rawStock != null)
                    {
                        IReadOnlyList<object?> stockArr = MiniJson.AsArray(rawStock);
                        for (int i = 0; i < stockArr.Count; i++)
                        {
                            IReadOnlyDictionary<string, object?> sObj = MiniJson.AsObject(stockArr[i]);
                            int itemId = GetFirstInt(sObj, 0, "item_id", "itemId");
                            int price = GetFirstInt(sObj, 0, "price");
                            int quantity = GetFirstInt(sObj, -1, "quantity", "qty");
                            stock.Add(new ShopStockItem(itemId, price, quantity));
                        }
                    }

                    shops[shopId] = new ShopDefinition(shopId, name, inflationPct, stock);
                }
            }
        }

        private static bool[][] ParseGrid7x7(IReadOnlyDictionary<string, object?> obj)
        {
            bool[][] grid = new bool[7][];
            for (int y = 0; y < 7; y++)
            {
                grid[y] = new bool[7];
            }

            if (!TryGetAny(obj, out object? rawGrid, "grid_7x7", "grid7x7", "grid") || rawGrid == null)
            {
                return grid;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(rawGrid);
            if (arr.Count == 49 && !(arr[0] is IReadOnlyList<object?>))
            {
                for (int y = 0; y < 7; y++)
                {
                    for (int x = 0; x < 7; x++)
                    {
                        grid[y][x] = ToBoolCell(arr[y * 7 + x]);
                    }
                }

                return grid;
            }

            int rowCount = Math.Min(7, arr.Count);
            for (int y = 0; y < rowCount; y++)
            {
                IReadOnlyList<object?> row = MiniJson.AsArray(arr[y]);
                int colCount = Math.Min(7, row.Count);
                for (int x = 0; x < colCount; x++)
                {
                    grid[y][x] = ToBoolCell(row[x]);
                }
            }

            return grid;
        }

        private static bool ToBoolCell(object? raw)
        {
            if (raw is bool b)
            {
                return b;
            }

            if (raw is long l)
            {
                return l != 0;
            }

            if (raw is int i)
            {
                return i != 0;
            }

            return false;
        }

        private static CharConditionId? ParseConditionId(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            if (!TryGetAny(obj, out object? raw, keys) || raw == null)
            {
                return null;
            }

            if (raw is string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                if (Enum.TryParse<CharConditionId>(s, ignoreCase: true, out CharConditionId parsed))
                {
                    return parsed;
                }

                return null;
            }

            int numeric = MiniJson.ToInt32(raw, keys[0]);
            if (Enum.IsDefined(typeof(CharConditionId), numeric))
            {
                return (CharConditionId)numeric;
            }

            return null;
        }

        private static List<GridCoord> ParseCoordArray(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            List<GridCoord> result = new List<GridCoord>();
            if (!TryGetAny(obj, out object? raw, keys) || raw == null)
            {
                return result;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(raw);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is IReadOnlyDictionary<string, object?> coordObj)
                {
                    int x = GetFirstInt(coordObj, 0, "x", "dx");
                    int y = GetFirstInt(coordObj, 0, "y", "dy");
                    result.Add(new GridCoord(x, y));
                }
                else if (arr[i] is IReadOnlyList<object?> pair && pair.Count >= 2)
                {
                    int x = MiniJson.ToInt32(pair[0], "coord[0]");
                    int y = MiniJson.ToInt32(pair[1], "coord[1]");
                    result.Add(new GridCoord(x, y));
                }
            }

            return result;
        }

        private static IEnumerable<IReadOnlyDictionary<string, object?>> EnumerateObjects(object raw)
        {
            if (raw is IReadOnlyList<object?> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    yield return MiniJson.AsObject(list[i]);
                }
            }
            else if (raw is IReadOnlyDictionary<string, object?> dict)
            {
                foreach (object? val in dict.Values)
                {
                    yield return MiniJson.AsObject(val);
                }
            }
        }

        private static bool TryGetAny(IReadOnlyDictionary<string, object?> obj, out object? value, params string[] keys)
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

        private static bool HasAnyKey(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.ContainsKey(keys[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetFirstString(IReadOnlyDictionary<string, object?> obj, string defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    if (raw is string s)
                    {
                        return s;
                    }

                    return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? defaultValue;
                }
            }

            return defaultValue;
        }

        private static string GetStringOrInt(IReadOnlyDictionary<string, object?> obj, string defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    if (raw is string s)
                    {
                        return s;
                    }

                    return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? defaultValue;
                }
            }

            return defaultValue;
        }

        private static int GetFirstInt(IReadOnlyDictionary<string, object?> obj, int defaultValue, params string[] keys)
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

        private static double GetFirstDouble(IReadOnlyDictionary<string, object?> obj, double defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    return MiniJson.ToDouble(raw, keys[i]);
                }
            }

            return defaultValue;
        }

        private static bool GetFirstBool(IReadOnlyDictionary<string, object?> obj, bool defaultValue, params string[] keys)
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

                    if (raw is int iv)
                    {
                        return iv != 0;
                    }
                }
            }

            return defaultValue;
        }

        private static List<int> ParseIntArray(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            List<int> list = new List<int>();
            if (!TryGetAny(obj, out object? raw, keys) || raw == null)
            {
                return list;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(raw);
            for (int i = 0; i < arr.Count; i++)
            {
                list.Add(MiniJson.ToInt32(arr[i], $"{keys[0]}[{i}]"));
            }

            return list;
        }

        private static List<bool> ParseBoolArray(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            List<bool> list = new List<bool>();
            if (!TryGetAny(obj, out object? raw, keys) || raw == null)
            {
                return list;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(raw);
            for (int i = 0; i < arr.Count; i++)
            {
                list.Add(ToBoolCell(arr[i]));
            }

            return list;
        }

        private static string Slugify(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            char[] buf = new char[text.Length];
            int idx = 0;
            bool prevUnderscore = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    buf[idx++] = c;
                    prevUnderscore = false;
                }
                else if (!prevUnderscore && idx > 0)
                {
                    buf[idx++] = '_';
                    prevUnderscore = true;
                }
            }

            while (idx > 0 && buf[idx - 1] == '_')
            {
                idx--;
            }

            return idx == 0 ? fallback : new string(buf, 0, idx);
        }
    }
}
