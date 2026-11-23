using System;
using RealmzCore.Data;
using UnityEngine;

namespace Realmz.World
{
    public enum BywaterTileKind
    {
        Grass,
        Cobblestone,
        DirtPath,
        ShallowWater,
        TownBuildingSolid,
        TownBuildingDoor,
        ForestCover,
        WallBlock,
        DungeonFloor,
        DungeonWall,
        DungeonDoor,
        DungeonStairs
    }

    public enum BywaterOutdoorPropKind
    {
        None,
        CottageFacade,
        StoneFence,
        BoulderCliff,
        OakTree,
        PineTree,
        WildflowerBush,
        MarketCrates
    }

    /// <summary>
    /// Classifies raw Bywater land_0 and dungeon_0 tile IDs and rules into semantic HD-2D terrain kinds.
    /// </summary>
    public static class BywaterTileClassifier
    {
        public static int DeterministicTileHash(int x, int y)
        {
            return unchecked(((x * 73856093) ^ (y * 19349663) ^ ((x + y) * 83492791)) & 0x7FFFFFFF);
        }

        public static int GetTileVariantIndex(int x, int y, int variantCount = 3)
        {
            return DeterministicTileHash(x, y) % Mathf.Max(1, variantCount);
        }

        public static BywaterOutdoorPropKind ClassifyOutdoorProp(TileMapData map, int x, int y, out int variantHash)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            variantHash = DeterministicTileHash(x, y);
            if (map.IsDungeon || !map.InBounds(x, y))
            {
                return BywaterOutdoorPropKind.None;
            }

            int rawTile = map.GetTile(x, y);
            BywaterTileKind kind = Classify(map, x, y);
            int decodedId = LandMapData.DecodeTerrainTileIndex(rawTile) + 1;

            if (kind == BywaterTileKind.TownBuildingSolid)
            {
                return BywaterOutdoorPropKind.CottageFacade;
            }

            if (kind == BywaterTileKind.WallBlock)
            {
                if (decodedId >= 181 && decodedId <= 185)
                {
                    return BywaterOutdoorPropKind.CottageFacade;
                }

                if (decodedId >= 94 && decodedId <= 104)
                {
                    return BywaterOutdoorPropKind.StoneFence;
                }

                if (x >= 2 && x <= 24 && y >= 2 && y <= 18 && HasAdjacentTownFeature(map, x, y))
                {
                    return BywaterOutdoorPropKind.CottageFacade;
                }

                if (decodedId == 61)
                {
                    return (variantHash % 3) != 0
                        ? BywaterOutdoorPropKind.PineTree
                        : BywaterOutdoorPropKind.BoulderCliff;
                }

                return BywaterOutdoorPropKind.BoulderCliff;
            }

            if (kind == BywaterTileKind.ForestCover)
            {
                if (CountForestNeighbors(map, x, y) < 3)
                {
                    return BywaterOutdoorPropKind.WildflowerBush;
                }

                return (variantHash & 1) == 0
                    ? BywaterOutdoorPropKind.OakTree
                    : BywaterOutdoorPropKind.PineTree;
            }

            if (kind == BywaterTileKind.Grass)
            {
                if ((variantHash % 9) == 0)
                {
                    return BywaterOutdoorPropKind.WildflowerBush;
                }

                return BywaterOutdoorPropKind.None;
            }

            if (kind == BywaterTileKind.Cobblestone)
            {
                if (y > 0)
                {
                    BywaterTileKind northKind = Classify(map, x, y - 1);
                    if ((northKind == BywaterTileKind.TownBuildingSolid
                         || northKind == BywaterTileKind.TownBuildingDoor)
                        && (variantHash % 7) == 0)
                    {
                        return BywaterOutdoorPropKind.MarketCrates;
                    }
                }

                return BywaterOutdoorPropKind.None;
            }

            return BywaterOutdoorPropKind.None;
        }

        private static int CountForestNeighbors(TileMapData map, int x, int y)
        {
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;
                    if (map.InBounds(nx, ny) && Classify(map, nx, ny) == BywaterTileKind.ForestCover)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool HasAdjacentTownFeature(TileMapData map, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0))
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;
                    if (!map.InBounds(nx, ny))
                    {
                        continue;
                    }

                    BywaterTileKind neighbor = Classify(map, nx, ny);
                    if (neighbor == BywaterTileKind.Cobblestone
                        || neighbor == BywaterTileKind.TownBuildingDoor
                        || neighbor == BywaterTileKind.TownBuildingSolid)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static BywaterTileKind Classify(TileMapData map, int x, int y)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (!map.InBounds(x, y))
            {
                return map.IsDungeon ? BywaterTileKind.DungeonWall : BywaterTileKind.WallBlock;
            }

            int rawTile = map.GetTile(x, y);
            TileRule rule = map.GetTileRule(rawTile);

            if (map.IsDungeon)
            {
                if (map.GetPortalAt(x, y) != null || rawTile == 4105 || rawTile == 4233)
                {
                    return BywaterTileKind.DungeonStairs;
                }

                if (rawTile == 131 || rawTile == 133 || rawTile == 8211 || rawTile == 8213)
                {
                    return BywaterTileKind.DungeonDoor;
                }

                if (!rule.Passable)
                {
                    return BywaterTileKind.DungeonWall;
                }

                return BywaterTileKind.DungeonFloor;
            }

            if (rawTile < 0)
            {
                return !rule.Passable ? BywaterTileKind.TownBuildingSolid : BywaterTileKind.TownBuildingDoor;
            }

            if (rule.NeedsBoat)
            {
                return BywaterTileKind.ShallowWater;
            }

            if (!rule.Passable)
            {
                return BywaterTileKind.WallBlock;
            }

            int decodedId = LandMapData.DecodeTerrainTileIndex(rawTile) + 1;
            if (decodedId >= 132 && decodedId <= 146)
            {
                return BywaterTileKind.Cobblestone;
            }

            if (rule.IsPath)
            {
                return (x <= 25 && y <= 25) ? BywaterTileKind.Cobblestone : BywaterTileKind.DirtPath;
            }

            if (rule.IsForest)
            {
                return BywaterTileKind.ForestCover;
            }

            if (decodedId == 159 || decodedId == 160 || decodedId == 165)
            {
                return BywaterTileKind.DirtPath;
            }

            return BywaterTileKind.Grass;
        }
    }
}
