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
    public class BywaterScriptedSimulationTests
    {
        private static string FindBywaterM1Dir()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "extracted", "bywater_m1");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                string remasterCandidate = Path.Combine(current.FullName, "remaster", "extracted", "bywater_m1");
                if (Directory.Exists(remasterCandidate))
                {
                    return remasterCandidate;
                }

                current = current.Parent;
            }

            if (Directory.Exists(@"C:\realmz\remaster\extracted\bywater_m1"))
            {
                return @"C:\realmz\remaster\extracted\bywater_m1";
            }

            throw new DirectoryNotFoundException("Could not locate extracted/bywater_m1.");
        }

        [Fact]
        public void ScriptedBywaterCombat_SeededFullPartyVsMonsters_AchievesVictory()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            PartyState party = PartyState.FromBundle(bundle);
            DeterministicDiceRoller roller = new DeterministicDiceRoller(0xBEEF2026UL);

            CombatGrid grid = new CombatGrid(14, 12);
            grid.SetBlocked(6, 5, blocksLos: true);
            grid.SetCover(7, 4, moveCost: 2, blocksLos: false);

            List<CombatantState> units = new List<CombatantState>();
            for (int i = 0; i < party.Members.Count; i++)
            {
                units.Add(CombatantState.FromHero(
                    party.Members[i],
                    new GridCoord(2, 2 + i),
                    bundle.Items));
            }

            MonsterDefinition frogDef = bundle.Monsters.First(m => m.Id == "giant_frog");
            MonsterDefinition goblinDef = bundle.Monsters.First(m => m.Id == "goblin");
            MonsterDefinition shamanDef = bundle.Monsters.First(m => m.Id == "goblin_shaman");
            MonsterDefinition ratDef = bundle.Monsters.First(m => m.Id == "giant_rat");

            units.Add(CombatantState.FromMonster(frogDef, "mob_frog", new GridCoord(9, 4), roller));
            units.Add(CombatantState.FromMonster(goblinDef, "mob_goblin", new GridCoord(9, 6), roller));
            units.Add(CombatantState.FromMonster(shamanDef, "mob_shaman", new GridCoord(10, 5), roller));
            units.Add(CombatantState.FromMonster(ratDef, "mob_rat", new GridCoord(8, 3), roller));

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                units,
                bundle.Spells,
                bundle.Items,
                roller,
                RulesetMode.ModernBalanced,
                party);

            int safetyTurns = 0;
            while (session.Outcome == CombatOutcome.InProgress && safetyTurns < 120)
            {
                safetyTurns++;
                CombatantState? actor = session.CurrentActor;
                Assert.NotNull(actor);

                if (actor!.Side == CombatSide.Monster)
                {
                    session.ExecuteMonsterAiTurn();
                    continue;
                }

                ExecuteHeroScriptedTurn(session, actor);
            }

            Assert.Equal(CombatOutcome.Victory, session.Outcome);
            Assert.True(session.TotalXpEarned >= frogDef.XpReward + goblinDef.XpReward + shamanDef.XpReward + ratDef.XpReward);
            Assert.True(session.Log.Count >= 4);
            Assert.Contains(session.Combatants, c => c.Side == CombatSide.Hero && c.IsAlive);
            Assert.DoesNotContain(session.Combatants, c => c.Side == CombatSide.Monster && c.IsAlive);
        }

        [Fact]
        public void ScriptedBywaterCombat_OutmatchedHeroesVsMonsters_EndsInDefeat()
        {
            BywaterM1Bundle bundle = BywaterJsonLoader.LoadFromDirectory(FindBywaterM1Dir());
            DeterministicDiceRoller roller = new DeterministicDiceRoller(0xDEAD2026UL);
            CombatGrid grid = new CombatGrid(10, 10);

            // Two wounded level-1 heroes (no spells) holding position against an overwhelming pack.
            HeroPresetData magePreset = bundle.Party.First(h => h.Id == "hero_mage");
            PartyMemberState woundedMage = new PartyMemberState(magePreset, bundle.Items);
            woundedMage.Hp = 4;
            woundedMage.Sp = 0;

            HeroPresetData roguePreset = bundle.Party.First(h => h.Id == "hero_rogue");
            PartyMemberState woundedRogue = new PartyMemberState(roguePreset, bundle.Items);
            woundedRogue.Hp = 5;

            CombatantState h1 = CombatantState.FromHero(woundedMage, new GridCoord(2, 4), bundle.Items);
            CombatantState h2 = CombatantState.FromHero(woundedRogue, new GridCoord(2, 5), bundle.Items);
            h1.Ac = 0;
            h2.Ac = 0;

            MonsterDefinition frogDef = bundle.Monsters.First(m => m.Id == "giant_frog");
            MonsterDefinition zombieDef = bundle.Monsters.First(m => m.Id == "zombie");

            List<CombatantState> units = new List<CombatantState>
            {
                h1,
                h2,
                CombatantState.FromMonster(frogDef, "frog_1", new GridCoord(4, 4), roller),
                CombatantState.FromMonster(frogDef, "frog_2", new GridCoord(4, 5), roller),
                CombatantState.FromMonster(zombieDef, "zombie_1", new GridCoord(5, 4), roller),
                CombatantState.FromMonster(zombieDef, "zombie_2", new GridCoord(5, 5), roller)
            };

            // Give monsters solid hit bonus so defeat resolves briskly and deterministically.
            for (int i = 2; i < units.Count; i++)
            {
                units[i].ToHit = 35;
            }

            TacticalCombatSession session = new TacticalCombatSession(
                grid,
                units,
                bundle.Spells,
                bundle.Items,
                roller,
                RulesetMode.ModernBalanced);

            int safetyTurns = 0;
            while (session.Outcome == CombatOutcome.InProgress && safetyTurns < 80)
            {
                safetyTurns++;
                CombatantState? actor = session.CurrentActor;
                Assert.NotNull(actor);

                if (actor!.Side == CombatSide.Monster)
                {
                    session.ExecuteMonsterAiTurn();
                }
                else
                {
                    if (actor.IsConscious)
                    {
                        session.GuardOverwatch();
                    }

                    session.EndTurn();
                }
            }

            Assert.Equal(CombatOutcome.Defeat, session.Outcome);
            Assert.DoesNotContain(session.Combatants, c => c.Side == CombatSide.Hero && c.IsAlive);
            Assert.Contains(session.Combatants, c => c.Side == CombatSide.Monster && c.IsAlive);
        }

        private static void ExecuteHeroScriptedTurn(TacticalCombatSession session, CombatantState actor)
        {
            if (!actor.IsConscious)
            {
                session.EndTurn();
                return;
            }

            List<CombatantState> enemies = session.Combatants
                .Where(c => c.Side == CombatSide.Monster && c.IsAlive)
                .ToList();

            if (enemies.Count == 0)
            {
                session.EndTurn();
                return;
            }

            // 1. Cast offensive spell if hero has SP and known damage spell.
            if (actor.SpellIds.Count > 0 && actor.Sp > 0)
            {
                for (int i = 0; i < actor.SpellIds.Count; i++)
                {
                    int spellId = actor.SpellIds[i];
                    if (!session.Spells.TryGetValue(spellId, out SpellDefinition? spell) ||
                        !string.Equals(spell.EffectKind, "Damage", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    for (int e = 0; e < enemies.Count; e++)
                    {
                        TacticalSpellPreview preview = session.PreviewSpell(actor.Id, spellId.ToString(), enemies[e].Position);
                        if (preview.CanExecute && preview.AoeFootprint.AffectedAllyIds.Count == 0)
                        {
                            session.CastSpell(spellId.ToString(), enemies[e].Position);
                            session.EndTurn();
                            return;
                        }
                    }
                }
            }

            // 2. Try melee attack if already adjacent.
            CombatantState? adj = enemies.FirstOrDefault(e => actor.Position.ChebyshevDistance(e.Position) == 1);
            if (adj != null)
            {
                session.ExecuteMeleeAttack(adj.Id);
                session.EndTurn();
                return;
            }

            // 3. Try ranged attack if equipped and clear/partial LOS.
            if (actor.RangedRange > 1 && !string.IsNullOrWhiteSpace(actor.RangedDice))
            {
                for (int e = 0; e < enemies.Count; e++)
                {
                    if (session.PreviewRangedAttack(actor.Id, enemies[e].Id).CanExecute)
                    {
                        session.ExecuteRangedAttack(enemies[e].Id);
                        session.EndTurn();
                        return;
                    }
                }
            }

            // 4. Advance toward nearest enemy via GetReachableTiles.
            IReadOnlyList<ReachableTileInfo> reachable = session.GetReachableTiles(actor.Id);
            ReachableTileInfo? bestTile = null;
            int bestScore = int.MinValue;

            for (int r = 0; r < reachable.Count; r++)
            {
                ReachableTileInfo tile = reachable[r];
                int minEnemyDist = int.MaxValue;
                bool adjacentToEnemy = false;
                for (int e = 0; e < enemies.Count; e++)
                {
                    int d = tile.Coord.ChebyshevDistance(enemies[e].Position);
                    if (d < minEnemyDist)
                    {
                        minEnemyDist = d;
                    }

                    if (d == 1)
                    {
                        adjacentToEnemy = true;
                    }
                }

                int score = (adjacentToEnemy ? 200 : 0) - (minEnemyDist * 10) - tile.PathCost;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            if (bestTile != null)
            {
                session.TryMoveTo(bestTile.Coord);
            }

            if (session.Outcome == CombatOutcome.InProgress && actor.IsConscious)
            {
                adj = enemies.FirstOrDefault(e => e.IsAlive && actor.Position.ChebyshevDistance(e.Position) == 1);
                if (adj != null)
                {
                    session.ExecuteMeleeAttack(adj.Id);
                }
                else
                {
                    session.GuardOverwatch();
                }
            }

            session.EndTurn();
        }
    }
}
