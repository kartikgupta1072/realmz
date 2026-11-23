using System;
using System.Collections.Generic;
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
    /// Pure C# data layout for the 18x18 Bywater Songs-of-Conquest-style vertical slice diorama.
    /// Coordinates map tile (x, y) to world XZ [x..x+1, y..y+1] with north at +Z (y = 13..17).
    /// </summary>
    public sealed class CartographyWorldData
    {
        public const int GridWidth = 18;
        public const int GridHeight = 18;

        public const float RiverElevation = -0.60f;
        public const float WaterSurfaceElevation = -0.22f;
        public const float PlazaElevation = 0.00f;
        public const float RidgeElevation = 0.75f;

        private readonly CartographyTile[,] _tiles = new CartographyTile[GridWidth, GridHeight];
        private readonly List<CartographyPropPlacement> _props = new List<CartographyPropPlacement>(64);
        private readonly List<CartographyLanternSource> _lanterns = new List<CartographyLanternSource>(8);

        public IReadOnlyList<CartographyPropPlacement> Props => _props;
        public IReadOnlyList<CartographyLanternSource> Lanterns => _lanterns;

        public CartographyTile GetTile(int x, int y)
        {
            int cx = Mathf.Clamp(x, 0, GridWidth - 1);
            int cy = Mathf.Clamp(y, 0, GridHeight - 1);
            return _tiles[cx, cy];
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;
        }

        public static CartographyWorldData BuildBywaterProofLayout()
        {
            CartographyWorldData data = new CartographyWorldData();
            data.PopulateBywaterLayout();
            return data;
        }

        private void PopulateBywaterLayout()
        {
            _props.Clear();
            _lanterns.Clear();

            for (int y = 0; y < GridHeight; y++)
            {
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
                    // River channel across y = 3..4 (-0.6m), Northern forest ridge across y = 13..17 (+0.75m)
                    // with two stepped outcrops at x=1..3, y=12 and x=14..16, y=12 for dynamic cliff silhouettes.
                    bool isRiver = (y == 3 || y == 4);
                    bool isNorthRidge = (y >= 13) || (y == 12 && (x <= 2 || x >= 15));

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

                    // 2. Riverbank mud transition & reed zone at y = 2 and y = 5
                    if (!isRiver && (y == 2 || y == 5))
                    {
                        tile.MudWeight = 0.28f;
                        tile.MossWeight = Mathf.Max(tile.MossWeight, 0.25f);
                        tile.CreaseAO = 0.84f;
                        tile.BiomeZone = CartographyBiomeZone.RiverbankReed;
                    }

                    // 3. Cobblestone road fork & stone bridge approach:
                    // South path x=8, y=0..2 -> bridge x=7..9, y=3..4 -> plaza stem x=8, y=5..9
                    // Branch west to Inn courtyard x=4..7, y=8; branch east x=9..12, y=7..9
                    bool isMainCobble = (x == 8 && y >= 5 && y <= 9) ||
                                        (y == 8 && x >= 4 && x <= 8) ||
                                        (x == 8 && y >= 1 && y <= 2);

                    bool isSouthDirt = (x == 8 && y == 0) || (x == 7 && y == 1) || (x == 9 && y == 1);
                    bool isEastDirtBranch = (y == 9 && x >= 9 && x <= 10) ||
                                            (y == 8 && x >= 9 && x <= 11) ||
                                            (y == 7 && x >= 10 && x <= 12);
                    bool isInnThreshold = (y == 8 && x >= 4 && x <= 6);
                    bool isBridgeSpan = (x >= 7 && x <= 9 && (y == 3 || y == 4));

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

                    // 4. Bywater Inn footprint (x = 3..7, y = 9..12, 5m x 4m)
                    bool isInnFootprint = (x >= 3 && x <= 7 && y >= 9 && y <= 12);
                    if (isInnFootprint)
                    {
                        tile.DirtWeight = 0.65f;
                        tile.CobbleWeight = 0.35f;
                        tile.BlocksFoliage = true;
                        tile.CreaseAO = 0.45f;
                        tile.BiomeZone = CartographyBiomeZone.Blocked;
                    }

                    // 5. Stone fences along inn yard and road border
                    bool isFenceTile = (y == 8 && (x == 2 || x == 3 || x == 7)) ||
                                       (x == 2 && y >= 9 && y <= 11) ||
                                       (x == 9 && y >= 10 && y <= 11);
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
                        bool inEastMeadow = (x >= 10 && x <= 16 && y >= 6 && y <= 11);
                        bool inWestMeadow = (x >= 1 && x <= 5 && y >= 5 && y <= 8);
                        bool inSouthMeadow = (y <= 2 && (x <= 6 || x >= 10));

                        if (tile.BiomeZone != CartographyBiomeZone.RiverbankReed)
                        {
                            if (inEastMeadow || inWestMeadow || inSouthMeadow)
                            {
                                tile.BiomeZone = CartographyBiomeZone.SunlitWildflower;
                            }
                        }

                        if (y == 12)
                        {
                            tile.BiomeZone = CartographyBiomeZone.ForestShade;
                            tile.MossWeight = Mathf.Max(tile.MossWeight, 0.45f);
                            tile.CreaseAO = Mathf.Min(tile.CreaseAO, 0.76f);
                        }

                        // Subtle mossy patches near boulders, trees, and inn perimeter
                        int h = DeterministicHash(x, y, 31);
                        if ((h % 5) == 0)
                        {
                            tile.MossWeight = Mathf.Max(tile.MossWeight, 0.38f + (h % 3) * 0.14f);
                        }
                    }

                    // Subtle foot-path dirt edges bordering cobble road
                    if (!tile.BlocksFoliage && !isNorthRidge && tile.DirtWeight < 0.01f)
                    {
                        if ((x == 7 || x == 9) && y >= 5 && y <= 8)
                        {
                            tile.DirtWeight = 0.26f;
                        }
                    }

                    _tiles[x, y] = tile;
                }
            }

            // Apply footprint AO around Bywater Inn perimeter (1-tile apron)
            for (int y = 8; y <= 13; y++)
            {
                for (int x = 2; x <= 8; x++)
                {
                    if (!IsInBounds(x, y))
                    {
                        continue;
                    }
                    bool inside = (x >= 3 && x <= 7 && y >= 9 && y <= 12);
                    if (!inside)
                    {
                        _tiles[x, y].CreaseAO = Mathf.Min(_tiles[x, y].CreaseAO, 0.68f);
                    }
                }
            }

            // Populate props, units, trees, and lanterns
            // Bywater Inn: centered over x=3..7, y=9..12 -> (5.5, 0, 11.0) at 64 px/m (320x256 -> 5.0 x 4.0m)
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.BywaterInn,
                WorldPosition = new Vector3(5.5f, PlazaElevation, 11.0f),
                SizeMeters = new Vector2(5.0f, 4.0f),
                CastsCanopyShadow = true,
                CanopyRadius = 2.9f,
                CanopyStrength = 0.45f
            });

            // Stone arched bridge spanning south bank (z=2.6) to north bank (z=5.4) across x = 7.4..9.6
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.StoneBridge,
                WorldPosition = new Vector3(8.5f, PlazaElevation, 4.0f),
                SizeMeters = new Vector2(2.2f, 2.8f),
                CastsCanopyShadow = false
            });

            // Stone fences framing inn courtyard and road
            AddProp(CartographyPropKind.StoneFenceH, 2.5f, PlazaElevation, 8.5f, 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceH, 3.5f, PlazaElevation, 8.5f, 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceH, 7.5f, PlazaElevation, 8.5f, 1.18f, 1.18f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, 9.5f, 1.20f, 1.20f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, 10.5f, 1.20f, 1.20f);
            AddProp(CartographyPropKind.StoneFenceV, 2.5f, PlazaElevation, 11.5f, 1.20f, 1.20f);
            AddProp(CartographyPropKind.StoneFenceV, 9.5f, PlazaElevation, 10.5f, 1.20f, 1.20f);
            AddProp(CartographyPropKind.StoneFenceV, 9.5f, PlazaElevation, 11.5f, 1.20f, 1.20f);

            // Market crates cleanly tucked on inn porch
            AddProp(CartographyPropKind.MarketCrates, 7.05f, PlazaElevation, 9.25f, 0.9f, 0.9f);

            // Hero Knight at (8.5, 7.5) and Giant Frog at (12.1, 6.9) scaled to unified 64 px/m ratio with subtle ground contact shadows
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.Knight,
                WorldPosition = new Vector3(8.5f, PlazaElevation, 7.5f),
                SizeMeters = new Vector2(1.85f, 1.85f),
                CastsCanopyShadow = true,
                CanopyRadius = 0.68f,
                CanopyStrength = 0.36f
            });
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.GiantFrog,
                WorldPosition = new Vector3(12.1f, PlazaElevation, 6.9f),
                SizeMeters = new Vector2(1.55f, 1.55f),
                CastsCanopyShadow = true,
                CanopyRadius = 0.62f,
                CanopyStrength = 0.36f
            });

            // Torch brazier near north bridge landing
            AddProp(CartographyPropKind.TorchBrazier, 9.5f, PlazaElevation, 5.4f, 0.85f, 1.15f);

            // Riverbank weeping willow at (13, 5)
            _props.Add(new CartographyPropPlacement
            {
                Kind = CartographyPropKind.TreeWillowA,
                WorldPosition = new Vector3(13.5f, PlazaElevation, 5.5f),
                SizeMeters = new Vector2(2.8f, 3.2f),
                CastsCanopyShadow = true,
                CanopyRadius = 2.1f,
                CanopyStrength = 0.65f
            });

            // Northern forest ridge trees at y = 13..17 (+0.75m)
            AddCanopyTree(CartographyPropKind.TreeOakA, 1.8f, RidgeElevation, 14.2f, 2.6f, 3.1f, 2.0f);
            AddCanopyTree(CartographyPropKind.TreePineA, 4.0f, RidgeElevation, 15.6f, 2.2f, 3.4f, 1.7f);
            AddCanopyTree(CartographyPropKind.TreeOakB, 6.5f, RidgeElevation, 14.5f, 2.7f, 3.2f, 2.1f);
            AddCanopyTree(CartographyPropKind.TreePineB, 9.2f, RidgeElevation, 15.8f, 2.3f, 3.5f, 1.8f);
            AddCanopyTree(CartographyPropKind.TreeOakA, 11.8f, RidgeElevation, 14.3f, 2.6f, 3.0f, 2.0f);
            AddCanopyTree(CartographyPropKind.TreePineA, 14.4f, RidgeElevation, 15.4f, 2.3f, 3.4f, 1.8f);
            AddCanopyTree(CartographyPropKind.TreeOakB, 16.3f, RidgeElevation, 14.0f, 2.5f, 2.9f, 1.9f);
            AddCanopyTree(CartographyPropKind.TreePineB, 2.2f, RidgeElevation, 16.4f, 2.2f, 3.2f, 1.7f);
            AddCanopyTree(CartographyPropKind.TreeOakA, 13.0f, RidgeElevation, 16.5f, 2.5f, 3.0f, 1.9f);

            // Warm subtle top-down lantern pools (balanced for bright sunlit daytime contrast)
            _lanterns.Add(new CartographyLanternSource
            {
                WorldXZ = new Vector2(5.2f, 8.6f), // Inn doorway
                Radius = 2.2f,
                Intensity = 0.36f
            });
            _lanterns.Add(new CartographyLanternSource
            {
                WorldXZ = new Vector2(9.5f, 5.4f), // Bridge brazier
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
