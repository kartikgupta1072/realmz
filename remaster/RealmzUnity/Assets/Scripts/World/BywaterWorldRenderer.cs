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

        public void Initialize(BywaterAssetCatalog catalog)
        {
            _catalog = catalog;
            EnsureContainers();
            EnsurePartyLantern();
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

        public void SetCombatArenaFrame(int originX, int originY, int width, int height, bool active)
        {
            EnsureContainers();

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

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);
                    int tileHash = BywaterTileClassifier.DeterministicTileHash(x, y);
                    int variantIdx = ResolveWeightedTileVariant(kind, isDungeon, x, y, tileHash);

                    // Floor quad with multi-variant material, deterministic yaw, and subtle per-tile tint
                    float floorY = kind == BywaterTileKind.ShallowWater ? -0.12f : 0f;
                    MeshRenderer floorQuad = GetFloorQuad(floorIdx++);
                    floorQuad.transform.position = new Vector3(x, floorY, -y);
                    floorQuad.transform.rotation = Quaternion.Euler(90f, ResolveFloorYaw(kind, tileHash), 0f);
                    floorQuad.sharedMaterial = ResolveFloorMaterial(kind, isDungeon, variantIdx);
                    ApplyFloorTint(floorQuad, kind, tileHash);
                    floorQuad.gameObject.SetActive(true);

                    if (isDungeon)
                    {
                        switch (kind)
                        {
                            case BywaterTileKind.DungeonWall:
                            case BywaterTileKind.WallBlock:
                            {
                                Material wallMat = _catalog != null
                                    ? _catalog.GetTileVariantMaterial(_catalog.dungeonWallMaterial, _catalog.dungeonWallVariantMaterials, variantIdx)
                                    : null;
                                MeshRenderer wallCube = GetBlockCube(blockIdx++);
                                wallCube.transform.position = new Vector3(x, 0.675f, -y);
                                wallCube.transform.localScale = new Vector3(1.0f, 1.35f, 1.0f);
                                wallCube.sharedMaterial = wallMat;
                                wallCube.gameObject.SetActive(true);

                                MeshRenderer trimCap = GetBlockCube(blockIdx++);
                                trimCap.transform.position = new Vector3(x, 1.385f, -y);
                                trimCap.transform.localScale = new Vector3(1.05f, 0.07f, 1.05f);
                                trimCap.sharedMaterial = FirstNonNull(_catalog != null ? _catalog.stoneFenceMaterial : null, _catalog != null ? _catalog.doorArchMaterial : null, wallMat);
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
