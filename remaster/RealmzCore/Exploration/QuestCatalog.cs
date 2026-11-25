using System;
using System.Collections.Generic;
using RealmzCore.Data;

namespace RealmzCore.Exploration
{
    public sealed class QuestDefinition
    {
        public QuestDefinition(
            string questId,
            string title,
            string source,
            string description,
            string targetMonsterId,
            int targetCount = 1,
            int goldReward = 0,
            int xpReward = 0,
            string locationHint = "",
            bool isNoticeBoardBounty = false)
        {
            QuestId = questId ?? string.Empty;
            Title = title ?? string.Empty;
            Source = source ?? string.Empty;
            Description = description ?? string.Empty;
            TargetMonsterId = targetMonsterId ?? string.Empty;
            TargetCount = Math.Max(1, targetCount);
            GoldReward = Math.Max(0, goldReward);
            XpReward = Math.Max(0, xpReward);
            LocationHint = locationHint ?? string.Empty;
            IsNoticeBoardBounty = isNoticeBoardBounty;
        }

        public string QuestId { get; }
        public string Title { get; }
        public string Source { get; }
        public string Description { get; }
        public string TargetMonsterId { get; }
        public int TargetCount { get; }
        public int GoldReward { get; }
        public int XpReward { get; }
        public string LocationHint { get; }
        public bool IsNoticeBoardBounty { get; }
    }

    public static class QuestCatalog
    {
        public static readonly QuestDefinition RiverbankFrogsBounty = new QuestDefinition(
            questId: "bounty_river_frogs",
            title: "Clear the Riverbed Frogs",
            source: "BywaterNoticeBoard",
            description: "Giant frogs along the Bywater riverbanks have been ambushing ferrymen and pulling livestock into the reeds. Thin their numbers before dusk.",
            targetMonsterId: "giant_frog",
            targetCount: 4,
            goldReward: 120,
            xpReward: 150,
            locationHint: "Bywater Riverbanks (East & South Meadows)",
            isNoticeBoardBounty: true);

        public static readonly QuestDefinition GoblinRaidersBounty = new QuestDefinition(
            questId: "bounty_road_goblins",
            title: "Break the Road Goblin Ambush",
            source: "BywaterNoticeBoard",
            description: "Goblin raiders have been harassing merchant wagons along the trade road outside Bywater Gate. Drive them off the road.",
            targetMonsterId: "goblin",
            targetCount: 6,
            goldReward: 140,
            xpReward: 180,
            locationHint: "Bywater Trade Road & Foothills",
            isNoticeBoardBounty: true);

        public static readonly QuestDefinition CryptBonesBounty = new QuestDefinition(
            questId: "bounty_cavern_undead",
            title: "Purge the Cavern Undead",
            source: "BywaterNoticeBoard",
            description: "Restless skeletal warriors patrol the upper chambers of the Bywater Cavern. Put them back to rest and report to the Magistrate.",
            targetMonsterId: "skeletal_warrior",
            targetCount: 5,
            goldReward: 200,
            xpReward: 250,
            locationHint: "Bywater Cavern (Dungeon Level 0)",
            isNoticeBoardBounty: true);

        public static readonly QuestDefinition OrcVanguardBounty = new QuestDefinition(
            questId: "bounty_thornwood_orcs",
            title: "Push Back the Thornwood Orcs",
            source: "BywaterNoticeBoard",
            description: "Heavy orc warriors have set up camp in the Thornwood approaches. Break their warband and claim the magistrate's bounty.",
            targetMonsterId: "orc_warrior",
            targetCount: 3,
            goldReward: 180,
            xpReward: 220,
            locationHint: "Thornwood Forest & Outlying Woods",
            isNoticeBoardBounty: true);

        public static readonly IReadOnlyList<QuestDefinition> BywaterNoticeBoardBounties = new[]
        {
            RiverbankFrogsBounty,
            GoblinRaidersBounty,
            CryptBonesBounty,
            OrcVanguardBounty
        };

        public static readonly IReadOnlyList<QuestDefinition> AllDefaultQuests = new[]
        {
            RiverbankFrogsBounty,
            GoblinRaidersBounty,
            CryptBonesBounty,
            OrcVanguardBounty
        };

        public static QuestDefinition? GetById(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                return null;
            }

            for (int i = 0; i < AllDefaultQuests.Count; i++)
            {
                if (string.Equals(AllDefaultQuests[i].QuestId, questId, StringComparison.OrdinalIgnoreCase))
                {
                    return AllDefaultQuests[i];
                }
            }

            return null;
        }

        public static IReadOnlyList<QuestDefinition> GetNoticeBoardBounties(BalanceOverrides? overrides = null)
        {
            if (overrides != null && overrides.Quests.Count > 0)
            {
                List<QuestDefinition> bounties = new List<QuestDefinition>();
                for (int i = 0; i < overrides.Quests.Count; i++)
                {
                    if (overrides.Quests[i].IsNoticeBoardBounty)
                    {
                        bounties.Add(overrides.Quests[i]);
                    }
                }

                if (bounties.Count > 0)
                {
                    return bounties;
                }
            }

            return BywaterNoticeBoardBounties;
        }
    }
}
