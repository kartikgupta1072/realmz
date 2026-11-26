using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealmzCore.Combat;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using Xunit;

namespace RealmzCore.Tests
{
    public class W249ProficiencyDarknessLootTests
    {
        private static BywaterM1Bundle LoadBundle()
        {
            string bundleDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "RealmzUnity", "Assets", "StreamingAssets", "bywater_m1"));
            return BywaterJsonLoader.LoadFromDirectory(bundleDir);
        }

        [Fact]
        public void ClassProficiencies_EnforceAuthenticRestrictions_AndRoleWeightedUpgrades()
        {
            BywaterM1Bundle bundle = LoadBundle();
            PartyState party = PartyState.FromBundle(bundle);
            IReadOnlyDictionary<int, ItemDefinition> items = bundle.Items;

            PartyMemberState aldric = party.Members[0];   // Knight
            PartyMemberState liora = party.Members[1];    // Cleric
            PartyMemberState kaelen = party.Members[2];   // Ranger
            PartyMemberState theron = party.Members[3];   // Mage
            PartyMemberState seraphina = party.Members[4]; // Paladin
            PartyMemberState vex = party.Members[5];      // Rogue

            // Weapons: Longsword(10), Mace(37), Short Sword(75), Quarter Staff(98), Bow(104), Dagger(1)
            Assert.True(PartyState.CanHeroEquip(aldric, items[10], out _));
            Assert.True(PartyState.CanHeroEquip(seraphina, items[10], out _));
            Assert.False(PartyState.CanHeroEquip(theron, items[10], out string mageSwordReason));
            Assert.Contains("Mage", mageSwordReason, StringComparison.OrdinalIgnoreCase);
            Assert.False(PartyState.CanHeroEquip(liora, items[10], out string clericSwordReason));
            Assert.Contains("blunt", clericSwordReason, StringComparison.OrdinalIgnoreCase);

            // Cleric strictly barred from edged daggers/swords & bows; allowed blunt & staves
            Assert.False(PartyState.CanHeroEquip(liora, items[1], out _));   // Dagger
            Assert.False(PartyState.CanHeroEquip(liora, items[75], out _));  // Short Sword
            Assert.False(PartyState.CanHeroEquip(liora, items[104], out _)); // Bow
            Assert.True(PartyState.CanHeroEquip(liora, items[37], out _));   // Mace
            Assert.True(PartyState.CanHeroEquip(liora, items[59], out _));   // Morning Star
            Assert.True(PartyState.CanHeroEquip(liora, items[98], out _));   // Quarter Staff

            // Quarter Staff reserved for Mage/Cleric (never martial Knight/Paladin/Ranger/Rogue)
            Assert.True(PartyState.CanHeroEquip(theron, items[98], out _));
            Assert.False(PartyState.CanHeroEquip(aldric, items[98], out _));
            Assert.False(PartyState.CanHeroEquip(kaelen, items[98], out _));
            Assert.False(PartyState.CanHeroEquip(vex, items[98], out _));

            // Armor, Shield(418), Helm(400), Leather Armor(209), Chain(215), Banded(219), Robe(200)
            Assert.False(PartyState.CanHeroEquip(theron, items[418], out _)); // Mage cannot equip Shield
            Assert.False(PartyState.CanHeroEquip(theron, items[400], out _)); // Mage cannot equip Helm
            Assert.False(PartyState.CanHeroEquip(theron, items[209], out _)); // Mage cannot equip Leather Armor
            Assert.True(PartyState.CanHeroEquip(theron, items[200], out _));  // Robe
            Assert.False(PartyState.CanHeroEquip(kaelen, items[418], out _)); // Ranger no Shield
            Assert.False(PartyState.CanHeroEquip(kaelen, items[215], out _)); // Ranger no Chain
            Assert.False(PartyState.CanHeroEquip(vex, items[418], out _));    // Rogue no Shield
            Assert.False(PartyState.CanHeroEquip(vex, items[219], out _));    // Rogue no Banded
            Assert.False(PartyState.CanHeroEquip(liora, items[219], out _));  // Cleric no Banded
            Assert.True(PartyState.CanHeroEquip(aldric, items[219], out _));  // Knight Banded Armor OK

            // EquipFromStash enforces proficiency & returns restriction reason
            party.AddItemToStash(10, 1); // Longsword at index 0
            int stashLongswordIdx = party.Stash.Count - 1;
            Assert.False(party.EquipFromStash(3, stashLongswordIdx, items, null, out string failMsg));
            Assert.False(string.IsNullOrWhiteSpace(failMsg));
            Assert.True(party.EquipFromStash(0, stashLongswordIdx, items, null, out _));
            Assert.Equal(10, aldric.GetEquipped(EquipmentSlot.Weapon)!.ItemId);

            // FindBestUpgradeRecipient never tags incompatible heroes
            // All shield-users (Aldric, Liora, Seraphina) already start with Shield(418), and Ranger/Mage/Rogue cannot use shields -> null!
            Assert.Null(PartyState.FindBestUpgradeRecipient(party, items[418], items));
            // Same for Helm(400): Aldric/Liora/Seraphina already wear Helm(400) and Ranger/Mage/Rogue cannot wear heavy metal Helm -> null!
            Assert.Null(PartyState.FindBestUpgradeRecipient(party, items[400], items));

            // Bow +1 (108) targets Kaelen cleanly
            HeroUpgradeRecommendation? bowRec = PartyState.FindBestUpgradeRecipient(party, items[108], items);
            Assert.NotNull(bowRec);
            Assert.Equal(kaelen.Id, bowRec!.Hero.Id);

            // Quarter Staff +1 (99) targets Mage (Theron) over Cleric/martial
            HeroUpgradeRecommendation? staffRec = PartyState.FindBestUpgradeRecipient(party, items[99], items);
            Assert.NotNull(staffRec);
            Assert.Equal(theron.Id, staffRec!.Hero.Id);

            // Banded Armor (219) targets heavy frontline upgrade (Aldric wears Leather +9 -> +21 AC vs Seraphina Chain +15)
            HeroUpgradeRecommendation? bandedRec = PartyState.FindBestUpgradeRecipient(party, items[219], items);
            Assert.NotNull(bandedRec);
            Assert.True(bandedRec!.Hero.Id == aldric.Id || bandedRec.Hero.Id == seraphina.Id);

            // Speckled Luck Stone +2 (622) role-weights Rogue (Vex) cleanly
            HeroUpgradeRecommendation? luckRec = PartyState.FindBestUpgradeRecipient(party, items[622], items);
            Assert.NotNull(luckRec);
            Assert.Equal(vex.Id, luckRec!.Hero.Id);
        }

        [Fact]
        public void SelectiveVictoryLoot_DepositsOnlyCheckedStacks_AndIsIdempotent()
        {
            BywaterM1Bundle bundle = LoadBundle();
            PartyState party = PartyState.FromBundle(bundle);
            int initialStashCount = party.Stash.Sum(s => s.Quantity);

            MonsterDefinition foe = new MonsterDefinition(1, "gob", "Goblin Raider", hpMin: 4, hpMax: 4, hpDefault: 4, ac: 0, dexterity: 2, xpReward: 60, goldMin: 24, goldMax: 24, lootItemIds: new[] { 10, 418, 622 });
            CombatantState hero = CombatantState.FromHero(party.Members[0], new GridCoord(2, 2), bundle.Items);
            hero.ToHit = 95;
            hero.WeaponDice = "2d6+8";
            CombatantState mob = CombatantState.FromMonster(foe, "m1", new GridCoord(3, 2));

            TacticalCombatSession session = new TacticalCombatSession(
                new CombatGrid(8, 8),
                new[] { hero, mob },
                spells: bundle.Spells,
                items: bundle.Items,
                roller: new QueuedDiceRoller(20, 4, 4),
                party: party,
                deferLootDeposit: true);

            CombatActionResult atk = session.ExecuteMeleeAttack("m1");
            Assert.True(atk.Success);
            Assert.Equal(CombatOutcome.Victory, session.Outcome);
            Assert.True(session.HasPendingLootDeposit);
            Assert.NotNull(session.LastVictorySummary);
            Assert.Equal(3, session.LastVictorySummary!.LootStacks.Count);

            // Nothing added to Stash until player clicks Take Selected
            Assert.Equal(initialStashCount, party.Stash.Sum(s => s.Quantity));

            // Select only indices 0 (Longsword) and 2 (Speckled Luck Stone +2), skip Shield (index 1)
            int taken = session.TakeLootItems(party, new[] { 0, 2 });
            Assert.Equal(2, taken);
            Assert.False(session.HasPendingLootDeposit);
            Assert.Equal(initialStashCount + 2, party.Stash.Sum(s => s.Quantity));
            Assert.Contains(party.Stash, s => s.ItemId == 622);
            Assert.DoesNotContain(party.Stash, s => s.ItemId == 418 && s.Quantity > 0);

            // Second call is completely idempotent
            Assert.Equal(0, session.TakeLootItems(party, new[] { 0, 1, 2 }));
        }

        [Fact]
        public void NightCombatDarkness_AppliesRangedPenalty_AndClearsOnTorchOrFireSpell()
        {
            BywaterM1Bundle bundle = LoadBundle();
            PartyState party = PartyState.FromBundle(bundle);
            party.TorchTurnsRemaining = 0;
            party.AddItemToStash(805, 1, isIdentified: true, charges: 6);

            PartyMemberState mage = party.Members[3];   // Theron with Flame Hands (Heat spell)
            PartyMemberState ranger = party.Members[2]; // Kaelen with Bow
            CombatantState cRanger = CombatantState.FromHero(ranger, new GridCoord(1, 3), bundle.Items);
            CombatantState cMage = CombatantState.FromHero(mage, new GridCoord(1, 4), bundle.Items);
            CombatantState cGob = CombatantState.FromMonster(bundle.Monsters[0], "m1", new GridCoord(5, 3));
            cGob.MovementMax = 0;

            // Session 1: Night combat with no torch -> In the Dark -15% Ranged -> cleared by lighting Torch
            TacticalCombatSession session1 = new TacticalCombatSession(
                new CombatGrid(10, 8),
                new[] { cRanger, cGob },
                spells: bundle.Spells,
                items: bundle.Items,
                roller: new DeterministicDiceRoller(12UL),
                party: party,
                isAmbientDarkness: true);

            Assert.True(session1.IsAmbientDarkness);
            Assert.True(session1.IsInTheDark);
            TacticalAttackPreview darkPreview = session1.PreviewRangedAttack(cRanger.Id, "m1");
            Assert.Contains(darkPreview.ConditionTags, t => t.Contains("In the Dark", StringComparison.OrdinalIgnoreCase));

            int fireCalls = 0;
            session1.OnArenaIlluminationChanged += lit => { if (lit) fireCalls++; };
            CombatActionResult torchRes = session1.TryLightTorchInCombat(cRanger.Id);
            Assert.True(torchRes.Success);
            Assert.Equal(1, fireCalls);
            Assert.False(session1.IsInTheDark);
            Assert.Equal(PartyState.DefaultTorchDurationMinutes, party.TorchTurnsRemaining);

            TacticalAttackPreview litPreview = session1.PreviewRangedAttack(cRanger.Id, "m1");
            Assert.Equal(darkPreview.HitChancePct + TacticalCombatSession.DarknessRangedHitPenaltyPct, litPreview.HitChancePct);
            Assert.DoesNotContain(litPreview.ConditionTags, t => t.Contains("In the Dark", StringComparison.OrdinalIgnoreCase));

            // Session 2: Night combat with no torch -> cleared by casting a Fire/Heat spell (Flame Hands / Fireball)
            party.TorchTurnsRemaining = 0;
            CombatantState mMage = CombatantState.FromHero(mage, new GridCoord(1, 4), bundle.Items);
            CombatantState mRanger = CombatantState.FromHero(ranger, new GridCoord(1, 3), bundle.Items);
            CombatantState mGob = CombatantState.FromMonster(bundle.Monsters[0], "m2", new GridCoord(3, 4));
            mMage.Dexterity = 50;
            mRanger.Dexterity = 30;
            mGob.Dexterity = 1;

            TacticalCombatSession session2 = new TacticalCombatSession(
                new CombatGrid(10, 8),
                new[] { mMage, mRanger, mGob },
                spells: bundle.Spells,
                items: bundle.Items,
                roller: new DeterministicDiceRoller(15UL),
                party: party,
                isAmbientDarkness: true);

            Assert.True(session2.IsInTheDark);
            Assert.Equal(mMage.Id, session2.CurrentActorId);

            // Pick a Heat/Fire spell known by Theron (e.g., Flame Hands 1108 or Fireball 1110)
            SpellDefinition fireSpell = bundle.Spells.Values.First(s =>
                string.Equals(s.DamageType, "Heat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.DamageType, "Fire", StringComparison.OrdinalIgnoreCase));
            CombatActionResult castRes = session2.CastSpell(fireSpell.Key, new GridCoord(3, 4));
            Assert.True(castRes.Success);
            Assert.True(session2.IsCombatFireIlluminated);
            Assert.False(session2.IsInTheDark);
        }
    }
}
