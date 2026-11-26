using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RealmzCore.Exploration;
using RealmzCore.Serialization;

namespace RealmzCore.Data
{
    public sealed class MonsterBalanceOverride
    {
        private readonly string[] _specialAbilities;

        public MonsterBalanceOverride(
            string id,
            string name = "",
            string alias = "",
            int? hitDice = null,
            int? hpBonus = null,
            int? hpMin = null,
            int? hpMax = null,
            int? hpDefault = null,
            int? xpReward = null,
            string role = "",
            IReadOnlyList<string>? specialAbilities = null,
            int? baseMp = null,
            int? baseAp = null)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Alias = alias ?? string.Empty;
            HitDice = hitDice;
            HpBonus = hpBonus;
            HpMin = hpMin;
            HpMax = hpMax;
            HpDefault = hpDefault;
            XpReward = xpReward;
            Role = role ?? string.Empty;
            _specialAbilities = CopyStringList(specialAbilities);
            BaseMp = baseMp;
            BaseAp = baseAp;
        }

        public string Id { get; }
        public string Name { get; }
        public string Alias { get; }
        public int? HitDice { get; }
        public int? HpBonus { get; }
        public int? HpMin { get; }
        public int? HpMax { get; }
        public int? HpDefault { get; }
        public int? XpReward { get; }
        public string Role { get; }
        public IReadOnlyList<string> SpecialAbilities => _specialAbilities;
        public int? BaseMp { get; }
        public int? BaseAp { get; }

        private static string[] CopyStringList(IReadOnlyList<string>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> filtered = new List<string>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    filtered.Add(source[i].Trim());
                }
            }

            return filtered.Count == 0 ? Array.Empty<string>() : filtered.ToArray();
        }
    }

    public sealed class VisionRadiusConfig
    {
        public VisionRadiusConfig(
            int dayBase = 11,
            int dayScoutBonus = 2,
            int duskUnlit = 8,
            int duskLitTorch = 10,
            int nightUnlit = 4,
            int nightLitTorch = 9,
            int dungeonUnlit = 3,
            int dungeonLitTorch = 8)
        {
            DayBase = Math.Max(1, dayBase);
            DayScoutBonus = Math.Max(0, dayScoutBonus);
            DuskUnlit = Math.Max(1, duskUnlit);
            DuskLitTorch = Math.Max(1, duskLitTorch);
            NightUnlit = Math.Max(1, nightUnlit);
            NightLitTorch = Math.Max(1, nightLitTorch);
            DungeonUnlit = Math.Max(1, dungeonUnlit);
            DungeonLitTorch = Math.Max(1, dungeonLitTorch);
        }

        public int DayBase { get; }
        public int DayScoutBonus { get; }
        public int DuskUnlit { get; }
        public int DuskLitTorch { get; }
        public int NightUnlit { get; }
        public int NightLitTorch { get; }
        public int DungeonUnlit { get; }
        public int DungeonLitTorch { get; }
    }

    public sealed class SurvivalBalanceConfig
    {
        public SurvivalBalanceConfig(
            int torchPrice = 12,
            int rationPrice = 5,
            int torchTurnDuration = 120,
            int bandageMaxCharges = 2,
            VisionRadiusConfig? visionRadii = null,
            float minutesPerRealSecond = 1.0f)
        {
            TorchPrice = Math.Max(1, torchPrice);
            RationPrice = Math.Max(1, rationPrice);
            TorchTurnDuration = Math.Max(1, torchTurnDuration);
            BandageMaxCharges = Math.Max(1, bandageMaxCharges);
            VisionRadii = visionRadii ?? new VisionRadiusConfig();
            MinutesPerRealSecond = float.IsNaN(minutesPerRealSecond) || float.IsInfinity(minutesPerRealSecond)
                ? 1.0f
                : Math.Clamp(minutesPerRealSecond, 0f, 10f);
        }

        public int TorchPrice { get; }
        public int RationPrice { get; }
        public int TorchTurnDuration { get; }
        public int BandageMaxCharges { get; }
        public VisionRadiusConfig VisionRadii { get; }
        public float MinutesPerRealSecond { get; }
    }

    public sealed class BalanceOverrides
    {
        private readonly List<MonsterBalanceOverride> _monsters;
        private readonly Dictionary<string, MonsterBalanceOverride> _monstersById;
        private readonly List<EquipmentSetDefinition> _equipmentSets;
        private readonly List<QuestDefinition> _quests;

        public BalanceOverrides(
            int version = 1,
            IReadOnlyList<MonsterBalanceOverride>? monsters = null,
            SurvivalBalanceConfig? survival = null,
            IReadOnlyList<EquipmentSetDefinition>? equipmentSets = null,
            IReadOnlyList<QuestDefinition>? quests = null)
        {
            Version = version;
            _monsters = monsters != null
                ? new List<MonsterBalanceOverride>(monsters)
                : new List<MonsterBalanceOverride>();
            _monstersById = new Dictionary<string, MonsterBalanceOverride>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _monsters.Count; i++)
            {
                MonsterBalanceOverride ov = _monsters[i];
                if (!string.IsNullOrWhiteSpace(ov.Id))
                {
                    _monstersById[ov.Id] = ov;
                }
            }

            Survival = survival ?? new SurvivalBalanceConfig();
            _equipmentSets = equipmentSets != null
                ? new List<EquipmentSetDefinition>(equipmentSets)
                : new List<EquipmentSetDefinition>();
            _quests = quests != null
                ? new List<QuestDefinition>(quests)
                : new List<QuestDefinition>();
        }

        public int Version { get; }
        public IReadOnlyList<MonsterBalanceOverride> Monsters => _monsters;
        public IReadOnlyDictionary<string, MonsterBalanceOverride> MonstersById => _monstersById;
        public SurvivalBalanceConfig Survival { get; }
        public IReadOnlyList<EquipmentSetDefinition> EquipmentSets => _equipmentSets;
        public IReadOnlyList<QuestDefinition> Quests => _quests;

        public static BalanceOverrides CreateDefault()
        {
            List<MonsterBalanceOverride> monsters = new List<MonsterBalanceOverride>
            {
                new MonsterBalanceOverride(
                    id: "orc_warrior",
                    name: "Orc Warrior",
                    alias: "orc",
                    hitDice: 3,
                    hpBonus: 3,
                    hpMin: 18,
                    hpMax: 24,
                    hpDefault: 21,
                    xpReward: 65,
                    role: "Bruiser",
                    specialAbilities: new[] { "cleave_2tile" },
                    baseMp: 4,
                    baseAp: 6),
                new MonsterBalanceOverride(
                    id: "giant_frog",
                    name: "Giant Frog",
                    alias: "frog",
                    hitDice: 3,
                    hpBonus: 0,
                    hpMin: 15,
                    hpMax: 19,
                    hpDefault: 17,
                    xpReward: 70,
                    role: "Controller",
                    specialAbilities: new[] { "tongue_pull" },
                    baseMp: 5,
                    baseAp: 6),
                new MonsterBalanceOverride(
                    id: "goblin",
                    name: "Goblin",
                    alias: "goblin_scout",
                    hitDice: 1,
                    hpBonus: 0,
                    hpMin: 8,
                    hpMax: 11,
                    hpDefault: 9,
                    role: "Skirmisher",
                    baseMp: 4,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "goblin_archer",
                    name: "Goblin Archer",
                    alias: "goblin_bowman",
                    hitDice: 1,
                    hpBonus: 0,
                    hpMin: 6,
                    hpMax: 9,
                    hpDefault: 8,
                    role: "Ranged",
                    specialAbilities: new[] { "high_ground_seeker" },
                    baseMp: 4,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "giant_rat",
                    name: "Giant Rat",
                    hpMin: 6,
                    hpMax: 8,
                    hpDefault: 7,
                    role: "Skirmisher",
                    baseMp: 3,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "zombie",
                    name: "Zombie",
                    hpMin: 18,
                    hpMax: 23,
                    hpDefault: 20,
                    role: "Skirmisher",
                    baseMp: 3,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "skeletal_warrior",
                    name: "Skeletal Warrior",
                    hpMin: 12,
                    hpMax: 16,
                    hpDefault: 14,
                    role: "Skirmisher",
                    baseMp: 4,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "orc_archer",
                    name: "Orc Archer",
                    hpMin: 10,
                    hpMax: 14,
                    hpDefault: 12,
                    role: "Ranged",
                    baseMp: 4,
                    baseAp: 4),
                new MonsterBalanceOverride(
                    id: "monstrous_rat",
                    name: "Monstrous Rat",
                    hpMin: 16,
                    hpMax: 20,
                    hpDefault: 18,
                    role: "Skirmisher",
                    baseMp: 4,
                    baseAp: 5),
                new MonsterBalanceOverride(
                    id: "giant_spider",
                    name: "Giant Spider",
                    hpMin: 11,
                    hpMax: 15,
                    hpDefault: 13,
                    role: "Skirmisher",
                    baseMp: 4,
                    baseAp: 5),
                new MonsterBalanceOverride(
                    id: "goblin_champion",
                    name: "Goblin Champion",
                    hpMin: 20,
                    hpMax: 25,
                    hpDefault: 22,
                    role: "Skirmisher",
                    baseMp: 4,
                    baseAp: 6),
                new MonsterBalanceOverride(
                    id: "goblin_shaman",
                    name: "Goblin Shaman",
                    hpMin: 16,
                    hpMax: 20,
                    hpDefault: 18,
                    role: "Caster",
                    baseMp: 4,
                    baseAp: 6)
            };

            SurvivalBalanceConfig survival = new SurvivalBalanceConfig(
                torchPrice: 12,
                rationPrice: 5,
                torchTurnDuration: 120,
                bandageMaxCharges: 2,
                visionRadii: new VisionRadiusConfig(),
                minutesPerRealSecond: 1.0f);

            return new BalanceOverrides(
                version: 1,
                monsters: monsters,
                survival: survival,
                equipmentSets: EquipmentSetCatalog.AllSets,
                quests: QuestCatalog.BywaterNoticeBoardBounties);
        }

        public static BalanceOverrides LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("File path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Balance overrides file not found: {path}", path);
            }

            string json = File.ReadAllText(path);
            return FromJson(json);
        }

        public static BalanceOverrides FromJson(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefault();
            }

            IReadOnlyDictionary<string, object?> root = MiniJson.AsObject(MiniJson.Parse(json));
            BalanceOverrides defaults = CreateDefault();

            int version = GetFirstInt(root, defaults.Version, "version");

            List<MonsterBalanceOverride> monsters;
            if (TryGetAny(root, out object? rawMonsters, "monsters") && rawMonsters != null)
            {
                monsters = ParseMonsterOverrides(MiniJson.AsArray(rawMonsters));
            }
            else
            {
                monsters = new List<MonsterBalanceOverride>(defaults.Monsters);
            }

            SurvivalBalanceConfig survival = defaults.Survival;
            if (TryGetAny(root, out object? rawSurvival, "survival") &&
                rawSurvival is IReadOnlyDictionary<string, object?> survivalObj)
            {
                survival = ParseSurvival(survivalObj, defaults.Survival);
            }

            List<EquipmentSetDefinition> equipmentSets;
            if (TryGetAny(root, out object? rawSets, "equipment_sets", "equipmentSets") && rawSets != null)
            {
                equipmentSets = ParseEquipmentSets(MiniJson.AsArray(rawSets));
            }
            else
            {
                equipmentSets = new List<EquipmentSetDefinition>(defaults.EquipmentSets);
            }

            List<QuestDefinition> quests;
            if (TryGetAny(root, out object? rawQuests, "quests") && rawQuests != null)
            {
                quests = ParseQuests(MiniJson.AsArray(rawQuests));
            }
            else
            {
                quests = new List<QuestDefinition>(defaults.Quests);
            }

            return new BalanceOverrides(version, monsters, survival, equipmentSets, quests);
        }

        public string ToJson(bool indented = true)
        {
            Dictionary<string, object?> root = new Dictionary<string, object?>
            {
                ["version"] = Version,
                ["monsters"] = SerializeMonsters(),
                ["survival"] = SerializeSurvival(),
                ["equipment_sets"] = SerializeEquipmentSets(),
                ["quests"] = SerializeQuests()
            };

            return MiniJsonWriter.Serialize(root, indented);
        }

        public List<MonsterDefinition> ApplyToMonsters(IReadOnlyList<MonsterDefinition> monsters)
        {
            if (monsters == null)
            {
                throw new ArgumentNullException(nameof(monsters));
            }

            List<MonsterDefinition> updated = new List<MonsterDefinition>(monsters.Count);
            for (int i = 0; i < monsters.Count; i++)
            {
                updated.Add(ApplyToMonster(monsters[i]));
            }

            return updated;
        }

        public MonsterDefinition ApplyToMonster(MonsterDefinition monster)
        {
            if (monster == null)
            {
                throw new ArgumentNullException(nameof(monster));
            }

            MonsterBalanceOverride? ov = FindOverrideForMonster(monster);
            if (ov == null)
            {
                return monster;
            }

            string name = !string.IsNullOrWhiteSpace(ov.Name) ? ov.Name : monster.Name;
            int hitDice = ov.HitDice ?? monster.HitDice;
            int hpBonus = ov.HpBonus ?? monster.HpBonus;
            int hpMin = ov.HpMin ?? monster.HpMin;
            int hpMax = ov.HpMax ?? monster.HpMax;
            int hpDefault = ov.HpDefault ?? monster.HpDefault;
            int xpReward = ov.XpReward ?? monster.XpReward;
            string role = !string.IsNullOrWhiteSpace(ov.Role) ? ov.Role : monster.Role;
            IReadOnlyList<string> specialAbilities = ov.SpecialAbilities.Count > 0
                ? ov.SpecialAbilities
                : monster.SpecialAbilities;
            int baseMp = ov.BaseMp ?? monster.BaseMp;
            int baseAp = ov.BaseAp ?? monster.BaseAp;

            return new MonsterDefinition(
                index: monster.Index,
                id: monster.Id,
                name: name,
                iconId: monster.IconId,
                hitDice: hitDice,
                hpBonus: hpBonus,
                hpMin: hpMin,
                hpMax: hpMax,
                hpDefault: hpDefault,
                ac: monster.Ac,
                toHitBonus: monster.ToHitBonus,
                dexterity: monster.Dexterity,
                movementMax: monster.MovementMax,
                size: monster.Size,
                numAttacks: monster.NumAttacks,
                attackDice: monster.AttackDice,
                damageBonus: monster.DamageBonus,
                missileChancePct: monster.MissileChancePct,
                castChancePct: monster.CastChancePct,
                rangedRange: monster.RangedRange,
                rangedDice: monster.RangedDice,
                spellIds: monster.SpellIds,
                magicResistance: monster.MagicResistance,
                saves: monster.Saves,
                spellImmunities: monster.SpellImmunities,
                isEvil: monster.IsEvil,
                isUndead: monster.IsUndead,
                xpReward: xpReward,
                goldMin: monster.GoldMin,
                goldMax: monster.GoldMax,
                lootItemIds: monster.LootItemIds,
                role: role,
                specialAbilities: specialAbilities,
                baseMp: baseMp,
                baseAp: baseAp);
        }

        private MonsterBalanceOverride? FindOverrideForMonster(MonsterDefinition monster)
        {
            if (!string.IsNullOrWhiteSpace(monster.Id) &&
                _monstersById.TryGetValue(monster.Id, out MonsterBalanceOverride? direct))
            {
                return direct;
            }

            for (int i = 0; i < _monsters.Count; i++)
            {
                MonsterBalanceOverride ov = _monsters[i];
                if (!string.IsNullOrWhiteSpace(ov.Alias) &&
                    (string.Equals(ov.Alias, monster.Id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ov.Alias, monster.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return ov;
                }

                if (!string.IsNullOrWhiteSpace(ov.Name) &&
                    string.Equals(ov.Name, monster.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return ov;
                }
            }

            return null;
        }

        private List<object?> SerializeMonsters()
        {
            List<object?> list = new List<object?>(_monsters.Count);
            for (int i = 0; i < _monsters.Count; i++)
            {
                MonsterBalanceOverride m = _monsters[i];
                Dictionary<string, object?> obj = new Dictionary<string, object?>
                {
                    ["id"] = m.Id,
                    ["name"] = m.Name,
                    ["alias"] = m.Alias
                };

                if (m.HitDice.HasValue) obj["hit_dice"] = m.HitDice.Value;
                if (m.HpBonus.HasValue) obj["hp_bonus"] = m.HpBonus.Value;
                if (m.HpMin.HasValue) obj["hp_min"] = m.HpMin.Value;
                if (m.HpMax.HasValue) obj["hp_max"] = m.HpMax.Value;
                if (m.HpDefault.HasValue) obj["hp_default"] = m.HpDefault.Value;
                if (m.XpReward.HasValue) obj["xp_reward"] = m.XpReward.Value;
                if (m.BaseMp.HasValue) obj["base_mp"] = m.BaseMp.Value;
                if (m.BaseAp.HasValue) obj["base_ap"] = m.BaseAp.Value;

                obj["role"] = m.Role;
                List<string> abilities = new List<string>(m.SpecialAbilities);
                obj["special_abilities"] = abilities;
                list.Add(obj);
            }

            return list;
        }

        private Dictionary<string, object?> SerializeSurvival()
        {
            VisionRadiusConfig v = Survival.VisionRadii;
            Dictionary<string, object?> radiiObj = new Dictionary<string, object?>
            {
                ["day_base"] = v.DayBase,
                ["day_scout_bonus"] = v.DayScoutBonus,
                ["dusk_unlit"] = v.DuskUnlit,
                ["dusk_lit_torch"] = v.DuskLitTorch,
                ["night_unlit"] = v.NightUnlit,
                ["night_lit_torch"] = v.NightLitTorch,
                ["dungeon_unlit"] = v.DungeonUnlit,
                ["dungeon_lit_torch"] = v.DungeonLitTorch
            };

            return new Dictionary<string, object?>
            {
                ["torch_price"] = Survival.TorchPrice,
                ["ration_price"] = Survival.RationPrice,
                ["torch_turn_duration"] = Survival.TorchTurnDuration,
                ["bandage_max_charges"] = Survival.BandageMaxCharges,
                ["minutes_per_real_second"] = Survival.MinutesPerRealSecond,
                ["vision_radii"] = radiiObj
            };
        }

        private List<object?> SerializeEquipmentSets()
        {
            List<object?> list = new List<object?>(_equipmentSets.Count);
            for (int i = 0; i < _equipmentSets.Count; i++)
            {
                EquipmentSetDefinition set = _equipmentSets[i];
                List<object?> tiers = new List<object?>(set.Tiers.Count);
                for (int t = 0; t < set.Tiers.Count; t++)
                {
                    EquipmentSetBonusTier tier = set.Tiers[t];
                    tiers.Add(new Dictionary<string, object?>
                    {
                        ["required_pieces"] = tier.RequiredPieces,
                        ["ac_bonus"] = tier.AcBonus,
                        ["ap_bonus"] = tier.ApBonus,
                        ["free_move_tiles"] = tier.FreeMoveTiles,
                        ["guard_ap_discount"] = tier.GuardApDiscount,
                        ["crit_bonus_pct"] = tier.CritBonusPct,
                        ["ranged_range_bonus"] = tier.RangedRangeBonus,
                        ["ignore_half_cover"] = tier.IgnoreHalfCover,
                        ["sp_cost_discount"] = tier.SpCostDiscount,
                        ["spell_damage_bonus"] = tier.SpellDamageBonus,
                        ["healing_bonus"] = tier.HealingBonus,
                        ["support_spell_ap_discount"] = tier.SupportSpellApDiscount,
                        ["undead_damage_bonus_pct"] = tier.UndeadDamageBonusPct,
                        ["summary"] = tier.Summary
                    });
                }

                list.Add(new Dictionary<string, object?>
                {
                    ["set_id"] = set.SetId,
                    ["name"] = set.Name,
                    ["item_ids"] = new List<int>(set.ItemIds),
                    ["tiers"] = tiers,
                    ["display_max_pieces"] = set.DisplayMaxPieces,
                    ["is_preview"] = set.IsPreview
                });
            }

            return list;
        }

        private List<object?> SerializeQuests()
        {
            List<object?> list = new List<object?>(_quests.Count);
            for (int i = 0; i < _quests.Count; i++)
            {
                QuestDefinition q = _quests[i];
                list.Add(new Dictionary<string, object?>
                {
                    ["quest_id"] = q.QuestId,
                    ["title"] = q.Title,
                    ["source"] = q.Source,
                    ["description"] = q.Description,
                    ["target_monster_id"] = q.TargetMonsterId,
                    ["target_count"] = q.TargetCount,
                    ["gold_reward"] = q.GoldReward,
                    ["xp_reward"] = q.XpReward,
                    ["location_hint"] = q.LocationHint,
                    ["is_notice_board_bounty"] = q.IsNoticeBoardBounty,
                    ["reward_item_id"] = q.RewardItemId
                });
            }

            return list;
        }

        private static List<MonsterBalanceOverride> ParseMonsterOverrides(IReadOnlyList<object?> arr)
        {
            List<MonsterBalanceOverride> list = new List<MonsterBalanceOverride>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                list.Add(new MonsterBalanceOverride(
                    id: GetFirstString(obj, string.Empty, "id", "monster_id", "monsterId"),
                    name: GetFirstString(obj, string.Empty, "name"),
                    alias: GetFirstString(obj, string.Empty, "alias"),
                    hitDice: GetOptionalInt(obj, "hit_dice", "hitDice", "hd"),
                    hpBonus: GetOptionalInt(obj, "hp_bonus", "hpBonus"),
                    hpMin: GetOptionalInt(obj, "hp_min", "hpMin"),
                    hpMax: GetOptionalInt(obj, "hp_max", "hpMax"),
                    hpDefault: GetOptionalInt(obj, "hp_default", "hpDefault"),
                    xpReward: GetOptionalInt(obj, "xp_reward", "xpReward"),
                    role: GetFirstString(obj, string.Empty, "role"),
                    specialAbilities: ParseStringArray(obj, "special_abilities", "specialAbilities"),
                    baseMp: GetOptionalInt(obj, "base_mp", "baseMp"),
                    baseAp: GetOptionalInt(obj, "base_ap", "baseAp")));
            }

            return list;
        }

        private static SurvivalBalanceConfig ParseSurvival(
            IReadOnlyDictionary<string, object?> obj,
            SurvivalBalanceConfig defaults)
        {
            VisionRadiusConfig radii = defaults.VisionRadii;
            if (TryGetAny(obj, out object? rawRadii, "vision_radii", "visionRadii") &&
                rawRadii is IReadOnlyDictionary<string, object?> radiiObj)
            {
                radii = new VisionRadiusConfig(
                    dayBase: GetFirstInt(radiiObj, defaults.VisionRadii.DayBase, "day_base", "dayBase"),
                    dayScoutBonus: GetFirstInt(radiiObj, defaults.VisionRadii.DayScoutBonus, "day_scout_bonus", "dayScoutBonus"),
                    duskUnlit: GetFirstInt(radiiObj, defaults.VisionRadii.DuskUnlit, "dusk_unlit", "duskUnlit"),
                    duskLitTorch: GetFirstInt(radiiObj, defaults.VisionRadii.DuskLitTorch, "dusk_lit_torch", "duskLitTorch"),
                    nightUnlit: GetFirstInt(radiiObj, defaults.VisionRadii.NightUnlit, "night_unlit", "nightUnlit"),
                    nightLitTorch: GetFirstInt(radiiObj, defaults.VisionRadii.NightLitTorch, "night_lit_torch", "nightLitTorch"),
                    dungeonUnlit: GetFirstInt(radiiObj, defaults.VisionRadii.DungeonUnlit, "dungeon_unlit", "dungeonUnlit"),
                    dungeonLitTorch: GetFirstInt(radiiObj, defaults.VisionRadii.DungeonLitTorch, "dungeon_lit_torch", "dungeonLitTorch"));
            }

            return new SurvivalBalanceConfig(
                torchPrice: GetFirstInt(obj, defaults.TorchPrice, "torch_price", "torchPrice"),
                rationPrice: GetFirstInt(obj, defaults.RationPrice, "ration_price", "rationPrice"),
                torchTurnDuration: GetFirstInt(obj, defaults.TorchTurnDuration, "torch_turn_duration", "torchTurnDuration"),
                bandageMaxCharges: GetFirstInt(obj, defaults.BandageMaxCharges, "bandage_max_charges", "bandageMaxCharges"),
                visionRadii: radii,
                minutesPerRealSecond: GetFirstFloat(obj, defaults.MinutesPerRealSecond, "minutes_per_real_second", "minutesPerRealSecond"));
        }

        private static List<EquipmentSetDefinition> ParseEquipmentSets(IReadOnlyList<object?> arr)
        {
            List<EquipmentSetDefinition> sets = new List<EquipmentSetDefinition>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                string setId = GetFirstString(obj, $"set_{i}", "set_id", "setId", "id");
                string name = GetFirstString(obj, setId, "name");
                List<int> itemIds = ParseIntArray(obj, "item_ids", "itemIds");
                int displayMax = GetFirstInt(obj, 3, "display_max_pieces", "displayMaxPieces");
                bool isPreview = GetFirstBool(obj, false, "is_preview", "isPreview");

                List<EquipmentSetBonusTier> tiers = new List<EquipmentSetBonusTier>();
                if (TryGetAny(obj, out object? rawTiers, "tiers") && rawTiers != null)
                {
                    IReadOnlyList<object?> tierArr = MiniJson.AsArray(rawTiers);
                    for (int t = 0; t < tierArr.Count; t++)
                    {
                        IReadOnlyDictionary<string, object?> tObj = MiniJson.AsObject(tierArr[t]);
                        tiers.Add(new EquipmentSetBonusTier(
                            requiredPieces: GetFirstInt(tObj, 2, "required_pieces", "requiredPieces"),
                            acBonus: GetFirstInt(tObj, 0, "ac_bonus", "acBonus"),
                            apBonus: GetFirstInt(tObj, 0, "ap_bonus", "apBonus"),
                            freeMoveTiles: GetFirstInt(tObj, 0, "free_move_tiles", "freeMoveTiles"),
                            guardApDiscount: GetFirstInt(tObj, 0, "guard_ap_discount", "guardApDiscount"),
                            summary: GetFirstString(tObj, string.Empty, "summary"),
                            critBonusPct: GetFirstInt(tObj, 0, "crit_bonus_pct", "critBonusPct"),
                            rangedRangeBonus: GetFirstInt(tObj, 0, "ranged_range_bonus", "rangedRangeBonus"),
                            ignoreHalfCover: GetFirstBool(tObj, false, "ignore_half_cover", "ignoreHalfCover"),
                            spCostDiscount: GetFirstInt(tObj, 0, "sp_cost_discount", "spCostDiscount"),
                            spellDamageBonus: GetFirstInt(tObj, 0, "spell_damage_bonus", "spellDamageBonus"),
                            healingBonus: GetFirstInt(tObj, 0, "healing_bonus", "healingBonus"),
                            supportSpellApDiscount: GetFirstInt(tObj, 0, "support_spell_ap_discount", "supportSpellApDiscount"),
                            undeadDamageBonusPct: GetFirstInt(tObj, 0, "undead_damage_bonus_pct", "undeadDamageBonusPct")));
                    }
                }

                sets.Add(new EquipmentSetDefinition(setId, name, itemIds, tiers, displayMax, isPreview));
            }

            return sets;
        }

        private static List<QuestDefinition> ParseQuests(IReadOnlyList<object?> arr)
        {
            List<QuestDefinition> quests = new List<QuestDefinition>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                IReadOnlyDictionary<string, object?> obj = MiniJson.AsObject(arr[i]);
                quests.Add(new QuestDefinition(
                    questId: GetFirstString(obj, $"quest_{i}", "quest_id", "questId", "id"),
                    title: GetFirstString(obj, string.Empty, "title", "name"),
                    source: GetFirstString(obj, string.Empty, "source"),
                    description: GetFirstString(obj, string.Empty, "description"),
                    targetMonsterId: GetFirstString(obj, string.Empty, "target_monster_id", "targetMonsterId"),
                    targetCount: GetFirstInt(obj, 1, "target_count", "targetCount"),
                    goldReward: GetFirstInt(obj, 0, "gold_reward", "goldReward"),
                    xpReward: GetFirstInt(obj, 0, "xp_reward", "xpReward"),
                    locationHint: GetFirstString(obj, string.Empty, "location_hint", "locationHint"),
                    isNoticeBoardBounty: GetFirstBool(obj, false, "is_notice_board_bounty", "isNoticeBoardBounty"),
                    rewardItemId: GetFirstInt(obj, 0, "reward_item_id", "rewardItemId")));
            }

            return quests;
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

        private static float GetFirstFloat(IReadOnlyDictionary<string, object?> obj, float defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    return (float)MiniJson.ToDouble(raw, keys[i]);
                }
            }

            return defaultValue;
        }

        private static int? GetOptionalInt(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object? raw) && raw != null)
                {
                    return MiniJson.ToInt32(raw, keys[i]);
                }
            }

            return null;
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

        private static List<string> ParseStringArray(IReadOnlyDictionary<string, object?> obj, params string[] keys)
        {
            List<string> list = new List<string>();
            if (!TryGetAny(obj, out object? raw, keys) || raw == null)
            {
                return list;
            }

            IReadOnlyList<object?> arr = MiniJson.AsArray(raw);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is string s && !string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
                else if (arr[i] != null)
                {
                    string converted = Convert.ToString(arr[i], CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(converted))
                    {
                        list.Add(converted.Trim());
                    }
                }
            }

            return list;
        }
    }
}
