using System;
using System.Collections.Generic;
using Realmz.World;
using RealmzCore.Data;
using UnityEngine;

namespace Realmz.Cartography
{
    public enum CartographyTerrainLayer
    {
        MeadowGrass = 0,
        MossPatch = 1,
        DirtRoad = 2,
        CobblePlaza = 3,
        RiverMud = 4,
        CliffRock = 5
    }

    public enum CartographyPropKind
    {
        None = 0,
        BywaterInn = 1,
        StoneBridge = 2,
        StoneFenceH = 3,
        StoneFenceV = 4,
        Knight = 5,
        GiantFrog = 6,
        TreeOakA = 7,
        TreeOakB = 8,
        TreePineA = 9,
        TreePineB = 10,
        TreeWillowA = 11,
        TorchBrazier = 12,
        MarketCrates = 13
    }

    public enum CartographyBiomeZone
    {
        Meadow = 0,
        SunlitWildflower = 1,
        RiverbankReed = 2,
        ForestShade = 3,
        Blocked = 4
    }

    [Serializable]
    public struct CartographyTile
    {
        public int X;
        public int Y;
        public float Elevation;
        public float MossWeight;
        public float DirtWeight;
        public float CobbleWeight;
        public float MudWeight;
        public float CreaseAO;
        public bool BlocksFoliage;
        public bool IsWater;
        public CartographyBiomeZone BiomeZone;
    }

    [Serializable]
    public struct CartographyPropPlacement
    {
        public CartographyPropKind Kind;
        public Vector3 WorldPosition;
        public Vector2 SizeMeters;
        public bool CastsCanopyShadow;
        public float CanopyRadius;
        public float CanopyStrength;
    }

    [Serializable]
    public struct CartographyLanternSource
    {
        public Vector2 WorldXZ;
        public float Radius;
        public float Intensity;
    }

    /// <summary>
    /// Pure C# data layout for both the 18x18 Bywater proof diorama and arbitrary 90x90 Realmz maps.
    /// Canonical coordinates map tile (x, y) with x in [0, Width-1] (West->East +X) and
    /// y in [0, Height-1] (North 0 -> South Height-1) to world XZ [x..x+1, -(y+1)..-y],
    /// centered at (x + 0.5, elevation, -(y + 0.5)).
    /// </summary>
    public sealed class CartographyWorldData
    {
        public const int GridWidth = 18;
        public const int GridHeight = 18;

        public const float RiverElevation = -0.60f;
        public const float WaterSurfaceElevation = -0.22f;
        public const float TileMapWaterSurfaceElevation = -0.18f;
        public const float MudElevation = -0.05f;
        public const float PlazaElevation = 0.00f;
        public const float CobbleElevation = 0.04f;
        public const float BridgeElevation = 0.10f;
        public const float HighlandElevation = 0.55f;
        public const float RidgeElevation = 0.75f;
        public const float CliffLowElevation = 1.15f;
        public const float CliffHighElevation = 1.65f;
        public const float DungeonDaisElevation = 0.34f;
        public const float DungeonWallElevation = 0.52f;
        public const float DungeonWaterElevation = -0.55f;

        private readonly CartographyTile[,] _tiles;
        private readonly List<CartographyPropPlacement> _props = new List<CartographyPropPlacement>(128);
        private readonly List<CartographyLanternSource> _lanterns = new List<CartographyLanternSource>(32);

        public int Width { get; }
        public int Height { get; }
        public bool IsDungeon { get; }
        public float WaterSurfaceHeight { get; set; } = WaterSurfaceElevation;

        public IReadOnlyList<CartographyPropPlacement> Props => _props;
        public IReadOnlyList<CartographyLanternSource> Lanterns => _lanterns;

        public CartographyWorldData()
            : this(GridWidth, GridHeight, false)
        {
        }

        public CartographyWorldData(int width, int height, bool isDungeon = false)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            IsDungeon = isDungeon;
            _tiles = new CartographyTile[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    _tiles[x, y] = new CartographyTile
                    {
                        X = x,
                        Y = y,
                        Elevation = PlazaElevation,
                        CreaseAO = 1.0f,
                        BiomeZone = isDungeon ? CartographyBiomeZone.Blocked : CartographyBiomeZone.Meadow,
                        BlocksFoliage = isDungeon
                    };
                }
            }
        }

        public static Vector3 GridToWorld(int x, int y, float elevation = 0f)
        {
            return new Vector3(x + 0.5f, elevation, -(y + 0.5f));
        }

        public static Vector3 GridToWorld(GridCoord coord, float elevation = 0f)
        {
            return GridToWorld(coord.X, coord.Y, elevation);
        }

        public static void WorldToGrid(Vector3 worldPos, out int x, out int y)
        {
            x = Mathf.FloorToInt(worldPos.x);
            y = Mathf.FloorToInt(-worldPos.z);
        }

        public static GridCoord ToGridCoord(Vector3 worldPos)
        {
            WorldToGrid(worldPos, out int x, out int y);
            return new GridCoord(x, y);
        }

        public CartographyTile GetTile(int x, int y)
        {
            int cx = Mathf.Clamp(x, 0, Width - 1);
            int cy = Mathf.Clamp(y, 0, Height - 1);
            return _tiles[cx, cy];
        }

        public void SetTile(int x, int y, CartographyTile tile)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            tile.X = x;
            tile.Y = y;
            _tiles[x, y] = tile;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool IsInBounds(int x, int y)
        {
            return InBounds(x, y);
        }

        public float GetElevation(int x, int y)
        {
            return GetTile(x, y).Elevation;
        }

        public float GetInterpolatedElevation(float worldX, float worldZ)
        {
            WorldToGrid(new Vector3(worldX, 0f, worldZ), out int curX, out int curY);
            float curElev = GetElevation(curX, curY);

            float sampleX = worldX - 0.5f;
            float sampleY = -worldZ - 0.5f;
            int x0 = Mathf.FloorToInt(sampleX);
            int y0 = Mathf.FloorToInt(sampleY);
            float fx = Mathf.Clamp01(sampleX - x0);
            float fy = Mathf.Clamp01(sampleY - y0);

            float ResolveSample(int sx, int sy)
            {
                float e = GetElevation(sx, sy);
                // Keep stepped cliff walls crisp instead of floating/sinking across sharp drops.
                return Mathf.Abs(e - curElev) <= 0.35f ? e : curElev;
            }

            float e00 = ResolveSample(x0, y0);
            float e10 = ResolveSample(x0 + 1, y0);
            float e01 = ResolveSample(x0, y0 + 1);
            float e11 = ResolveSample(x0 + 1, y0 + 1);

            float top = Mathf.Lerp(e00, e10, fx);
            float bot = Mathf.Lerp(e01, e11, fx);
            return Mathf.Lerp(top, bot, fy);
        }

        public static CartographyWorldData BuildBywaterProofLayout()
        {
            CartographyWorldData data = new CartographyWorldData(GridWidth, GridHeight, false)
            {
                WaterSurfaceHeight = WaterSurfaceElevation
            };
            data.PopulateBywaterProofLayout();
            return data;
        }

        public static CartographyWorldData BuildFromTileMap(TileMapData map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            int w = map.Width;
            int h = map.Height;
            bool isDungeon = map.IsDungeon;
            CartographyWorldData data = new CartographyWorldData(w, h, isDungeon)
            {
                WaterSurfaceHeight = TileMapWaterSurfaceElevation
            };

            BywaterTileKind[,] kinds = new BywaterTileKind[w, h];
            TileRule[,] rules = new TileRule[w, h];
            bool[,] isWater = new bool[w, h];
            bool[,] isBridge = new bool[w, h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int raw = map.GetTile(x, y);
                    TileRule rule = map.GetTileRule(raw);
                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);
                    kinds[x, y] = kind;
                    rules[x, y] = rule;
                    isWater[x, y] = (kind == BywaterTileKind.ShallowWater || rule.NeedsBoat);
                }
            }

            bool WaterAt(int tx, int ty) => tx >= 0 && tx < w && ty >= 0 && ty < h && isWater[tx, ty];
            bool RoadAt(int tx, int ty)
            {
                if (tx < 0 || tx >= w || ty < 0 || ty >= h || isWater[tx, ty]) return false;
                BywaterTileKind k = kinds[tx, ty];
                return k == BywaterTileKind.Cobblestone || k == BywaterTileKind.DirtPath || rules[tx, ty].IsPath;
            }

            if (!isDungeon)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (isWater[x, y] || !RoadAt(x, y) || !rules[x, y].Passable)
                        {
                            continue;
                        }

                        bool wW = WaterAt(x - 1, y);
                        bool wE = WaterAt(x + 1, y);
                        bool wN = WaterAt(x, y - 1);
                        bool wS = WaterAt(x, y + 1);

                        bool spanX = (wW && wE) ||
                                     (wW && RoadAt(x + 1, y) && WaterAt(x + 2, y)) ||
                                     (wE && RoadAt(x - 1, y) && WaterAt(x - 2, y));
                        bool spanY = (wN && wS) ||
                                     (wN && RoadAt(x, y + 1) && WaterAt(x, y + 2)) ||
                                     (wS && RoadAt(x, y - 1) && WaterAt(x, y - 2));

                        isBridge[x, y] = spanX || spanY;
                    }
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    BywaterTileKind kind = kinds[x, y];
                    TileRule rule = rules[x, y];

                    CartographyTile tile = new CartographyTile
                    {
                        X = x,
                        Y = y,
                        Elevation = PlazaElevation,
                        CreaseAO = 1.0f,
                        BlocksFoliage = isDungeon,
                        IsWater = false,
                        BiomeZone = isDungeon ? CartographyBiomeZone.Blocked : CartographyBiomeZone.Meadow
                    };

                    if (isDungeon)
                    {
                        if (isWater[x, y])
                        {
                            tile.Elevation = DungeonWaterElevation;
                            tile.IsWater = true;
                            tile.BlocksFoliage = true;
                            tile.MudWeight = 0.85f;
                            tile.DirtWeight = 0.15f;
                            tile.CreaseAO = 0.52f;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;
                        }
                        else if (kind == BywaterTileKind.DungeonWall || kind == BywaterTileKind.WallBlock || !rule.Passable)
                        {
                            tile.Elevation = DungeonWallElevation;
                            tile.BlocksFoliage = true;
                            tile.CobbleWeight = 0.72f;
                            tile.DirtWeight = 0.28f;
                            tile.CreaseAO = 0.68f;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;
                        }
                        else if (kind == BywaterTileKind.DungeonStairs || kind == BywaterTileKind.DungeonDoor)
                        {
                            tile.Elevation = PlazaElevation;
                            tile.BlocksFoliage = true;
                            tile.CobbleWeight = 0.86f;
                            tile.DirtWeight = 0.14f;
                            tile.CreaseAO = 0.80f;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;
                        }
                        else
                        {
                            tile.Elevation = PlazaElevation;
                            tile.CobbleWeight = 0.55f;
                            tile.DirtWeight = 0.45f;
                            tile.BlocksFoliage = true;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;

                            if (IsEligibleDungeonDais(map, kinds, isWater, x, y))
                            {
                                tile.Elevation = DungeonDaisElevation;
                                tile.CobbleWeight = 0.88f;
                                tile.DirtWeight = 0.12f;
                                tile.CreaseAO = 0.92f;
                            }

                            if (IsAdjacentToKind(kinds, x, y, w, h, BywaterTileKind.DungeonWall))
                            {
                                tile.CreaseAO = Mathf.Min(tile.CreaseAO, 0.72f);
                            }
                        }

                        data._tiles[x, y] = tile;
                        continue;
                    }

                    // Outdoor land map
                    if (isWater[x, y])
                    {
                        tile.Elevation = RiverElevation;
                        tile.IsWater = true;
                        tile.BlocksFoliage = true;
                        tile.MudWeight = 0.85f;
                        tile.DirtWeight = 0.15f;
                        tile.CreaseAO = 0.52f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (isBridge[x, y])
                    {
                        tile.Elevation = BridgeElevation;
                        tile.CobbleWeight = 0.88f;
                        tile.DirtWeight = 0.12f;
                        tile.BlocksFoliage = true;
                        tile.CreaseAO = 0.90f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (kind == BywaterTileKind.TownBuildingSolid || kind == BywaterTileKind.TownBuildingDoor)
                    {
                        tile.Elevation = PlazaElevation;
                        tile.CobbleWeight = 0.45f;
                        tile.DirtWeight = 0.55f;
                        tile.BlocksFoliage = true;
                        tile.CreaseAO = 0.52f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (kind == BywaterTileKind.Cobblestone)
                    {
                        tile.Elevation = CobbleElevation;
                        tile.CobbleWeight = 0.88f;
                        tile.DirtWeight = 0.12f;
                        tile.BlocksFoliage = true;
                        tile.CreaseAO = 0.94f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (kind == BywaterTileKind.WallBlock)
                    {
                        BywaterOutdoorPropKind wallProp = BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out _);
                        if (wallProp == BywaterOutdoorPropKind.CottageFacade || wallProp == BywaterOutdoorPropKind.StoneFence)
                        {
                            tile.Elevation = PlazaElevation;
                            tile.CobbleWeight = 0.35f;
                            tile.DirtWeight = 0.45f;
                            tile.BlocksFoliage = true;
                            tile.CreaseAO = 0.68f;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;
                        }
                        else
                        {
                            bool interiorWall = IsSurroundedCardinal(kinds, x, y, w, h, BywaterTileKind.WallBlock);
                            bool nearPortal = IsNearPortal(map, x, y, 7);
                            bool isNorthRidge = y < 14;
                            bool isPerimeter = x <= 1 || x >= w - 2 || y <= 1 || y >= h - 2;

                            if (interiorWall && (isNorthRidge || nearPortal || isPerimeter))
                            {
                                tile.Elevation = CliffHighElevation;
                            }
                            else
                            {
                                tile.Elevation = CliffLowElevation;
                            }

                            tile.MossWeight = 0.55f;
                            tile.BlocksFoliage = true;
                            tile.CreaseAO = 0.78f;
                            tile.BiomeZone = CartographyBiomeZone.ForestShade;
                        }
                    }
                    else
                    {
                        bool shoreAdjacent = WaterAt(x - 1, y) || WaterAt(x + 1, y) || WaterAt(x, y - 1) || WaterAt(x, y + 1);
                        if (shoreAdjacent)
                        {
                            tile.Elevation = MudElevation;
                            tile.MudWeight = (kind == BywaterTileKind.DirtPath) ? 0.52f : 0.76f;
                            tile.DirtWeight = (kind == BywaterTileKind.DirtPath) ? 0.48f : 0.12f;
                            tile.MossWeight = (kind == BywaterTileKind.DirtPath) ? 0f : 0.22f;
                            tile.CreaseAO = 0.84f;
                            tile.BlocksFoliage = (kind == BywaterTileKind.DirtPath);
                            tile.BiomeZone = (kind == BywaterTileKind.DirtPath)
                                ? CartographyBiomeZone.Blocked
                                : CartographyBiomeZone.RiverbankReed;
                        }
                        else if (kind == BywaterTileKind.DirtPath)
                        {
                            tile.Elevation = PlazaElevation;
                            tile.DirtWeight = 0.86f;
                            tile.BlocksFoliage = true;
                            tile.CreaseAO = 0.96f;
                            tile.BiomeZone = CartographyBiomeZone.Blocked;
                        }
                        else
                        {
                            // Grass or ForestCover
                            tile.Elevation = IsEligibleOutdoorHighland(map, kinds, isWater, x, y)
                                ? HighlandElevation
                                : PlazaElevation;

                            if (kind == BywaterTileKind.ForestCover)
                            {
                                tile.MossWeight = 0.62f;
                                tile.CreaseAO = 0.84f;
                                tile.BiomeZone = CartographyBiomeZone.ForestShade;
                            }
                            else
                            {
                                int cellHash = DeterministicHash(x / 3, y / 3, 211);
                                tile.BiomeZone = (cellHash % 3) == 0
                                    ? CartographyBiomeZone.SunlitWildflower
                                    : CartographyBiomeZone.Meadow;
                                int hPatch = DeterministicHash(x, y, 31);
                                if ((hPatch % 5) == 0)
                                {
                                    tile.MossWeight = 0.38f + (hPatch % 3) * 0.14f;
                                }
                            }
                        }
                    }

                    data._tiles[x, y] = tile;
                }
            }

            data.PopulatePropsAndLanternsFromTileMap(map, kinds);
            return data;
        }

        private void PopulatePropsAndLanternsFromTileMap(TileMapData map, BywaterTileKind[,] kinds)
        {
            _props.Clear();
            _lanterns.Clear();

            int w = map.Width;
            int h = map.Height;

            if (map.IsDungeon)
            {
                List<Vector2Int> placedSconces = new List<Vector2Int>(64);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (kinds[x, y] != BywaterTileKind.DungeonWall && kinds[x, y] != BywaterTileKind.WallBlock)
                        {
                            continue;
                        }

                        // Only mount sconces on North walls facing South open floor so props never occlude heroes from camera.
                        bool southIsOpenFloor = (y + 1 < h) &&
                            kinds[x, y + 1] != BywaterTileKind.DungeonWall &&
                            kinds[x, y + 1] != BywaterTileKind.WallBlock &&
                            !_tiles[x, y + 1].IsWater;
                        if (!southIsOpenFloor)
                        {
                            continue;
                        }

                        bool nearPortalOrSpawnNorth = false;
                        if (map.StartSpawn.X == x && map.StartSpawn.Y == y + 1)
                        {
                            nearPortalOrSpawnNorth = true;
                        }
                        else
                        {
                            for (int pi = 0; pi < map.Portals.Count; pi++)
                            {
                                MapPortal p = map.Portals[pi];
                                if ((p.X == x || p.X == x - 2 || p.X == x + 2) && p.Y == y + 1)
                                {
                                    nearPortalOrSpawnNorth = true;
                                    break;
                                }
                            }
                        }

                        int hash = DeterministicHash(x, y, 617);
                        if (!nearPortalOrSpawnNorth && (hash % 13) != 0)
                        {
                            continue;
                        }

                        bool tooClose = false;
                        for (int s = 0; s < placedSconces.Count; s++)
                        {
                            int dx = placedSconces[s].x - x;
                            int dy = placedSconces[s].y - y;
                            if (dx * dx + dy * dy < 20)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose)
                        {
                            continue;
                        }

                        placedSconces.Add(new Vector2Int(x, y));
                        Vector3 pos = GridToWorld(x, y + 1, GetElevation(x, y + 1));
                        AddProp(CartographyPropKind.TorchBrazier, pos.x, pos.y + 0.02f, pos.z + 0.34f, 0.54f, 0.78f);
                        _lanterns.Add(new CartographyLanternSource
                        {
                            WorldXZ = new Vector2(pos.x, pos.z + 0.15f),
                            Radius = 3.8f,
                            Intensity = 0.72f
                        });
                    }
                }

                // Ensure start spawn & portals always have warm illumination
                AddUniqueLantern(GridToWorld(map.StartSpawn, 0f), 3.6f, 0.72f);
                for (int i = 0; i < map.Portals.Count; i++)
                {
                    MapPortal p = map.Portals[i];
                    AddUniqueLantern(GridToWorld(p.X, p.Y, 0f), 3.6f, 0.75f);
                }
                return;
            }

            // Outdoor props & lanterns (uncapped)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    BywaterTileKind kind = kinds[x, y];
                    float elev = _tiles[x, y].Elevation;
                    Vector3 pos = GridToWorld(x, y, elev);

                    BywaterOutdoorPropKind prop = BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out int vHash);
                    switch (prop)
                    {
                        case BywaterOutdoorPropKind.CottageFacade:
                            _props.Add(new CartographyPropPlacement
                            {
                                Kind = CartographyPropKind.BywaterInn,
                                WorldPosition = pos,
                                SizeMeters = new Vector2(1.45f, 1.45f),
                                CastsCanopyShadow = true,
                                CanopyRadius = 1.35f,
                                CanopyStrength = 0.42f
                            });
                            if ((vHash % 4) == 0)
                            {
                                _lanterns.Add(new CartographyLanternSource
                                {
                                    WorldXZ = new Vector2(pos.x, pos.z),
                                    Radius = 2.4f,
                                    Intensity = 0.36f
                                });
                            }
                            break;

                        case BywaterOutdoorPropKind.StoneFence:
                        {
                            bool verticalFence = IsFenceLike(map, kinds, x, y - 1) || IsFenceLike(map, kinds, x, y + 1);
                            bool horizontalFence = IsFenceLike(map, kinds, x - 1, y) || IsFenceLike(map, kinds, x + 1, y);
                            CartographyPropKind fenceKind = (verticalFence && !horizontalFence)
                                ? CartographyPropKind.StoneFenceV
                                : CartographyPropKind.StoneFenceH;
                            AddProp(fenceKind, pos.x, pos.y, pos.z, 1.15f, 1.25f);
                            break;
                        }

                        case BywaterOutdoorPropKind.OakTree:
                            AddCanopyTree((vHash & 2) == 0 ? CartographyPropKind.TreeOakA : CartographyPropKind.TreeOakB,
                                pos.x, pos.y, pos.z, 2.5f, 3.0f, 2.0f);
                            break;

                        case BywaterOutdoorPropKind.PineTree:
                            AddCanopyTree((vHash & 2) == 0 ? CartographyPropKind.TreePineA : CartographyPropKind.TreePineB,
                                pos.x, pos.y, pos.z, 2.2f, 3.3f, 1.7f);
                            break;

                        case BywaterOutdoorPropKind.MarketCrates:
                            AddProp(CartographyPropKind.MarketCrates, pos.x, pos.y, pos.z, 0.9f, 0.9f);
                            break;
                    }

                    if (kind == BywaterTileKind.TownBuildingDoor)
                    {
                        _lanterns.Add(new CartographyLanternSource
                        {
                            WorldXZ = new Vector2(pos.x, pos.z),
                            Radius = 2.6f,
                            Intensity = 0.44f
                        });
                    }
                    else if ((kind == BywaterTileKind.Cobblestone || kind == BywaterTileKind.DirtPath) &&
                             CountRoadNeighbors(kinds, x, y, w, h) >= 3 &&
                             (DeterministicHash(x, y, 503) % 7) == 0)
                    {
                        AddProp(CartographyPropKind.TorchBrazier, pos.x + 0.35f, pos.y, pos.z + 0.35f, 0.85f, 1.15f);
                        _lanterns.Add(new CartographyLanternSource
                        {
                            WorldXZ = new Vector2(pos.x + 0.35f, pos.z + 0.35f),
                            Radius = 2.5f,
                            Intensity = 0.42f
                        });
                    }
                }
            }

            for (int i = 0; i < map.PointsOfInterest.Count; i++)
            {
                MapPointOfInterest poi = map.PointsOfInterest[i];
                float elev = GetElevation(poi.X, poi.Y);
                Vector3 pos = GridToWorld(poi.X, poi.Y, elev);
                AddUniqueLantern(pos, 2.8f, 0.48f);
            }

            for (int i = 0; i < map.Portals.Count; i++)
            {
                MapPortal portal = map.Portals[i];
                float elev = GetElevation(portal.X, portal.Y);
                Vector3 pos = GridToWorld(portal.X, portal.Y, elev);
                AddProp(CartographyPropKind.TorchBrazier, pos.x - 0.4f, pos.y, pos.z, 0.85f, 1.15f);
                AddUniqueLantern(pos, 3.0f, 0.55f);
            }
        }

        private void AddUniqueLantern(Vector3 worldPos, float radius, float intensity)
        {
            Vector2 xz = new Vector2(worldPos.x, worldPos.z);
            for (int i = 0; i < _lanterns.Count; i++)
            {
                if ((_lanterns[i].WorldXZ - xz).sqrMagnitude < 1.2f)
                {
                    return;
                }
            }
            _lanterns.Add(new CartographyLanternSource
            {
                WorldXZ = xz,
                Radius = radius,
                Intensity = intensity
            });
        }

        private static bool IsFenceLike(TileMapData map, BywaterTileKind[,] kinds, int x, int y)
        {
            if (x < 0 || x >= map.Width || y < 0 || y >= map.Height) return false;
            if (kinds[x, y] != BywaterTileKind.WallBlock) return false;
            return BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out _) == BywaterOutdoorPropKind.StoneFence;
        }

        private static int CountRoadNeighbors(BywaterTileKind[,] kinds, int x, int y, int w, int h)
        {
            int count = 0;
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                BywaterTileKind k = kinds[nx, ny];
                if (k == BywaterTileKind.Cobblestone || k == BywaterTileKind.DirtPath)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsSurroundedCardinal(BywaterTileKind[,] kinds, int x, int y, int w, int h, BywaterTileKind target)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (kinds[nx, ny] != target) return false;
            }
            return true;
        }

        private static bool IsAdjacentToKind(BywaterTileKind[,] kinds, int x, int y, int w, int h, BywaterTileKind target)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && kinds[nx, ny] == target)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsAdjacentToFloor(BywaterTileKind[,] kinds, int x, int y, int w, int h)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                BywaterTileKind k = kinds[nx, ny];
                if (k == BywaterTileKind.DungeonFloor || k == BywaterTileKind.DungeonDoor || k == BywaterTileKind.DungeonStairs)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNearPortal(TileMapData map, int x, int y, int radius)
        {
            for (int i = 0; i < map.Portals.Count; i++)
            {
                MapPortal p = map.Portals[i];
                if (Mathf.Max(Mathf.Abs(p.X - x), Mathf.Abs(p.Y - y)) <= radius)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNearAnchor(TileMapData map, int x, int y, int radius)
        {
            if (Mathf.Max(Mathf.Abs(map.StartSpawn.X - x), Mathf.Abs(map.StartSpawn.Y - y)) <= radius)
            {
                return true;
            }

            if (IsNearPortal(map, x, y, radius))
            {
                return true;
            }

            for (int i = 0; i < map.PointsOfInterest.Count; i++)
            {
                MapPointOfInterest poi = map.PointsOfInterest[i];
                if (Mathf.Max(Mathf.Abs(poi.X - x), Mathf.Abs(poi.Y - y)) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEligibleOutdoorHighland(TileMapData map, BywaterTileKind[,] kinds, bool[,] isWater, int x, int y)
        {
            // Keep town center and map anchors flat at base elevation
            if (x <= 25 && y <= 25) return false;
            if (IsNearAnchor(map, x, y, 4)) return false;

            int gx = x / 4;
            int gy = y / 4;
            if ((DeterministicHash(gx, gy, 733) % 7) != 0)
            {
                return false;
            }

            int w = map.Width;
            int h = map.Height;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
                    if (isWater[nx, ny]) return false;
                    BywaterTileKind k = kinds[nx, ny];
                    if (k == BywaterTileKind.Cobblestone ||
                        k == BywaterTileKind.DirtPath ||
                        k == BywaterTileKind.TownBuildingSolid ||
                        k == BywaterTileKind.TownBuildingDoor ||
                        k == BywaterTileKind.WallBlock)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsEligibleDungeonDais(TileMapData map, BywaterTileKind[,] kinds, bool[,] isWater, int x, int y)
        {
            if (IsNearAnchor(map, x, y, 3)) return false;

            int gx = x / 3;
            int gy = y / 3;
            if ((DeterministicHash(gx, gy, 409) % 13) != 0)
            {
                return false;
            }

            int w = map.Width;
            int h = map.Height;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
                    if (isWater[nx, ny]) return false;
                    BywaterTileKind k = kinds[nx, ny];
                    if (k == BywaterTileKind.DungeonWall ||
                        k == BywaterTileKind.DungeonDoor ||
                        k == BywaterTileKind.DungeonStairs ||
                        k == BywaterTileKind.WallBlock)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void PopulateBywaterProofLayout()
        {
            _props.Clear();
            _lanterns.Clear();

            for (int legacyZ = 0; legacyZ < GridHeight; legacyZ++)
            {
                int y = (GridHeight - 1) - legacyZ;
                for (int x = 0; x < GridWidth; x++)
                {
                    CartographyTile tile = new CartographyTile
                    {
                        X = x,
                        Y = y,
                        Elevation = PlazaElevation,
                        MossWeight = 0f,
                        DirtWeight = 0f,
                        CobbleWeight = 0f,
                        MudWeight = 0f,
                        CreaseAO = 1.0f,
                        BlocksFoliage = false,
                        IsWater = false,
                        BiomeZone = CartographyBiomeZone.Meadow
                    };

                    // 1. Elevation tiers:
                    // River channel across legacyZ = 3..4 (-0.6m), Northern forest ridge across legacyZ = 13..17 (+0.75m)
                    // flipped into canonical North=0..South=17 grid coordinates.
                    bool isRiver = (legacyZ == 3 || legacyZ == 4);
                    bool isNorthRidge = (legacyZ >= 13) || (legacyZ == 12 && (x <= 2 || x >= 15));

                    if (isRiver)
                    {
                        tile.Elevation = RiverElevation;
                        tile.IsWater = true;
                        tile.BlocksFoliage = true;
                        tile.MudWeight = 0.85f;
                        tile.DirtWeight = 0.15f;
                        tile.CreaseAO = 0.52f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (isNorthRidge)
                    {
                        tile.Elevation = RidgeElevation;
                        tile.MossWeight = 0.65f;
                        tile.BiomeZone = CartographyBiomeZone.ForestShade;
                    }

                    // 2. Riverbank mud transition & reed zone at legacyZ = 2 and legacyZ = 5
                    if (!isRiver && (legacyZ == 2 || legacyZ == 5))
                    {
                        tile.MudWeight = 0.28f;
                        tile.MossWeight = Mathf.Max(tile.MossWeight, 0.25f);
                        tile.CreaseAO = 0.84f;
                        tile.BiomeZone = CartographyBiomeZone.RiverbankReed;
                    }

                    // 3. Cobblestone road fork & stone bridge approach
                    bool isMainCobble = (x == 8 && legacyZ >= 5 && legacyZ <= 9) ||
                                        (legacyZ == 8 && x >= 4 && x <= 8) ||
                                        (x == 8 && legacyZ >= 1 && legacyZ <= 2);

                    bool isSouthDirt = (x == 8 && legacyZ == 0) || (x == 7 && legacyZ == 1) || (x == 9 && legacyZ == 1);
                    bool isEastDirtBranch = (legacyZ == 9 && x >= 9 && x <= 10) ||
                                            (legacyZ == 8 && x >= 9 && x <= 11) ||
                                            (legacyZ == 7 && x >= 10 && x <= 12);
                    bool isInnThreshold = (legacyZ == 8 && x >= 4 && x <= 6);
                    bool isBridgeSpan = (x >= 7 && x <= 9 && (legacyZ == 3 || legacyZ == 4));

                    if (isBridgeSpan)
                    {
                        tile.BlocksFoliage = true;
                        tile.CobbleWeight = 0.70f;
                        tile.MudWeight = 0.30f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (!isRiver && (isMainCobble || isInnThreshold))
                    {
                        tile.CobbleWeight = 0.88f;
                        tile.DirtWeight = 0.12f;
                        tile.BlocksFoliage = true;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }
                    else if (!isRiver && (isSouthDirt || isEastDirtBranch))
                    {
                        tile.DirtWeight = 0.86f;
                        tile.BlocksFoliage = true;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }

                    // 4. Bywater Inn footprint (x = 3..7, legacyZ = 9..12, 5m x 4m)
                    bool isInnFootprint = (x >= 3 && x <= 7 && legacyZ >= 9 && legacyZ <= 12);
                    if (isInnFootprint)
                    {
                        tile.DirtWeight = 0.65f;
                        tile.CobbleWeight = 0.35f;
                        tile.BlocksFoliage = true;
                        tile.CreaseAO = 0.45f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }

                    // 5. Stone fences along inn yard and road border
                    bool isFenceTile = (legacyZ == 8 && (x == 2 || x == 3 || x == 7)) ||
                                       (x == 2 && legacyZ >= 9 && legacyZ <= 11) ||
                                       (x == 9 && legacyZ >= 10 && legacyZ <= 11);
                    if (isFenceTile)
                    {
                        tile.DirtWeight = Mathf.Max(tile.DirtWeight, 0.35f);
                        tile.CreaseAO = Mathf.Min(tile.CreaseAO, 0.72f);
                        tile.BlocksFoliage = true;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }

                    // 6. Sunlit wildflower meadows & moss organic variation
                    if (!tile.BlocksFoliage && !isNorthRidge)
                    {
                        bool inEastMeadow = (x >= 10 && x <= 16 && legacyZ >= 6 && legacyZ <= 11);
                        bool inWestMeadow = (x >= 1 && x <= 5 && legacyZ >= 5 && legacyZ <= 8);
                        bool inSouthMeadow = (legacyZ <= 2 && (x <= 6 || x >= 10));

                        if (tile.BiomeZone != CartographyBiomeZone.RiverbankReed)
                        {
                            if (inEastMeadow || inWestMeadow || inSouthMeadow)
                            {
                                tile.BiomeZone = CartographyBiomeZone.SunlitWildflower;
                            }
                        }

                        if (legacyZ == 12)
                        {
                            tile.BiomeZone = CartographyBiomeZone.ForestShade;
                            tile.MossWeight = Mathf.Max(tile.MossWeight, 0.45f);
                            tile.CreaseAO = Mathf.Min(tile.CreaseAO, 0.76f);
                        }

                        int h = DeterministicHash(x, y, 31);
                        if ((h % 5) == 0)
                        {
                            tile.MossWeight = Mathf.Max(tile.MossWeight, 0.38f + (h % 3) * 0.14f);
                        }
                    }

                    // Subtle foot-path dirt edges bordering cobble road
                    if (!tile.BlocksFoliage && !isNorthRidge && tile.DirtWeight < 0.01f)
                    {
                        if ((x == 7 || x == 9) && legacyZ >= 5 && legacyZ <= 8)
                        {
                            tile.DirtWeight = 0.26f;
                        }
                    }

                    _tiles[x, y] = tile;
                }
            }

            // Apply footprint AO around Bywater Inn perimeter (1-tile apron)
            for (int legacyZ = 8; legacyZ <= 13; legacyZ++)
            {
                int y = (GridHeight - 1) - legacyZ;
                for (int x = 2; x <= 8; x++)
                {
                    if (!InBounds(x, y))
                    {
                        continue;
                    }
                    bool inside = (x >= 3 && x <= 7 && legacyZ >= 9 && legacyZ <= 12);
                    if (!inside)
                    {
                        _tiles[x, y].CreaseAO = Mathf.Min(_tiles[x, y].CreaseAO, 0.68f);
                    }
                }
            }

            // Helper for mapping legacy world Z in [0..18] to canonical Z in [-18..0]
            static float FlipZ(float legacyWz) => legacyWz - GridHeight;

            // Bywater Inn: centered at (5.5, 0, -7.0)
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.BywaterInn,
                WorldPosition = new Vector3(5.5f, PlazaElevation, FlipZ(11.0f)),
                SizeMeters = new Vector2(5.0f, 4.0f),
                CastsCanopyShadow = true,
                CanopyRadius = 2.9f,
                CanopyStrength = 0.45f
            });

            // Stone arched bridge spanning south bank to north bank at (8.5, 0, -14.0)
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.StoneBridge,
                WorldPosition = new Vector3(8.5f, PlazaElevation, FlipZ(4.0f)),
                SizeMeters = new Vector2(2.2f, 2.8f),
                CastsCanopyShadow = false
            });

            // Stone fences framing inn courtyard and road
            AddProp(CartographyPropKind.StoneFenceH, 2.5f, PlazaElevation, FlipZ(8.5f), 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceH, 3.5f, PlazaElevation, FlipZ(8.5f), 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceH, 7.5f, PlazaElevation, FlipZ(8.5f), 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, FlipZ(9.5f), 1.25f, 1.48f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, FlipZ(10.5f), 1.25f, 1.48f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, FlipZ(11.5f), 1.25f, 1.48f);
            AddProp(CartographyPropKind.StoneFenceV, 9.5f, PlazaElevation, FlipZ(10.5f), 1.25f, 1.48f);
            AddProp(CartographyPropKind.StoneFenceV, 9.5f, PlazaElevation, FlipZ(11.5f), 1.25f, 1.48f);

            // Market crates cleanly tucked on inn porch
            AddProp(CartographyPropKind.MarketCrates, 7.05f, PlazaElevation, FlipZ(9.25f), 0.9f, 0.9f);

            // Hero Knight and Giant Frog
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.Knight,
                WorldPosition = new Vector3(8.5f, PlazaElevation, FlipZ(7.5f)),
                SizeMeters = new Vector2(1.85f, 1.85f),
                CastsCanopyShadow = true,
                CanopyRadius = 0.68f,
                CanopyStrength = 0.36f
            });
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.GiantFrog,
                WorldPosition = new Vector3(12.1f, PlazaElevation, FlipZ(6.9f)),
                SizeMeters = new Vector2(1.55f, 1.55f),
                CastsCanopyShadow = true,
                CanopyRadius = 0.62f,
                CanopyStrength = 0.36f
            });

            // Torch brazier near north bridge landing
            AddProp(CartographyPropKind.TorchBrazier, 9.5f, PlazaElevation, FlipZ(5.4f), 0.85f, 1.15f);

            // Riverbank weeping willow
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.TreeWillowA,
                WorldPosition = new Vector3(13.5f, PlazaElevation, FlipZ(5.5f)),
                SizeMeters = new Vector2(2.8f, 3.2f),
                CastsCanopyShadow = true,
                CanopyRadius = 2.1f,
                CanopyStrength = 0.65f
            });

            // Northern forest ridge trees (+0.75m)
            AddCanopyTree(CartographyPropKind.TreeOakA, 1.8f, RidgeElevation, FlipZ(14.2f), 2.6f, 3.1f, 2.0f);
            AddCanopyTree(CartographyPropKind.TreePineA, 4.0f, RidgeElevation, FlipZ(15.6f), 2.2f, 3.4f, 1.7f);
            AddCanopyTree(CartographyPropKind.TreeOakB, 6.5f, RidgeElevation, FlipZ(14.5f), 2.7f, 3.2f, 2.1f);
            AddCanopyTree(CartographyPropKind.TreePineB, 9.2f, RidgeElevation, FlipZ(15.8f), 2.3f, 3.5f, 1.8f);
            AddCanopyTree(CartographyPropKind.TreeOakA, 11.8f, RidgeElevation, FlipZ(14.3f), 2.6f, 3.0f, 2.0f);
            AddCanopyTree(CartographyPropKind.TreePineA, 14.4f, RidgeElevation, FlipZ(15.4f), 2.3f, 3.4f, 1.8f);
            AddCanopyTree(CartographyPropKind.TreeOakB, 16.3f, RidgeElevation, FlipZ(14.0f), 2.5f, 2.9f, 1.9f);
            AddCanopyTree(CartographyPropKind.TreePineB, 2.2f, RidgeElevation, FlipZ(16.4f), 2.2f, 3.2f, 1.7f);
            AddCanopyTree(CartographyPropKind.TreeOakA, 13.0f, RidgeElevation, FlipZ(16.5f), 2.5f, 3.0f, 1.9f);

            // Warm subtle top-down lantern pools
            _lanterns.Add(new CartographyLanternSource
            {
                WorldXZ = new Vector2(5.2f, FlipZ(8.6f)),
                Radius = 2.2f,
                Intensity = 0.36f
            });
            _lanterns.Add(new CartographyLanternSource
            {
                WorldXZ = new Vector2(9.5f, FlipZ(5.4f)),
                Radius = 1.6f,
                Intensity = 0.28f
            });
        }

        private void AddProp(CartographyPropKind kind, float wx, float wy, float wz, float w, float h)
        {
            _props.Add(new CartographyPropPlacement
            {
                Kind = kind,
                WorldPosition = new Vector3(wx, wy, wz),
                SizeMeters = new Vector2(w, h),
                CastsCanopyShadow = false
            });
        }

        private void AddCanopyTree(
            CartographyPropKind kind,
            float wx,
            float wy,
            float wz,
            float w,
            float h,
            float canopyRadius)
        {
            _props.Add(new CartographyPropPlacement
            {
                Kind = kind,
                WorldPosition = new Vector3(wx, wy, wz),
                SizeMeters = new Vector2(w, h),
                CastsCanopyShadow = true,
                CanopyRadius = canopyRadius,
                CanopyStrength = 0.72f
            });
        }

        public static int DeterministicHash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7FFFFFFF;
            }
        }
    }
}
