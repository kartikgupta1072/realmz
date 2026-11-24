using System;
using System.Collections.Generic;
using System.Linq;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class UxOverhaulPhase1Tests
    {
        private static Dictionary<int, ItemDefinition> BuildNineSlotCatalog()
        {
            return new Dictionary<int, ItemDefinition>
            {
                [1] = new ItemDefinition(1, "Dagger", category: "Weapon", slot: "Weapon", damageDice: "1d4", weaponBonus: 0, armorClassBonus: 0, weight: 5, basePrice: 10),
                [2] = new ItemDefinition(2, "Dagger of Shielding +2", category: "Weapon", slot: "Weapon", damageDice: "1d4", weaponBonus: 2, armorClassBonus: 2, weight: 6, basePrice: 10000),
                [10] = new ItemDefinition(10, "Longsword", category: "Weapon", slot: "Weapon", damageDice: "1d8", weaponBonus: 1, weight: 120, basePrice: 100),
                [104] = new ItemDefinition(104, "Bow", category: "Weapon", slot: "MissileWeapon", damageDice: "1d6", weaponBonus: 1, range: 8, weight: 80, basePrice: 80),
                [209] = new ItemDefinition(209, "Leather Armor", category: "Armor", slot: "Armor", armorClassBonus: 9, weight: 150, basePrice: 90),
                [250] = new ItemDefinition(250, "Padded Cloak", category: "Cloak", slot: "Accessory2", armorClassBonus: 2, weight: 55, basePrice: 25),
                [400] = new ItemDefinition(400, "Helm", category: "Helm", slot: "Helm", armorClassBonus: 3, weight: 40, basePrice: 30),
                [418] = new ItemDefinition(418, "Shield", category: "Shield", slot: "Shield", armorClassBonus: 6, weight: 150, basePrice: 40),
                [452] = new ItemDefinition(452, "Leather Gloves", category: "Gloves", slot: "Hands", armorClassBonus: 2, weight: 12, basePrice: 5),
                [454] = new ItemDefinition(454, "Leather Boots", category: "Boots", slot: "Feet", armorClassBonus: 1, weight: 40, basePrice: 25),
                [455] = new ItemDefinition(455, "Steel Shod Boots", category: "Boots", slot: "Feet", armorClassBonus: 2, weight: 65, basePrice: 35),
                [602] = new ItemDefinition(602, "Heal Poison", category: "Potion", slot: "Consumable", charges: 3, healDice: "1d6+2", basePrice: 80),
                [603] = new ItemDefinition(603, "Heat Resistance", category: "Potion", slot: "Consumable", charges: 3, healDice: "", basePrice: 300),
                [611] = new ItemDefinition(611, "Heal Small Wounds", category: "Potion", slot: "Consumable", charges: 3, healDice: "1d8+2", basePrice: 50),
                [622] = new ItemDefinition(622, "Speckled Luck Stone +2", category: "Accessory", slot: "Accessory1", armorClassBonus: 2, weight: 1, basePrice: 750),
                [675] = new ItemDefinition(675, "Quiver of Protection +2", category: "Accessory", slot: "Accessory2", armorClassBonus: 2, weight: 70, basePrice: 750),
                [723] = new ItemDefinition(723, "Refresh Potion", category: "Potion", slot: "Consumable", charges: 2, healDice: "1d6+2", basePrice: 500),
                [747] = new ItemDefinition(747, "Salt", category: "Supply", slot: "Utility", charges: 20, basePrice: 100),
                [805] = new ItemDefinition(805, "Torch", category: "Supply", slot: "Utility", charges: 6, basePrice: 3),
                [999] = new ItemDefinition(999, "Heavy Anvil", category: "Supply", slot: "Utility", weight: 5000, basePrice: 10),
            };
        }

        [Fact]
        public void NineSlotEquipment_StackingAndSmartAccessoryFallback_WorkCleanly()
        {
            Dictionary<int, ItemDefinition> catalog = BuildNineSlotCatalog();
            HeroPresetData preset = new HeroPresetData("h1", "Aldric", archetype: "Knight", level: 1, hp: 24, maxHp: 24, ac: 11, toHit: 18);
            PartyMemberState hero = new PartyMemberState(preset, catalog);
            PartyState party = new PartyState(new[] { hero });

            // Stackable charged potions & torches coalesce into single stack entries.
            for (int i = 0; i < 4; i++)
            {
                party.AddItemToStash(611, 1, isIdentified: true, charges: 3);
            }
            party.AddItemToStash(805, 1, isIdentified: true, charges: 6);
            party.AddItemToStash(805, 1, isIdentified: true, charges: 6);

            Assert.Equal(2, party.Stash.Count);
            Assert.Equal(4, party.Stash[0].Quantity);
            Assert.Equal(3, party.Stash[0].RemainingCharges);
            Assert.Equal(2, party.Stash[1].Quantity);
            Assert.Equal(12, party.GetTotalTorchCharges());

            // Consuming 1 torch charge decrements stack & creates a 5-charge unit while keeping the full 6-charge unit.
            Assert.True(party.TryConsumeItemCharge(805, out _, catalog));
            Assert.Equal(11, party.GetTotalTorchCharges());
            Assert.Equal(3, party.Stash.Count); // 611x4, 805(6)x1, 805(5)x1

            // Equip Gloves (Hands), Boots (Feet), Cloak (Accessory2), Quiver of Protection (Accessory2 -> smart fallback to Accessory1).
            party.AddItemToStash(452, 1); // Hands +2 AC
            party.AddItemToStash(455, 1); // Feet +2 AC
            party.AddItemToStash(250, 1); // Accessory2 +2 AC
            party.AddItemToStash(675, 1); // Accessory2 -> falls back to open Accessory1 (+2 AC)!

            int equipCount = party.EquipBestForHero(0, catalog);
            Assert.Equal(4, equipCount);
            Assert.Equal(452, hero.GetEquipped(EquipmentSlot.Hands)!.ItemId);
            Assert.Equal(455, hero.GetEquipped(EquipmentSlot.Feet)!.ItemId);
            Assert.NotNull(hero.GetEquipped(EquipmentSlot.Accessory1));
            Assert.NotNull(hero.GetEquipped(EquipmentSlot.Accessory2));
            Assert.Equal(11 + 2 + 2 + 2 + 2, hero.GetEffectiveAc(catalog));

            // Shop Armor filter includes Gloves, Boots, Cloak, Helm, Shield, Armor.
            ShopDefinition tannery = new ShopDefinition("4", "Bywater Tannery", 0, new[]
            {
                new ShopStockItem(452, 5, 1),
                new ShopStockItem(455, 35, 1),
                new ShopStockItem(250, 25, 1),
                new ShopStockItem(747, 100, 1),
            });
            IReadOnlyList<ShopStockView> armorGroup = TownShopService.FilterStock(tannery, catalog, party, new ShopFilter { Category = "Armor" });
            Assert.Equal(3, armorGroup.Count);
            Assert.DoesNotContain(armorGroup, s => s.Item.Id == 747);
        }

        [Fact]
        public void CampRules_EnforceQuickBandageLimit_TorchCost_AndAmbushRisk()
        {
            Dictionary<int, ItemDefinition> catalog = BuildNineSlotCatalog();
            HeroPresetData p1 = new HeroPresetData("h1", "Aldric", hp: 10, maxHp: 30, sp: 2, maxSp: 10);
            HeroPresetData p2 = new HeroPresetData("h2", "Vex", hp: 0, maxHp: 20, sp: 0, maxSp: 0);
            PartyState party = new PartyState(new[] { new PartyMemberState(p1, catalog), new PartyMemberState(p2, catalog) });

            int[] tiles = new int[100];
            Array.Fill(tiles, 1);
            Dictionary<int, TileRule> rules = new Dictionary<int, TileRule> { [1] = new TileRule(1, passable: true) };
            EncounterZoneData hostileZone = new EncounterZoneData(1, 0, 0, 9, 9, percent: 100, battleIds: new[] { 5 }, monsterIndices: new[] { 92 });

            TileMapData safeTown = new TileMapData("land_0", "Bywater", 0, false, 10, 10, tiles, new GridCoord(2, 1), rules);
            TileMapData dungeon = new TileMapData("dungeon_0", "Cavern", 0, true, 10, 10, tiles, new GridCoord(3, 3), rules, encounterZones: new[] { hostileZone });

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = safeTown, ["dungeon_0"] = dungeon },
                "land_0",
                party,
                roller: new QueuedDiceRoller(5, 80));

            Assert.True(session.IsInSafeZone());

            // 1. Quick Bandage #1: heals living +35% ceil (11 -> 21), does NOT revive p2, advances 1 turn, leaves 1 charge.
            RestResult qb1 = session.TryCampRest(CampMode.QuickBandage, out _, out _);
            Assert.True(qb1.Success);
            Assert.Equal(21, party.Members[0].Hp);
            Assert.Equal(0, party.Members[1].Hp);
            Assert.Equal(1, party.QuickBandageCharges);
            Assert.Equal(1, session.TurnCount);

            // 2. Quick Bandage #2 uses final charge; #3 fails cleanly.
            RestResult qb2 = session.TryCampRest(CampMode.QuickBandage, out _, out _);
            Assert.True(qb2.Success);
            Assert.Equal(0, party.QuickBandageCharges);
            RestResult qb3 = session.TryCampRest(CampMode.QuickBandage, out _, out _);
            Assert.False(qb3.Success);

            // 3. Full Rest in safe town is free (no torch required), +8 turns, refills bandages to 2/2, revives fallen.
            party.Members[0].ExhaustionStacks = 2;
            party.Members[0].CurseStacks = 1;
            party.Members[0].Conditions.Apply(CharConditionId.Poisoned);
            RestResult townRest = session.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.True(townRest.Success);
            Assert.Equal(8, townRest.TurnsElapsed);
            Assert.Equal(30, party.Members[0].Hp);
            Assert.Equal(20, party.Members[1].Hp);
            Assert.Equal(0, party.Members[0].ExhaustionStacks);
            Assert.Equal(1, party.Members[0].CurseStacks);
            Assert.True(party.Members[0].Conditions.Has(CharConditionId.Poisoned));
            Assert.Equal(2, party.QuickBandageCharges);

            // 4. Dungeon without Torch -> Full Rest blocked!
            ExplorationSession dungeonSession = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["dungeon_0"] = dungeon },
                "dungeon_0",
                party,
                roller: new QueuedDiceRoller(5, 80));
            Assert.False(dungeonSession.IsInSafeZone());
            RestResult noTorch = dungeonSession.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.False(noTorch.Success);
            Assert.False(noTorch.Ambushed);

            // 5. Add 2 Torches -> Roll 5 <= 15 triggers ambush (burns 1 torch charge). Roll 80 > 15 succeeds cleanly.
            party.AddItemToStash(805, 1, isIdentified: true, charges: 6);
            RestResult ambushed = dungeonSession.TryCampRest(CampMode.FullRest, out EncounterZoneData? az, out int battleId);
            Assert.False(ambushed.Success);
            Assert.True(ambushed.Ambushed);
            Assert.NotNull(az);
            Assert.Equal(5, battleId);
            Assert.Equal(5, party.GetTotalTorchCharges());

            RestResult clearDungeonRest = dungeonSession.TryCampRest(CampMode.FullRest, out _, out _);
            Assert.True(clearDungeonRest.Success);
            Assert.False(clearDungeonRest.Ambushed);
            Assert.Equal(4, party.GetTotalTorchCharges());
        }

        [Fact]
        public void AllSixAttributes_DeferredLevelUp_AndVictorySummary_AreWiredCorrectly()
        {
            Dictionary<int, ItemDefinition> catalog = BuildNineSlotCatalog();
            HeroPresetData magePreset = new HeroPresetData(
                "hero_mage", "Theron", archetype: "Mage", level: 1, hp: 14, maxHp: 14, sp: 20, maxSp: 20,
                ac: 11, toHit: 12, movementMax: 9, brawn: 15, knowledge: 19, judgment: 18, agility: 19, vitality: 16, luck: 18,
                knownSpellIds: new[] { 1108 });
            HeroPresetData roguePreset = new HeroPresetData(
                "hero_rogue", "Vex", archetype: "Rogue", level: 1, hp: 0, maxHp: 16, sp: 0, maxSp: 0,
                ac: 13, toHit: 15, movementMax: 12, brawn: 11, knowledge: 13, judgment: 9, agility: 19, vitality: 12, luck: 18);

            PartyMemberState mage = new PartyMemberState(magePreset, catalog);
            PartyMemberState fallenRogue = new PartyMemberState(roguePreset, catalog);
            PartyState party = new PartyState(new[] { mage, fallenRogue }, gold: 100, deferLevelUp: true);

            // Check 6 attribute bonuses.
            Assert.Equal(1, mage.GetMeleeDamageAttributeBonus()); // (15-12)/3 = 1
            Assert.Equal(2, mage.GetArcaneSpellPowerBonus());     // (19-10)/4 = 2
            Assert.Equal(9, mage.GetSpellHitDcBonusPercent());    // 19-10 = 9
            Assert.Equal(2, mage.GetDivineHealingBonus());       // (18-10)/4 = 2
            Assert.Equal(24, mage.GetMagicResistancePercent());  // (18-10)*2 + (18-10) = 24
            Assert.Equal(2, mage.GetDodgeAcBonus());             // (19-12)/3 = 2
            Assert.Equal(14, mage.GetRangedHitAgilityBonusPercent()); // (19-12)*2 = 14
            Assert.Equal(20, mage.GetConditionSaveBonusPercent());    // (16-10)*2 + (18-10) = 20
            Assert.Equal(9, mage.GetCritChancePercent());        // 5 + (18-10)/2 = 9
            Assert.Equal(8, party.GetGoldLootBonusPercent());    // avg Luck 18 -> +8%

            // Check Encumbrance -2 Move penalty.
            party.AddItemToStash(999, 1); // 5000 wt > cap
            Assert.True(party.IsEncumbered(catalog));
            CombatantState encMage = CombatantState.FromHero(mage, new GridCoord(1, 1), catalog, isEncumbered: true);
            Assert.Equal(7, encMage.MovementMax);
            party.RemoveItemFromStash(0, 1);

            // Check Heat Resistance (603) + Refresh Potion (723) + Combat Victory Summary.
            party.AddItemToStash(603, 1, charges: 3);
            party.AddItemToStash(723, 1, charges: 2);
            mage.ExhaustionStacks = 2;
            mage.Sp = 2;
            Assert.True(party.TryUseConsumableOnHero(0, 0, catalog, null, out _)); // 603 -> FireProtection
            Assert.True(mage.Conditions.Has(CharConditionId.FireProtection));
            Assert.True(party.TryUseConsumableOnHero(0, 1, catalog, new QueuedDiceRoller(3), out _)); // 723 -> +SP, clear Exhaustion
            Assert.Equal(10, mage.Sp);
            Assert.Equal(0, mage.ExhaustionStacks);

            // Build Combat and defeat a goblin -> awards 300 XP (2:1 split -> 200 mage / 100 fallen) + Luck gold bonus + stacked loot.
            MonsterDefinition bossDef = new MonsterDefinition(1, "skel", "Skeletal Warrior", hpMin: 6, hpMax: 6, hpDefault: 6, ac: 0, dexterity: 5, xpReward: 300, goldMin: 100, goldMax: 100, lootItemIds: new[] { 452, 452, 622 });
            CombatantState heroUnit = CombatantState.FromHero(mage, new GridCoord(2, 2), catalog);
            heroUnit.ToHit = 90;
            heroUnit.WeaponDice = "2d6+6";
            CombatantState deadUnit = CombatantState.FromHero(fallenRogue, new GridCoord(1, 2), catalog);
            CombatantState mob = CombatantState.FromMonster(bossDef, "m1", new GridCoord(3, 2));

            CombatGrid grid = new CombatGrid(8, 8);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { heroUnit, deadUnit, mob }, items: catalog, roller: new QueuedDiceRoller(20, 4, 4), party: party);
            CombatActionResult res = session.ExecuteMeleeAttack("m1");
            Assert.True(res.Success);
            Assert.Equal(CombatOutcome.Victory, session.Outcome);
            Assert.NotNull(session.LastVictorySummary);

            CombatVictorySummary summary = session.LastVictorySummary!;
            Assert.Equal(100, summary.BaseGoldLoot);
            Assert.Equal(8, summary.LuckBonusGold);
            Assert.Equal(108, summary.TotalGoldLoot);
            Assert.Equal(208, party.Gold);
            Assert.Equal(2, summary.LootStacks.Count); // 452 x2, 622 x1

            HeroVictorySummary mageSum = summary.Heroes.First(h => h.HeroId == "hero_mage");
            HeroVictorySummary rogueSum = summary.Heroes.First(h => h.HeroId == "hero_rogue");
            Assert.Equal(200, mageSum.XpAwarded);
            Assert.Equal(100, rogueSum.XpAwarded);
            Assert.True(mageSum.ReadyToLevelUp);
            Assert.False(rogueSum.ReadyToLevelUp);
            Assert.True(rogueSum.WasFallenRevived);
            Assert.True(mageSum.IsMvp);
            Assert.Equal(1, mageSum.Kills);
            Assert.True(mageSum.DamageDealt >= 6);

            // Interactive deferred level-up applies +2 points + spell unlock cleanly.
            Assert.Equal(1, mage.Level);
            Assert.Equal(1, mage.PendingLevelUps);
            Assert.False(mage.ApplyPendingLevelUp(new AttributeAllocation { KnowledgeDelta = 3 })); // Invalid > 2 pts
            Assert.True(mage.ApplyPendingLevelUp(new AttributeAllocation { KnowledgeDelta = 2 }));
            Assert.Equal(2, mage.Level);
            Assert.Equal(21, mage.Knowledge);
            Assert.Equal(0, mage.PendingLevelUps);
            Assert.True(mage.KnownSpellIds.Count >= 2);
        }
    }
}
