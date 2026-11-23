using System;
using RealmzCore.Data;

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

    /// <summary>
    /// Classifies raw Bywater land_0 and dungeon_0 tile IDs and rules into semantic HD-2D terrain kinds.
    /// </summary>
    public static class BywaterTileClassifier
    {
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
