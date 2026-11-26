using System;
using System.Collections.Generic;
using System.Globalization;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Serialization;
using UnityEngine;

namespace Realmz.Data
{
    /// <summary>
    /// Designer-editable ScriptableObject mirroring StreamingAssets/bywater_m1/balance_overrides.json
    /// for monster balance overrides, survival economy/vision radii, equipment sets, and bounty quests.
    /// </summary>
    [CreateAssetMenu(fileName = "RealmzBalanceCatalog", menuName = "Realmz/Balance Catalog")]
    public sealed class RealmzBalanceCatalog : ScriptableObject
    {
        public int version = 1;
        public MonsterBalanceEntry[] monsters = CreateDefaultMonsters();
        public SurvivalBalanceEntry survival = CreateDefaultSurvival();
        public EquipmentSetEntry[] equipmentSets = CreateDefaultEquipmentSets();
        public QuestBountyEntry[] quests = CreateDefaultQuests();

        [Serializable]
        public sealed class MonsterBalanceEntry
        {
            public string id = string.Empty;
            public string name = string.Empty;
            public string alias = string.Empty;
            public int hitDice;
            public int hpBonus;
            public int hpMin;
            public int hpMax;
            public int hpDefault;
            public int xpReward;
            public int baseMp;
            public int baseAp;
            public string role = string.Empty;
            public string[] specialAbilities = Array.Empty<string>();
        }

        [Serializable]
        public sealed class VisionRadiiConfig
        {
            public int dayBase = 11;
            public int dayScoutBonus = 2;
            public int duskUnlit = 8;
            public int duskLitTorch = 10;
            public int nightUnlit = 4;
            public int nightLitTorch = 9;
            public int dungeonUnlit = 3;
            public int dungeonLitTorch = 8;
        }

        [Serializable]
        public sealed class SurvivalBalanceEntry
        {
            public int torchPrice = 12;
            public int rationPrice = 5;
            public int torchTurnDuration = 120;
            public int bandageMaxCharges = 2;
            public float minutesPerRealSecond = 1.0f;
            public VisionRadiiConfig visionRadii = new VisionRadiiConfig();
        }

        [Serializable]
        public sealed class EquipmentSetTierEntry
        {
            public int requiredPieces;
            public int acBonus;
            public int apBonus;
            public int freeMoveTiles;
            public int guardApDiscount;
            public int critBonusPct;
            public int rangedRangeBonus;
            public bool ignoreHalfCover;
            public int spCostDiscount;
            public int spellDamageBonus;
            public int healingBonus;
            public int supportSpellApDiscount;
            public int undeadDamageBonusPct;
            public string summary = string.Empty;
        }

        [Serializable]
        public sealed class EquipmentSetEntry
        {
            public string setId = string.Empty;
            public string name = string.Empty;
            public bool isPreview;
            public int displayMaxPieces = 3;
            public int[] itemIds = Array.Empty<int>();
            public EquipmentSetTierEntry[] tiers = Array.Empty<EquipmentSetTierEntry>();
        }

        [Serializable]
        public sealed class QuestBountyEntry
        {
            public string questId = string.Empty;
            public string title = string.Empty;
            public string source = string.Empty;
            public string description = string.Empty;
            public string targetMonsterId = string.Empty;
            public int targetCount = 1;
            public int goldReward;
            public int xpReward;
            public string locationHint = string.Empty;
            public bool isNoticeBoardBounty;
            public int rewardItemId;
        }

        private void OnEnable()
        {
            if (version <= 0)
            {
                version = 1;
            }

            if (survival == null)
            {
                survival = CreateDefaultSurvival();
            }
            else if (survival.visionRadii == null)
            {
                survival.visionRadii = new VisionRadiiConfig();
            }

            if (monsters == null || monsters.Length == 0)
            {
                monsters = CreateDefaultMonsters();
            }

            if (equipmentSets == null || equipmentSets.Length == 0)
            {
                equipmentSets = CreateDefaultEquipmentSets();
            }

            if (quests == null || quests.Length == 0)
            {
                quests = CreateDefaultQuests();
            }
        }

        private void Reset()
        {
            ResetToDefaults();
        }

        [ContextMenu("Reset To Defaults")]
        public void ResetToDefaults()
        {
            version = 1;
            monsters = CreateDefaultMonsters();
            survival = CreateDefaultSurvival();
            equipmentSets = CreateDefaultEquipmentSets();
            quests = CreateDefaultQuests();
        }

        public bool LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                object parsed = MiniJson.Parse(json);
                IReadOnlyDictionary<string, object> root = MiniJson.AsObject(parsed);

                version = Math.Max(1, GetFirstInt(root, version > 0 ? version : 1, "version"));

                if (TryGetAny(root, out object rawMonsters, "monsters") && rawMonsters != null)
                {
                    monsters = ParseMonsters(MiniJson.AsArray(rawMonsters));
                }
                else if (monsters == null || monsters.Length == 0)
                {
                    monsters = CreateDefaultMonsters();
                }

                if (TryGetAny(root, out object rawSurvival, "survival") && rawSurvival != null)
                {
                    survival = ParseSurvival(MiniJson.AsObject(rawSurvival));
                }
                else if (survival == null)
                {
                    survival = CreateDefaultSurvival();
                }

                if (TryGetAny(root, out object rawSets, "equipment_sets", "equipmentSets") && rawSets != null)
                {
                    equipmentSets = ParseEquipmentSets(MiniJson.AsArray(rawSets));
                }
                else if (equipmentSets == null || equipmentSets.Length == 0)
                {
                    equipmentSets = CreateDefaultEquipmentSets();
                }

                if (TryGetAny(root, out object rawQuests, "quests") && rawQuests != null)
                {
                    quests = ParseQuests(MiniJson.AsArray(rawQuests));
                }
                else if (quests == null || quests.Length == 0)
                {
                    quests = CreateDefaultQuests();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RealmzBalanceCatalog] Failed to parse balance JSON: " + ex.Message);
                return false;
            }
        }

        public string ToJson(bool indented = true)
        {
            return ExportToJson(indented);
        }

        public BalanceOverrides ApplyToBalanceOverrides()
        {
            MonsterBalanceEntry[] monsterSource = monsters ?? Array.Empty<MonsterBalanceEntry>();
            List<MonsterBalanceOverride> monsterOverrides = new List<MonsterBalanceOverride>(monsterSource.Length);
            for (int i = 0; i < monsterSource.Length; i++)
            {
                MonsterBalanceEntry m = monsterSource[i];
                if (m == null || string.IsNullOrWhiteSpace(m.id))
                {
                    continue;
                }

                monsterOverrides.Add(new MonsterBalanceOverride(
                    id: m.id,
                    name: m.name ?? string.Empty,
                    alias: m.alias ?? string.Empty,
                    hitDice: m.hitDice > 0 ? m.hitDice : (int?)null,
                    hpBonus: m.hpBonus != 0 ? m.hpBonus : (int?)null,
                    hpMin: m.hpMin > 0 ? m.hpMin : (int?)null,
                    hpMax: m.hpMax > 0 ? m.hpMax : (int?)null,
                    hpDefault: m.hpDefault > 0 ? m.hpDefault : (int?)null,
                    xpReward: m.xpReward > 0 ? m.xpReward : (int?)null,
                    role: m.role ?? string.Empty,
                    specialAbilities: m.specialAbilities,
                    baseMp: m.baseMp > 0 ? m.baseMp : (int?)null,
                    baseAp: m.baseAp > 0 ? m.baseAp : (int?)null));
            }

            SurvivalBalanceEntry s = survival ?? CreateDefaultSurvival();
            VisionRadiiConfig v = s.visionRadii ?? new VisionRadiiConfig();
            VisionRadiusConfig visionRadiusConfig = new VisionRadiusConfig(
                dayBase: v.dayBase,
                dayScoutBonus: v.dayScoutBonus,
                duskUnlit: v.duskUnlit,
                duskLitTorch: v.duskLitTorch,
                nightUnlit: v.nightUnlit,
                nightLitTorch: v.nightLitTorch,
                dungeonUnlit: v.dungeonUnlit,
                dungeonLitTorch: v.dungeonLitTorch);

            float clampedMinutesPerRealSecond = Mathf.Clamp(s.minutesPerRealSecond, 0f, 10f);
            SurvivalBalanceConfig survivalConfig = new SurvivalBalanceConfig(
                torchPrice: Math.Max(1, s.torchPrice),
                rationPrice: Math.Max(1, s.rationPrice),
                torchTurnDuration: Math.Max(1, s.torchTurnDuration),
                bandageMaxCharges: Math.Max(1, s.bandageMaxCharges),
                visionRadii: visionRadiusConfig,
                minutesPerRealSecond: clampedMinutesPerRealSecond);

            EquipmentSetEntry[] setSource = equipmentSets ?? Array.Empty<EquipmentSetEntry>();
            List<EquipmentSetDefinition> setDefinitions = new List<EquipmentSetDefinition>(setSource.Length);
            for (int i = 0; i < setSource.Length; i++)
            {
                EquipmentSetEntry set = setSource[i];
                if (set == null || string.IsNullOrWhiteSpace(set.setId))
                {
                    continue;
                }

                EquipmentSetTierEntry[] tierSource = set.tiers ?? Array.Empty<EquipmentSetTierEntry>();
                List<EquipmentSetBonusTier> tiers = new List<EquipmentSetBonusTier>(tierSource.Length);
                for (int t = 0; t < tierSource.Length; t++)
                {
                    EquipmentSetTierEntry tier = tierSource[t];
                    if (tier == null)
                    {
                        continue;
                    }

                    tiers.Add(new EquipmentSetBonusTier(
                        requiredPieces: Math.Max(1, tier.requiredPieces),
                        acBonus: tier.acBonus,
                        apBonus: tier.apBonus,
                        freeMoveTiles: tier.freeMoveTiles,
                        guardApDiscount: tier.guardApDiscount,
                        summary: tier.summary ?? string.Empty,
                        critBonusPct: tier.critBonusPct,
                        rangedRangeBonus: tier.rangedRangeBonus,
                        ignoreHalfCover: tier.ignoreHalfCover,
                        spCostDiscount: tier.spCostDiscount,
                        spellDamageBonus: tier.spellDamageBonus,
                        healingBonus: tier.healingBonus,
                        supportSpellApDiscount: tier.supportSpellApDiscount,
                        undeadDamageBonusPct: tier.undeadDamageBonusPct));
                }

                setDefinitions.Add(new EquipmentSetDefinition(
                    setId: set.setId,
                    name: string.IsNullOrWhiteSpace(set.name) ? set.setId : set.name,
                    itemIds: set.itemIds ?? Array.Empty<int>(),
                    tiers: tiers,
                    displayMaxPieces: Math.Max(1, set.displayMaxPieces),
                    isPreview: set.isPreview));
            }

            QuestBountyEntry[] questSource = quests ?? Array.Empty<QuestBountyEntry>();
            List<QuestDefinition> questDefinitions = new List<QuestDefinition>(questSource.Length);
            for (int i = 0; i < questSource.Length; i++)
            {
                QuestBountyEntry q = questSource[i];
                if (q == null || string.IsNullOrWhiteSpace(q.questId))
                {
                    continue;
                }

                questDefinitions.Add(new QuestDefinition(
                    questId: q.questId,
                    title: q.title ?? string.Empty,
                    source: q.source ?? string.Empty,
                    description: q.description ?? string.Empty,
                    targetMonsterId: q.targetMonsterId ?? string.Empty,
                    targetCount: Math.Max(1, q.targetCount),
                    goldReward: Math.Max(0, q.goldReward),
                    xpReward: Math.Max(0, q.xpReward),
                    locationHint: q.locationHint ?? string.Empty,
                    isNoticeBoardBounty: q.isNoticeBoardBounty,
                    rewardItemId: Math.Max(0, q.rewardItemId)));
            }

            return new BalanceOverrides(
                version: version > 0 ? version : 1,
                monsters: monsterOverrides,
                survival: survivalConfig,
                equipmentSets: setDefinitions,
                quests: questDefinitions);
        }

        public string ExportToJson(bool indented = true)
        {
            Dictionary<string, object> root = new Dictionary<string, object>
            {
                ["version"] = version > 0 ? version : 1,
                ["monsters"] = SerializeMonsters(),
                ["survival"] = SerializeSurvival(),
                ["equipment_sets"] = SerializeEquipmentSets(),
                ["quests"] = SerializeQuests()
            };

            return MiniJsonWriter.Serialize(root, indented);
        }

        private List<object> SerializeMonsters()
        {
            MonsterBalanceEntry[] source = monsters ?? Array.Empty<MonsterBalanceEntry>();
            List<object> list = new List<object>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                MonsterBalanceEntry m = source[i];
                if (m == null)
                {
                    continue;
                }

                Dictionary<string, object> obj = new Dictionary<string, object>
                {
                    ["id"] = m.id ?? string.Empty,
                    ["name"] = m.name ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(m.alias))
                {
                    obj["alias"] = m.alias;
                }

                if (m.hitDice > 0) obj["hit_dice"] = m.hitDice;
                if (m.hpBonus != 0) obj["hp_bonus"] = m.hpBonus;
                if (m.hpMin > 0) obj["hp_min"] = m.hpMin;
                if (m.hpMax > 0) obj["hp_max"] = m.hpMax;
                if (m.hpDefault > 0) obj["hp_default"] = m.hpDefault;
                if (m.xpReward > 0) obj["xp_reward"] = m.xpReward;
                if (m.baseMp > 0) obj["base_mp"] = m.baseMp;
                if (m.baseAp > 0) obj["base_ap"] = m.baseAp;

                obj["role"] = m.role ?? string.Empty;

                List<string> abilities = new List<string>();
                if (m.specialAbilities != null)
                {
                    for (int a = 0; a < m.specialAbilities.Length; a++)
                    {
                        if (!string.IsNullOrWhiteSpace(m.specialAbilities[a]))
                        {
                            abilities.Add(m.specialAbilities[a].Trim());
                        }
                    }
                }
                obj["special_abilities"] = abilities;
                list.Add(obj);
            }

            return list;
        }

        private Dictionary<string, object> SerializeSurvival()
        {
            SurvivalBalanceEntry s = survival ?? CreateDefaultSurvival();
            VisionRadiiConfig v = s.visionRadii ?? new VisionRadiiConfig();

            Dictionary<string, object> radiiObj = new Dictionary<string, object>
            {
                ["day_base"] = v.dayBase,
                ["day_scout_bonus"] = v.dayScoutBonus,
                ["dusk_unlit"] = v.duskUnlit,
                ["dusk_lit_torch"] = v.duskLitTorch,
                ["night_unlit"] = v.nightUnlit,
                ["night_lit_torch"] = v.nightLitTorch,
                ["dungeon_unlit"] = v.dungeonUnlit,
                ["dungeon_lit_torch"] = v.dungeonLitTorch
            };

            return new Dictionary<string, object>
            {
                ["torch_price"] = s.torchPrice,
                ["ration_price"] = s.rationPrice,
                ["torch_turn_duration"] = s.torchTurnDuration,
                ["bandage_max_charges"] = s.bandageMaxCharges,
                ["minutes_per_real_second"] = Mathf.Clamp(s.minutesPerRealSecond, 0f, 10f),
                ["vision_radii"] = radiiObj
            };
        }

        private List<object> SerializeEquipmentSets()
        {
            EquipmentSetEntry[] source = equipmentSets ?? Array.Empty<EquipmentSetEntry>();
            List<object> list = new List<object>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                EquipmentSetEntry set = source[i];
                if (set == null)
                {
                    continue;
                }

                List<int> itemIdList = set.itemIds != null
                    ? new List<int>(set.itemIds)
                    : new List<int>();

                EquipmentSetTierEntry[] tierSource = set.tiers ?? Array.Empty<EquipmentSetTierEntry>();
                List<object> tierList = new List<object>(tierSource.Length);
                for (int t = 0; t < tierSource.Length; t++)
                {
                    EquipmentSetTierEntry tier = tierSource[t];
                    if (tier == null)
                    {
                        continue;
                    }

                    tierList.Add(new Dictionary<string, object>
                    {
                        ["required_pieces"] = tier.requiredPieces,
                        ["ac_bonus"] = tier.acBonus,
                        ["ap_bonus"] = tier.apBonus,
                        ["free_move_tiles"] = tier.freeMoveTiles,
                        ["guard_ap_discount"] = tier.guardApDiscount,
                        ["crit_bonus_pct"] = tier.critBonusPct,
                        ["ranged_range_bonus"] = tier.rangedRangeBonus,
                        ["ignore_half_cover"] = tier.ignoreHalfCover,
                        ["sp_cost_discount"] = tier.spCostDiscount,
                        ["spell_damage_bonus"] = tier.spellDamageBonus,
                        ["healing_bonus"] = tier.healingBonus,
                        ["support_spell_ap_discount"] = tier.supportSpellApDiscount,
                        ["undead_damage_bonus_pct"] = tier.undeadDamageBonusPct,
                        ["summary"] = tier.summary ?? string.Empty
                    });
                }

                list.Add(new Dictionary<string, object>
                {
                    ["set_id"] = set.setId ?? string.Empty,
                    ["name"] = set.name ?? string.Empty,
                    ["is_preview"] = set.isPreview,
                    ["display_max_pieces"] = Math.Max(1, set.displayMaxPieces),
                    ["item_ids"] = itemIdList,
                    ["tiers"] = tierList
                });
            }

            return list;
        }

        private List<object> SerializeQuests()
        {
            QuestBountyEntry[] source = quests ?? Array.Empty<QuestBountyEntry>();
            List<object> list = new List<object>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                QuestBountyEntry q = source[i];
                if (q == null)
                {
                    continue;
                }

                list.Add(new Dictionary<string, object>
                {
                    ["quest_id"] = q.questId ?? string.Empty,
                    ["title"] = q.title ?? string.Empty,
                    ["source"] = q.source ?? string.Empty,
                    ["description"] = q.description ?? string.Empty,
                    ["target_monster_id"] = q.targetMonsterId ?? string.Empty,
                    ["target_count"] = Math.Max(1, q.targetCount),
                    ["gold_reward"] = Math.Max(0, q.goldReward),
                    ["xp_reward"] = Math.Max(0, q.xpReward),
                    ["location_hint"] = q.locationHint ?? string.Empty,
                    ["is_notice_board_bounty"] = q.isNoticeBoardBounty,
                    ["reward_item_id"] = Math.Max(0, q.rewardItemId)
                });
            }

            return list;
        }

        private static MonsterBalanceEntry[] ParseMonsters(IReadOnlyList<object> arr)
        {
            List<MonsterBalanceEntry> list = new List<MonsterBalanceEntry>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null)
                {
                    continue;
                }

                IReadOnlyDictionary<string, object> obj = MiniJson.AsObject(arr[i]);
                list.Add(new MonsterBalanceEntry
                {
                    id = GetFirstString(obj, string.Empty, "id", "monster_id", "monsterId"),
                    name = GetFirstString(obj, string.Empty, "name"),
                    alias = GetFirstString(obj, string.Empty, "alias"),
                    hitDice = GetFirstInt(obj, 0, "hit_dice", "hitDice", "hd"),
                    hpBonus = GetFirstInt(obj, 0, "hp_bonus", "hpBonus"),
                    hpMin = GetFirstInt(obj, 0, "hp_min", "hpMin"),
                    hpMax = GetFirstInt(obj, 0, "hp_max", "hpMax"),
                    hpDefault = GetFirstInt(obj, 0, "hp_default", "hpDefault"),
                    xpReward = GetFirstInt(obj, 0, "xp_reward", "xpReward"),
                    baseMp = GetFirstInt(obj, 0, "base_mp", "baseMp"),
                    baseAp = GetFirstInt(obj, 0, "base_ap", "baseAp"),
                    role = GetFirstString(obj, string.Empty, "role"),
                    specialAbilities = ParseStringArray(obj, "special_abilities", "specialAbilities")
                });
            }

            return list.ToArray();
        }

        private static SurvivalBalanceEntry ParseSurvival(IReadOnlyDictionary<string, object> obj)
        {
            SurvivalBalanceEntry defaults = CreateDefaultSurvival();
            VisionRadiiConfig radii = defaults.visionRadii;
            if (TryGetAny(obj, out object rawRadii, "vision_radii", "visionRadii") && rawRadii != null)
            {
                IReadOnlyDictionary<string, object> rObj = MiniJson.AsObject(rawRadii);
                radii = new VisionRadiiConfig
                {
                    dayBase = Math.Max(1, GetFirstInt(rObj, radii.dayBase, "day_base", "dayBase")),
                    dayScoutBonus = Math.Max(0, GetFirstInt(rObj, radii.dayScoutBonus, "day_scout_bonus", "dayScoutBonus")),
                    duskUnlit = Math.Max(1, GetFirstInt(rObj, radii.duskUnlit, "dusk_unlit", "duskUnlit")),
                    duskLitTorch = Math.Max(1, GetFirstInt(rObj, radii.duskLitTorch, "dusk_lit_torch", "duskLitTorch")),
                    nightUnlit = Math.Max(1, GetFirstInt(rObj, radii.nightUnlit, "night_unlit", "nightUnlit")),
                    nightLitTorch = Math.Max(1, GetFirstInt(rObj, radii.nightLitTorch, "night_lit_torch", "nightLitTorch")),
                    dungeonUnlit = Math.Max(1, GetFirstInt(rObj, radii.dungeonUnlit, "dungeon_unlit", "dungeonUnlit")),
                    dungeonLitTorch = Math.Max(1, GetFirstInt(rObj, radii.dungeonLitTorch, "dungeon_lit_torch", "dungeonLitTorch"))
                };
            }

            float rawMinutesPerRealSecond = GetFirstFloat(
                obj,
                defaults.minutesPerRealSecond,
                "minutes_per_real_second",
                "minutesPerRealSecond");
            float minutesPerRealSecond = float.IsNaN(rawMinutesPerRealSecond) || float.IsInfinity(rawMinutesPerRealSecond)
                ? defaults.minutesPerRealSecond
                : Mathf.Clamp(rawMinutesPerRealSecond, 0f, 10f);

            return new SurvivalBalanceEntry
            {
                torchPrice = Math.Max(1, GetFirstInt(obj, defaults.torchPrice, "torch_price", "torchPrice")),
                rationPrice = Math.Max(1, GetFirstInt(obj, defaults.rationPrice, "ration_price", "rationPrice")),
                torchTurnDuration = Math.Max(1, GetFirstInt(obj, defaults.torchTurnDuration, "torch_turn_duration", "torchTurnDuration")),
                bandageMaxCharges = Math.Max(1, GetFirstInt(obj, defaults.bandageMaxCharges, "bandage_max_charges", "bandageMaxCharges")),
                minutesPerRealSecond = minutesPerRealSecond,
                visionRadii = radii
            };
        }

        private static EquipmentSetEntry[] ParseEquipmentSets(IReadOnlyList<object> arr)
        {
            List<EquipmentSetEntry> sets = new List<EquipmentSetEntry>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null)
                {
                    continue;
                }

                IReadOnlyDictionary<string, object> obj = MiniJson.AsObject(arr[i]);
                string setId = GetFirstString(obj, "set_" + i.ToString(CultureInfo.InvariantCulture), "set_id", "setId", "id");
                string name = GetFirstString(obj, setId, "name");
                bool isPreview = GetFirstBool(obj, false, "is_preview", "isPreview");
                int displayMaxPieces = Math.Max(1, GetFirstInt(obj, 3, "display_max_pieces", "displayMaxPieces"));
                int[] itemIds = ParseIntArray(obj, "item_ids", "itemIds");

                List<EquipmentSetTierEntry> tiers = new List<EquipmentSetTierEntry>();
                if (TryGetAny(obj, out object rawTiers, "tiers") && rawTiers != null)
                {
                    IReadOnlyList<object> tierArr = MiniJson.AsArray(rawTiers);
                    for (int t = 0; t < tierArr.Count; t++)
                    {
                        if (tierArr[t] == null)
                        {
                            continue;
                        }

                        IReadOnlyDictionary<string, object> tObj = MiniJson.AsObject(tierArr[t]);
                        tiers.Add(new EquipmentSetTierEntry
                        {
                            requiredPieces = Math.Max(1, GetFirstInt(tObj, 2, "required_pieces", "requiredPieces")),
                            acBonus = GetFirstInt(tObj, 0, "ac_bonus", "acBonus"),
                            apBonus = GetFirstInt(tObj, 0, "ap_bonus", "apBonus"),
                            freeMoveTiles = GetFirstInt(tObj, 0, "free_move_tiles", "freeMoveTiles"),
                            guardApDiscount = GetFirstInt(tObj, 0, "guard_ap_discount", "guardApDiscount"),
                            critBonusPct = GetFirstInt(tObj, 0, "crit_bonus_pct", "critBonusPct"),
                            rangedRangeBonus = GetFirstInt(tObj, 0, "ranged_range_bonus", "rangedRangeBonus"),
                            ignoreHalfCover = GetFirstBool(tObj, false, "ignore_half_cover", "ignoreHalfCover"),
                            spCostDiscount = GetFirstInt(tObj, 0, "sp_cost_discount", "spCostDiscount"),
                            spellDamageBonus = GetFirstInt(tObj, 0, "spell_damage_bonus", "spellDamageBonus"),
                            healingBonus = GetFirstInt(tObj, 0, "healing_bonus", "healingBonus"),
                            supportSpellApDiscount = GetFirstInt(tObj, 0, "support_spell_ap_discount", "supportSpellApDiscount"),
                            undeadDamageBonusPct = GetFirstInt(tObj, 0, "undead_damage_bonus_pct", "undeadDamageBonusPct"),
                            summary = GetFirstString(tObj, string.Empty, "summary")
                        });
                    }
                }

                sets.Add(new EquipmentSetEntry
                {
                    setId = setId,
                    name = name,
                    isPreview = isPreview,
                    displayMaxPieces = displayMaxPieces,
                    itemIds = itemIds,
                    tiers = tiers.ToArray()
                });
            }

            return sets.ToArray();
        }

        private static QuestBountyEntry[] ParseQuests(IReadOnlyList<object> arr)
        {
            List<QuestBountyEntry> quests = new List<QuestBountyEntry>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null)
                {
                    continue;
                }

                IReadOnlyDictionary<string, object> obj = MiniJson.AsObject(arr[i]);
                quests.Add(new QuestBountyEntry
                {
                    questId = GetFirstString(obj, "quest_" + i.ToString(CultureInfo.InvariantCulture), "quest_id", "questId", "id"),
                    title = GetFirstString(obj, string.Empty, "title", "name"),
                    source = GetFirstString(obj, string.Empty, "source"),
                    description = GetFirstString(obj, string.Empty, "description"),
                    targetMonsterId = GetFirstString(obj, string.Empty, "target_monster_id", "targetMonsterId"),
                    targetCount = Math.Max(1, GetFirstInt(obj, 1, "target_count", "targetCount")),
                    goldReward = Math.Max(0, GetFirstInt(obj, 0, "gold_reward", "goldReward")),
                    xpReward = Math.Max(0, GetFirstInt(obj, 0, "xp_reward", "xpReward")),
                    locationHint = GetFirstString(obj, string.Empty, "location_hint", "locationHint"),
                    isNoticeBoardBounty = GetFirstBool(obj, false, "is_notice_board_bounty", "isNoticeBoardBounty"),
                    rewardItemId = Math.Max(0, GetFirstInt(obj, 0, "reward_item_id", "rewardItemId"))
                });
            }

            return quests.ToArray();
        }

        private static bool TryGetAny(IReadOnlyDictionary<string, object> obj, out object value, params string[] keys)
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

        private static string GetFirstString(IReadOnlyDictionary<string, object> obj, string defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object raw) && raw != null)
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

        private static int GetFirstInt(IReadOnlyDictionary<string, object> obj, int defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object raw) && raw != null)
                {
                    return MiniJson.ToInt32(raw, keys[i]);
                }
            }

            return defaultValue;
        }

        private static float GetFirstFloat(IReadOnlyDictionary<string, object> obj, float defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object raw) && raw != null)
                {
                    return (float)MiniJson.ToDouble(raw, keys[i]);
                }
            }

            return defaultValue;
        }

        private static bool GetFirstBool(IReadOnlyDictionary<string, object> obj, bool defaultValue, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (obj.TryGetValue(keys[i], out object raw) && raw != null)
                {
                    if (raw is bool b)
                    {
                        return b;
                    }

                    if (raw is long l)
                    {
                        return l != 0L;
                    }

                    if (raw is int iv)
                    {
                        return iv != 0;
                    }
                }
            }

            return defaultValue;
        }

        private static int[] ParseIntArray(IReadOnlyDictionary<string, object> obj, params string[] keys)
        {
            if (!TryGetAny(obj, out object raw, keys) || raw == null)
            {
                return Array.Empty<int>();
            }

            IReadOnlyList<object> arr = MiniJson.AsArray(raw);
            int[] result = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                result[i] = MiniJson.ToInt32(arr[i], keys[0] + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }

            return result;
        }

        private static string[] ParseStringArray(IReadOnlyDictionary<string, object> obj, params string[] keys)
        {
            if (!TryGetAny(obj, out object raw, keys) || raw == null)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<object> arr = MiniJson.AsArray(raw);
            List<string> list = new List<string>(arr.Count);
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

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private static MonsterBalanceEntry[] CreateDefaultMonsters()
        {
            return new[]
            {
                new MonsterBalanceEntry
                {
                    id = "orc_warrior",
                    name = "Orc Warrior",
                    alias = string.Empty,
                    hitDice = 3,
                    hpBonus = 3,
                    hpMin = 18,
                    hpMax = 24,
                    hpDefault = 21,
                    xpReward = 65,
                    baseMp = 4,
                    baseAp = 6,
                    role = "Bruiser",
                    specialAbilities = new[] { "cleave_2tile" }
                },
                new MonsterBalanceEntry
                {
                    id = "giant_frog",
                    name = "Giant Frog",
                    alias = string.Empty,
                    hitDice = 0,
                    hpBonus = 0,
                    hpMin = 15,
                    hpMax = 19,
                    hpDefault = 17,
                    xpReward = 70,
                    baseMp = 5,
                    baseAp = 6,
                    role = "Controller",
                    specialAbilities = new[] { "tongue_pull" }
                },
                new MonsterBalanceEntry
                {
                    id = "goblin",
                    name = "Goblin",
                    alias = "Goblin Raider",
                    hitDice = 0,
                    hpBonus = 0,
                    hpMin = 8,
                    hpMax = 11,
                    hpDefault = 9,
                    xpReward = 0,
                    baseMp = 4,
                    baseAp = 4,
                    role = "Skirmisher",
                    specialAbilities = Array.Empty<string>()
                },
                new MonsterBalanceEntry
                {
                    id = "goblin_archer",
                    name = "Goblin Archer",
                    alias = string.Empty,
                    hitDice = 0,
                    hpBonus = 0,
                    hpMin = 6,
                    hpMax = 9,
                    hpDefault = 8,
                    xpReward = 0,
                    baseMp = 4,
                    baseAp = 4,
                    role = "Ranged",
                    specialAbilities = new[] { "high_ground_seeker" }
                },
                new MonsterBalanceEntry { id = "giant_rat", name = "Giant Rat", hpMin = 6, hpMax = 8, hpDefault = 7, baseMp = 3, baseAp = 4, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "zombie", name = "Zombie", hpMin = 18, hpMax = 23, hpDefault = 20, baseMp = 3, baseAp = 4, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "skeletal_warrior", name = "Skeletal Warrior", hpMin = 12, hpMax = 16, hpDefault = 14, baseMp = 4, baseAp = 4, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "orc_archer", name = "Orc Archer", hpMin = 10, hpMax = 14, hpDefault = 12, baseMp = 4, baseAp = 4, role = "Ranged" },
                new MonsterBalanceEntry { id = "monstrous_rat", name = "Monstrous Rat", hpMin = 16, hpMax = 20, hpDefault = 18, baseMp = 4, baseAp = 5, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "giant_spider", name = "Giant Spider", hpMin = 11, hpMax = 15, hpDefault = 13, baseMp = 4, baseAp = 5, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "goblin_champion", name = "Goblin Champion", hpMin = 20, hpMax = 25, hpDefault = 22, baseMp = 4, baseAp = 6, role = "Skirmisher" },
                new MonsterBalanceEntry { id = "goblin_shaman", name = "Goblin Shaman", hpMin = 16, hpMax = 20, hpDefault = 18, baseMp = 4, baseAp = 6, role = "Caster" }
            };
        }

        private static SurvivalBalanceEntry CreateDefaultSurvival()
        {
            return new SurvivalBalanceEntry
            {
                torchPrice = 12,
                rationPrice = 5,
                torchTurnDuration = 120,
                bandageMaxCharges = 2,
                minutesPerRealSecond = 1.0f,
                visionRadii = new VisionRadiiConfig
                {
                    dayBase = 11,
                    dayScoutBonus = 2,
                    duskUnlit = 8,
                    duskLitTorch = 10,
                    nightUnlit = 4,
                    nightLitTorch = 9,
                    dungeonUnlit = 3,
                    dungeonLitTorch = 8
                }
            };
        }

        private static EquipmentSetEntry[] CreateDefaultEquipmentSets()
        {
            return new[]
            {
                new EquipmentSetEntry
                {
                    setId = "scouts_leather",
                    name = "Scout's Leather",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 209, 210, 211, 450, 452, 454, 902 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            freeMoveTiles = 2,
                            summary = "+2 MP"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            critBonusPct = 10,
                            summary = "+1 Max AP & +10% Crit"
                        }
                    }
                },
                new EquipmentSetEntry
                {
                    setId = "iron_vanguard",
                    name = "Iron Vanguard",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 400, 418, 455, 219 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            acBonus = 2,
                            summary = "+2 AC"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            guardApDiscount = 1,
                            summary = "Guard costs 1 AP & +1 Max AP"
                        }
                    }
                },
                new EquipmentSetEntry
                {
                    setId = "thornwood_stalker",
                    name = "Thornwood Stalker",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 903, 904, 905 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            freeMoveTiles = 1,
                            rangedRangeBonus = 1,
                            summary = "+1 MP & +1 Ranged Range"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            ignoreHalfCover = true,
                            summary = "+1 Max AP & Ignore Half-Cover"
                        }
                    }
                },
                new EquipmentSetEntry
                {
                    setId = "bywater_arcanist",
                    name = "Bywater Arcanist",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 906, 907, 908 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            freeMoveTiles = 1,
                            spCostDiscount = 1,
                            summary = "-1 SP Cost & +1 MP"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            spellDamageBonus = 2,
                            summary = "+1 Max AP & +2 Spell Damage"
                        }
                    }
                },
                new EquipmentSetEntry
                {
                    setId = "sanctuary_acolyte",
                    name = "Sanctuary Acolyte",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 909, 910, 911 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            acBonus = 1,
                            healingBonus = 2,
                            summary = "+2 Healing & +1 AC"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            supportSpellApDiscount = 1,
                            summary = "+1 Max AP & Cure/Bless -1 AP"
                        }
                    }
                },
                new EquipmentSetEntry
                {
                    setId = "cryptbreaker",
                    name = "Cryptbreaker",
                    isPreview = false,
                    displayMaxPieces = 3,
                    itemIds = new[] { 912, 913, 914 },
                    tiers = new[]
                    {
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 2,
                            acBonus = 2,
                            undeadDamageBonusPct = 25,
                            summary = "+2 AC & +25% vs Undead"
                        },
                        new EquipmentSetTierEntry
                        {
                            requiredPieces = 3,
                            apBonus = 1,
                            freeMoveTiles = 1,
                            summary = "+1 Max AP & +1 MP"
                        }
                    }
                }
            };
        }

        private static QuestBountyEntry[] CreateDefaultQuests()
        {
            return new[]
            {
                new QuestBountyEntry
                {
                    questId = "bounty_river_frogs",
                    title = "Clear the Riverbed Frogs",
                    source = "BywaterNoticeBoard",
                    description = "Giant frogs along the Bywater riverbanks have been ambushing ferrymen and pulling livestock into the reeds. Thin their numbers before dusk.",
                    targetMonsterId = "giant_frog",
                    targetCount = 4,
                    goldReward = 120,
                    xpReward = 150,
                    locationHint = "Bywater Riverbanks (East & South Meadows)",
                    isNoticeBoardBounty = true,
                    rewardItemId = 905
                },
                new QuestBountyEntry
                {
                    questId = "bounty_road_goblins",
                    title = "Break the Road Goblin Ambush",
                    source = "BywaterNoticeBoard",
                    description = "Goblin raiders have been harassing merchant wagons along the trade road outside Bywater Gate. Drive them off the road.",
                    targetMonsterId = "goblin",
                    targetCount = 6,
                    goldReward = 140,
                    xpReward = 180,
                    locationHint = "Bywater Trade Road & Foothills",
                    isNoticeBoardBounty = true,
                    rewardItemId = 902
                },
                new QuestBountyEntry
                {
                    questId = "bounty_thornwood_orcs",
                    title = "Push Back the Thornwood Orcs",
                    source = "BywaterNoticeBoard",
                    description = "Heavy orc warriors have set up camp in the Thornwood approaches. Break their warband and claim the magistrate's bounty.",
                    targetMonsterId = "orc_warrior",
                    targetCount = 3,
                    goldReward = 180,
                    xpReward = 220,
                    locationHint = "Thornwood Forest & Outlying Woods",
                    isNoticeBoardBounty = true,
                    rewardItemId = 455
                },
                new QuestBountyEntry
                {
                    questId = "bounty_cavern_undead",
                    title = "Purge the Cavern Undead",
                    source = "BywaterNoticeBoard",
                    description = "Restless skeletal warriors patrol the upper chambers of the Bywater Cavern. Put them back to rest and report to the Magistrate.",
                    targetMonsterId = "skeletal_warrior",
                    targetCount = 5,
                    goldReward = 200,
                    xpReward = 250,
                    locationHint = "Bywater Cavern (Dungeon Level 0)",
                    isNoticeBoardBounty = true,
                    rewardItemId = 912
                }
            };
        }
    }
}
