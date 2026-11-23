using System.Collections.Generic;
using Realmz.Data;
using Realmz.Rendering;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Pooled sliding-window 3D HD-2D terrain, structure, portal, torchlight, and combat-arena renderer.
    /// Zero per-step heap allocations when panning/walking across the 90x90 Bywater maps.
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

        private BywaterAssetCatalog _catalog;
        private TileMapData _currentMap;
        private GridCoord _focusCenter;
        private bool _hasRendered;

        private Transform _floorContainer;
        private Transform _structureContainer;
        private Transform _torchContainer;
        private Transform _portalContainer;
        private Transform _arenaContainer;

        private readonly List<MeshRenderer> _floorPool = new List<MeshRenderer>(1089);
        private readonly List<MeshRenderer> _blockPool = new List<MeshRenderer>(1024);
        private readonly List<TorchPropEntry> _torchPool = new List<TorchPropEntry>(MaxActiveTorchLights);
        private readonly List<PortalMarkerEntry> _portalPool = new List<PortalMarkerEntry>(16);
        private readonly List<MeshRenderer> _arenaBlocks = new List<MeshRenderer>(8);
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

            _partyLantern.transform.position = new Vector3(worldPos.x, worldPos.y + 1.35f, worldPos.z);
            if (isDungeon)
            {
                _partyLantern.intensity = 7.5f;
                _partyLantern.range = 9.5f;
            }
            else
            {
                _partyLantern.intensity = 4.2f;
                _partyLantern.range = 6.5f;
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
            int minX = Mathf.Max(0, center.X - WindowRadius);
            int maxX = Mathf.Min(map.Width - 1, center.X + WindowRadius);
            int minY = Mathf.Max(0, center.Y - WindowRadius);
            int maxY = Mathf.Min(map.Height - 1, center.Y + WindowRadius);

            int floorIdx = 0;
            int blockIdx = 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    BywaterTileKind kind = BywaterTileClassifier.Classify(map, x, y);

                    // Floor quad
                    float floorY = kind == BywaterTileKind.ShallowWater ? -0.12f : 0f;
                    MeshRenderer floorQuad = GetFloorQuad(floorIdx++);
                    floorQuad.transform.position = new Vector3(x, floorY, -y);
                    floorQuad.sharedMaterial = ResolveFloorMaterial(kind, map.IsDungeon);
                    floorQuad.gameObject.SetActive(true);

                    // 3D structure blocks
                    switch (kind)
                    {
                        case BywaterTileKind.WallBlock:
                        case BywaterTileKind.DungeonWall:
                        case BywaterTileKind.TownBuildingSolid:
                        {
                            MeshRenderer wallCube = GetBlockCube(blockIdx++);
                            wallCube.transform.position = new Vector3(x, 0.675f, -y);
                            wallCube.transform.localScale = new Vector3(1.0f, 1.35f, 1.0f);
                            wallCube.sharedMaterial = ResolveWallMaterial(kind, map.IsDungeon);
                            wallCube.gameObject.SetActive(true);
                            break;
                        }

                        case BywaterTileKind.ForestCover:
                        {
                            Material treeMat = _catalog != null ? _catalog.treeCoverMaterial : null;
                            MeshRenderer shrubA = GetBlockCube(blockIdx++);
                            shrubA.transform.position = new Vector3(x - 0.30f, 0.16f, -y + 0.30f);
                            shrubA.transform.localScale = new Vector3(0.30f, 0.32f, 0.30f);
                            shrubA.sharedMaterial = treeMat;
                            shrubA.gameObject.SetActive(true);

                            if (((x + y) & 1) == 0)
                            {
                                MeshRenderer shrubB = GetBlockCube(blockIdx++);
                                shrubB.transform.position = new Vector3(x + 0.30f, 0.14f, -y - 0.30f);
                                shrubB.transform.localScale = new Vector3(0.26f, 0.28f, 0.26f);
                                shrubB.sharedMaterial = treeMat;
                                shrubB.gameObject.SetActive(true);
                            }
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

                        case BywaterTileKind.TownBuildingDoor:
                        {
                            // Keep town thresholds open and readable without thin stick clutter
                            break;
                        }

                        case BywaterTileKind.DungeonStairs:
                        {
                            Material stairMat = _catalog != null
                                ? (_catalog.doorArchMaterial != null ? _catalog.doorArchMaterial : _catalog.dungeonWallMaterial)
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
            }

            for (int i = floorIdx; i < _floorPool.Count; i++)
            {
                _floorPool[i].gameObject.SetActive(false);
            }

            for (int i = blockIdx; i < _blockPool.Count; i++)
            {
                _blockPool[i].gameObject.SetActive(false);
            }

            UpdateWindowPortals(map, minX, maxX, minY, maxY);
            UpdateWindowTorches(center, minX, maxX, minY, maxY);
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

        private void UpdateWindowTorches(GridCoord center, int minX, int maxX, int minY, int maxY)
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

            for (int i = 0; i < _selectedTorches.Count; i++)
            {
                GridCoord tc = _selectedTorches[i];
                TorchPropEntry entry = GetTorchProp(i);
                entry.Root.transform.position = new Vector3(tc.X + 0.34f, 0f, -tc.Y + 0.34f);
                if (_catalog != null)
                {
                    entry.QuadRenderer.sharedMaterial = _catalog.torchPropMaterial;
                    entry.EmberRenderer.sharedMaterial = _catalog.torchEmberMaterial;
                }
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

        private Material ResolveFloorMaterial(BywaterTileKind kind, bool isDungeon)
        {
            if (_catalog == null)
            {
                return null;
            }

            switch (kind)
            {
                case BywaterTileKind.Grass:
                case BywaterTileKind.ForestCover:
                    return _catalog.grassMaterial;
                case BywaterTileKind.Cobblestone:
                case BywaterTileKind.TownBuildingDoor:
                    return _catalog.cobblestoneMaterial;
                case BywaterTileKind.DirtPath:
                    return _catalog.dirtPathMaterial;
                case BywaterTileKind.DungeonFloor:
                case BywaterTileKind.DungeonDoor:
                case BywaterTileKind.DungeonStairs:
                    return _catalog.dungeonFloorMaterial;
                case BywaterTileKind.ShallowWater:
                    return _catalog.shallowWaterMaterial;
                default:
                    return isDungeon ? _catalog.dungeonFloorMaterial : _catalog.grassMaterial;
            }
        }

        private Material ResolveWallMaterial(BywaterTileKind kind, bool isDungeon)
        {
            if (_catalog == null)
            {
                return null;
            }

            switch (kind)
            {
                case BywaterTileKind.DungeonWall:
                    return _catalog.dungeonWallMaterial;
                case BywaterTileKind.TownBuildingSolid:
                    return _catalog.buildingWallMaterial != null ? _catalog.buildingWallMaterial : _catalog.dungeonWallMaterial;
                case BywaterTileKind.WallBlock:
                    return isDungeon
                        ? _catalog.dungeonWallMaterial
                        : (_catalog.buildingWallMaterial != null ? _catalog.buildingWallMaterial : _catalog.dungeonWallMaterial);
                default:
                    return _catalog.dungeonWallMaterial;
            }
        }

        private void WarmupPools()
        {
            for (int i = _floorPool.Count; i < 1089; i++)
            {
                MeshRenderer r = CreateFloorQuad(i);
                r.gameObject.SetActive(false);
                _floorPool.Add(r);
            }

            for (int i = _blockPool.Count; i < 400; i++)
            {
                MeshRenderer r = CreateBlockCube(i);
                r.gameObject.SetActive(false);
                _blockPool.Add(r);
            }

            for (int i = _torchPool.Count; i < MaxActiveTorchLights; i++)
            {
                TorchPropEntry entry = CreateTorchProp(i);
                entry.Root.SetActive(false);
                _torchPool.Add(entry);
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

        private TorchPropEntry CreateTorchProp(int index)
        {
            GameObject root = new GameObject("TorchProp_" + index);
            root.transform.SetParent(_torchContainer, false);

            GameObject quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "TorchBillboard";
            quadGo.transform.SetParent(root.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, 0.575f, 0f);
            quadGo.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
            DestroyCollider(quadGo);
            quadGo.AddComponent<PixelBillboard>();
            MeshRenderer quadRenderer = quadGo.GetComponent<MeshRenderer>();
            quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            quadRenderer.receiveShadows = true;

            GameObject emberGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            emberGo.name = "TorchEmber";
            emberGo.transform.SetParent(root.transform, false);
            emberGo.transform.localPosition = new Vector3(0f, 1.02f, 0f);
            emberGo.transform.localScale = new Vector3(0.14f, 0.20f, 0.14f);
            DestroyCollider(emberGo);
            MeshRenderer emberRenderer = emberGo.GetComponent<MeshRenderer>();
            emberRenderer.shadowCastingMode = ShadowCastingMode.Off;
            emberRenderer.receiveShadows = false;

            GameObject lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            Light pointLight = lightGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1.0f, 0.580f, 0.239f, 1f); // #FF943D
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
            _partyLantern.color = new Color(1.0f, 0.635f, 0.290f, 1f); // #FFA24A
            _partyLantern.intensity = 4.2f;
            _partyLantern.range = 6.5f;
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
