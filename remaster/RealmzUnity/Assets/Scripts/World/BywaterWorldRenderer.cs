using System.Collections.Generic;
using Realmz.Data;
using Realmz.Rendering;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Pooled sliding-window 3D HD-2D terrain, modular 2.5D medieval prop, portal, brazier,
    /// and combat-arena renderer. Zero per-step heap allocations when panning/walking across Bywater maps.
    /// </summary>
    public sealed class BywaterWorldRenderer : MonoBehaviour
    {
        private const int WindowRadius = 16;
        private const int MaxActiveTorchLights = 8;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SplatMapId = Shader.PropertyToID("_SplatMap");
        private static readonly int WorldBoundsId = Shader.PropertyToID("_WorldBounds");
        private static readonly int Layer0AlbedoId = Shader.PropertyToID("_Layer0Albedo");
        private static readonly int GridHintStrengthId = Shader.PropertyToID("_GridHintStrength");

        private static readonly GridCoord[] LandTorchHints =
        {
            new GridCoord(2, 2),
            new GridCoord(8, 11),
            new GridCoord(10, 13),
            new GridCoord(8, 17),
            new GridCoord(10, 9),
            new GridCoord(38, 13),
            new GridCoord(88, 12)
        };

        private static readonly GridCoord[] DungeonTorchHints =
        {
            new GridCoord(33, 72)
        };

        private sealed class TorchPropEntry
        {
            public GameObject Root;
            public MeshRenderer StandRenderer;
            public MeshRenderer BowlRenderer;
            public MeshRenderer QuadRenderer;
            public MeshRenderer EmberRenderer;
            public Light PointLight;
            public BywaterTorchFlicker Flicker;
        }

        private sealed class PortalMarkerEntry
        {
            public GameObject Root;
            public MeshRenderer RuneQuad;
            public MeshRenderer BeaconPillar;
        }

        private sealed class CottagePropEntry
        {
            public GameObject Root;
            public GameObject ModularRoot;
            public MeshRenderer Plinth;
            public MeshRenderer WallBody;
            public MeshRenderer PostFL;
            public MeshRenderer PostFR;
            public MeshRenderer PostBL;
            public MeshRenderer PostBR;
            public MeshRenderer TimberBeam;
            public MeshRenderer RoofLeft;
            public MeshRenderer RoofRight;
            public MeshRenderer GablePeak;
            public MeshRenderer RidgeBeam;
            public MeshRenderer WindowFront;
            public MeshRenderer BillboardQuad;
        }

        private sealed class FencePropEntry
        {
            public GameObject Root;
            public GameObject ModularRoot;
            public MeshRenderer Wall;
            public MeshRenderer Coping;
            public MeshRenderer Post;
            public MeshRenderer BillboardQuad;
        }

        private sealed class TreePropEntry
        {
            public GameObject Root;
            public Transform SwayTarget;
            public BywaterPropSway Sway;
            public GameObject OakRoot;
            public MeshRenderer OakTrunk;
            public MeshRenderer OakCanopyLower;
            public MeshRenderer OakCanopyUpper;
            public GameObject PineRoot;
            public MeshRenderer PineTrunk;
            public MeshRenderer PineTierLower;
            public MeshRenderer PineTierMid;
            public MeshRenderer PineTierTop;
            public MeshRenderer BillboardQuad;
        }

        private sealed class BoulderPropEntry
        {
            public GameObject Root;
            public MeshRenderer MainRock;
            public MeshRenderer SubRock;
            public MeshRenderer TopCap;
            public MeshRenderer MossShrub;
        }

        private sealed class WildflowerPropEntry
        {
            public GameObject Root;
            public Transform SwayTarget;
            public BywaterPropSway Sway;
            public GameObject ModularRoot;
            public MeshRenderer Greenery;
            public MeshRenderer BlossomA;
            public MeshRenderer BlossomB;
            public MeshRenderer BlossomC;
            public MeshRenderer BillboardQuad;
        }

        private sealed class CratePropEntry
        {
            public GameObject Root;
            public GameObject ModularRoot;
            public MeshRenderer CrateBox;
            public MeshRenderer CrateTrim;
            public MeshRenderer BarrelBody;
            public MeshRenderer BarrelBand;
            public MeshRenderer BillboardQuad;
        }

        private BywaterAssetCatalog _catalog;
        private TileMapData _currentMap;
        private GridCoord _focusCenter;
        private bool _hasRendered;

        private Transform _floorContainer;
        private Transform _structureContainer;
        private Transform _propsContainer;
        private Transform _torchContainer;
        private Transform _portalContainer;
        private Transform _arenaContainer;

        private readonly List<MeshRenderer> _floorPool = new List<MeshRenderer>(1089);
        private readonly List<MeshRenderer> _blockPool = new List<MeshRenderer>(1400);
        private readonly List<CottagePropEntry> _cottagePool = new List<CottagePropEntry>(128);
        private readonly List<FencePropEntry> _fencePool = new List<FencePropEntry>(96);
        private readonly List<TreePropEntry> _treePool = new List<TreePropEntry>(160);
        private readonly List<BoulderPropEntry> _boulderPool = new List<BoulderPropEntry>(128);
        private readonly List<WildflowerPropEntry> _wildflowerPool = new List<WildflowerPropEntry>(96);
        private readonly List<CratePropEntry> _cratePool = new List<CratePropEntry>(32);
        private readonly List<TorchPropEntry> _torchPool = new List<TorchPropEntry>(MaxActiveTorchLights);
        private readonly List<PortalMarkerEntry> _portalPool = new List<PortalMarkerEntry>(16);
        private readonly List<MeshRenderer> _arenaBlocks = new List<MeshRenderer>(8);
        private MaterialPropertyBlock _floorPropBlock;
        private MaterialPropertyBlock _arenaPropBlock;

        private readonly List<GridCoord> _mapTorchCoords = new List<GridCoord>(64);
        private readonly List<GridCoord> _selectedTorches = new List<GridCoord>(MaxActiveTorchLights);
        private readonly List<int> _selectedTorchDistSq = new List<int>(MaxActiveTorchLights);

        private Light _partyLantern;

        private BywaterGroundScatter _groundScatter;
        private MeshFilter _terrainMeshFilter;
        private MeshRenderer _terrainMeshRenderer;
        private Mesh _terrainMesh;
        private Texture2D _splatTexture;
        private Material _activeTerrainMat;
        private TileMapData _splatBuiltForMap;
        private MaterialPropertyBlock _terrainPropBlock;
        private Material _fallbackGrassTuftMat;
        private Material _fallbackFlowerTuftMat;
        private Material _fallbackPebbleScatterMat;
        private bool _combatScatterSuppressed;
        private GridCoord _combatOrigin;
        private int _combatW;
        private int _combatH;
        private float _terrainGridHintStrength;

        private readonly List<Vector3> _terrainVertices = new List<Vector3>(5100);
        private readonly List<Vector2> _terrainUvs = new List<Vector2>(5100);
        private readonly List<int> _terrainTriangles = new List<int>(30000);

        public void Initialize(BywaterAssetCatalog catalog)
        {
            _catalog = catalog;
            EnsureContainers();
            EnsurePartyLantern();
            SyncScatterMaterials();
            WarmupPools();
        }

        public void RenderMap(TileMapData map, GridCoord focusCenter, bool forceRebuild = false)
        {
            if (map == null)
            {
                return;
            }

            EnsureContainers();
            EnsurePartyLantern();

            bool mapChanged = !ReferenceEquals(_currentMap, map);
            _currentMap = map;

            if (mapChanged || forceRebuild)
            {
                BuildTorchLocations(map);
            }

            if (!forceRebuild && !mapChanged && _hasRendered && focusCenter == _focusCenter)
            {
                return;
            }

            _focusCenter = focusCenter;
            _hasRendered = true;
            RebuildWindow(map, focusCenter);
        }

        public void UpdateFocusCenter(GridCoord focusCenter)
        {
            if (_currentMap == null)
            {
                return;
            }

            if (_hasRendered && focusCenter == _focusCenter)
            {
                return;
            }

            _focusCenter = focusCenter;
            _hasRendered = true;
            RebuildWindow(_currentMap, focusCenter);
        }

        public void SetPartyTorchPosition(Vector3 worldPos, bool isDungeon)
        {
            EnsurePartyLantern();
            if (_partyLantern == null)
            {
                return;
            }

            _partyLantern.transform.position = new Vector3(worldPos.x, worldPos.y + 1.65f, worldPos.z - 0.85f);
            if (isDungeon)
            {
                _partyLantern.enabled = true;
                _partyLantern.intensity = 2.6f;
                _partyLantern.range = 10.5f;
            }
            else
            {
                _partyLantern.intensity = 0f;
                _partyLantern.enabled = false;
            }
        }

        public void SetTerrainGridHint(float strength)
        {
            _terrainGridHintStrength = Mathf.Max(0f, strength);
            ApplyTerrainPropertyBlock();
        }

        public void SetCombatArenaFrame(int originX, int originY, int width, int height, bool active)
        {
            EnsureContainers();

            _combatScatterSuppressed = active && width > 0 && height > 0;
            _combatOrigin = new GridCoord(originX, originY);
            _combatW = width;
            _combatH = height;

            if (_currentMap != null && _hasRendered && _groundScatter != null)
            {
                _groundScatter.RebuildForMap(
                    _currentMap,
                    _focusCenter,
                    WindowRadius,
                    _combatScatterSuppressed,
                    _combatOrigin,
                    _combatW,
                    _combatH);
            }

            if (!active || width <= 0 || height <= 0)
            {
                for (int i = 0; i < _arenaBlocks.Count; i++)
                {
                    _arenaBlocks[i].gameObject.SetActive(false);
                }
                return;
            }

            EnsureArenaMarkers();

            float left = originX - 0.5f;
            float right = originX + width - 0.5f;
            float north = -(originY - 0.5f);
            float south = -(originY + height - 0.5f);
            float midX = (left + right) * 0.5f;
            float midZ = (north + south) * 0.5f;
            float spanX = Mathf.Max(1f, right - left);
            float spanZ = Mathf.Max(1f, north - south);

            Material borderMat = _catalog != null
                ? (_catalog.overlayBaseMaterial != null ? _catalog.overlayBaseMaterial : _catalog.portalBeaconMaterial)
                : null;

            _arenaPropBlock ??= new MaterialPropertyBlock();
            _arenaPropBlock.Clear();
            _arenaPropBlock.SetColor(BaseColorId, new Color(0.98f, 0.78f, 0.22f, 0.72f));

            // 4 corner posts
            ConfigureArenaPiece(_arenaBlocks[0], new Vector3(left, 0.36f, north), new Vector3(0.16f, 0.72f, 0.16f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[1], new Vector3(right, 0.36f, north), new Vector3(0.16f, 0.72f, 0.16f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[2], new Vector3(left, 0.36f, south), new Vector3(0.16f, 0.72f, 0.16f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[3], new Vector3(right, 0.36f, south), new Vector3(0.16f, 0.72f, 0.16f), borderMat);

            // 4 perimeter strips
            ConfigureArenaPiece(_arenaBlocks[4], new Vector3(midX, 0.03f, north), new Vector3(spanX + 0.08f, 0.04f, 0.08f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[5], new Vector3(midX, 0.03f, south), new Vector3(spanX + 0.08f, 0.04f, 0.08f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[6], new Vector3(left, 0.03f, midZ), new Vector3(0.08f, 0.04f, spanZ + 0.08f), borderMat);
            ConfigureArenaPiece(_arenaBlocks[7], new Vector3(right, 0.03f, midZ), new Vector3(0.08f, 0.04f, spanZ + 0.08f), borderMat);
        }

        private void ConfigureArenaPiece(MeshRenderer renderer, Vector3 pos, Vector3 scale, Material mat)
        {
            renderer.transform.position = pos;
            renderer.transform.localScale = scale;
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }
            renderer.SetPropertyBlock(_arenaPropBlock);
            renderer.gameObject.SetActive(true);
        }

        private void RebuildWindow(TileMapData map, GridCoord center)
        {
            _floorPropBlock ??= new MaterialPropertyBlock();

            int minX = Mathf.Max(0, center.X - WindowRadius);
            int maxX = Mathf.Min(map.Width - 1, center.X + WindowRadius);
            int minY = Mathf.Max(0, center.Y - WindowRadius);
            int maxY = Mathf.Min(map.Height - 1, center.Y + WindowRadius);

            int floorIdx = 0;
            int blockIdx = 0;
            int cottageIdx = 0;
            int fenceIdx = 0;
            int treeIdx = 0;
            int boulderIdx = 0;
            int wildflowerIdx = 0;
            int crateIdx = 0;

            bool isDungeon = map.IsDungeon;
            Material terrainMat = ResolveTerrainBlendMaterial(map);
            bool useTerrainBlend = terrainMat != null;
            _activeTerrainMat = terrainMat;

            if (useTerrainBlend)
            {
                EnsureSplatTexture(map);
                UpdateTerrainMesh(map, minX, maxX, minY, maxY);
                if (_terrainMeshRenderer != null)
                {
                    _terrainMeshRenderer.sharedMaterial = terrainMat;
                    _terrainMeshRenderer.gameObject.SetActive(true);
                    ApplyTerrainPropertyBlock();
                }
            }
            else if (_terrainMeshRenderer != null)
            {
                _terrainMeshRenderer.gameObject.SetActive(false);
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);
                    int tileHash = BywaterTileClassifier.DeterministicTileHash(x, y);
                    int variantIdx = ResolveWeightedTileVariant(kind, isDungeon, x, y, tileHash);

                    // Floor quad: when height-blended terrain mesh is active, only keep animated water quads
                    if (!useTerrainBlend || kind == BywaterTileKind.ShallowWater)
                    {
                        float floorY = kind == BywaterTileKind.ShallowWater
                            ? (useTerrainBlend ? -0.10f : -0.12f)
                            : 0f;
                        MeshRenderer floorQuad = GetFloorQuad(floorIdx++);
                        floorQuad.transform.position = new Vector3(x, floorY, -y);
                        floorQuad.transform.rotation = Quaternion.Euler(90f, ResolveFloorYaw(kind, tileHash), 0f);
                        floorQuad.sharedMaterial = ResolveFloorMaterial(kind, isDungeon, variantIdx);
                        ApplyFloorTint(floorQuad, kind, tileHash);
                        floorQuad.gameObject.SetActive(true);
                    }

                    if (isDungeon)
                    {
                        switch (kind)
                        {
                            case BywaterTileKind.DungeonWall:
                            case BywaterTileKind.WallBlock:
                            {
                                Material fallbackWallMat = _catalog != null
                                    ? _catalog.GetTileVariantMaterial(_catalog.dungeonWallMaterial, _catalog.dungeonWallVariantMaterials, variantIdx)
                                    : null;
                                Material wallMat = _catalog != null
                                    ? FirstNonNull(_catalog.painterlyDungeonWallMaterial, fallbackWallMat)
                                    : null;
                                MeshRenderer wallCube = GetBlockCube(blockIdx++);
                                wallCube.transform.position = new Vector3(x, 0.675f, -y);
                                wallCube.transform.localScale = new Vector3(1.0f, 1.35f, 1.0f);
                                wallCube.sharedMaterial = wallMat;
                                wallCube.gameObject.SetActive(true);

                                MeshRenderer trimCap = GetBlockCube(blockIdx++);
                                trimCap.transform.position = new Vector3(x, 1.385f, -y);
                                trimCap.transform.localScale = new Vector3(1.05f, 0.07f, 1.05f);
                                trimCap.sharedMaterial = FirstNonNull(_catalog != null ? _catalog.painterlyDungeonWallMaterial : null, _catalog != null ? _catalog.stoneFenceMaterial : null, wallMat);
                                trimCap.gameObject.SetActive(true);
                                break;
                            }

                            case BywaterTileKind.DungeonDoor:
                            {
                                Material archMat = _catalog != null ? _catalog.doorArchMaterial : null;
                                MeshRenderer leftJamb = GetBlockCube(blockIdx++);
                                leftJamb.transform.position = new Vector3(x - 0.36f, 0.575f, -y);
                                leftJamb.transform.localScale = new Vector3(0.18f, 1.15f, 0.18f);
                                leftJamb.sharedMaterial = archMat;
                                leftJamb.gameObject.SetActive(true);

                                MeshRenderer rightJamb = GetBlockCube(blockIdx++);
                                rightJamb.transform.position = new Vector3(x + 0.36f, 0.575f, -y);
                                rightJamb.transform.localScale = new Vector3(0.18f, 1.15f, 0.18f);
                                rightJamb.sharedMaterial = archMat;
                                rightJamb.gameObject.SetActive(true);
                                break;
                            }

                            case BywaterTileKind.DungeonStairs:
                            {
                                Material stairMat = _catalog != null
                                    ? FirstNonNull(_catalog.doorArchMaterial, _catalog.dungeonWallMaterial)
                                    : null;

                                for (int step = 0; step < 3; step++)
                                {
                                    MeshRenderer stepBlock = GetBlockCube(blockIdx++);
                                    float stepHeight = 0.12f * (step + 1);
                                    float offsetZ = (step - 1) * 0.22f;
                                    stepBlock.transform.position = new Vector3(x, stepHeight * 0.5f, -y + offsetZ);
                                    stepBlock.transform.localScale = new Vector3(0.72f, stepHeight, 0.22f);
                                    stepBlock.sharedMaterial = stairMat;
                                    stepBlock.gameObject.SetActive(true);
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        BywaterOutdoorPropKind propKind = BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out int variantHash);
                        switch (propKind)
                        {
                            case BywaterOutdoorPropKind.CottageFacade:
                                ConfigureCottageProp(GetCottageProp(cottageIdx++), x, y, variantHash);
                                break;
                            case BywaterOutdoorPropKind.StoneFence:
                                ConfigureFenceProp(GetFenceProp(fenceIdx++), x, y, variantHash);
                                break;
                            case BywaterOutdoorPropKind.OakTree:
                                ConfigureTreeProp(GetTreeProp(treeIdx++), x, y, variantHash, isPine: false);
                                break;
                            case BywaterOutdoorPropKind.PineTree:
                                ConfigureTreeProp(GetTreeProp(treeIdx++), x, y, variantHash, isPine: true);
                                break;
                            case BywaterOutdoorPropKind.BoulderCliff:
                                ConfigureBoulderProp(GetBoulderProp(boulderIdx++), x, y, variantHash);
                                break;
                            case BywaterOutdoorPropKind.WildflowerBush:
                                ConfigureWildflowerProp(GetWildflowerProp(wildflowerIdx++), x, y, variantHash);
                                break;
                            case BywaterOutdoorPropKind.MarketCrates:
                                ConfigureCrateProp(GetCrateProp(crateIdx++), x, y, variantHash);
                                break;
                        }
                    }
                }
            }

            DeactivateUnused(_floorPool, floorIdx);
            DeactivateUnused(_blockPool, blockIdx);
            DeactivateUnusedCottages(cottageIdx);
            DeactivateUnusedFences(fenceIdx);
            DeactivateUnusedTrees(treeIdx);
            DeactivateUnusedBoulders(boulderIdx);
            DeactivateUnusedWildflowers(wildflowerIdx);
            DeactivateUnusedCrates(crateIdx);

            UpdateWindowPortals(map, minX, maxX, minY, maxY);
            UpdateWindowTorches(center, minX, maxX, minY, maxY, isDungeon);

            if (_groundScatter != null)
            {
                SyncScatterMaterials();
                _groundScatter.RebuildForMap(
                    map,
                    center,
                    WindowRadius,
                    _combatScatterSuppressed,
                    _combatOrigin,
                    _combatW,
                    _combatH);
            }
        }

        private void ApplyFloorTint(MeshRenderer floorQuad, BywaterTileKind kind, int tileHash)
        {
            _floorPropBlock.Clear();
            if (kind == BywaterTileKind.Grass ||
                kind == BywaterTileKind.ForestCover ||
                kind == BywaterTileKind.DirtPath ||
                kind == BywaterTileKind.DungeonFloor ||
                kind == BywaterTileKind.DungeonDoor ||
                kind == BywaterTileKind.DungeonStairs)
            {
                int step = (tileHash >> 4) % 11;
                float lightness = 0.95f + step * 0.01f;
                _floorPropBlock.SetColor(BaseColorId, new Color(lightness, lightness, lightness, 1f));
            }
            else
            {
                _floorPropBlock.SetColor(BaseColorId, Color.white);
            }

            floorQuad.SetPropertyBlock(_floorPropBlock);
        }

        private static int ResolveWeightedTileVariant(BywaterTileKind kind, bool isDungeon, int x, int y, int tileHash)
        {
            if (isDungeon)
            {
                return BywaterTileClassifier.GetTileVariantIndex(x, y, 3);
            }

            if (kind == BywaterTileKind.Grass || kind == BywaterTileKind.ForestCover)
            {
                int roll = tileHash % 10;
                return roll < 7 ? 0 : (roll < 9 ? 1 : 2);
            }

            if (kind == BywaterTileKind.Cobblestone || kind == BywaterTileKind.TownBuildingDoor)
            {
                int roll = tileHash % 10;
                return roll < 7 ? 0 : (roll < 9 ? 1 : 2);
            }

            return BywaterTileClassifier.GetTileVariantIndex(x, y, 3);
        }

        private static float ResolveFloorYaw(BywaterTileKind kind, int tileHash)
        {
            // Keep 0 deg alignment so seamless W-222 tile borders and +Y-up normal bevels align cleanly.
            return 0f;
        }

        private void ConfigureCottageProp(CottagePropEntry entry, int x, int y, int variantHash)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);

            bool useBillboard = _catalog != null
                && _catalog.cottageBillboardSprite != null
                && _catalog.cottageBillboardMaterial != null;

            entry.ModularRoot.SetActive(!useBillboard);
            entry.BillboardQuad.gameObject.SetActive(useBillboard);

            if (useBillboard)
            {
                entry.BillboardQuad.sharedMaterial = _catalog.cottageBillboardMaterial;
                entry.Root.SetActive(true);
                return;
            }

            Material plinthMat = _catalog != null
                ? FirstNonNull(_catalog.stoneFenceMaterial, _catalog.boulderMaterial, _catalog.buildingWallMaterial)
                : null;
            Material wallMat = _catalog != null
                ? FirstNonNull(_catalog.cottageWallMaterial, _catalog.buildingWallMaterial, _catalog.dungeonWallMaterial)
                : null;
            Material timberMat = _catalog != null
                ? FirstNonNull(_catalog.timberTrimMaterial, _catalog.treeTrunkMaterial, _catalog.doorArchMaterial)
                : null;
            Material windowMat = _catalog != null
                ? FirstNonNull(_catalog.warmWindowGlowMaterial, _catalog.torchEmberMaterial, _catalog.portalBeaconMaterial)
                : null;

            Material roofMat = null;
            if (_catalog != null)
            {
                int roofChoice = variantHash % 3;
                if (roofChoice == 0)
                {
                    roofMat = FirstNonNull(_catalog.cottageRoofTerracottaMaterial, _catalog.cottageRoofSlateMaterial, _catalog.cottageRoofThatchMaterial);
                }
                else if (roofChoice == 1)
                {
                    roofMat = FirstNonNull(_catalog.cottageRoofSlateMaterial, _catalog.cottageRoofTerracottaMaterial, _catalog.cottageRoofThatchMaterial);
                }
                else
                {
                    roofMat = FirstNonNull(_catalog.cottageRoofThatchMaterial, _catalog.cottageRoofTerracottaMaterial, _catalog.cottageRoofSlateMaterial);
                }
                roofMat = FirstNonNull(roofMat, _catalog.buildingWallMaterial, _catalog.dungeonWallMaterial);
            }

            entry.Plinth.sharedMaterial = plinthMat;
            entry.WallBody.sharedMaterial = wallMat;
            entry.PostFL.sharedMaterial = timberMat;
            entry.PostFR.sharedMaterial = timberMat;
            entry.PostBL.sharedMaterial = timberMat;
            entry.PostBR.sharedMaterial = timberMat;
            entry.TimberBeam.sharedMaterial = timberMat;
            entry.RoofLeft.sharedMaterial = roofMat;
            entry.RoofRight.sharedMaterial = roofMat;
            entry.GablePeak.sharedMaterial = wallMat;
            entry.RidgeBeam.sharedMaterial = timberMat;
            entry.WindowFront.sharedMaterial = windowMat;

            entry.Root.SetActive(true);
        }

        private void ConfigureFenceProp(FencePropEntry entry, int x, int y, int variantHash)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);

            bool useBillboard = _catalog != null
                && _catalog.stoneFenceBillboardSprite != null
                && _catalog.stoneFenceBillboardMaterial != null;

            entry.ModularRoot.SetActive(!useBillboard);
            entry.BillboardQuad.gameObject.SetActive(useBillboard);

            if (useBillboard)
            {
                entry.BillboardQuad.sharedMaterial = _catalog.stoneFenceBillboardMaterial;
                entry.Root.SetActive(true);
                return;
            }

            Material fenceMat = _catalog != null
                ? FirstNonNull(_catalog.stoneFenceMaterial, _catalog.boulderMaterial, _catalog.dungeonWallMaterial)
                : null;
            Material copingMat = _catalog != null
                ? FirstNonNull(_catalog.boulderMaterial, _catalog.stoneFenceMaterial, _catalog.dungeonWallMaterial)
                : null;

            entry.Wall.sharedMaterial = fenceMat;
            entry.Coping.sharedMaterial = copingMat;
            entry.Post.sharedMaterial = fenceMat;

            float postX = (variantHash & 1) == 0 ? -0.38f : 0.38f;
            entry.Post.transform.localPosition = new Vector3(postX, 0.36f, 0f);

            entry.Root.SetActive(true);
        }

        private void ConfigureTreeProp(TreePropEntry entry, int x, int y, int variantHash, bool isPine)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);
            float yaw = ((variantHash >> 2) % 4) * 90f;
            float scaleJitter = 0.94f + ((variantHash >> 5) % 13) * 0.01f;
            entry.Root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            entry.Root.transform.localScale = new Vector3(scaleJitter, scaleJitter, scaleJitter);

            bool useBillboard = _catalog != null && (isPine
                ? (_catalog.pineTreeBillboardSprite != null && _catalog.pineTreeBillboardMaterial != null)
                : (_catalog.oakTreeBillboardSprite != null && _catalog.oakTreeBillboardMaterial != null));

            entry.BillboardQuad.gameObject.SetActive(useBillboard);

            if (useBillboard)
            {
                entry.OakRoot.SetActive(false);
                entry.PineRoot.SetActive(false);
                entry.BillboardQuad.sharedMaterial = isPine ? _catalog.pineTreeBillboardMaterial : _catalog.oakTreeBillboardMaterial;
            }
            else
            {
                Material trunkMat = _catalog != null
                    ? FirstNonNull(_catalog.treeTrunkMaterial, _catalog.timberTrimMaterial, _catalog.crateWoodMaterial)
                    : null;

                if (isPine)
                {
                    Material pineMat = _catalog != null
                        ? FirstNonNull(_catalog.pineFoliageMaterial, _catalog.oakFoliageMaterial, _catalog.treeCoverMaterial)
                        : null;
                    entry.OakRoot.SetActive(false);
                    entry.PineRoot.SetActive(true);
                    entry.PineTrunk.sharedMaterial = trunkMat;
                    entry.PineTierLower.sharedMaterial = pineMat;
                    entry.PineTierMid.sharedMaterial = pineMat;
                    entry.PineTierTop.sharedMaterial = pineMat;
                }
                else
                {
                    Material oakMat = _catalog != null
                        ? FirstNonNull(_catalog.oakFoliageMaterial, _catalog.pineFoliageMaterial, _catalog.treeCoverMaterial)
                        : null;
                    entry.PineRoot.SetActive(false);
                    entry.OakRoot.SetActive(true);
                    entry.OakTrunk.sharedMaterial = trunkMat;
                    entry.OakCanopyLower.sharedMaterial = oakMat;
                    entry.OakCanopyUpper.sharedMaterial = oakMat;
                }
            }

            entry.Sway.ConfigureSeed(x, y);
            entry.Root.SetActive(true);
        }

        private void ConfigureBoulderProp(BoulderPropEntry entry, int x, int y, int variantHash)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);
            float yaw = (variantHash % 4) * 90f;
            float scaleJitter = 0.90f + ((variantHash >> 3) % 15) * 0.01f;
            entry.Root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            entry.Root.transform.localScale = new Vector3(scaleJitter, scaleJitter, scaleJitter);

            Material rockMat = _catalog != null
                ? FirstNonNull(_catalog.boulderMaterial, _catalog.stoneFenceMaterial, _catalog.dungeonWallMaterial)
                : null;
            Material mossMat = _catalog != null
                ? FirstNonNull(_catalog.oakFoliageMaterial, _catalog.treeCoverMaterial, rockMat)
                : null;

            entry.MainRock.sharedMaterial = rockMat;
            entry.SubRock.sharedMaterial = rockMat;
            entry.TopCap.sharedMaterial = rockMat;
            entry.MossShrub.sharedMaterial = mossMat;

            entry.Root.SetActive(true);
        }

        private void ConfigureWildflowerProp(WildflowerPropEntry entry, int x, int y, int variantHash)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);

            bool useBillboard = _catalog != null
                && _catalog.wildflowerBushBillboardSprite != null
                && _catalog.wildflowerBushBillboardMaterial != null;

            entry.ModularRoot.SetActive(!useBillboard);
            entry.BillboardQuad.gameObject.SetActive(useBillboard);

            if (useBillboard)
            {
                entry.BillboardQuad.sharedMaterial = _catalog.wildflowerBushBillboardMaterial;
            }
            else
            {
                Material greenMat = _catalog != null
                    ? FirstNonNull(_catalog.oakFoliageMaterial, _catalog.treeCoverMaterial, _catalog.grassMaterial)
                    : null;
                Material flowerMat = _catalog != null
                    ? FirstNonNull(_catalog.wildflowerMaterial, _catalog.torchEmberMaterial, greenMat)
                    : null;

                entry.Greenery.sharedMaterial = greenMat;
                entry.BlossomA.sharedMaterial = flowerMat;
                entry.BlossomB.sharedMaterial = flowerMat;
                entry.BlossomC.sharedMaterial = flowerMat;
            }

            entry.Sway.ConfigureSeed(x, y);
            entry.Root.SetActive(true);
        }

        private void ConfigureCrateProp(CratePropEntry entry, int x, int y, int variantHash)
        {
            entry.Root.transform.position = new Vector3(x, 0f, -y);

            bool useBillboard = _catalog != null
                && _catalog.marketCratesBillboardSprite != null
                && _catalog.marketCratesBillboardMaterial != null;

            entry.ModularRoot.SetActive(!useBillboard);
            entry.BillboardQuad.gameObject.SetActive(useBillboard);

            if (useBillboard)
            {
                entry.BillboardQuad.sharedMaterial = _catalog.marketCratesBillboardMaterial;
                entry.Root.SetActive(true);
                return;
            }

            Material woodMat = _catalog != null
                ? FirstNonNull(_catalog.crateWoodMaterial, _catalog.treeTrunkMaterial, _catalog.timberTrimMaterial)
                : null;
            Material ironMat = _catalog != null
                ? FirstNonNull(_catalog.ironTrimMaterial, _catalog.stoneFenceMaterial, _catalog.dungeonWallMaterial)
                : null;

            entry.CrateBox.sharedMaterial = woodMat;
            entry.CrateTrim.sharedMaterial = ironMat;
            entry.BarrelBody.sharedMaterial = woodMat;
            entry.BarrelBand.sharedMaterial = ironMat;

            entry.BarrelBody.gameObject.SetActive((variantHash & 1) == 0);
            entry.BarrelBand.gameObject.SetActive((variantHash & 1) == 0);

            entry.Root.SetActive(true);
        }

        private void UpdateWindowPortals(TileMapData map, int minX, int maxX, int minY, int maxY)
        {
            int portalIdx = 0;
            IReadOnlyList<MapPortal> portals = map.Portals;
            if (portals != null)
            {
                Material portalMat = _catalog != null ? _catalog.portalBeaconMaterial : null;
                for (int i = 0; i < portals.Count; i++)
                {
                    MapPortal portal = portals[i];
                    if (portal.X < minX || portal.X > maxX || portal.Y < minY || portal.Y > maxY)
                    {
                        continue;
                    }

                    PortalMarkerEntry entry = GetPortalMarker(portalIdx++);
                    entry.Root.transform.position = new Vector3(portal.X, 0f, -portal.Y);
                    entry.RuneQuad.sharedMaterial = portalMat;
                    entry.BeaconPillar.sharedMaterial = portalMat;
                    entry.Root.SetActive(true);
                }
            }

            for (int i = portalIdx; i < _portalPool.Count; i++)
            {
                _portalPool[i].Root.SetActive(false);
            }
        }

        private void UpdateWindowTorches(GridCoord center, int minX, int maxX, int minY, int maxY, bool isDungeon)
        {
            _selectedTorches.Clear();
            _selectedTorchDistSq.Clear();

            for (int i = 0; i < _mapTorchCoords.Count; i++)
            {
                GridCoord tc = _mapTorchCoords[i];
                if (tc.X < minX || tc.X > maxX || tc.Y < minY || tc.Y > maxY)
                {
                    continue;
                }

                int dx = tc.X - center.X;
                int dy = tc.Y - center.Y;
                int distSq = dx * dx + dy * dy;

                if (_selectedTorches.Count < MaxActiveTorchLights)
                {
                    _selectedTorches.Add(tc);
                    _selectedTorchDistSq.Add(distSq);
                }
                else
                {
                    int worstIdx = 0;
                    int worstDist = _selectedTorchDistSq[0];
                    for (int k = 1; k < _selectedTorchDistSq.Count; k++)
                    {
                        if (_selectedTorchDistSq[k] > worstDist)
                        {
                            worstDist = _selectedTorchDistSq[k];
                            worstIdx = k;
                        }
                    }

                    if (distSq < worstDist)
                    {
                        _selectedTorches[worstIdx] = tc;
                        _selectedTorchDistSq[worstIdx] = distSq;
                    }
                }
            }

            float targetIntensity = isDungeon ? 2.8f : 0.70f;
            float targetRange = isDungeon ? 8.5f : 3.8f;
            float targetFlickerAmp = isDungeon ? 0.35f : 0.14f;

            Material ironMat = _catalog != null
                ? FirstNonNull(_catalog.ironTrimMaterial, _catalog.stoneFenceMaterial, _catalog.dungeonWallMaterial)
                : null;
            bool hasBrazierBillboard = _catalog != null
                && _catalog.brazierBillboardSprite != null
                && _catalog.brazierBillboardMaterial != null;
            Material brazierQuadMat = hasBrazierBillboard
                ? _catalog.brazierBillboardMaterial
                : (_catalog != null ? _catalog.torchPropMaterial : null);
            Material emberMat = _catalog != null ? _catalog.torchEmberMaterial : null;

            for (int i = 0; i < _selectedTorches.Count; i++)
            {
                GridCoord tc = _selectedTorches[i];
                TorchPropEntry entry = GetTorchProp(i);
                entry.Root.transform.position = new Vector3(tc.X + 0.34f, 0f, -tc.Y + 0.34f);

                if (ironMat != null)
                {
                    entry.StandRenderer.sharedMaterial = ironMat;
                    entry.BowlRenderer.sharedMaterial = ironMat;
                }
                entry.StandRenderer.gameObject.SetActive(!hasBrazierBillboard);
                entry.BowlRenderer.gameObject.SetActive(!hasBrazierBillboard);
                entry.EmberRenderer.gameObject.SetActive(!hasBrazierBillboard);
                entry.QuadRenderer.transform.localPosition = hasBrazierBillboard
                    ? new Vector3(0f, 0.54f, 0f)
                    : new Vector3(0f, 0.72f, 0f);
                entry.QuadRenderer.transform.localScale = hasBrazierBillboard
                    ? new Vector3(1.12f, 1.12f, 1f)
                    : new Vector3(0.88f, 0.88f, 1f);
                entry.QuadRenderer.sharedMaterial = brazierQuadMat;
                entry.EmberRenderer.sharedMaterial = emberMat;

                entry.PointLight.intensity = targetIntensity;
                entry.PointLight.range = targetRange;
                entry.Flicker.baseIntensity = targetIntensity;
                entry.Flicker.flickerAmplitude = targetFlickerAmp;

                entry.Root.SetActive(true);
            }

            for (int i = _selectedTorches.Count; i < _torchPool.Count; i++)
            {
                _torchPool[i].Root.SetActive(false);
            }
        }

        private void BuildTorchLocations(TileMapData map)
        {
            _mapTorchCoords.Clear();

            GridCoord[] hints = map.IsDungeon ? DungeonTorchHints : LandTorchHints;
            for (int i = 0; i < hints.Length; i++)
            {
                TryAddTorchNear(map, hints[i], minSpacing: 3);
            }

            IReadOnlyList<MapPortal> portals = map.Portals;
            if (portals != null)
            {
                for (int i = 0; i < portals.Count; i++)
                {
                    TryAddTorchNear(map, portals[i].Coord, minSpacing: 3);
                }
            }

            IReadOnlyList<MapPointOfInterest> pois = map.PointsOfInterest;
            if (pois != null)
            {
                for (int i = 0; i < pois.Count; i++)
                {
                    TryAddTorchNear(map, pois[i].Coord, minSpacing: 3);
                }
            }

            if (map.IsDungeon)
            {
                for (int cy = 0; cy < map.Height; cy += 7)
                {
                    for (int cx = 0; cx < map.Width; cx += 7)
                    {
                        if (TryFindJunctionInCell(map, cx, cy, 7, out GridCoord junction))
                        {
                            TryAddTorchNear(map, junction, minSpacing: 5);
                        }
                    }
                }
            }
        }

        private void TryAddTorchNear(TileMapData map, GridCoord target, int minSpacing)
        {
            if (!TryFindWalkableSpot(map, target, out GridCoord spot))
            {
                return;
            }

            for (int i = 0; i < _mapTorchCoords.Count; i++)
            {
                if (_mapTorchCoords[i].ChebyshevDistance(spot) < minSpacing)
                {
                    return;
                }
            }

            _mapTorchCoords.Add(spot);
        }

        private static bool TryFindWalkableSpot(TileMapData map, GridCoord origin, out GridCoord result)
        {
            for (int r = 0; r <= 2; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = origin.X + dx;
                        int y = origin.Y + dy;
                        if (!map.InBounds(x, y))
                        {
                            continue;
                        }

                        if (!IsSolidStructure(BywaterTileClassifier.Classify(map, x, y)))
                        {
                            result = new GridCoord(x, y);
                            return true;
                        }
                    }
                }
            }

            result = default;
            return false;
        }

        private static bool TryFindJunctionInCell(TileMapData map, int startX, int startY, int cellSize, out GridCoord bestCoord)
        {
            int endX = Mathf.Min(map.Width - 1, startX + cellSize - 1);
            int endY = Mathf.Min(map.Height - 1, startY + cellSize - 1);
            int bestNeighbors = -1;
            bestCoord = default;

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (!map.IsPassable(x, y) || IsSolidStructure(BywaterTileClassifier.Classify(map, x, y)))
                    {
                        continue;
                    }

                    int openNeighbors = 0;
                    if (map.InBounds(x + 1, y) && map.IsPassable(x + 1, y)) openNeighbors++;
                    if (map.InBounds(x - 1, y) && map.IsPassable(x - 1, y)) openNeighbors++;
                    if (map.InBounds(x, y + 1) && map.IsPassable(x, y + 1)) openNeighbors++;
                    if (map.InBounds(x, y - 1) && map.IsPassable(x, y - 1)) openNeighbors++;

                    if (openNeighbors > bestNeighbors)
                    {
                        bestNeighbors = openNeighbors;
                        bestCoord = new GridCoord(x, y);
                    }
                }
            }

            return bestNeighbors >= 2;
        }

        private static bool IsSolidStructure(BywaterTileKind kind)
        {
            return kind == BywaterTileKind.WallBlock
                || kind == BywaterTileKind.DungeonWall
                || kind == BywaterTileKind.TownBuildingSolid
                || kind == BywaterTileKind.ShallowWater;
        }

        private Material ResolveFloorMaterial(BywaterTileKind kind, bool isDungeon, int variantIdx)
        {
            if (_catalog == null)
            {
                return null;
            }

            switch (kind)
            {
                case BywaterTileKind.Grass:
                case BywaterTileKind.ForestCover:
                    return _catalog.GetTileVariantMaterial(_catalog.grassMaterial, _catalog.grassVariantMaterials, variantIdx);
                case BywaterTileKind.Cobblestone:
                case BywaterTileKind.TownBuildingDoor:
                    return _catalog.GetTileVariantMaterial(_catalog.cobblestoneMaterial, _catalog.cobblestoneVariantMaterials, variantIdx);
                case BywaterTileKind.DirtPath:
                    return _catalog.GetTileVariantMaterial(_catalog.dirtPathMaterial, _catalog.dirtPathVariantMaterials, variantIdx);
                case BywaterTileKind.DungeonFloor:
                case BywaterTileKind.DungeonDoor:
                case BywaterTileKind.DungeonStairs:
                    return _catalog.GetTileVariantMaterial(_catalog.dungeonFloorMaterial, _catalog.dungeonFloorVariantMaterials, variantIdx);
                case BywaterTileKind.ShallowWater:
                    return _catalog.shallowWaterMaterial;
                default:
                    return isDungeon
                        ? _catalog.GetTileVariantMaterial(_catalog.dungeonFloorMaterial, _catalog.dungeonFloorVariantMaterials, variantIdx)
                        : _catalog.GetTileVariantMaterial(_catalog.grassMaterial, _catalog.grassVariantMaterials, variantIdx);
            }
        }

        private static Material FirstNonNull(Material a, Material b = null, Material c = null)
        {
            if (a != null) return a;
            if (b != null) return b;
            return c;
        }

        private void WarmupPools()
        {
            _floorPropBlock ??= new MaterialPropertyBlock();

            for (int i = _floorPool.Count; i < 1089; i++)
            {
                MeshRenderer r = CreateFloorQuad(i);
                r.gameObject.SetActive(false);
                _floorPool.Add(r);
            }

            for (int i = _blockPool.Count; i < 1400; i++)
            {
                MeshRenderer r = CreateBlockCube(i);
                r.gameObject.SetActive(false);
                _blockPool.Add(r);
            }

            for (int i = _cottagePool.Count; i < 128; i++)
            {
                CottagePropEntry e = CreateCottageProp(i);
                e.Root.SetActive(false);
                _cottagePool.Add(e);
            }

            for (int i = _fencePool.Count; i < 96; i++)
            {
                FencePropEntry e = CreateFenceProp(i);
                e.Root.SetActive(false);
                _fencePool.Add(e);
            }

            for (int i = _treePool.Count; i < 160; i++)
            {
                TreePropEntry e = CreateTreeProp(i);
                e.Root.SetActive(false);
                _treePool.Add(e);
            }

            for (int i = _boulderPool.Count; i < 128; i++)
            {
                BoulderPropEntry e = CreateBoulderProp(i);
                e.Root.SetActive(false);
                _boulderPool.Add(e);
            }

            for (int i = _wildflowerPool.Count; i < 96; i++)
            {
                WildflowerPropEntry e = CreateWildflowerProp(i);
                e.Root.SetActive(false);
                _wildflowerPool.Add(e);
            }

            for (int i = _cratePool.Count; i < 32; i++)
            {
                CratePropEntry e = CreateCrateProp(i);
                e.Root.SetActive(false);
                _cratePool.Add(e);
            }

            for (int i = _torchPool.Count; i < MaxActiveTorchLights; i++)
            {
                TorchPropEntry entry = CreateTorchProp(i);
                entry.Root.SetActive(false);
                _torchPool.Add(entry);
            }

            for (int i = _portalPool.Count; i < 16; i++)
            {
                PortalMarkerEntry entry = CreatePortalMarker(i);
                entry.Root.SetActive(false);
                _portalPool.Add(entry);
            }
        }

        private MeshRenderer GetFloorQuad(int index)
        {
            while (_floorPool.Count <= index)
            {
                MeshRenderer r = CreateFloorQuad(_floorPool.Count);
                _floorPool.Add(r);
            }
            return _floorPool[index];
        }

        private MeshRenderer GetBlockCube(int index)
        {
            while (_blockPool.Count <= index)
            {
                MeshRenderer r = CreateBlockCube(_blockPool.Count);
                _blockPool.Add(r);
            }
            return _blockPool[index];
        }

        private CottagePropEntry GetCottageProp(int index)
        {
            while (_cottagePool.Count <= index)
            {
                CottagePropEntry e = CreateCottageProp(_cottagePool.Count);
                _cottagePool.Add(e);
            }
            return _cottagePool[index];
        }

        private FencePropEntry GetFenceProp(int index)
        {
            while (_fencePool.Count <= index)
            {
                FencePropEntry e = CreateFenceProp(_fencePool.Count);
                _fencePool.Add(e);
            }
            return _fencePool[index];
        }

        private TreePropEntry GetTreeProp(int index)
        {
            while (_treePool.Count <= index)
            {
                TreePropEntry e = CreateTreeProp(_treePool.Count);
                _treePool.Add(e);
            }
            return _treePool[index];
        }

        private BoulderPropEntry GetBoulderProp(int index)
        {
            while (_boulderPool.Count <= index)
            {
                BoulderPropEntry e = CreateBoulderProp(_boulderPool.Count);
                _boulderPool.Add(e);
            }
            return _boulderPool[index];
        }

        private WildflowerPropEntry GetWildflowerProp(int index)
        {
            while (_wildflowerPool.Count <= index)
            {
                WildflowerPropEntry e = CreateWildflowerProp(_wildflowerPool.Count);
                _wildflowerPool.Add(e);
            }
            return _wildflowerPool[index];
        }

        private CratePropEntry GetCrateProp(int index)
        {
            while (_cratePool.Count <= index)
            {
                CratePropEntry e = CreateCrateProp(_cratePool.Count);
                _cratePool.Add(e);
            }
            return _cratePool[index];
        }

        private TorchPropEntry GetTorchProp(int index)
        {
            while (_torchPool.Count <= index)
            {
                TorchPropEntry entry = CreateTorchProp(_torchPool.Count);
                _torchPool.Add(entry);
            }
            return _torchPool[index];
        }

        private PortalMarkerEntry GetPortalMarker(int index)
        {
            while (_portalPool.Count <= index)
            {
                PortalMarkerEntry entry = CreatePortalMarker(_portalPool.Count);
                _portalPool.Add(entry);
            }
            return _portalPool[index];
        }

        private static void DeactivateUnused(List<MeshRenderer> pool, int activeCount)
        {
            for (int i = activeCount; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        private void DeactivateUnusedCottages(int activeCount)
        {
            for (int i = activeCount; i < _cottagePool.Count; i++)
            {
                _cottagePool[i].Root.SetActive(false);
            }
        }

        private void DeactivateUnusedFences(int activeCount)
        {
            for (int i = activeCount; i < _fencePool.Count; i++)
            {
                _fencePool[i].Root.SetActive(false);
            }
        }

        private void DeactivateUnusedTrees(int activeCount)
        {
            for (int i = activeCount; i < _treePool.Count; i++)
            {
                _treePool[i].Root.SetActive(false);
            }
        }

        private void DeactivateUnusedBoulders(int activeCount)
        {
            for (int i = activeCount; i < _boulderPool.Count; i++)
            {
                _boulderPool[i].Root.SetActive(false);
            }
        }

        private void DeactivateUnusedWildflowers(int activeCount)
        {
            for (int i = activeCount; i < _wildflowerPool.Count; i++)
            {
                _wildflowerPool[i].Root.SetActive(false);
            }
        }

        private void DeactivateUnusedCrates(int activeCount)
        {
            for (int i = activeCount; i < _cratePool.Count; i++)
            {
                _cratePool[i].Root.SetActive(false);
            }
        }

        private MeshRenderer CreateFloorQuad(int index)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TileFloor_" + index;
            go.transform.SetParent(_floorContainer, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one;
            DestroyCollider(go);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        private MeshRenderer CreateBlockCube(int index)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TileBlock_" + index;
            go.transform.SetParent(_structureContainer, false);
            DestroyCollider(go);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private CottagePropEntry CreateCottageProp(int index)
        {
            GameObject root = new GameObject("CottageProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            GameObject modular = new GameObject("Modular");
            modular.transform.SetParent(root.transform, false);

            MeshRenderer plinth = CreatePrimitivePiece(PrimitiveType.Cube, "Plinth", modular.transform, new Vector3(0f, 0.11f, 0f), new Vector3(1.02f, 0.22f, 1.02f), Quaternion.identity);
            MeshRenderer wallBody = CreatePrimitivePiece(PrimitiveType.Cube, "WallBody", modular.transform, new Vector3(0f, 0.57f, 0f), new Vector3(0.94f, 0.70f, 0.94f), Quaternion.identity);
            MeshRenderer postFL = CreatePrimitivePiece(PrimitiveType.Cube, "PostFL", modular.transform, new Vector3(-0.45f, 0.57f, -0.45f), new Vector3(0.09f, 0.72f, 0.09f), Quaternion.identity);
            MeshRenderer postFR = CreatePrimitivePiece(PrimitiveType.Cube, "PostFR", modular.transform, new Vector3(0.45f, 0.57f, -0.45f), new Vector3(0.09f, 0.72f, 0.09f), Quaternion.identity);
            MeshRenderer postBL = CreatePrimitivePiece(PrimitiveType.Cube, "PostBL", modular.transform, new Vector3(-0.45f, 0.57f, 0.45f), new Vector3(0.09f, 0.72f, 0.09f), Quaternion.identity);
            MeshRenderer postBR = CreatePrimitivePiece(PrimitiveType.Cube, "PostBR", modular.transform, new Vector3(0.45f, 0.57f, 0.45f), new Vector3(0.09f, 0.72f, 0.09f), Quaternion.identity);
            MeshRenderer timberBeam = CreatePrimitivePiece(PrimitiveType.Cube, "TimberBeam", modular.transform, new Vector3(0f, 0.93f, 0f), new Vector3(1.00f, 0.08f, 1.00f), Quaternion.identity);
            MeshRenderer gablePeak = CreatePrimitivePiece(PrimitiveType.Cube, "GablePeak", modular.transform, new Vector3(0f, 0.97f, 0f), new Vector3(0.58f, 0.58f, 0.90f), Quaternion.Euler(0f, 0f, 45f));
            MeshRenderer roofLeft = CreatePrimitivePiece(PrimitiveType.Cube, "RoofLeft", modular.transform, new Vector3(-0.27f, 1.21f, 0f), new Vector3(0.74f, 0.08f, 1.08f), Quaternion.Euler(0f, 0f, 38f));
            MeshRenderer roofRight = CreatePrimitivePiece(PrimitiveType.Cube, "RoofRight", modular.transform, new Vector3(0.27f, 1.21f, 0f), new Vector3(0.74f, 0.08f, 1.08f), Quaternion.Euler(0f, 0f, -38f));
            MeshRenderer ridgeBeam = CreatePrimitivePiece(PrimitiveType.Cube, "RidgeBeam", modular.transform, new Vector3(0f, 1.46f, 0f), new Vector3(0.10f, 0.10f, 1.10f), Quaternion.identity);
            MeshRenderer windowFront = CreatePrimitivePiece(PrimitiveType.Cube, "WindowFront", modular.transform, new Vector3(0f, 0.56f, -0.475f), new Vector3(0.30f, 0.28f, 0.03f), Quaternion.identity, castShadows: false);

            MeshRenderer billboardQuad = CreateBillboardQuad("CottageBillboard", root.transform, new Vector3(0f, 0.86f, 0f), new Vector3(1.76f, 1.76f, 1f));
            billboardQuad.gameObject.SetActive(false);

            return new CottagePropEntry
            {
                Root = root,
                ModularRoot = modular,
                Plinth = plinth,
                WallBody = wallBody,
                PostFL = postFL,
                PostFR = postFR,
                PostBL = postBL,
                PostBR = postBR,
                TimberBeam = timberBeam,
                RoofLeft = roofLeft,
                RoofRight = roofRight,
                GablePeak = gablePeak,
                RidgeBeam = ridgeBeam,
                WindowFront = windowFront,
                BillboardQuad = billboardQuad
            };
        }

        private FencePropEntry CreateFenceProp(int index)
        {
            GameObject root = new GameObject("FenceProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            GameObject modular = new GameObject("Modular");
            modular.transform.SetParent(root.transform, false);

            MeshRenderer wall = CreatePrimitivePiece(PrimitiveType.Cube, "FenceWall", modular.transform, new Vector3(0f, 0.27f, 0f), new Vector3(0.96f, 0.54f, 0.42f), Quaternion.identity);
            MeshRenderer coping = CreatePrimitivePiece(PrimitiveType.Cube, "FenceCoping", modular.transform, new Vector3(0f, 0.56f, 0f), new Vector3(1.00f, 0.08f, 0.60f), Quaternion.identity);
            MeshRenderer post = CreatePrimitivePiece(PrimitiveType.Cube, "FencePost", modular.transform, new Vector3(-0.38f, 0.36f, 0f), new Vector3(0.24f, 0.72f, 0.54f), Quaternion.identity);

            MeshRenderer billboardQuad = CreateBillboardQuad("FenceBillboard", root.transform, new Vector3(0f, 0.48f, 0f), new Vector3(1.16f, 0.96f, 1f));
            billboardQuad.gameObject.SetActive(false);

            return new FencePropEntry
            {
                Root = root,
                ModularRoot = modular,
                Wall = wall,
                Coping = coping,
                Post = post,
                BillboardQuad = billboardQuad
            };
        }

        private TreePropEntry CreateTreeProp(int index)
        {
            GameObject root = new GameObject("TreeProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            GameObject swayGo = new GameObject("SwayTarget");
            swayGo.transform.SetParent(root.transform, false);
            BywaterPropSway sway = swayGo.AddComponent<BywaterPropSway>();
            sway.targetTransform = swayGo.transform;
            sway.swayAmplitudeDeg = 2.4f;
            sway.swaySpeed = 1.35f;
            sway.bobAmplitude = 0.012f;
            sway.CacheBasePose();

            GameObject oakRoot = new GameObject("Oak");
            oakRoot.transform.SetParent(swayGo.transform, false);
            MeshRenderer oakTrunk = CreatePrimitivePiece(PrimitiveType.Cylinder, "OakTrunk", oakRoot.transform, new Vector3(0f, 0.34f, 0f), new Vector3(0.22f, 0.34f, 0.22f), Quaternion.identity);
            MeshRenderer oakLower = CreatePrimitivePiece(PrimitiveType.Sphere, "OakCanopyLower", oakRoot.transform, new Vector3(-0.04f, 0.92f, 0.02f), new Vector3(0.94f, 0.68f, 0.90f), Quaternion.identity);
            MeshRenderer oakUpper = CreatePrimitivePiece(PrimitiveType.Sphere, "OakCanopyUpper", oakRoot.transform, new Vector3(0.14f, 1.24f, -0.06f), new Vector3(0.68f, 0.56f, 0.66f), Quaternion.identity);

            GameObject pineRoot = new GameObject("Pine");
            pineRoot.transform.SetParent(swayGo.transform, false);
            MeshRenderer pineTrunk = CreatePrimitivePiece(PrimitiveType.Cylinder, "PineTrunk", pineRoot.transform, new Vector3(0f, 0.32f, 0f), new Vector3(0.18f, 0.32f, 0.18f), Quaternion.identity);
            MeshRenderer pineLower = CreatePrimitivePiece(PrimitiveType.Cube, "PineTierLower", pineRoot.transform, new Vector3(0f, 0.66f, 0f), new Vector3(0.88f, 0.34f, 0.88f), Quaternion.Euler(0f, 15f, 0f));
            MeshRenderer pineMid = CreatePrimitivePiece(PrimitiveType.Cube, "PineTierMid", pineRoot.transform, new Vector3(0f, 0.98f, 0f), new Vector3(0.64f, 0.36f, 0.64f), Quaternion.Euler(0f, 45f, 0f));
            MeshRenderer pineTop = CreatePrimitivePiece(PrimitiveType.Cube, "PineTierTop", pineRoot.transform, new Vector3(0f, 1.32f, 0f), new Vector3(0.38f, 0.40f, 0.38f), Quaternion.Euler(0f, 20f, 0f));

            MeshRenderer billboardQuad = CreateBillboardQuad("TreeBillboard", swayGo.transform, new Vector3(0f, 0.90f, 0f), new Vector3(1.82f, 1.82f, 1f));
            billboardQuad.gameObject.SetActive(false);

            return new TreePropEntry
            {
                Root = root,
                SwayTarget = swayGo.transform,
                Sway = sway,
                OakRoot = oakRoot,
                OakTrunk = oakTrunk,
                OakCanopyLower = oakLower,
                OakCanopyUpper = oakUpper,
                PineRoot = pineRoot,
                PineTrunk = pineTrunk,
                PineTierLower = pineLower,
                PineTierMid = pineMid,
                PineTierTop = pineTop,
                BillboardQuad = billboardQuad
            };
        }

        private BoulderPropEntry CreateBoulderProp(int index)
        {
            GameObject root = new GameObject("BoulderProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            MeshRenderer mainRock = CreatePrimitivePiece(PrimitiveType.Cube, "MainRock", root.transform, new Vector3(-0.06f, 0.36f, 0.04f), new Vector3(0.76f, 0.72f, 0.72f), Quaternion.Euler(6f, 18f, -5f));
            MeshRenderer subRock = CreatePrimitivePiece(PrimitiveType.Cube, "SubRock", root.transform, new Vector3(0.22f, 0.24f, -0.14f), new Vector3(0.50f, 0.46f, 0.48f), Quaternion.Euler(-8f, 42f, 10f));
            MeshRenderer topCap = CreatePrimitivePiece(PrimitiveType.Cube, "TopCap", root.transform, new Vector3(-0.10f, 0.68f, 0.08f), new Vector3(0.44f, 0.28f, 0.42f), Quaternion.Euler(4f, -25f, 8f));
            MeshRenderer mossShrub = CreatePrimitivePiece(PrimitiveType.Sphere, "MossShrub", root.transform, new Vector3(0.24f, 0.42f, -0.18f), new Vector3(0.32f, 0.24f, 0.32f), Quaternion.identity);

            return new BoulderPropEntry
            {
                Root = root,
                MainRock = mainRock,
                SubRock = subRock,
                TopCap = topCap,
                MossShrub = mossShrub
            };
        }

        private WildflowerPropEntry CreateWildflowerProp(int index)
        {
            GameObject root = new GameObject("WildflowerProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            GameObject swayGo = new GameObject("SwayTarget");
            swayGo.transform.SetParent(root.transform, false);
            swayGo.transform.localPosition = new Vector3(0.06f, 0f, 0.06f);

            BywaterPropSway sway = swayGo.AddComponent<BywaterPropSway>();
            sway.targetTransform = swayGo.transform;
            sway.swayAmplitudeDeg = 2.0f;
            sway.swaySpeed = 1.65f;
            sway.bobAmplitude = 0.008f;
            sway.CacheBasePose();

            GameObject modular = new GameObject("Modular");
            modular.transform.SetParent(swayGo.transform, false);

            MeshRenderer greenery = CreatePrimitivePiece(PrimitiveType.Sphere, "Greenery", modular.transform, new Vector3(0f, 0.07f, 0f), new Vector3(0.24f, 0.14f, 0.22f), Quaternion.identity, castShadows: false);
            MeshRenderer blossomA = CreatePrimitivePiece(PrimitiveType.Sphere, "BlossomA", modular.transform, new Vector3(-0.07f, 0.16f, 0.04f), new Vector3(0.08f, 0.08f, 0.08f), Quaternion.identity, castShadows: false);
            MeshRenderer blossomB = CreatePrimitivePiece(PrimitiveType.Sphere, "BlossomB", modular.transform, new Vector3(0.06f, 0.18f, -0.04f), new Vector3(0.08f, 0.08f, 0.08f), Quaternion.identity, castShadows: false);
            MeshRenderer blossomC = CreatePrimitivePiece(PrimitiveType.Sphere, "BlossomC", modular.transform, new Vector3(0.01f, 0.15f, 0.07f), new Vector3(0.07f, 0.07f, 0.07f), Quaternion.identity, castShadows: false);

            MeshRenderer billboardQuad = CreateBillboardQuad("WildflowerBillboard", swayGo.transform, new Vector3(0f, 0.34f, 0f), new Vector3(0.70f, 0.70f, 1f), castShadows: false);
            billboardQuad.gameObject.SetActive(false);

            return new WildflowerPropEntry
            {
                Root = root,
                SwayTarget = swayGo.transform,
                Sway = sway,
                ModularRoot = modular,
                Greenery = greenery,
                BlossomA = blossomA,
                BlossomB = blossomB,
                BlossomC = blossomC,
                BillboardQuad = billboardQuad
            };
        }

        private CratePropEntry CreateCrateProp(int index)
        {
            GameObject root = new GameObject("CrateProp_" + index);
            root.transform.SetParent(_propsContainer, false);

            GameObject modular = new GameObject("Modular");
            modular.transform.SetParent(root.transform, false);

            MeshRenderer crateBox = CreatePrimitivePiece(PrimitiveType.Cube, "CrateBox", modular.transform, new Vector3(0.20f, 0.13f, 0.38f), new Vector3(0.24f, 0.26f, 0.24f), Quaternion.Euler(0f, 12f, 0f));
            MeshRenderer crateTrim = CreatePrimitivePiece(PrimitiveType.Cube, "CrateTrim", modular.transform, new Vector3(0.20f, 0.13f, 0.38f), new Vector3(0.26f, 0.05f, 0.26f), Quaternion.Euler(0f, 12f, 0f));
            MeshRenderer barrelBody = CreatePrimitivePiece(PrimitiveType.Cylinder, "BarrelBody", modular.transform, new Vector3(-0.16f, 0.15f, 0.38f), new Vector3(0.20f, 0.15f, 0.20f), Quaternion.identity);
            MeshRenderer barrelBand = CreatePrimitivePiece(PrimitiveType.Cylinder, "BarrelBand", modular.transform, new Vector3(-0.16f, 0.19f, 0.38f), new Vector3(0.22f, 0.03f, 0.22f), Quaternion.identity);

            MeshRenderer billboardQuad = CreateBillboardQuad("CrateBillboard", root.transform, new Vector3(0f, 0.42f, 0.24f), new Vector3(0.88f, 0.88f, 1f));
            billboardQuad.gameObject.SetActive(false);

            return new CratePropEntry
            {
                Root = root,
                ModularRoot = modular,
                CrateBox = crateBox,
                CrateTrim = crateTrim,
                BarrelBody = barrelBody,
                BarrelBand = barrelBand,
                BillboardQuad = billboardQuad
            };
        }

        private static MeshRenderer CreatePrimitivePiece(
            PrimitiveType primitiveType,
            string pieceName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            bool castShadows = true)
        {
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.name = pieceName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRotation;
            DestroyCollider(go);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static MeshRenderer CreateBillboardQuad(
            string quadName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            bool castShadows = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = quadName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            DestroyCollider(go);
            go.AddComponent<PixelBillboard>();

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        private TorchPropEntry CreateTorchProp(int index)
        {
            GameObject root = new GameObject("TorchProp_" + index);
            root.transform.SetParent(_torchContainer, false);

            MeshRenderer standRenderer = CreatePrimitivePiece(
                PrimitiveType.Cylinder,
                "BrazierStand",
                root.transform,
                new Vector3(0f, 0.28f, 0f),
                new Vector3(0.11f, 0.28f, 0.11f),
                Quaternion.identity);

            MeshRenderer bowlRenderer = CreatePrimitivePiece(
                PrimitiveType.Cylinder,
                "BrazierBowl",
                root.transform,
                new Vector3(0f, 0.56f, 0f),
                new Vector3(0.36f, 0.08f, 0.36f),
                Quaternion.identity);

            GameObject quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "TorchBillboard";
            quadGo.transform.SetParent(root.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            quadGo.transform.localScale = new Vector3(0.88f, 0.88f, 1f);
            DestroyCollider(quadGo);
            quadGo.AddComponent<PixelBillboard>();
            MeshRenderer quadRenderer = quadGo.GetComponent<MeshRenderer>();
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            quadRenderer.receiveShadows = true;

            GameObject emberGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            emberGo.name = "TorchEmber";
            emberGo.transform.SetParent(root.transform, false);
            emberGo.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            emberGo.transform.localScale = new Vector3(0.16f, 0.22f, 0.16f);
            DestroyCollider(emberGo);
            MeshRenderer emberRenderer = emberGo.GetComponent<MeshRenderer>();
            emberRenderer.shadowCastingMode = ShadowCastingMode.Off;
            emberRenderer.receiveShadows = false;

            GameObject lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.35f, -0.45f);
            Light pointLight = lightGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1.0f, 0.580f, 0.239f, 1f);
            pointLight.range = 7.5f;
            pointLight.intensity = 6.2f;
            pointLight.shadows = LightShadows.None;

            BywaterTorchFlicker flicker = lightGo.AddComponent<BywaterTorchFlicker>();
            flicker.baseIntensity = 6.2f;
            flicker.flickerAmplitude = 0.75f;
            flicker.flickerSpeed = 6.5f;
            flicker.emberTransform = emberGo.transform;

            return new TorchPropEntry
            {
                Root = root,
                StandRenderer = standRenderer,
                BowlRenderer = bowlRenderer,
                QuadRenderer = quadRenderer,
                EmberRenderer = emberRenderer,
                PointLight = pointLight,
                Flicker = flicker
            };
        }

        private PortalMarkerEntry CreatePortalMarker(int index)
        {
            GameObject root = new GameObject("PortalRune_" + index);
            root.transform.SetParent(_portalContainer, false);

            GameObject runeGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            runeGo.name = "RuneFloor";
            runeGo.transform.SetParent(root.transform, false);
            runeGo.transform.localPosition = new Vector3(0f, 0.024f, 0f);
            runeGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            runeGo.transform.localScale = new Vector3(0.86f, 0.86f, 1f);
            DestroyCollider(runeGo);
            MeshRenderer runeRenderer = runeGo.GetComponent<MeshRenderer>();
            runeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            runeRenderer.receiveShadows = false;

            GameObject pillarGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillarGo.name = "RuneBeacon";
            pillarGo.transform.SetParent(root.transform, false);
            pillarGo.transform.localPosition = new Vector3(-0.34f, 0.425f, 0.34f);
            pillarGo.transform.localScale = new Vector3(0.16f, 0.85f, 0.16f);
            DestroyCollider(pillarGo);
            MeshRenderer pillarRenderer = pillarGo.GetComponent<MeshRenderer>();
            pillarRenderer.shadowCastingMode = ShadowCastingMode.Off;
            pillarRenderer.receiveShadows = false;

            return new PortalMarkerEntry
            {
                Root = root,
                RuneQuad = runeRenderer,
                BeaconPillar = pillarRenderer
            };
        }

        private void EnsureArenaMarkers()
        {
            while (_arenaBlocks.Count < 8)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "ArenaMarker_" + _arenaBlocks.Count;
                go.transform.SetParent(_arenaContainer, false);
                DestroyCollider(go);
                MeshRenderer r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _arenaBlocks.Add(r);
            }
        }

        private void EnsureContainers()
        {
            if (_floorContainer == null) _floorContainer = CreateChildContainer("Floors");
            if (_structureContainer == null) _structureContainer = CreateChildContainer("Structures");
            if (_propsContainer == null) _propsContainer = CreateChildContainer("Props");
            if (_torchContainer == null) _torchContainer = CreateChildContainer("Torches");
            if (_portalContainer == null) _portalContainer = CreateChildContainer("Portals");
            if (_arenaContainer == null) _arenaContainer = CreateChildContainer("CombatArenaFrame");

            if (_terrainMeshFilter == null || _terrainMeshRenderer == null)
            {
                Transform terrainTransform = CreateChildContainer("TerrainGround");
                _terrainMeshFilter = terrainTransform.GetComponent<MeshFilter>();
                if (_terrainMeshFilter == null)
                {
                    _terrainMeshFilter = terrainTransform.gameObject.AddComponent<MeshFilter>();
                }

                _terrainMeshRenderer = terrainTransform.GetComponent<MeshRenderer>();
                if (_terrainMeshRenderer == null)
                {
                    _terrainMeshRenderer = terrainTransform.gameObject.AddComponent<MeshRenderer>();
                }

                _terrainMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _terrainMeshRenderer.receiveShadows = true;
                terrainTransform.gameObject.SetActive(false);
            }

            if (_groundScatter == null)
            {
                Transform scatterTransform = CreateChildContainer("GroundScatter");
                _groundScatter = scatterTransform.GetComponent<BywaterGroundScatter>();
                if (_groundScatter == null)
                {
                    _groundScatter = scatterTransform.gameObject.AddComponent<BywaterGroundScatter>();
                }
            }
        }

        private void SyncScatterMaterials()
        {
            if (_groundScatter == null)
            {
                return;
            }

            Material grassMat = _catalog != null && _catalog.grassTuftMaterial != null
                ? _catalog.grassTuftMaterial
                : (_fallbackGrassTuftMat ??= BywaterGroundScatter.CreateDefaultGrassTuftMaterial());

            Material flowerMat = _catalog != null && _catalog.wildflowerTuftMaterial != null
                ? _catalog.wildflowerTuftMaterial
                : (_fallbackFlowerTuftMat ??= BywaterGroundScatter.CreateDefaultFlowerMaterial());

            Material pebbleMat = _catalog != null && _catalog.pebbleScatterMaterial != null
                ? _catalog.pebbleScatterMaterial
                : (_fallbackPebbleScatterMat ??= BywaterGroundScatter.CreateDefaultPebbleMaterial());

            _groundScatter.SetMaterials(grassMat, flowerMat, pebbleMat);
        }

        private Material ResolveTerrainBlendMaterial(TileMapData map)
        {
            if (_catalog == null || map == null)
            {
                return null;
            }

            Material mat = map.IsDungeon
                ? _catalog.terrainBlendDungeonMaterial
                : _catalog.terrainBlendOverworldMaterial;
            if (mat == null || !mat.HasProperty(Layer0AlbedoId) || mat.GetTexture(Layer0AlbedoId) == null)
            {
                return null;
            }

            return mat;
        }

        private void ApplyTerrainPropertyBlock()
        {
            if (_terrainMeshRenderer == null || _currentMap == null || _splatTexture == null)
            {
                return;
            }

            _terrainPropBlock ??= new MaterialPropertyBlock();
            _terrainPropBlock.Clear();
            _terrainPropBlock.SetTexture(SplatMapId, _splatTexture);
            _terrainPropBlock.SetVector(WorldBoundsId, new Vector4(
                -0.5f,
                -(_currentMap.Height - 0.5f),
                1.0f / Mathf.Max(1, _currentMap.Width),
                1.0f / Mathf.Max(1, _currentMap.Height)));
            _terrainPropBlock.SetFloat(GridHintStrengthId, _terrainGridHintStrength);
            _terrainMeshRenderer.SetPropertyBlock(_terrainPropBlock);
        }

        private void EnsureSplatTexture(TileMapData map)
        {
            if (map == null || map.Width <= 0 || map.Height <= 0)
            {
                return;
            }

            int texW = map.Width * 2;
            int texH = map.Height * 2;
            if (_splatTexture != null &&
                ReferenceEquals(_splatBuiltForMap, map) &&
                _splatTexture.width == texW &&
                _splatTexture.height == texH)
            {
                return;
            }

            if (_splatTexture == null || _splatTexture.width != texW || _splatTexture.height != texH)
            {
                DestroyResource(_splatTexture);
                _splatTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, mipChain: false, linear: true);
                _splatTexture.name = "BywaterTerrainSplat";
                _splatTexture.filterMode = FilterMode.Bilinear;
                _splatTexture.wrapMode = TextureWrapMode.Clamp;
            }

            int count = texW * texH;
            Color[] raw = new Color[count];
            bool isDungeon = map.IsDungeon;

            for (int sy = 0; sy < texH; sy++)
            {
                int y = sy >> 1;
                int subY = sy & 1;
                for (int sx = 0; sx < texW; sx++)
                {
                    int x = sx >> 1;
                    int subX = sx & 1;
                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);

                    Color w;
                    if (isDungeon)
                    {
                        if (kind == BywaterTileKind.DungeonWall || kind == BywaterTileKind.WallBlock)
                        {
                            w = new Color(0f, 1f, 0f, 0f);
                        }
                        else
                        {
                            int edgeNx = subX == 0 ? x - 1 : x + 1;
                            int edgeNy = subY == 0 ? y - 1 : y + 1;
                            bool subAdjacentWall = IsDungeonWallTile(map, edgeNx, y)
                                || IsDungeonWallTile(map, x, edgeNy)
                                || IsDungeonWallTile(map, edgeNx, edgeNy);

                            if (subAdjacentWall)
                            {
                                w = new Color(0.54f, 0.28f, 0.18f, 0f);
                            }
                            else if (IsDungeonWallTile(map, x - 1, y)
                                || IsDungeonWallTile(map, x + 1, y)
                                || IsDungeonWallTile(map, x, y - 1)
                                || IsDungeonWallTile(map, x, y + 1))
                            {
                                w = new Color(0.78f, 0.14f, 0.08f, 0f);
                            }
                            else
                            {
                                w = new Color(1f, 0f, 0f, 0f);
                            }
                        }
                    }
                    else
                    {
                        if (kind == BywaterTileKind.ShallowWater)
                        {
                            w = new Color(0f, 0f, 0f, 1f);
                        }
                        else
                        {
                            switch (kind)
                            {
                                case BywaterTileKind.Cobblestone:
                                case BywaterTileKind.TownBuildingDoor:
                                case BywaterTileKind.TownBuildingSolid:
                                    w = new Color(0f, 1f, 0f, 0f);
                                    break;
                                case BywaterTileKind.DirtPath:
                                    w = new Color(0f, 0f, 1f, 0f);
                                    break;
                                case BywaterTileKind.WallBlock:
                                {
                                    BywaterOutdoorPropKind prop = BywaterTileClassifier.ClassifyOutdoorProp(map, x, y, out _);
                                    w = (prop == BywaterOutdoorPropKind.CottageFacade || prop == BywaterOutdoorPropKind.StoneFence)
                                        ? new Color(0.25f, 0.75f, 0f, 0f)
                                        : new Color(1f, 0f, 0f, 0f);
                                    break;
                                }
                                default:
                                    w = new Color(1f, 0f, 0f, 0f);
                                    break;
                            }

                            // Smooth shoreline transition within 1.5 tiles of ShallowWater
                            float subWorldX = x + (subX == 0 ? -0.25f : 0.25f);
                            float subTileY = y + (subY == 0 ? -0.25f : 0.25f);
                            float minDistSq = float.MaxValue;
                            for (int dy = -2; dy <= 2; dy++)
                            {
                                int wy = y + dy;
                                if (wy < 0 || wy >= map.Height) continue;
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    int wx = x + dx;
                                    if (wx < 0 || wx >= map.Width) continue;
                                    if (BywaterTileClassifier.Classify(map, wx, wy) == BywaterTileKind.ShallowWater)
                                    {
                                        float ddx = wx - subWorldX;
                                        float ddy = wy - subTileY;
                                        float distSq = ddx * ddx + ddy * ddy;
                                        if (distSq < minDistSq)
                                        {
                                            minDistSq = distSq;
                                        }
                                    }
                                }
                            }

                            if (minDistSq <= 1.55f * 1.55f)
                            {
                                float dist = Mathf.Sqrt(minDistSq);
                                float bank = Mathf.Lerp(0.80f, 0.45f, Mathf.Clamp01((dist - 0.70f) / 0.85f));
                                if (kind == BywaterTileKind.Cobblestone)
                                {
                                    bank *= 0.45f;
                                }

                                float landScale = 1f - bank;
                                w.r *= landScale;
                                w.g *= landScale;
                                w.b *= landScale;
                                w.a = Mathf.Max(w.a, bank);
                            }
                        }
                    }

                    raw[sy * texW + sx] = w;
                }
            }

            // Gentle 3x3 smoothing while keeping tile centers crisp; flip Y so row 0 maps to top (world Z = 0)
            Color[] finalPixels = new Color[count];
            for (int sy = 0; sy < texH; sy++)
            {
                int py = (texH - 1) - sy;
                for (int sx = 0; sx < texW; sx++)
                {
                    Color acc = raw[sy * texW + sx] * 0.56f;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int ny = Mathf.Clamp(sy + oy, 0, texH - 1);
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = Mathf.Clamp(sx + ox, 0, texW - 1);
                            float weight = (ox == 0 || oy == 0) ? 0.08f : 0.03f;
                            acc += raw[ny * texW + nx] * weight;
                        }
                    }

                    float sum = acc.r + acc.g + acc.b + acc.a;
                    if (sum > 0.0001f)
                    {
                        float inv = 1f / sum;
                        acc.r *= inv;
                        acc.g *= inv;
                        acc.b *= inv;
                        acc.a *= inv;
                    }
                    else
                    {
                        acc = new Color(1f, 0f, 0f, 0f);
                    }

                    finalPixels[py * texW + sx] = acc;
                }
            }

            _splatTexture.SetPixels(finalPixels);
            _splatTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _splatBuiltForMap = map;
        }

        private static bool IsDungeonWallTile(TileMapData map, int x, int y)
        {
            if (!map.InBounds(x, y))
            {
                return true;
            }

            BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);
            return kind == BywaterTileKind.DungeonWall || kind == BywaterTileKind.WallBlock;
        }

        private void UpdateTerrainMesh(TileMapData map, int minX, int maxX, int minY, int maxY)
        {
            if (_terrainMeshFilter == null)
            {
                return;
            }

            if (_terrainMesh == null)
            {
                _terrainMesh = new Mesh
                {
                    name = "BywaterTerrainWindowMesh"
                };
                _terrainMesh.MarkDynamic();
            }

            int gMinX = Mathf.Max(0, minX - 1);
            int gMaxX = Mathf.Min(map.Width - 1, maxX + 1);
            int gMinY = Mathf.Max(0, minY - 1);
            int gMaxY = Mathf.Min(map.Height - 1, maxY + 1);

            int quadsX = (gMaxX - gMinX + 1) * 2;
            int quadsZ = (gMaxY - gMinY + 1) * 2;
            int vertsX = quadsX + 1;
            int vertsZ = quadsZ + 1;

            _terrainVertices.Clear();
            _terrainUvs.Clear();
            _terrainTriangles.Clear();

            for (int iz = 0; iz < vertsZ; iz++)
            {
                float vz = -(gMinY - 0.5f) - iz * 0.5f;
                for (int ix = 0; ix < vertsX; ix++)
                {
                    float vx = (gMinX - 0.5f) + ix * 0.5f;
                    float vy = ComputeTerrainVertexHeight(map, gMinX, gMinY, ix, iz);
                    _terrainVertices.Add(new Vector3(vx, vy, vz));
                    _terrainUvs.Add(new Vector2(vx, -vz));
                }
            }

            for (int qz = 0; qz < quadsZ; qz++)
            {
                int row0 = qz * vertsX;
                int row1 = (qz + 1) * vertsX;
                for (int qx = 0; qx < quadsX; qx++)
                {
                    int v00 = row0 + qx;
                    int v10 = v00 + 1;
                    int v01 = row1 + qx;
                    int v11 = v01 + 1;

                    _terrainTriangles.Add(v00);
                    _terrainTriangles.Add(v10);
                    _terrainTriangles.Add(v11);

                    _terrainTriangles.Add(v00);
                    _terrainTriangles.Add(v11);
                    _terrainTriangles.Add(v01);
                }
            }

            _terrainMesh.Clear();
            _terrainMesh.SetVertices(_terrainVertices);
            _terrainMesh.SetUVs(0, _terrainUvs);
            _terrainMesh.SetTriangles(_terrainTriangles, 0, true);
            _terrainMesh.RecalculateNormals();
            _terrainMesh.RecalculateTangents();
            _terrainMesh.RecalculateBounds();
            _terrainMeshFilter.sharedMesh = _terrainMesh;
        }

        private static float ComputeTerrainVertexHeight(TileMapData map, int gMinX, int gMinY, int ix, int iz)
        {
            int txStart = (ix & 1) == 1 ? gMinX + (ix >> 1) : gMinX + (ix >> 1) - 1;
            int txEnd = (ix & 1) == 1 ? txStart : txStart + 1;
            int tyStart = (iz & 1) == 1 ? gMinY + (iz >> 1) : gMinY + (iz >> 1) - 1;
            int tyEnd = (iz & 1) == 1 ? tyStart : tyStart + 1;

            int totalTiles = 0;
            int waterTiles = 0;
            for (int ty = tyStart; ty <= tyEnd; ty++)
            {
                for (int tx = txStart; tx <= txEnd; tx++)
                {
                    totalTiles++;
                    if (map.InBounds(tx, ty) && BywaterTileClassifier.Classify(map, tx, ty) == BywaterTileKind.ShallowWater)
                    {
                        waterTiles++;
                    }
                }
            }

            if (waterTiles == 0)
            {
                return 0f;
            }

            if (waterTiles == totalTiles)
            {
                return -0.24f;
            }

            float ratio = waterTiles / (float)totalTiles;
            return Mathf.Lerp(-0.01f, -0.07f, ratio);
        }

        private void OnDestroy()
        {
            DestroyResource(_terrainMesh);
            _terrainMesh = null;

            DestroyResource(_splatTexture);
            _splatTexture = null;

            DestroyResource(_fallbackGrassTuftMat);
            _fallbackGrassTuftMat = null;

            DestroyResource(_fallbackFlowerTuftMat);
            _fallbackFlowerTuftMat = null;

            DestroyResource(_fallbackPebbleScatterMat);
            _fallbackPebbleScatterMat = null;
        }

        private static void DestroyResource(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        private Transform CreateChildContainer(string containerName)
        {
            Transform existing = transform.Find(containerName);
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject(containerName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void EnsurePartyLantern()
        {
            if (_partyLantern != null)
            {
                return;
            }

            GameObject go = new GameObject("PartyLantern");
            go.transform.SetParent(transform, false);
            _partyLantern = go.AddComponent<Light>();
            _partyLantern.type = LightType.Point;
            _partyLantern.color = new Color(1.0f, 0.635f, 0.290f, 1f);
            _partyLantern.intensity = 2.4f;
            _partyLantern.range = 5.2f;
            _partyLantern.shadows = LightShadows.Soft;
            _partyLantern.shadowStrength = 0.65f;
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(col);
                }
                else
                {
                    DestroyImmediate(col);
                }
            }
        }
    }
}
