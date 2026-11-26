using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;
using Xunit.Abstractions;

namespace RealmzCore.Tests
{
    public class TacticalBalanceSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public TacticalBalanceSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string ResolveM1Dir()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "RealmzUnity", "Assets", "StreamingAssets", "bywater_m1");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                string remasterCandidate = Path.Combine(current.FullName, "remaster", "RealmzUnity", "Assets", "StreamingAssets", "bywater_m1");
                if (Directory.Exists(remasterCandidate))
                {
                    return remasterCandidate;
                }

                current = current.Parent;
            }

            return @"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\StreamingAssets\bywater_m1";
        }

        private static BywaterM1Bundle LoadM1WithOverrides()
        {
            string m1Dir = ResolveM1Dir();
            return BywaterJsonLoader.LoadFromDirectory(m1Dir);
        }

        [Fact]
        public void DirectionA_ApCosts_And_TieredMonsterEconomy_EnforceExpectedPacing()
        {
            BywaterM1Bundle bundle = LoadM1WithOverrides();
            PartyState party = PartyState.FromBundle(bundle);

            // Weapon & Spell AP cost tiers in Direction A
            Assert.Equal(3, CombatActionEconomy.LightWeaponApCost);
            Assert.Equal(4, CombatActionEconomy.StandardWeaponApCost);
            Assert.Equal(3, CombatWeaponCosts.GetWeaponApCost(bundle.Items[1]));  // Dagger
            Assert.Equal(3, CombatWeaponCosts.GetWeaponApCost(bundle.Items[75])); // Short Sword
            Assert.Equal(4, CombatWeaponCosts.GetWeaponApCost(bundle.Items[10])); // Longsword
            Assert.Equal(4, CombatWeaponCosts.GetWeaponApCost(bundle.Items[104], isRangedSlot: true)); // Bow
            Assert.Equal(3, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1108])); // Magic Darts
            Assert.Equal(4, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1104])); // Flame Hands
            Assert.Equal(5, CombatWeaponCosts.GetSpellApCost(bundle.Spells[1306])); // Fireball

            // CombatantState.FromHero initializes straight into Split 4 MP + 6 AP (not legacy D&D 0 AP).
            PartyMemberState aldric = party.Members.First(m => m.Name == "Aldric");
            CombatantState cAldric = CombatantState.FromHero(aldric, new GridCoord(1, 4), bundle.Items);
            Assert.True(cAldric.Economy.IsSplitMpApMode);
            Assert.False(cAldric.Economy.IsDnd5eMode);
            Assert.Equal(4, cAldric.Economy.MoveBudget);
            Assert.Equal(6, cAldric.Economy.MaxAp);
            Assert.Equal(6, cAldric.Economy.CurrentAp);

            // Verify tiered Monster MP & AP from balance_overrides.json
            var expectedTiers = new (string id, int mp, int ap)[]
            {
                ("giant_rat", 3, 4),
                ("zombie", 3, 4),
                ("goblin", 4, 4),
                ("giant_spider", 4, 5),
                ("orc_warrior", 4, 6),
                ("giant_frog", 5, 6),
            };

            foreach ((string id, int expectedMp, int expectedAp) in expectedTiers)
            {
                MonsterDefinition def = bundle.Monsters.First(m => m.Id == id);
                CombatantState mob = CombatantState.FromMonster(def, $"test_{id}", new GridCoord(5, 5));
                Assert.Equal(expectedMp, mob.GetBaseMoveBudget());
                Assert.Equal(expectedMp, mob.Economy.MoveBudget);
                Assert.Equal(expectedAp, mob.GetBaseApPerTurn());
                Assert.Equal(expectedAp, mob.Economy.MaxAp);
            }
        }

        [Fact]
        public void AllSixEquipmentSets_ActivateBonusesInRealCombat_AndBountyRewardsDeliverItems()
        {
            BywaterM1Bundle bundle = LoadM1WithOverrides();
            PartyState party = PartyState.FromBundle(bundle);
            void EquipId(PartyMemberState hero, int itemId)
            {
                party.AddItemToStash(itemId, 1, isIdentified: true);
                int heroIdx = party.Members.ToList().IndexOf(hero);
                int stashIdx = party.Stash.ToList().FindIndex(s => s.ItemId == itemId);
                Assert.True(stashIdx >= 0, $"Item {itemId} missing from stash");
                Assert.True(party.EquipFromStash(heroIdx, stashIdx, bundle.Items), $"Failed to equip {itemId} on {hero.Name}");
            }

            // 1. Iron Vanguard 3pc (Aldric starts 1/3 Shield -> add Helm 400 + Boots 455)
            PartyMemberState aldric = party.Members.First(m => m.Name == "Aldric");
            Assert.Equal(1, aldric.GetEquipmentSetProgress(EquipmentSetCatalog.IronVanguardSetId)!.EquippedPieces);
            EquipId(aldric, 400);
            EquipId(aldric, 455);
            Assert.Equal(2, aldric.GetSetAcBonus());
            Assert.Equal(1, aldric.GetSetApBonus());
            Assert.Equal(1, aldric.GetSetGuardApDiscount());

            // 2. Scout's Leather 3pc (Vex starts 1/3 Leather Armor -> add Cap 450 + Shadowstep Boots 902)
            PartyMemberState vex = party.Members.First(m => m.Name == "Vex");
            Assert.Equal(1, vex.GetEquipmentSetProgress(EquipmentSetCatalog.ScoutsLeatherSetId)!.EquippedPieces);
            EquipId(vex, 450);
            EquipId(vex, 902);
            Assert.Equal(2, vex.GetSetFreeMoveTiles());
            Assert.Equal(1, vex.GetSetApBonus());
            Assert.Equal(10, vex.GetSetCritBonusPct());

            // 3. Thornwood Stalker 3pc (Kaelen: 903 Bow + 904 Cloak + 905 Boots -> +1 MP, +1 Range, +1 AP, Ignore Half-Cover)
            PartyMemberState kaelen = party.Members.First(m => m.Name == "Kaelen");
            EquipId(kaelen, 903);
            EquipId(kaelen, 904);
            EquipId(kaelen, 905);
            Assert.Equal(1, kaelen.GetSetRangedRangeBonus());
            Assert.Equal(9, kaelen.GetEffectiveRangedRange(bundle.Items));
            Assert.True(kaelen.GetSetIgnoreHalfCover());
            Assert.Equal(1, kaelen.GetSetApBonus());

            // 4. Bywater Arcanist 3pc (Theron: 906 Staff + 907 Robe + 908 Signet -> -1 SP, +1 MP, +1 AP, +2 Spell Dmg)
            PartyMemberState theron = party.Members.First(m => m.Name == "Theron");
            EquipId(theron, 906);
            EquipId(theron, 907);
            EquipId(theron, 908);
            Assert.Equal(1, theron.GetSetSpCostDiscount());
            Assert.Equal(2, theron.GetSetSpellDamageBonus());
            Assert.Equal(1, theron.GetSetApBonus());

            // 5. Sanctuary Acolyte 3pc (Liora: 909 Mace + 910 Vestments + 911 Amulet -> +2 Healing, +1 AC, +1 AP, Support AP -1)
            PartyMemberState liora = party.Members.First(m => m.Name == "Liora");
            EquipId(liora, 909);
            EquipId(liora, 910);
            EquipId(liora, 911);
            Assert.Equal(2, liora.GetSetHealingBonus());
            Assert.Equal(1, liora.GetSetAcBonus());
            Assert.Equal(1, liora.GetSetSupportSpellApDiscount());
            Assert.Equal(1, liora.GetSetApBonus());

            // 6. Cryptbreaker 3pc (Seraphina: 912 Sword + 913 Buckler + 914 Ring -> +2 AC, +25% vs Undead, +1 AP, +1 MP)
            PartyMemberState seraphina = party.Members.First(m => m.Name == "Seraphina");
            EquipId(seraphina, 912);
            EquipId(seraphina, 913);
            EquipId(seraphina, 914);
            Assert.Equal(25, seraphina.GetSetUndeadDamageBonusPct());
            Assert.Equal(2, seraphina.GetSetAcBonus());
            Assert.Equal(1, seraphina.GetSetFreeMoveTiles());
            Assert.Equal(1, seraphina.GetSetApBonus());

            // Test live combat effects: Ignore Half-Cover, -1 SP & +2 Spell Dmg, -1 Support AP, and +25% vs Undead
            CombatGrid grid = new CombatGrid(12, 10);
            grid.SetCover(4, 5, moveCost: 2, blocksLos: false); // Partial cover between Kaelen (1,5) and Skeleton (7,5)

            CombatantState cKaelen = CombatantState.FromHero(kaelen, new GridCoord(1, 5), bundle.Items);
            CombatantState cTheron = CombatantState.FromHero(theron, new GridCoord(1, 3), bundle.Items);
            CombatantState cLiora = CombatantState.FromHero(liora, new GridCoord(2, 4), bundle.Items);
            CombatantState cSeraphina = CombatantState.FromHero(seraphina, new GridCoord(6, 5), bundle.Items);
            MonsterDefinition skelDef = bundle.Monsters.First(m => m.Id == "skeletal_warrior");
            CombatantState cSkel = CombatantState.FromMonster(skelDef, "skel_1", new GridCoord(7, 5));
            cSkel.Hp = 30;
            cSkel.MaxHp = 30;
            cSkel.Ac = 0;

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                new[] { cKaelen, cTheron, cLiora, cSeraphina, cSkel },
                bundle.Spells,
                bundle.Items,
                roller: new QueuedDiceRoller(95, 4, 4, 2)); // Deterministic high roll -> hits/fails save cleanly

            // Thornwood Stalker: Ignored cover tag on Kaelen vs covered Skeleton
            TacticalAttackPreview rangedPreview = session.PreviewRangedAttack(cKaelen.Id, cSkel.Id);
            Assert.Contains("COVER IGNORED", rangedPreview.ConditionTags);

            // Bywater Arcanist: Magic Darts (1108) costs SpCost - 1 (min 1)
            TacticalSpellPreview dartsPreview = session.PreviewSpell(cTheron.Id, "1108", cSkel.Position);
            Assert.Equal(Math.Max(1, bundle.Spells[1108].SpCost - 1), dartsPreview.SpCost);

            // Sanctuary Acolyte: Heal Small Wounds (2105, base 3 AP -> 2 AP with SupportSpellApDiscount=1)
            TacticalSpellPreview healPreview = session.PreviewSpell(cLiora.Id, "2105", cSeraphina.Position);
            Assert.Equal(2, healPreview.ApCost);

            // Cryptbreaker: Undead bonus tag and damage increase on Seraphina vs Skeleton
            TacticalAttackPreview meleePreview = session.PreviewMeleeAttack(cSeraphina.Id, cSkel.Id);
            Assert.Contains("UNDEAD +25%", meleePreview.ConditionTags);

            // Bounty reward item delivery check
            QuestDefinition frogsBounty = QuestCatalog.GetById("bounty_river_frogs")!;
            int goldBefore = party.Gold;
            string claimMsg = frogsBounty.ApplyRewardsToParty(party, bundle.Items);
            Assert.Equal(goldBefore + frogsBounty.GoldReward, party.Gold);
            Assert.Contains("Stalker Boots", claimMsg);
            Assert.Contains(party.Stash, s => s.ItemId == 905);
        }

        [Fact]
        public void BywaterM1_100BattleAutoSim_MeetsWinRateAndRoundPacingTargets()
        {
            BywaterM1Bundle bundle = LoadM1WithOverrides();
            var encounters = new (string label, CombatArenaArchetype arena, string[] monsterIds)[]
            {
                ("RoadGoblins", CombatArenaArchetype.RoadAmbush, new[] { "goblin", "goblin", "goblin", "goblin_archer", "goblin_archer" }),
                ("RiverFrogs", CombatArenaArchetype.RiverbankCrossing, new[] { "giant_frog", "giant_frog", "goblin", "goblin_archer" }),
                ("HauntedQuarters", CombatArenaArchetype.RuinsOutskirts, new[] { "skeletal_warrior", "skeletal_warrior", "zombie", "zombie" }),
                ("ThornwoodOrcs", CombatArenaArchetype.DeepWoods, new[] { "orc_warrior", "orc_warrior", "orc_archer", "goblin_shaman" })
            };

            const int seedsPerEncounter = 25; // 4 x 25 = 100 battles
            int totalBattles = 0;
            int totalWins = 0;
            int totalRounds = 0;
            double totalHpPctRemaining = 0.0;

            foreach (var enc in encounters)
            {
                int encWins = 0;
                int encRounds = 0;
                for (int s = 0; s < seedsPerEncounter; s++)
                {
                    ulong seed = (ulong)(1000 + totalBattles * 37 + s * 13);
                    DeterministicDiceRoller roller = new DeterministicDiceRoller(seed);
                    PartyState party = PartyState.FromBundle(bundle);
                    CombatGrid grid = new CombatGrid(12, 10);

                    List<CombatantState> units = new List<CombatantState>();
                    GridCoord[] heroSpots =
                    {
                        new GridCoord(2, 4),
                        new GridCoord(2, 5),
                        new GridCoord(1, 3),
                        new GridCoord(1, 6),
                        new GridCoord(0, 4),
                        new GridCoord(0, 5)
                    };
                    for (int h = 0; h < party.Members.Count; h++)
                    {
                        units.Add(CombatantState.FromHero(party.Members[h], heroSpots[h], bundle.Items));
                    }

                    GridCoord[] mobSpots =
                    {
                        new GridCoord(9, 4),
                        new GridCoord(9, 5),
                        new GridCoord(10, 3),
                        new GridCoord(10, 6),
                        new GridCoord(10, 4)
                    };
                    for (int m = 0; m < enc.monsterIds.Length; m++)
                    {
                        MonsterDefinition mDef = bundle.Monsters.First(x => x.Id == enc.monsterIds[m]);
                        units.Add(CombatantState.FromMonster(mDef, $"m_{m}", mobSpots[m], roller));
                    }

                    List<GridCoord> reserved = units.Select(u => u.Position).ToList();
                    EncounterDirector.ApplyArenaArchetype(grid, enc.arena, reserved, roller);

                    TacticalCombatSession session = new TacticalCombatSession(
                        grid,
                        units,
                        bundle.Spells,
                        bundle.Items,
                        roller: roller,
                        party: party);

                    CombatOutcome outcome = session.QuickResolveCombat(maxTurns: 240);
                    totalBattles++;
                    totalRounds += session.RoundNumber;
                    encRounds += session.RoundNumber;

                    if (outcome == CombatOutcome.Victory)
                    {
                        totalWins++;
                        encWins++;
                    }

                    int livingHp = units.Where(u => u.Side == CombatSide.Hero && u.IsAlive).Sum(u => u.Hp);
                    int maxHp = units.Where(u => u.Side == CombatSide.Hero).Sum(u => u.MaxHp);
                    totalHpPctRemaining += maxHp > 0 ? (livingHp * 100.0 / maxHp) : 0.0;
                }

                _output.WriteLine($"{enc.label}: {encWins}/{seedsPerEncounter} wins, avg {(double)encRounds / seedsPerEncounter:F2} rounds");
            }

            double winRate = (double)totalWins / totalBattles;
            double avgRounds = (double)totalRounds / totalBattles;
            double avgHpPct = totalHpPctRemaining / totalBattles;
            _output.WriteLine($"TOTAL (100 battles): WinRate={winRate:P1}, AvgRounds={avgRounds:F2}, AvgPartyHpRemaining={avgHpPct:F1}%");

            Assert.Equal(100, totalBattles);
            Assert.True(winRate >= 0.80, $"Expected starter party win rate >= 80%, got {winRate:P1}");
            Assert.InRange(avgRounds, 1.8, 10.0);
        }
    }
}
