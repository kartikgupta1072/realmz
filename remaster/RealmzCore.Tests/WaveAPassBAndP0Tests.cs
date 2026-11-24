using System;
using System.Collections.Generic;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class WaveAPassBAndP0Tests
    {
        private static Dictionary<int, ItemDefinition> BuildEquipmentCatalog()
        {
            return new Dictionary<int, ItemDefinition>
            {
                [101] = new ItemDefinition(
                    101,
                    "Fine Longsword +1",
                    category: "Weapon",
                    slot: "Weapon",
                    damageDice: "1d8+1",
                    weaponBonus: 1,
                    basePrice: 120),
                [102] = new ItemDefinition(
                    102,
                    "Elven Bow +2",
                    category: "Weapon",
                    slot: "MissileWeapon",
                    damageDice: "1d8+2",
                    weaponBonus: 2,
                    range: 6,
                    basePrice: 200),
                [103] = new ItemDefinition(
                    103,
                    "Scale Mail",
                    category: "Armor",
                    slot: "Armor",
                    armorClassBonus: 6,
                    basePrice: 150),
                [104] = new ItemDefinition(
                    104,
                    "Goblin Charm",
                    category: "Misc",
                    slot: "",
                    basePrice: 25)
            };
        }

        private static List<GridCoord> BuildSquareOffsets(int radius)
        {
            List<GridCoord> offsets = new List<GridCoord>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    offsets.Add(new GridCoord(dx, dy));
                }
            }

            return offsets;
        }

        private static SpellDefinition BuildFireballSpell(int id = 1, int radius = 1, int range = 8, int spCost = 8)
        {
            return new SpellDefinition(
                id: id,
                key: "fireball",
                name: "Fireball",
                school: "Sorcerer",
                level: 3,
                spCost: spCost,
                range: range,
                targetMode: "Area",
                aoeOffsets: BuildSquareOffsets(radius),
                effectKind: "Damage",
                effectDice: "3d6",
                damageType: "Heat",
                appliedCondition: null,
                conditionDuration: 0,
                baseRecoveryPct: 0,
                saveKind: "None");
        }

        // =====================================================================
        // 1. P0.4 Single-Counted Weapon & Armor Bonuses
        // =====================================================================

        [Fact]
        public void P04_EquipmentBonuses_AreAppliedOnceAcrossPartyMemberAndCombatPreview()
        {
            Dictionary<int, ItemDefinition> catalog = BuildEquipmentCatalog();
            HeroPresetData unarmedPreset = new HeroPresetData(
                id: "h1_unarmed",
                name: "AldricUnarmed",
                hp: 30,
                maxHp: 30,
                ac: 10,
                toHit: 15,
                dodge: 10,
                missile: 12,
                movementMax: 6);
            HeroPresetData equippedPreset = new HeroPresetData(
                id: "h1_equipped",
                name: "AldricEquipped",
                hp: 30,
                maxHp: 30,
                ac: 10,
                toHit: 15,
                dodge: 10,
                missile: 12,
                movementMax: 6);

            PartyMemberState unarmed = new PartyMemberState(unarmedPreset, catalog);
            PartyMemberState equipped = new PartyMemberState(equippedPreset, catalog);
            PartyState party = new PartyState(new[] { equipped });
            party.AddItemToStash(101, 1); // +1 melee sword (+5% to hit)
            party.AddItemToStash(102, 1); // +2 bow (+10% to hit)
            party.AddItemToStash(103, 1); // +6 AC armor

            Assert.True(party.EquipFromStash(0, 0, catalog));
            Assert.True(party.EquipFromStash(0, 0, catalog));
            Assert.True(party.EquipFromStash(0, 0, catalog));

            // Effective stats on PartyMemberState apply AC (+6) and +5% per +1 weapon once.
            Assert.Equal(16, equipped.EffectiveAc);
            Assert.Equal(20, equipped.EffectiveMeleeToHit);
            Assert.Equal(22, equipped.EffectiveRangedToHit);

            // Exhaustion & Curse stacks are subtracted once in PartyMemberState and once in CombatantState.
            equipped.ExhaustionStacks = 2;
            equipped.CurseStacks = 1;
            Assert.Equal(13, equipped.EffectiveAc);         // 16 - 3
            Assert.Equal(14, equipped.EffectiveMeleeToHit); // 20 - 6
            Assert.Equal(16, equipped.EffectiveRangedToHit); // 22 - 6

            CombatantState unarmedUnit = CombatantState.FromHero(unarmed, new GridCoord(2, 2), catalog);
            CombatantState equippedUnit = CombatantState.FromHero(equipped, new GridCoord(2, 3), catalog);

            Assert.True(unarmedUnit.WeaponBonusIncludedInToHit);
            Assert.True(equippedUnit.WeaponBonusIncludedInToHit);
            Assert.Equal(13, equippedUnit.EffectiveAc);
            Assert.Equal(14, equippedUnit.EffectiveToHit);
            Assert.Equal(16, equippedUnit.EffectiveMissileSkill);

            // Reset stacks to compare exact +5% hit difference between unequipped and +1 melee weapon.
            equippedUnit.ExhaustionStacks = 0;
            equippedUnit.CurseStacks = 0;

            CombatGrid grid = new CombatGrid(8, 8);
            CombatantState dummy = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(3, 2), 20, 20, ac: 10);
            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { unarmedUnit, equippedUnit, dummy });

            TacticalAttackPreview unarmedPreview = session.PreviewMeleeAttack(unarmedUnit.Id, dummy.Id, new GridCoord(2, 2));
            TacticalAttackPreview equippedPreview = session.PreviewMeleeAttack(equippedUnit.Id, dummy.Id, new GridCoord(2, 2));

            Assert.Equal(5, equippedPreview.UncappedHitScore - unarmedPreview.UncappedHitScore);
            Assert.Equal(5, equippedPreview.HitChancePct - unarmedPreview.HitChancePct);
        }

        [Fact]
        public void P04_BywaterJsonLoader_LoadParty_LoadsUnequippedBaseStatsAndXp()
        {
            const string partyJson = @"{
                ""gold"": 250,
                ""stash"": [101, 103],
                ""heroes"": [
                    {
                        ""id"": ""kevlar"",
                        ""name"": ""Kevlar"",
                        ""archetype"": ""Warrior"",
                        ""level"": 1,
                        ""xp"": 175,
                        ""hp"": 28,
                        ""max_hp"": 30,
                        ""sp"": 4,
                        ""max_sp"": 8,
                        ""ac"": 11,
                        ""to_hit"": 18,
                        ""dodge"": 12,
                        ""missile"": 14,
                        ""movement_max"": 7,
                        ""attacks_per_round"": 1.5,
                        ""brawn"": 16,
                        ""vitality"": 14,
                        ""equipped_ids"": []
                    }
                ]
            }";

            List<HeroPresetData> heroes = BywaterJsonLoader.LoadParty(partyJson, out List<int> stash, out int gold);

            Assert.Equal(250, gold);
            Assert.Equal(new[] { 101, 103 }, stash);
            HeroPresetData preset = Assert.Single(heroes);
            Assert.Equal("kevlar", preset.Id);
            Assert.Equal("Kevlar", preset.Name);
            Assert.Equal(1, preset.Level);
            Assert.Equal(175, preset.Xp);
            Assert.Equal(11, preset.Ac);
            Assert.Equal(18, preset.ToHit);
            Assert.Equal(14, preset.Missile);
            Assert.Empty(preset.EquippedIds);

            PartyMemberState member = new PartyMemberState(preset);
            Assert.Equal(175, member.Xp);
            Assert.Equal(11, member.BaseAc);
            Assert.Equal(18, member.BaseToHit);
            Assert.Equal(14, member.BaseMissile);
            Assert.Empty(member.Equipped);
        }

        // =====================================================================
        // 2. P0.4 Spell Wall LOS Enforcement
        // =====================================================================

        [Fact]
        public void P04_SpellWallLos_BlocksTargetBehindWallAndPreservesSp()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetBlocked(4, 4, blocksLos: true);

            SpellDefinition bolt = new SpellDefinition(
                id: 10,
                key: "firebolt",
                name: "Firebolt",
                spCost: 6,
                range: 8,
                targetMode: "Single",
                effectKind: "Damage",
                effectDice: "2d6",
                damageType: "Heat",
                saveKind: "None");

            CombatantState mage = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(1, 4), 20, 20,
                sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 10 });
            CombatantState goblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(6, 4), 20, 20, dexterity: 10);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, goblin },
                spells: new Dictionary<int, SpellDefinition> { [10] = bolt });

            TacticalSpellPreview preview = session.PreviewSpell("h1", "firebolt", goblin.Position);
            Assert.False(preview.AoeFootprint.HasLineOfSight);
            Assert.False(preview.CanExecute);

            CombatActionResult cast = session.CastSpell("firebolt", goblin.Position);
            Assert.False(cast.Success);
            Assert.Equal(20, mage.Sp);
            Assert.True(mage.Economy.HasStandardAction);
            Assert.Equal(20, goblin.Hp);
        }

        [Fact]
        public void P04_SpellWallLos_AoeBlastInFrontOfWallDoesNotHitShelteredEnemyBehindWall()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            for (int y = 2; y <= 6; y++)
            {
                grid.SetBlocked(5, y, blocksLos: true);
            }

            SpellDefinition fireball = BuildFireballSpell(id: 1, radius: 2, range: 8, spCost: 8);
            CombatantState mage = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(1, 4), 20, 20,
                sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 1 });
            CombatantState exposedEnemy = new CombatantState(
                "m1", "FrontOrc", CombatSide.Monster, new GridCoord(4, 4), 30, 30, dexterity: 10);
            CombatantState shelteredEnemy = new CombatantState(
                "m2", "BackOrc", CombatSide.Monster, new GridCoord(6, 4), 30, 30, dexterity: 10);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, exposedEnemy, shelteredEnemy },
                spells: new Dictionary<int, SpellDefinition> { [1] = fireball },
                roller: new QueuedDiceRoller(4, 5, 6));

            GridCoord blastCenter = new GridCoord(4, 4);
            SpellAoeFootprint footprint = session.GetSpellAoeFootprint("fireball", blastCenter, "h1");

            Assert.True(footprint.HasLineOfSight);
            Assert.Contains("m1", footprint.AffectedEnemyIds);
            Assert.DoesNotContain("m2", footprint.AffectedEnemyIds);
            Assert.Contains(new GridCoord(6, 4), footprint.BlockedTiles);
            Assert.DoesNotContain(new GridCoord(6, 4), footprint.AffectedTiles);

            CombatActionResult result = session.CastSpell("fireball", blastCenter);
            Assert.True(result.Success);
            Assert.Equal(15, exposedEnemy.Hp);
            Assert.Equal(30, shelteredEnemy.Hp);
        }

        // =====================================================================
        // 3. P0.4 1-Per-Round HasReaction AoO Cap
        // =====================================================================

        [Fact]
        public void P04_ReactionCap_OnlyOneAoOPerRoundAndResetsOnMonsterTurn()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState hero1 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, ac: 0, movementMax: 6, dexterity: 20);
            CombatantState hero2 = new CombatantState("h2", "Vex", CombatSide.Hero, new GridCoord(5, 4), 30, 30, ac: 0, movementMax: 6, dexterity: 15);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(4, 4), 30, 30, toHit: 40, dexterity: 10, weaponDice: "1d6");

            // AoO 1 (hit 10, dmg 4), AoO 2 next round (hit 10, dmg 5)
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero1, hero2, goblin },
                roller: new QueuedDiceRoller(10, 4, 10, 5));

            Assert.True(goblin.Economy.HasReaction);
            Assert.True(session.WouldStepProvokeAoO("h1", hero1.Position, new GridCoord(2, 4)));

            // Hero 1 disengages -> triggers Goblin's single AoO and consumes HasReaction.
            CombatActionResult move1 = session.TryMoveTo(new GridCoord(2, 4));
            Assert.True(move1.Success);
            Assert.True(move1.AoOTriggered);
            Assert.Equal(4, move1.DamageDealt);
            Assert.Equal(26, hero1.Hp);
            Assert.False(goblin.Economy.HasReaction);

            // Second hero disengaging in the same round sees WouldStepProvokeAoO == false.
            Assert.False(session.WouldStepProvokeAoO("h2", hero2.Position, new GridCoord(6, 4)));
            session.EndTurn();
            Assert.Equal("h2", session.CurrentActorId);

            CombatActionResult move2 = session.TryMoveTo(new GridCoord(6, 4));
            Assert.True(move2.Success);
            Assert.False(move2.AoOTriggered);
            Assert.Equal(0, move2.DamageDealt);
            Assert.Equal(30, hero2.Hp);

            // Goblin's turn starts -> HasReaction resets to true.
            session.EndTurn();
            Assert.Equal("m1", session.CurrentActorId);
            Assert.True(goblin.Economy.HasReaction);

            // Advance to Round 2 Hero 1 turn: step adjacent then disengage -> AoO triggers again.
            session.EndTurn();
            Assert.Equal(2, session.RoundNumber);
            Assert.Equal("h1", session.CurrentActorId);

            Assert.True(session.TryMoveTo(new GridCoord(3, 4)).Success);
            CombatActionResult round2Disengage = session.TryMoveTo(new GridCoord(2, 4));
            Assert.True(round2Disengage.AoOTriggered);
            Assert.Equal(5, round2Disengage.DamageDealt);
            Assert.Equal(21, hero1.Hp);
            Assert.False(goblin.Economy.HasReaction);
        }

        // =====================================================================
        // 4. P0.4 Combat XP, Level-Up, Monster Loot & Spell Immunities
        // =====================================================================

        [Fact]
        public void P04_CombatVictory_AwardsEvenXpLevelUpGoldLootAndIsIdempotent()
        {
            Dictionary<int, ItemDefinition> catalog = BuildEquipmentCatalog();
            HeroPresetData p1 = new HeroPresetData("h1", "Aldric", level: 1, hp: 20, maxHp: 20, sp: 0, maxSp: 0, toHit: 40, vitality: 12);
            HeroPresetData p2 = new HeroPresetData("h2", "Liora", level: 1, hp: 14, maxHp: 14, sp: 10, maxSp: 10, toHit: 10, knowledge: 12);

            PartyMemberState m1 = new PartyMemberState(p1, catalog);
            PartyMemberState m2 = new PartyMemberState(p2, catalog);
            PartyState party = new PartyState(new[] { m1, m2 }, gold: 50);

            MonsterDefinition bossDef = new MonsterDefinition(
                index: 1,
                id: "goblin_chief",
                name: "Goblin Chief",
                hpMin: 8,
                hpMax: 8,
                hpDefault: 8,
                ac: 0,
                dexterity: 5,
                xpReward: 400,
                goldMin: 75,
                goldMax: 75,
                lootItemIds: new[] { 101, 104 });

            CombatantState h1 = CombatantState.FromHero(m1, new GridCoord(2, 2), catalog);
            h1.WeaponDice = "2d6+4";
            CombatantState h2 = CombatantState.FromHero(m2, new GridCoord(1, 2), catalog);
            CombatantState boss = CombatantState.FromMonster(bossDef, "boss_1", new GridCoord(3, 2));

            CombatGrid grid = new CombatGrid(8, 8);
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { h1, h2, boss },
                items: catalog,
                roller: new QueuedDiceRoller(20, 4, 4), // Hit 20, dmg 4+4+4 = 12 kills 8 HP boss
                party: party);

            CombatActionResult killRes = session.ExecuteMeleeAttack("boss_1");
            Assert.True(killRes.Success);
            Assert.Equal(CombatOutcome.Victory, session.Outcome);
            Assert.True(session.VictoryRewardsGranted);
            Assert.Equal(400, session.TotalXpEarned);
            Assert.Equal(75, session.TotalGoldLoot);
            Assert.Equal(new[] { 101, 104 }, session.CollectedLootItemIds);

            // 400 XP split evenly -> +200 each -> Lv 1 -> Lv 2 (200 XP threshold).
            Assert.Equal(200, m1.Xp);
            Assert.Equal(200, m2.Xp);
            Assert.Equal(2, m1.Level);
            Assert.Equal(2, m2.Level);
            Assert.True(m1.MaxHp > 20);
            Assert.True(m1.Hp > 20);
            Assert.True(m2.MaxHp > 14);
            Assert.Equal(2, session.VictoryLevelsGained);

            // Gold and loot items added to PartyState.
            Assert.Equal(125, party.Gold);
            Assert.Contains(party.Stash, i => i.ItemId == 101 && i.Quantity == 1);
            Assert.Contains(party.Stash, i => i.ItemId == 104 && i.Quantity == 1);

            // Idempotency: calling GrantVictoryRewardsToParty and SyncAfterCombat does not double-grant XP, Gold, or Loot.
            int stashCountBefore = party.Stash.Count;
            int m1MaxHpAfterLevel = m1.MaxHp;
            session.GrantVictoryRewardsToParty(party);

            Dictionary<string, TileMapData> maps = new Dictionary<string, TileMapData>
            {
                ["land_0"] = new TileMapData("land_0", "Test", 0, false, 4, 4, new int[16], new GridCoord(1, 1))
            };
            ExplorationSession exploration = new ExplorationSession(maps, "land_0", party);
            exploration.SyncAfterCombat(session);

            Assert.Equal(200, m1.Xp);
            Assert.Equal(200, m2.Xp);
            Assert.Equal(2, m1.Level);
            Assert.Equal(2, m2.Level);
            Assert.Equal(m1MaxHpAfterLevel, m1.MaxHp);
            Assert.Equal(125, party.Gold);
            Assert.Equal(stashCountBefore, party.Stash.Count);
        }

        [Fact]
        public void P04_SpellImmunities_ResistImmuneDamageTypesWithZeroDamageAndLogsWhileNonImmuneHits()
        {
            SpellDefinition heatSpell = new SpellDefinition(
                id: 1, key: "flame", name: "Flame Strike", spCost: 4, range: 6,
                targetMode: "Single", effectKind: "Damage", effectDice: "3d6",
                damageType: "Heat", appliedCondition: CharConditionId.Slow, conditionDuration: 2, saveKind: "None");
            SpellDefinition coldSpell = new SpellDefinition(
                id: 2, key: "frost", name: "Frost Lance", spCost: 4, range: 6,
                targetMode: "Single", effectKind: "Damage", effectDice: "3d6",
                damageType: "Cold", saveKind: "None");
            SpellDefinition mentalSpell = new SpellDefinition(
                id: 3, key: "mindblast", name: "Mind Blast", spCost: 4, range: 6,
                targetMode: "Single", effectKind: "Condition", effectDice: "2d6",
                damageType: "Mental", appliedCondition: CharConditionId.Helpless, conditionDuration: 2, saveKind: "None");
            SpellDefinition electricSpell = new SpellDefinition(
                id: 4, key: "shock", name: "Shock Bolt", spCost: 4, range: 6,
                targetMode: "Single", effectKind: "Damage", effectDice: "2d6",
                damageType: "Electric", saveKind: "None");

            Dictionary<int, SpellDefinition> spells = new Dictionary<int, SpellDefinition>
            {
                [1] = heatSpell,
                [2] = coldSpell,
                [3] = mentalSpell,
                [4] = electricSpell
            };

            // Immune to Heat (1), Cold (2), Mental (5); vulnerable to Electric (3).
            bool[] immunities = { false, true, true, false, false, true };
            CombatantState mage = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(1, 3), 30, 30,
                sp: 40, maxSp: 40, dexterity: 20, spellIds: new[] { 1, 2, 3, 4 });
            CombatantState elemental = new CombatantState(
                "m1", "Astral Elemental", CombatSide.Monster, new GridCoord(4, 3), 40, 40,
                dexterity: 5, spellImmunities: immunities);

            TacticalCombatSession session = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { mage, elemental },
                spells: spells,
                roller: new QueuedDiceRoller(5, 6));

            // 1. Heat (index 1) -> 0 damage, no condition, SpellImmune log + "Immune" message.
            CombatActionResult heatRes = session.CastSpell("flame", elemental.Position);
            Assert.True(heatRes.Success);
            Assert.Equal(0, heatRes.DamageDealt);
            Assert.Equal(40, elemental.Hp);
            Assert.False(elemental.Conditions.Has(CharConditionId.Slow));
            Assert.Contains("Immune", heatRes.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(session.Log, e => e.ActionType == "SpellImmune");

            // 2. Cold (index 2) -> immune.
            session.EndTurn();
            session.EndTurn();
            CombatActionResult coldRes = session.CastSpell("frost", elemental.Position);
            Assert.True(coldRes.Success);
            Assert.Equal(0, coldRes.DamageDealt);
            Assert.Equal(40, elemental.Hp);
            Assert.Contains("Immune", coldRes.Message, StringComparison.OrdinalIgnoreCase);

            // 3. Mental (index 5) -> immune, no Helpless applied.
            session.EndTurn();
            session.EndTurn();
            CombatActionResult mentalRes = session.CastSpell("mindblast", elemental.Position);
            Assert.True(mentalRes.Success);
            Assert.Equal(0, mentalRes.DamageDealt);
            Assert.Equal(40, elemental.Hp);
            Assert.False(elemental.Conditions.Has(CharConditionId.Helpless));
            Assert.Contains("Immune", mentalRes.Message, StringComparison.OrdinalIgnoreCase);

            // 4. Electric (index 3, not immune) -> deals 5 + 6 = 11 damage.
            session.EndTurn();
            session.EndTurn();
            CombatActionResult elecRes = session.CastSpell("shock", elemental.Position);
            Assert.True(elecRes.Success);
            Assert.Equal(11, elecRes.DamageDealt);
            Assert.Equal(29, elemental.Hp);
        }

        // =====================================================================
        // 5. P1.3 Elevation High Ground
        // =====================================================================

        [Fact]
        public void P13_ElevationHighGround_SetsTiersAndAppliesHitAndRangeModifiers()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            short[,] tiers = new short[10, 10];
            tiers[4, 1] = 2; // High ground ledge at (1, 4)
            tiers[4, 2] = 0; // Low ground at (2, 4)
            tiers[4, 6] = 0; // Low ground at (6, 4)
            grid.ApplyElevationTiers(tiers);

            Assert.Equal((short)2, grid.GetCell(1, 4).ElevationTier);
            Assert.Equal((short)0, grid.GetCell(6, 4).ElevationTier);

            SpellDefinition range4Spell = BuildFireballSpell(id: 1, radius: 0, range: 4, spCost: 4);
            CombatantState hero = new CombatantState(
                "h1", "Kaelen", CombatSide.Hero, new GridCoord(1, 4), 25, 25,
                sp: 20, maxSp: 20, toHit: 10, missileSkill: 10, rangedDice: "1d6", rangedRange: 4,
                dexterity: 20, spellIds: new[] { 1 });
            CombatantState meleeEnemy = new CombatantState(
                "m1", "OrcMelee", CombatSide.Monster, new GridCoord(2, 4), 20, 20, ac: 10, toHit: 10, dexterity: 10);
            CombatantState farEnemy = new CombatantState(
                "m2", "OrcFar", CombatSide.Monster, new GridCoord(6, 4), 20, 20, ac: 10, missileSkill: 10,
                rangedDice: "1d6", rangedRange: 4, dexterity: 5, spellIds: new[] { 1 });

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, meleeEnemy, farEnemy },
                spells: new Dictionary<int, SpellDefinition> { [1] = range4Spell });

            // Downhill melee (Tier 2 -> Tier 0): +15% hit & High Ground +15% tag.
            TacticalAttackPreview downhillMelee = session.PreviewMeleeAttack("h1", "m1");
            Assert.Equal(15, downhillMelee.ElevationModifierPct);
            Assert.Contains("High Ground +15%", downhillMelee.ConditionTags);
            Assert.Equal(65, downhillMelee.HitChancePct); // 50 + 10 - 10 + 15 = 65

            // Uphill melee (Tier 0 -> Tier 2): -15% hit & Low Ground -15% tag.
            TacticalAttackPreview uphillMelee = session.PreviewMeleeAttack("m1", "h1");
            Assert.Equal(-15, uphillMelee.ElevationModifierPct);
            Assert.Contains("Low Ground -15%", uphillMelee.ConditionTags);
            Assert.Equal(35, uphillMelee.HitChancePct); // 50 + 10 - 10 - 15 = 35

            // Downhill ranged at distance 5 with base RangedRange 4 -> +1 bonus range allows hit +15%.
            TacticalAttackPreview downhillRanged = session.PreviewRangedAttack("h1", "m2");
            Assert.True(downhillRanged.CanExecute);
            Assert.Equal(15, downhillRanged.ElevationModifierPct);
            Assert.Contains("High Ground +15%", downhillRanged.ConditionTags);

            // Downhill spell at distance 5 with base Range 4 -> +1 bonus range allows cast.
            TacticalSpellPreview downhillSpell = session.PreviewSpell("h1", "fireball", farEnemy.Position);
            Assert.True(downhillSpell.AoeFootprint.InRange);
            Assert.True(downhillSpell.CanExecute);
            Assert.Contains("High Ground +1 Range", downhillSpell.ConditionTags);

            // Uphill ranged/spell at distance 5 with base Range 4 -> NO range bonus, out of range.
            TacticalAttackPreview uphillRanged = session.PreviewRangedAttack("m2", "h1");
            Assert.False(uphillRanged.CanExecute);
            Assert.Equal(-15, uphillRanged.ElevationModifierPct);
            Assert.Contains("Low Ground -15%", uphillRanged.ConditionTags);

            TacticalSpellPreview uphillSpell = session.PreviewSpell("m2", "fireball", hero.Position);
            Assert.False(uphillSpell.AoeFootprint.InRange);
            Assert.False(uphillSpell.CanExecute);
        }

        // =====================================================================
        // 6. P1.3 Elemental Surfaces & Combo Matrix
        // =====================================================================

        [Fact]
        public void P13_SurfaceMatrix_HeatOnOilIgnitesContiguousClusterBurnsAndExpiresAfterTwoRounds()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            grid.SetSurface(4, 4, SurfaceType.Oil, -1);
            grid.SetSurface(5, 4, SurfaceType.Oil, -1);
            grid.SetSurface(6, 4, SurfaceType.Oil, -1);

            SpellDefinition singleSpark = new SpellDefinition(
                id: 1, key: "spark", name: "Spark", spCost: 2, range: 6,
                targetMode: "Single", effectKind: "Damage", effectDice: "1d4",
                damageType: "Heat", saveKind: "None");

            CombatantState mage = new CombatantState(
                "h1", "Liora", CombatSide.Hero, new GridCoord(1, 4), 30, 30,
                sp: 20, maxSp: 20, movementMax: 6, dexterity: 20, spellIds: new[] { 1 });
            CombatantState m1 = new CombatantState("m1", "Orc1", CombatSide.Monster, new GridCoord(4, 4), 40, 40, dexterity: 10);
            CombatantState m2 = new CombatantState("m2", "Orc2", CombatSide.Monster, new GridCoord(6, 4), 40, 40, dexterity: 5);

            // Rolls: 3 (spark on m1), 5 (1d6 ignition burst on m1), 4 (1d6 ignition burst on m2),
            // Round 1: 2 (m1 turn start Fire 1d4), 3 (m2 turn start Fire 1d4),
            // Round 2: 2 (mage step onto Fire 1d4), 1 (m1 turn start Fire 1d4), 1 (m2 turn start Fire 1d4).
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { mage, m1, m2 },
                spells: new Dictionary<int, SpellDefinition> { [1] = singleSpark },
                roller: new QueuedDiceRoller(3, 5, 4, 2, 3, 2, 1, 1));

            List<SurfaceChangeEvent> observedEvents = new List<SurfaceChangeEvent>();
            session.SurfaceChanged += changes => observedEvents.AddRange(changes);

            CombatActionResult castRes = session.CastSpell("spark", new GridCoord(4, 4));
            Assert.True(castRes.Success);

            // Entire contiguous Oil cluster converted to Fire (2 rounds).
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(4, 4));
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(5, 4));
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(6, 4));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(4, 4));
            Assert.Equal(3, observedEvents.Count);
            Assert.All(observedEvents, ev =>
            {
                Assert.Equal(SurfaceType.Oil, ev.PreviousSurface);
                Assert.Equal(SurfaceType.Fire, ev.NewSurface);
                Assert.Equal(2, ev.RoundsRemaining);
            });

            // Both m1 (on center) and m2 (on connected oil tile) took 1d6 ignition burst.
            Assert.Equal(32, m1.Hp); // 40 - 3 (spark) - 5 (oil burst)
            Assert.Equal(36, m2.Hp); // 40 - 4 (oil burst)

            // m1 turn start on Fire deals 1d4 burn (2 dmg).
            session.EndTurn();
            Assert.Equal("m1", session.CurrentActorId);
            Assert.Equal(30, m1.Hp);

            // m2 turn start on Fire deals 1d4 burn (3 dmg).
            session.EndTurn();
            Assert.Equal("m2", session.CurrentActorId);
            Assert.Equal(33, m2.Hp);

            // Round 2 starts -> Fire rounds drop from 2 to 1.
            session.EndTurn();
            Assert.Equal(2, session.RoundNumber);
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(5, 4));
            Assert.Equal(1, grid.GetSurfaceRoundsRemaining(5, 4));

            // Hero steps onto Fire at (5,4) -> takes 1d4 burn (2 dmg).
            Assert.True(session.TryMoveTo(new GridCoord(5, 4)).Success);
            Assert.Equal(28, mage.Hp);

            // Finish Round 2 -> Round 3 starts -> Fire expires after 2 rounds.
            session.EndTurn(); // m1 Round 2 turn start burn (1 dmg -> 29)
            Assert.Equal(29, m1.Hp);
            session.EndTurn(); // m2 Round 2 turn start burn (1 dmg -> 32)
            Assert.Equal(32, m2.Hp);
            session.EndTurn(); // Round 3 starts -> surfaces tick 1 -> 0 and expire
            Assert.Equal(3, session.RoundNumber);
            Assert.Equal(SurfaceType.None, grid.GetSurface(4, 4));
            Assert.Equal(SurfaceType.None, grid.GetSurface(5, 4));
            Assert.Equal(SurfaceType.None, grid.GetSurface(6, 4));
        }

        [Fact]
        public void P13_SurfaceMatrix_FireExpiresAfterTwoRoundsAndWaterCombosWork()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetSurface(2, 2, SurfaceType.Oil, -1);
            grid.SetSurface(3, 2, SurfaceType.Ice, 3);
            grid.SetSurface(4, 2, SurfaceType.Water, -1);
            grid.SetSurface(5, 2, SurfaceType.Water, -1);
            grid.SetSurface(6, 2, SurfaceType.Water, -1);

            CombatantState hero = new CombatantState("h1", "Mage", CombatSide.Hero, new GridCoord(0, 0), 30, 30, dexterity: 20);
            CombatantState orc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(9, 9), 30, 30, dexterity: 10);

            TacticalCombatSession session = new TacticalCombatSession(grid, new[] { hero, orc });

            // Heat on Oil -> Fire (2 rounds)
            session.ApplySurfaceInteraction(new[] { new GridCoord(2, 2) }, "Heat");
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(2, 2));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(2, 2));

            // Heat on Ice -> Water
            session.ApplySurfaceInteraction(new[] { new GridCoord(3, 2) }, "Heat");
            Assert.Equal(SurfaceType.Water, grid.GetSurface(3, 2));

            // Heat on Water -> SteamCloud (2 rounds)
            session.ApplySurfaceInteraction(new[] { new GridCoord(4, 2) }, "Heat");
            Assert.Equal(SurfaceType.SteamCloud, grid.GetSurface(4, 2));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(4, 2));

            // Cold on Water -> Ice (3 rounds)
            session.ApplySurfaceInteraction(new[] { new GridCoord(5, 2) }, "Cold");
            Assert.Equal(SurfaceType.Ice, grid.GetSurface(5, 2));
            Assert.Equal(3, grid.GetSurfaceRoundsRemaining(5, 2));

            // Electric on Water -> ElectrifiedWater (2 rounds)
            session.ApplySurfaceInteraction(new[] { new GridCoord(6, 2) }, "Electric");
            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(6, 2));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(6, 2));

            // Advance through Round 1 -> Round 2 (Fire/Steam/Elec=1, Ice=2).
            session.EndTurn();
            session.EndTurn();
            Assert.Equal(2, session.RoundNumber);
            Assert.Equal(SurfaceType.Fire, grid.GetSurface(2, 2));
            Assert.Equal(SurfaceType.SteamCloud, grid.GetSurface(4, 2));
            Assert.Equal(SurfaceType.Ice, grid.GetSurface(5, 2));
            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(6, 2));

            // Advance through Round 2 -> Round 3 (2-round surfaces expire; ElectrifiedWater reverts to Water).
            session.EndTurn();
            session.EndTurn();
            Assert.Equal(3, session.RoundNumber);
            Assert.Equal(SurfaceType.None, grid.GetSurface(2, 2));
            Assert.Equal(SurfaceType.None, grid.GetSurface(4, 2));
            Assert.Equal(SurfaceType.Ice, grid.GetSurface(5, 2));
            Assert.Equal(1, grid.GetSurfaceRoundsRemaining(5, 2));
            Assert.Equal(SurfaceType.Water, grid.GetSurface(6, 2));
        }

        [Fact]
        public void P13_SurfaceMatrix_SteamCloudBlocksIntermediateLosAndObscuresTargetInCloud()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            grid.SetSurface(4, 3, SurfaceType.SteamCloud, 2); // Intermediate cloud
            grid.SetSurface(6, 6, SurfaceType.SteamCloud, 2); // Target standing inside cloud

            CombatantState archer = new CombatantState(
                "h1", "Kaelen", CombatSide.Hero, new GridCoord(1, 3), 25, 25,
                missileSkill: 20, rangedDice: "1d6", rangedRange: 8, dexterity: 20);
            CombatantState behindSteam = new CombatantState(
                "m1", "OrcBehind", CombatSide.Monster, new GridCoord(6, 3), 20, 20, ac: 10);
            CombatantState insideSteam = new CombatantState(
                "m2", "OrcInside", CombatSide.Monster, new GridCoord(6, 6), 20, 20, ac: 10);
            CombatantState clearOrc = new CombatantState(
                "m3", "OrcClear", CombatSide.Monster, new GridCoord(1, 7), 20, 20, ac: 10);

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { archer, behindSteam, insideSteam, clearOrc });

            // Intermediate SteamCloud blocks ranged LOS.
            LosResult blockedLos = session.CheckLineOfSight(archer.Position, behindSteam.Position);
            Assert.Equal(LosKind.Blocked, blockedLos.Kind);
            Assert.True(blockedLos.IsSteamObscured);
            Assert.False(session.PreviewRangedAttack("h1", "m1").CanExecute);

            // Target standing inside SteamCloud applies -20% obscuration penalty.
            LosResult obscuredLos = session.CheckLineOfSight(archer.Position, insideSteam.Position);
            Assert.Equal(LosKind.PartialCover, obscuredLos.Kind);
            Assert.Equal(20, obscuredLos.CoverPenaltyPct);
            Assert.True(obscuredLos.IsSteamObscured);

            TacticalAttackPreview obscuredPreview = session.PreviewRangedAttack("h1", "m2");
            TacticalAttackPreview clearPreview = session.PreviewRangedAttack("h1", "m3");
            Assert.True(obscuredPreview.CanExecute);
            Assert.Contains("STEAM_OBSCURED -20%", obscuredPreview.ConditionTags);
            Assert.Equal(20, clearPreview.UncappedHitScore - obscuredPreview.UncappedHitScore);
        }

        [Fact]
        public void P13_SurfaceMatrix_IceSlipKnocksProneStopsMovementAndRecoversAfterOneRound()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            for (int y = 0; y < 8; y++)
            {
                if (y == 4)
                {
                    grid.SetSurface(3, y, SurfaceType.Ice, 3);
                }
                else
                {
                    grid.SetBlocked(3, y, blocksLos: false);
                }
            }

            CombatantState hero = new CombatantState(
                "h1", "Aldric", CombatSide.Hero, new GridCoord(1, 4), 25, 25,
                dodge: 10, movementMax: 6, dexterity: 20);
            CombatantState orc = new CombatantState(
                "m1", "Orc", CombatSide.Monster, new GridCoord(7, 7), 20, 20, dexterity: 10);

            // Roll 99 > save (25 + 20 = 45%) -> fails Ice save and slips Prone.
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, orc },
                roller: new QueuedDiceRoller(99));

            CombatActionResult move = session.TryMoveTo(new GridCoord(5, 4));
            Assert.True(move.Success);
            Assert.Equal(new GridCoord(3, 4), hero.Position); // Stopped on Ice tile
            Assert.True(hero.IsProne);
            Assert.True(hero.PronePendingMovePenalty);
            Assert.Equal(0, hero.Economy.MoveRemaining);

            // Next hero turn: loses Move for 1 round, remains Prone during turn, then stands up at EndTurn.
            session.EndTurn();
            session.EndTurn();
            Assert.Equal("h1", session.CurrentActorId);
            Assert.Equal(0, hero.Economy.MoveRemaining);
            Assert.True(hero.IsProne);
            Assert.False(hero.PronePendingMovePenalty);

            session.EndTurn();
            Assert.False(hero.IsProne);
        }

        [Fact]
        public void P13_SurfaceMatrix_ElectrifiedWaterShocksAndRevertsToWater()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            grid.SetSurface(4, 4, SurfaceType.Water, -1);

            CombatantState hero = new CombatantState("h1", "Mage", CombatSide.Hero, new GridCoord(1, 4), 30, 30, dexterity: 20);
            CombatantState orc = new CombatantState("m1", "Orc", CombatSide.Monster, new GridCoord(4, 4), 30, 30, dexterity: 10);

            // Roll 3 (1d4 electric reaction shock), then 4 (1d4 start-of-turn shock on orc)
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, orc },
                roller: new QueuedDiceRoller(3, 4));

            int reactionDmg = session.ApplySurfaceInteraction(new[] { new GridCoord(4, 4) }, "Electric");
            Assert.Equal(3, reactionDmg);
            Assert.Equal(SurfaceType.ElectrifiedWater, grid.GetSurface(4, 4));
            Assert.Equal(2, grid.GetSurfaceRoundsRemaining(4, 4));
            Assert.Equal(27, orc.Hp);

            session.EndTurn(); // orc turn start -> 4 shock dmg
            Assert.Equal(23, orc.Hp);

            // Move orc off ElectrifiedWater so no more shocks happen before expiry check
            orc.Position = new GridCoord(5, 4);
            session.EndTurn(); // Round 2
            Assert.Equal(1, grid.GetSurfaceRoundsRemaining(4, 4));

            session.EndTurn();
            session.EndTurn(); // Round 3 -> reverts to Water
            Assert.Equal(SurfaceType.Water, grid.GetSurface(4, 4));
        }

        // =====================================================================
        // 7. P1.3 Swift Action Shove
        // =====================================================================

        [Fact]
        public void P13_Shove_PushesEnemyOffHighGroundLedgeConsumingOnlySwiftAction()
        {
            CombatGrid grid = new CombatGrid(8, 8);
            grid.SetElevation(3, 4, 2);
            grid.SetElevation(4, 4, 2);
            grid.SetElevation(5, 4, 0);

            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, brawn: 16, dexterity: 20);
            CombatantState goblin = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(4, 4), 25, 25, brawn: 10, dexterity: 10);

            // Shove roll 1 (success), 2d6 fall damage for 2-tier drop: 4 + 5 = 9
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, goblin },
                roller: new QueuedDiceRoller(1, 4, 5));

            TacticalShovePreview preview = session.PreviewShove("h1", "m1");
            Assert.True(preview.CanExecute);
            Assert.True(preview.PushesOffLedge);
            Assert.Equal(2, preview.FallElevationDrop);
            Assert.Equal(new GridCoord(5, 4), preview.Destination);

            CombatActionResult res = session.TryShove("m1");
            Assert.True(res.Success);
            Assert.Equal(9, res.DamageDealt);
            Assert.Equal(new GridCoord(5, 4), goblin.Position);
            Assert.Equal(16, goblin.Hp);
            Assert.True(goblin.IsProne);
            Assert.False(hero.Economy.HasSwiftAction);
            Assert.True(hero.Economy.HasStandardAction);
        }

        [Fact]
        public void P13_Shove_WallCrushUnitCollisionFireAndAoOFreeAllyRescue()
        {
            // 1. Shove into wall -> 1d4 crush
            CombatGrid wallGrid = new CombatGrid(8, 8);
            wallGrid.SetBlocked(5, 4, blocksLos: true);
            CombatantState hero1 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, brawn: 16, dexterity: 20);
            CombatantState enemy1 = new CombatantState("m1", "Orc1", CombatSide.Monster, new GridCoord(4, 4), 20, 20);
            TacticalCombatSession wallSession = new TacticalCombatSession(
                wallGrid, new[] { hero1, enemy1 }, roller: new QueuedDiceRoller(1, 3));

            CombatActionResult wallRes = wallSession.TryShove("m1");
            Assert.True(wallRes.Success);
            Assert.Equal(3, wallRes.DamageDealt);
            Assert.Equal(17, enemy1.Hp);
            Assert.Equal(new GridCoord(4, 4), enemy1.Position);

            // 2. Shove into another unit -> 1d4 crush to both
            CombatGrid unitGrid = new CombatGrid(8, 8);
            CombatantState hero2 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, brawn: 16, dexterity: 20);
            CombatantState enemyA = new CombatantState("m1", "OrcA", CombatSide.Monster, new GridCoord(4, 4), 20, 20);
            CombatantState enemyB = new CombatantState("m2", "OrcB", CombatSide.Monster, new GridCoord(5, 4), 20, 20);
            TacticalCombatSession unitSession = new TacticalCombatSession(
                unitGrid, new[] { hero2, enemyA, enemyB }, roller: new QueuedDiceRoller(1, 2, 4));

            CombatActionResult unitRes = unitSession.TryShove("m1");
            Assert.True(unitRes.Success);
            Assert.Equal(6, unitRes.DamageDealt);
            Assert.Equal(18, enemyA.Hp);
            Assert.Equal(16, enemyB.Hp);

            // 3. Shove onto Fire -> 1d4 burn
            CombatGrid fireGrid = new CombatGrid(8, 8);
            fireGrid.SetSurface(5, 4, SurfaceType.Fire, 2);
            CombatantState hero3 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, brawn: 16, dexterity: 20);
            CombatantState enemyF = new CombatantState("m1", "OrcF", CombatSide.Monster, new GridCoord(4, 4), 20, 20);
            TacticalCombatSession fireSession = new TacticalCombatSession(
                fireGrid, new[] { hero3, enemyF }, roller: new QueuedDiceRoller(1, 4));

            CombatActionResult fireRes = fireSession.TryShove("m1");
            Assert.True(fireRes.Success);
            Assert.Equal(4, fireRes.DamageDealt);
            Assert.Equal(16, enemyF.Hp);
            Assert.Equal(new GridCoord(5, 4), enemyF.Position);

            // 4. Shove willing ally out of enemy's 3x3 ring -> 100% success, 0 AoO triggered
            CombatGrid allyGrid = new CombatGrid(8, 8);
            CombatantState rescuer = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, dexterity: 20);
            CombatantState ally = new CombatantState("h2", "Liora", CombatSide.Hero, new GridCoord(4, 4), 20, 20, dexterity: 15);
            CombatantState threat = new CombatantState("m1", "Goblin", CombatSide.Monster, new GridCoord(3, 5), 20, 20, toHit: 50, dexterity: 10);
            TacticalCombatSession allySession = new TacticalCombatSession(
                allyGrid, new[] { rescuer, ally, threat }, roller: new QueuedDiceRoller());

            TacticalShovePreview allyPreview = allySession.PreviewShove("h1", "h2");
            Assert.True(allyPreview.CanExecute);
            Assert.True(allyPreview.IsAllyShove);
            Assert.Equal(100, allyPreview.SuccessChancePct);
            Assert.Equal(new GridCoord(5, 4), allyPreview.Destination);

            CombatActionResult allyRes = allySession.TryShove("h2");
            Assert.True(allyRes.Success);
            Assert.False(allyRes.AoOTriggered);
            Assert.Equal(new GridCoord(5, 4), ally.Position);
            Assert.Equal(20, ally.Hp);
            Assert.True(threat.Economy.HasReaction);
        }

        // =====================================================================
        // 8. P1.3 Smarter MonsterAi & PredictNextIntent()
        // =====================================================================

        [Fact]
        public void P13_MonsterAi_CasterMovesIntoRangeAndCastsInSameTurn()
        {
            SpellDefinition bolt = new SpellDefinition(
                id: 1, key: "bolt", name: "Bolt", spCost: 4, range: 4,
                targetMode: "Single", effectKind: "Damage", effectDice: "2d4",
                damageType: "Magic", saveKind: "None");

            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(1, 4), 30, 30, dexterity: 10);
            CombatantState shaman = new CombatantState(
                "m1", "Goblin Shaman", CombatSide.Monster, new GridCoord(8, 4), 20, 20,
                sp: 20, maxSp: 20, movementMax: 6, dexterity: 20, spellIds: new[] { 1 });

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, shaman },
                spells: new Dictionary<int, SpellDefinition> { [1] = bolt },
                roller: new QueuedDiceRoller(3, 4));

            Assert.Equal("m1", session.CurrentActorId);
            Assert.True(shaman.Position.ChebyshevDistance(hero.Position) > 4);

            CombatActionResult res = session.ExecuteMonsterAiTurn();
            Assert.True(res.Success);
            Assert.True(shaman.Position.X < 8);
            Assert.True(shaman.Position.ChebyshevDistance(hero.Position) <= 4);
            Assert.Equal(16, shaman.Sp);
            Assert.Equal(23, hero.Hp);
            Assert.Contains(session.Log, e => e.ActorId == "m1" && e.ActionType == "CastSpell");
        }

        [Fact]
        public void P13_MonsterAi_PrefersRangedArcherKitesBackAndShootsInsteadOfMelee()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(3, 4), 30, 30, toHit: 10, dexterity: 10);
            CombatantState archer = new CombatantState(
                "m1", "Goblin Archer", CombatSide.Monster, new GridCoord(4, 4), 30, 30,
                ac: 40, toHit: 25, missileSkill: 30, movementMax: 6, dexterity: 20,
                rangedDice: "1d8", rangedRange: 6, prefersRanged: true);

            // Roll 1 (AoO miss at roll 95 vs low hit), Roll 2 (archer bow hit 15), Roll 3 (bow dmg 6)
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, archer },
                roller: new QueuedDiceRoller(95, 15, 6));

            Assert.Equal("m1", session.CurrentActorId);
            CombatActionResult res = session.ExecuteMonsterAiTurn();
            Assert.True(res.Success);
            Assert.True(archer.Position.ChebyshevDistance(hero.Position) >= 2);
            Assert.Equal(24, hero.Hp);
            Assert.Contains(session.Log, e => e.ActorId == "m1" && e.ActionType == "RangedAttack");
            Assert.DoesNotContain(session.Log, e => e.ActorId == "m1" && e.ActionType == "MeleeAttack");
        }

        [Fact]
        public void P13_MonsterAi_AoeCasterTargetsEmptyGroundCentroidBetweenSeparatedHeroes()
        {
            SpellDefinition fireball = BuildFireballSpell(id: 1, radius: 1, range: 8, spCost: 6);
            CombatGrid grid = new CombatGrid(10, 10);
            // Place heroes at opposite diagonal corners of a 3x3 centered at empty ground (3, 4)
            // so (3, 4) is the unique tile hitting both heroes.
            CombatantState hero1 = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 3), 30, 30, dexterity: 10);
            CombatantState hero2 = new CombatantState("h2", "Liora", CombatSide.Hero, new GridCoord(4, 5), 30, 30, dexterity: 8);
            CombatantState caster = new CombatantState(
                "m1", "Pyromancer", CombatSide.Monster, new GridCoord(3, 8), 25, 25,
                sp: 20, maxSp: 20, dexterity: 20, spellIds: new[] { 1 });

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero1, hero2, caster },
                spells: new Dictionary<int, SpellDefinition> { [1] = fireball },
                roller: new DeterministicDiceRoller(42UL));

            EnemyIntentPrediction intent = session.PredictNextIntent("m1");
            Assert.Equal(CombatIntentActionType.CastSpell, intent.ActionKind);
            Assert.Equal(new GridCoord(3, 4), intent.TargetCoord);

            CombatActionResult res = session.ExecuteMonsterAiTurn();
            Assert.True(res.Success);
            Assert.True(hero1.Hp < 30);
            Assert.True(hero2.Hp < 30);
        }

        [Fact]
        public void P13_MonsterAi_PredictNextIntent_IsDryRunAndMatchesUncontestedExecution()
        {
            CombatGrid grid = new CombatGrid(10, 10);
            CombatantState hero = new CombatantState("h1", "Aldric", CombatSide.Hero, new GridCoord(2, 4), 30, 30, ac: 10, dexterity: 20);
            CombatantState goblin = new CombatantState(
                "m1", "Goblin", CombatSide.Monster, new GridCoord(5, 4), 20, 20,
                toHit: 25, movementMax: 6, dexterity: 10, weaponDice: "1d6");

            // Roller starts empty so any accidental RNG call during PredictNextIntent throws immediately.
            QueuedDiceRoller roller = new QueuedDiceRoller();
            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { hero, goblin },
                roller: roller);

            Assert.Equal("h1", session.CurrentActorId);
            GridCoord heroBefore = hero.Position;
            GridCoord goblinBefore = goblin.Position;
            int goblinHpBefore = goblin.Hp;
            int goblinSpBefore = goblin.Sp;
            int goblinMoveBefore = goblin.Economy.MoveRemaining;
            bool goblinStdBefore = goblin.Economy.HasStandardAction;
            bool goblinSwiftBefore = goblin.Economy.HasSwiftAction;
            bool goblinReactBefore = goblin.Economy.HasReaction;

            EnemyIntentPrediction prediction = session.PredictNextIntent("m1");
            (GridCoord targetCoord, string actionType, string targetId) = prediction;

            Assert.Equal(CombatIntentActionType.MoveAndAttack, prediction.ActionKind);
            Assert.Equal("MoveAndAttack", actionType);
            Assert.Equal("h1", targetId);
            Assert.Equal(hero.Position, targetCoord);
            Assert.True(prediction.HasPlannedMove);
            Assert.Equal(1, prediction.PlannedMoveCoord.ChebyshevDistance(hero.Position));

            // Zero state mutation verified.
            Assert.Equal(0, roller.RemainingCount);
            Assert.Equal(heroBefore, hero.Position);
            Assert.Equal(goblinBefore, goblin.Position);
            Assert.Equal(goblinHpBefore, goblin.Hp);
            Assert.Equal(goblinSpBefore, goblin.Sp);
            Assert.Equal(goblinMoveBefore, goblin.Economy.MoveRemaining);
            Assert.Equal(goblinStdBefore, goblin.Economy.HasStandardAction);
            Assert.Equal(goblinSwiftBefore, goblin.Economy.HasSwiftAction);
            Assert.Equal(goblinReactBefore, goblin.Economy.HasReaction);

            // Hero passes uncontested; supply exact rolls for goblin's melee attack and execute.
            Assert.True(session.GuardOverwatch().Success);
            session.EndTurn();
            Assert.Equal("m1", session.CurrentActorId);

            roller.Enqueue(15, 4);
            CombatActionResult execRes = session.ExecuteMonsterAiTurn();
            Assert.True(execRes.Success);
            Assert.Equal(prediction.PlannedMoveCoord, goblin.Position);
            Assert.Contains(session.Log, e => e.ActorId == "m1" && e.ActionType == "MeleeAttack" && e.TargetIds.Count == 1 && e.TargetIds[0] == targetId);
        }

        // =====================================================================
        // 9. P1.4 Sequential Step Movement & Zero-GC A* Buffers
        // =====================================================================

        [Fact]
        public void P14_Exploration_PlanPathAndStepOneTile_AdvanceIncrementallyConsumeTorchAndHaltOnEvents()
        {
            int width = 12;
            int height = 6;
            int[] tiles = new int[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 1;
            }

            MapPointOfInterest poi = new MapPointOfInterest("poi_inn", "inn", 3, 2, "Sleepy Inn");
            MapPortal portal = new MapPortal("portal_stairs", 5, 2, "dungeon_1", 1, 1, "East", "Stairs");
            EncounterZoneData zone = new EncounterZoneData(0, top: 2, left: 7, bottom: 2, right: 8, percent: 10000, battleIds: new[] { 99 });

            TileMapData mainMap = new TileMapData(
                "land_0", "Bywater", 0, false, width, height, tiles, new GridCoord(1, 2),
                portals: new[] { portal },
                pointsOfInterest: new[] { poi },
                encounterZones: new[] { zone });
            TileMapData destMap = new TileMapData(
                "dungeon_1", "Crypt", 1, true, width, height, tiles, new GridCoord(1, 1));

            PartyMemberState hero = new PartyMemberState(new HeroPresetData("h1", "Kevlar"));
            PartyState party = new PartyState(new[] { hero }, torchTurnsRemaining: 2);

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData>
                {
                    ["land_0"] = mainMap,
                    ["dungeon_1"] = destMap
                },
                "land_0",
                party,
                new DeterministicDiceRoller(7UL));

            PathResult path = session.PlanPath(new GridCoord(7, 2), allowDiagonal: false);
            Assert.True(path.Found);
            Assert.Equal(6, path.Steps.Count);

            // Step 1 -> (2, 2): empty tile, torch 2 -> 1
            ExplorationStepResult s1 = session.StepOneTile(path.Steps[0]);
            Assert.True(s1.Moved);
            Assert.Equal(new GridCoord(2, 2), session.Position);
            Assert.Equal(1, session.TurnCount);
            Assert.Equal(1, party.TorchTurnsRemaining);
            Assert.False(s1.TorchExpiredThisStep);
            Assert.Equal(ExplorationHaltReason.None, s1.HaltReason);

            // Step 2 -> (3, 2): POI tile, torch 1 -> 0 (TorchExpiredThisStep = true), halts on PointOfInterest
            ExplorationStepResult s2 = session.StepOneTile(path.Steps[1]);
            Assert.True(s2.Moved);
            Assert.Equal(new GridCoord(3, 2), session.Position);
            Assert.Equal(2, session.TurnCount);
            Assert.Equal(0, party.TorchTurnsRemaining);
            Assert.True(s2.TorchExpiredThisStep);
            Assert.Equal(ExplorationHaltReason.PointOfInterest, s2.HaltReason);
            Assert.Equal("poi_inn", s2.PointOfInterest!.Id);

            // Step 3 -> (4, 2): empty tile, torch stays 0
            ExplorationStepResult s3 = session.StepOneTile(path.Steps[2]);
            Assert.True(s3.Moved);
            Assert.Equal(3, session.TurnCount);
            Assert.False(s3.TorchExpiredThisStep);
            Assert.Equal(ExplorationHaltReason.None, s3.HaltReason);

            // Step 4 -> (5, 2): Portal tile, halts on Portal at exact tile
            ExplorationStepResult s4 = session.StepOneTile(path.Steps[3]);
            Assert.True(s4.Moved);
            Assert.Equal(new GridCoord(5, 2), session.Position);
            Assert.Equal(ExplorationHaltReason.Portal, s4.HaltReason);
            Assert.Equal("portal_stairs", s4.Portal!.Id);

            // Step 5 -> (6, 2)
            ExplorationStepResult s5 = session.StepOneTile(path.Steps[4]);
            Assert.True(s5.Moved);
            Assert.Equal(ExplorationHaltReason.None, s5.HaltReason);

            // Step 6 -> (7, 2): EncounterZone tile, halts on EncounterZone at exact tile
            ExplorationStepResult s6 = session.StepOneTile(path.Steps[5]);
            Assert.True(s6.Moved);
            Assert.Equal(new GridCoord(7, 2), session.Position);
            Assert.Equal(ExplorationHaltReason.EncounterZone, s6.HaltReason);
            Assert.Equal(99, s6.TriggeredBattleId);
        }

        [Fact]
        public void P14_AStarPathfinder_ReusesBuffersAcrossTwenty90x90SearchesAndMatchesStaticFindPath()
        {
            const int width = 90;
            const int height = 90;
            int[] tiles = new int[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = 61;
            }

            // Add a vertical obstacle wall across x=45, y=5..84
            for (int y = 5; y < 85; y++)
            {
                tiles[(y * width) + 45] = -1092;
            }

            TileMapData map = new TileMapData(
                "land_90",
                "Bywater",
                0,
                isDungeon: false,
                width: width,
                height: height,
                tiles: tiles,
                startSpawn: new GridCoord(2, 2),
                solidNegativeIds: new[] { -1092 });

            AStarPathfinder pathfinder = new AStarPathfinder();
            Assert.Equal(8100, pathfinder.BufferCapacity);
            Assert.Equal(0, pathfinder.SearchCount);

            List<GridCoord> reusableSteps = new List<GridCoord>(256);

            for (int i = 0; i < 20; i++)
            {
                GridCoord start = new GridCoord(2, 10 + (i * 3));
                GridCoord goal = new GridCoord(86, 75 - (i * 3));

                bool found = pathfinder.FindPathSteps(map, start, goal, reusableSteps, out int totalCost);
                PathResult staticResult = AStarPathfinder.FindPath(map, start, goal);

                Assert.Equal(staticResult.Found, found);
                Assert.True(found);
                Assert.Equal(staticResult.TotalCost, totalCost);
                Assert.Equal(staticResult.Steps, reusableSteps);
                Assert.Equal(8100, pathfinder.BufferCapacity);
                Assert.Equal(i + 1, pathfinder.SearchCount);
            }

            Assert.Equal(8100, pathfinder.BufferCapacity);
            Assert.Equal(20, pathfinder.SearchCount);
        }

        [Fact]
        public void P15_PersistenceAccessors_RoundTripConditionEntriesTurnCountClearedEncountersAndPrngState()
        {
            ConditionTracker tracker = new ConditionTracker();
            tracker.Apply(CharConditionId.Poisoned, magnitude: 4, turnsRemaining: 6, baseRecoveryChancePercent: 25, canRecoverBySave: true, escalationStepPercent: 15);
            tracker.TickEndOfTurn(new QueuedDiceRoller(99), RulesetMode.ModernBalanced);

            IReadOnlyList<ConditionTracker.ConditionEntry> exported = tracker.ExportEntries();
            Assert.Single(exported);
            ConditionTracker restored = new ConditionTracker();
            restored.RestoreEntry(
                exported[0].Id,
                exported[0].Magnitude,
                exported[0].TurnsRemaining,
                exported[0].BaseRecoveryChancePercent,
                exported[0].CanRecoverBySave,
                exported[0].EscalationStepPercent,
                exported[0].ElapsedRounds);

            IReadOnlyList<ConditionTracker.ConditionEntry> reExported = restored.ExportEntries();
            Assert.Single(reExported);
            Assert.Equal(CharConditionId.Poisoned, reExported[0].Id);
            Assert.Equal(4, reExported[0].Magnitude);
            Assert.Equal(5, reExported[0].TurnsRemaining);
            Assert.Equal(1, reExported[0].ElapsedRounds);

            DeterministicDiceRoller roller = new DeterministicDiceRoller(123456UL);
            int expectedFirst = roller.NextInt(1, 1000);
            ulong savedState = roller.State;
            int expectedSecond = roller.NextInt(1, 1000);
            DeterministicDiceRoller resumedRoller = new DeterministicDiceRoller(savedState);
            Assert.Equal(expectedSecond, resumedRoller.NextInt(1, 1000));
            Assert.NotEqual(expectedFirst, expectedSecond);

            int[] tiles = new int[36];
            EncounterZoneData zone = new EncounterZoneData(slot: 2, top: 2, left: 2, bottom: 2, right: 2, percent: 100, battleIds: new[] { 7 });
            TileMapData map = new TileMapData("land_0", "Test", 0, false, 6, 6, tiles, new GridCoord(1, 2), encounterZones: new[] { zone });
            PartyState party = new PartyState(new[]
            {
                new PartyMemberState(new HeroPresetData("h1", "Aldric", level: 1, hp: 20, maxHp: 20, sp: 0, maxSp: 0, toHit: 10))
            });

            ExplorationSession session = new ExplorationSession(
                new Dictionary<string, TileMapData> { ["land_0"] = map },
                "land_0",
                party,
                roller: new QueuedDiceRoller(1, 0),
                initialFacing: "East",
                initialTurnCount: 14,
                clearedEncounterIds: new[] { "land_0:zone:2" });

            Assert.Equal("East", session.Facing);
            Assert.Equal(14, session.TurnCount);
            Assert.True(session.IsEncounterCleared("land_0:zone:2"));

            ExplorationStepResult step = session.StepOneTile(new GridCoord(2, 2));
            Assert.True(step.Moved);
            Assert.Equal(ExplorationHaltReason.None, step.HaltReason);
            Assert.Equal(15, session.TurnCount);
        }
    }
}
