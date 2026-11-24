using System.Collections.Generic;
using Realmz.Cartography;
using Realmz.Data;
using Realmz.Rendering;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Cartography-backed 3D HD-2D world renderer driving stepped elevation terrain, instanced foliage
    /// scatter, top-down cloud/canopy/lantern lightmaps, pooled 64 px/m prop billboards, arched stone
    /// bridges, dungeon vs overworld atmosphere shading, torch lights, and campfires.
    /// </summary>
    public sealed class BywaterWorldRenderer : MonoBehaviour
    {
        private const int MaxActiveTorchLights = 8;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GroundValueScaleId = Shader.PropertyToID("_GroundValueScale");
        private static readonly int RoadValueBoostId = Shader.PropertyToID("_RoadValueBoost");
        private static readonly int HorizonHazeColorId = Shader.PropertyToID("_HorizonHazeColor");
        private static readonly int HorizonHazeStrengthId = Shader.PropertyToID("_HorizonHazeStrength");
        private static readonly int LanternPoolIntensityId = Shader.PropertyToID("_LanternPoolIntensity");
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int HeroWorldPosId = Shader.PropertyToID("_HeroWorldPos");

        private sealed class PropBillboardEntry
        {
            public GameObject Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public PixelBillboard Billboard;
        }

        private sealed class StoneBridgeEntry
        {
            public GameObject Root;
            public MeshFilter DeckFilter;
            public MeshRenderer DeckRenderer;
            public MeshFilter CurbFilter;
            public MeshRenderer CurbRenderer;
            public Mesh DeckMesh;
            public Mesh CurbMesh;
            public float CachedWidth;
            public float CachedLength;
        }

        private sealed class TorchLightEntry
        {
            public GameObject Root;
            public Light PointLight;
            public BywaterTorchFlicker Flicker;
        }

        private sealed class PortalMarkerEntry
        {
            public GameObject Root;
            public MeshRenderer RuneQuad;
            public MeshRenderer BeaconQuad;
            public PixelBillboard BeaconBillboard;
        }

        private sealed class CampfireEntry
        {
            public GameObject Root;
            public MeshRenderer BillboardRenderer;
            public PixelBillboard Billboard;
            public Light PointLight;
            public BywaterTorchFlicker Flicker;
        }

        private BywaterAssetCatalog _catalog;
        private TileMapData _currentMap;
        private CartographyWorldData _cartographyData;
        private GridCoord _focusCenter;
        private bool _hasRendered;

        private BywaterElevationBuilder _elevationBuilder;
        private BywaterFoliageScatter _foliageScatter;
        private BywaterTopDownLightmap _topDownLightmap;
        private BywaterDioramaController _dioramaController;
        private Transform _heroAnchor;

        private Transform _propsContainer;
        private Transform _bridgesContainer;
        private Transform _torchLightContainer;
        private Transform _portalContainer;
        private Transform _arenaContainer;

        private readonly List<PropBillboardEntry> _propPool = new List<PropBillboardEntry>(256);
        private readonly List<StoneBridgeEntry> _bridgePool = new List<StoneBridgeEntry>(4);
        private readonly List<TorchLightEntry> _torchLightPool = new List<TorchLightEntry>(MaxActiveTorchLights);
        private readonly List<PortalMarkerEntry> _portalPool = new List<PortalMarkerEntry>(16);

        private readonly List<Vector3> _allLightPositions = new List<Vector3>(128);
        private readonly List<Vector3> _selectedLightPositions = new List<Vector3>(MaxActiveTorchLights);
        private readonly List<float> _selectedLightDistSq = new List<float>(MaxActiveTorchLights);
        private readonly List<Vector3> _arenaPointScratch = new List<Vector3>(64);

        private Light _partyLantern;
        private CampfireEntry _campfireEntry;
        private LineRenderer _arenaBorderLine;

        private Mesh _propBillboardQuadMesh;
        private Mesh _groundQuadMesh;
        private Material _runtimeTerrainMat;
        private Material _sourceTerrainMat;
        private Material _runtimeWaterMat;
        private Material _sourceWaterMat;
        private Material _fallbackPropMaterial;
        private Material _arenaBorderMaterial;
        private float _terrainGridHintStrength;

        public CartographyWorldData CartographyData => _cartographyData;
        public Collider TerrainCollider => _elevationBuilder != null ? _elevationBuilder.terrainMeshCollider : null;
        public Transform PropsRoot => _propsContainer;

        public Bounds WorldBounds
        {
            get
            {
                if (_cartographyData == null)
                {
                    return new Bounds(Vector3.zero, Vector3.zero);
                }

                Vector3 center = new Vector3(_cartographyData.Width * 0.5f, 0f, -_cartographyData.Height * 0.5f);
                Vector3 size = new Vector3(_cartographyData.Width, 4f, _cartographyData.Height);
                return new Bounds(center, size);
            }
        }

        public float GetTileElevation(int x, int y)
        {
            return _cartographyData != null ? _cartographyData.GetElevation(x, y) : 0f;
        }

        public void Initialize(BywaterAssetCatalog catalog)
        {
            _catalog = catalog;
            EnsureSharedMeshes();
            EnsureContainers();
            EnsureCartographyComponents();
            EnsurePartyLantern();
            WarmupLightAndPortalPools();
        }

        public void RenderMap(TileMapData map, GridCoord focusCenter, bool forceRebuild = false)
        {
            if (map == null)
            {
                return;
            }

            EnsureSharedMeshes();
            EnsureContainers();
            EnsureCartographyComponents();
            EnsurePartyLantern();

            bool mapChanged = !ReferenceEquals(_currentMap, map);
            _currentMap = map;

            if (mapChanged || forceRebuild || _cartographyData == null)
            {
                RebuildCartographyWorld(map);
                RebuildCartographyProps(map);
                RebuildMapPortals(map);
                CollectMapLightPositions();
            }

            if (!forceRebuild && !mapChanged && _hasRendered && focusCenter == _focusCenter)
            {
                return;
            }

            _focusCenter = focusCenter;
            _hasRendered = true;
            UpdateWindowTorches(focusCenter, map.IsDungeon);
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
            UpdateWindowTorches(focusCenter, _currentMap.IsDungeon);
        }

        public void UpdateHeroWorldPosition(Vector3 pos)
        {
            EnsureCartographyComponents();
            if (_heroAnchor != null)
            {
                _heroAnchor.position = pos;
            }

            Shader.SetGlobalVector(HeroWorldPosId, new Vector4(pos.x, pos.y, pos.z, 1f));
        }

        public void SetPartyTorchPosition(Vector3 worldPos, bool isDungeon)
        {
            UpdateHeroWorldPosition(worldPos);
            EnsurePartyLantern();
            if (_partyLantern == null)
            {
                return;
            }

            _partyLantern.transform.position = new Vector3(worldPos.x, worldPos.y + 1.65f, worldPos.z - 0.85f);
            if (isDungeon)
            {
                _partyLantern.enabled = true;
                _partyLantern.intensity = 2.8f;
                _partyLantern.range = 11.0f;
            }
            else
            {
                _partyLantern.intensity = 0f;
                _partyLantern.enabled = false;
            }
        }

        public void SetCampfireActive(bool active, Vector3 worldPos)
        {
            EnsureSharedMeshes();
            EnsureContainers();
            EnsureCampfireEntry();

            if (_campfireEntry == null || _campfireEntry.Root == null)
            {
                return;
            }

            if (!active)
            {
                _campfireEntry.Root.SetActive(false);
                return;
            }

            _campfireEntry.Root.transform.position = worldPos;
            Material brazierMat = ResolvePropMaterial(CartographyPropKind.TorchBrazier);
            if (_campfireEntry.BillboardRenderer != null && brazierMat != null)
            {
                _campfireEntry.BillboardRenderer.sharedMaterial = brazierMat;
            }
            if (_campfireEntry.Billboard != null)
            {
                _campfireEntry.Billboard.targetCamera = ResolveActiveCamera();
            }

            _campfireEntry.Root.SetActive(true);
        }

        public void SetTerrainGridHint(float strength)
        {
            _terrainGridHintStrength = Mathf.Max(0f, strength);
        }

        public void SetCombatArenaFrame(int originX, int originY, int width, int height, bool active)
        {
            EnsureContainers();

            if (!active || width <= 0 || height <= 0)
            {
                if (_arenaBorderLine != null)
                {
                    _arenaBorderLine.gameObject.SetActive(false);
                }
                return;
            }

            EnsureArenaBorderLine();
            _arenaPointScratch.Clear();

            int maxX = originX + width;
            int maxY = originY + height;

            // North edge (West -> East)
            for (int x = originX; x <= maxX; x++)
            {
                float elev = SampleCornerElevation(x, originY);
                _arenaPointScratch.Add(new Vector3(x, elev + 0.045f, -originY));
            }

            // East edge (North -> South)
            for (int y = originY + 1; y <= maxY; y++)
            {
                float elev = SampleCornerElevation(maxX, y);
                _arenaPointScratch.Add(new Vector3(maxX, elev + 0.045f, -y));
            }

            // South edge (East -> West)
            for (int x = maxX - 1; x >= originX; x--)
            {
                float elev = SampleCornerElevation(x, maxY);
                _arenaPointScratch.Add(new Vector3(x, elev + 0.045f, -maxY));
            }

            // West edge (South -> North)
            for (int y = maxY - 1; y > originY; y--)
            {
                float elev = SampleCornerElevation(originX, y);
                _arenaPointScratch.Add(new Vector3(originX, elev + 0.045f, -y));
            }

            _arenaBorderLine.positionCount = _arenaPointScratch.Count;
            for (int i = 0; i < _arenaPointScratch.Count; i++)
            {
                _arenaBorderLine.SetPosition(i, _arenaPointScratch[i]);
            }
            _arenaBorderLine.gameObject.SetActive(true);
        }

        private float SampleCornerElevation(int cornerX, int cornerY)
        {
            if (_cartographyData == null)
            {
                return 0f;
            }

            float maxElev = float.MinValue;
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int tx = Mathf.Clamp(cornerX + dx, 0, _cartographyData.Width - 1);
                    int ty = Mathf.Clamp(cornerY + dy, 0, _cartographyData.Height - 1);
                    float e = _cartographyData.GetElevation(tx, ty);
                    if (e > maxElev)
                    {
                        maxElev = e;
                    }
                }
            }

            return maxElev > float.MinValue ? maxElev : 0f;
        }

        private void RebuildCartographyWorld(TileMapData map)
        {
            _cartographyData = CartographyWorldData.BuildFromTileMap(map);
            bool isDungeon = map.IsDungeon;
            Camera cam = ResolveActiveCamera();

            // 1. Elevation Terrain + MeshCollider + ShallowWaterPlane
            Material terrainMat = ResolveRuntimeTerrainMaterial(isDungeon);
            Material waterMat = ResolveRuntimeWaterMaterial(isDungeon);
            _elevationBuilder.terrainMaterial = terrainMat;
            _elevationBuilder.waterMaterial = waterMat;
            _elevationBuilder.Rebuild(_cartographyData);

            // 2. Top-Down Lightmap (disable bright daytime clouds in underground dungeons)
            if (isDungeon)
            {
                _topDownLightmap.cloudStrength = 0.04f;
                _topDownLightmap.cloudShadowColor = new Color(0.24f, 0.28f, 0.42f, 1f);
                _topDownLightmap.canopyShadowColor = new Color(0.18f, 0.22f, 0.34f, 1f);
            }
            else
            {
                _topDownLightmap.cloudStrength = 0.82f;
                _topDownLightmap.cloudShadowColor = new Color(0.56f, 0.62f, 0.74f, 1f);
                _topDownLightmap.canopyShadowColor = new Color(0.48f, 0.54f, 0.66f, 1f);
            }

            _topDownLightmap.ConfigureFromWorldData(_cartographyData);
            _topDownLightmap.UpdateLightmap(Application.isPlaying ? Time.time : 0f);

            // 3. Instanced Foliage Scatter (skip outdoor grass foliage in dungeons)
            _foliageScatter.foliageMaterial = _catalog != null ? _catalog.cartographyFoliageMaterial : null;
            _foliageScatter.targetCamera = cam;
            _foliageScatter.RebuildScatter(_cartographyData);
            _foliageScatter.gameObject.SetActive(!isDungeon);

            // 4. Diorama Runtime Global Coordinator
            _dioramaController.targetCamera = cam;
            _dioramaController.topDownLightmap = _topDownLightmap;
            _dioramaController.foliageScatter = _foliageScatter;
            _dioramaController.heroTransform = _heroAnchor;
            _dioramaController.enableDemoPatrol = false;
            _dioramaController.SyncGlobals(Application.isPlaying ? Time.time : 0f);
        }

        private void RebuildCartographyProps(TileMapData map)
        {
            int propIdx = 0;
            int bridgeIdx = 0;

            if (_cartographyData != null && _cartographyData.Props != null)
            {
                IReadOnlyList<CartographyPropPlacement> props = _cartographyData.Props;
                Camera cam = ResolveActiveCamera();
                Quaternion initialYaw = Quaternion.identity;
                if (cam != null)
                {
                    Vector3 fwd = cam.transform.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude > 0.0001f)
                    {
                        initialYaw = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    }
                }

                for (int i = 0; i < props.Count; i++)
                {
                    CartographyPropPlacement prop = props[i];
                    if (prop.Kind == CartographyPropKind.None)
                    {
                        continue;
                    }

                    if (prop.Kind == CartographyPropKind.StoneBridge)
                    {
                        StoneBridgeEntry bridge = GetBridgeEntry(bridgeIdx++);
                        ConfigureStoneBridge(bridge, prop, map.IsDungeon);
                        continue;
                    }

                    PropBillboardEntry entry = GetPropEntry(propIdx++);
                    entry.Root.transform.position = prop.WorldPosition;
                    entry.Root.transform.rotation = initialYaw;
                    entry.Root.transform.localScale = new Vector3(
                        Mathf.Max(0.25f, prop.SizeMeters.x),
                        Mathf.Max(0.25f, prop.SizeMeters.y),
                        1f);
                    entry.Renderer.sharedMaterial = ResolvePropMaterial(prop.Kind);
                    entry.Billboard.targetCamera = cam;
                    entry.Root.SetActive(true);
                }
            }

            for (int i = propIdx; i < _propPool.Count; i++)
            {
                _propPool[i].Root.SetActive(false);
            }

            for (int i = bridgeIdx; i < _bridgePool.Count; i++)
            {
                _bridgePool[i].Root.SetActive(false);
            }
        }

        private void RebuildMapPortals(TileMapData map)
        {
            int portalIdx = 0;
            IReadOnlyList<MapPortal> portals = map.Portals;
            if (portals != null)
            {
                Material portalMat = _catalog != null ? _catalog.portalBeaconMaterial : null;
                Camera cam = ResolveActiveCamera();

                for (int i = 0; i < portals.Count; i++)
                {
                    MapPortal portal = portals[i];
                    float elev = GetTileElevation(portal.X, portal.Y);
                    Vector3 pos = CartographyWorldData.GridToWorld(portal.X, portal.Y, elev);

                    PortalMarkerEntry entry = GetPortalMarker(portalIdx++);
                    entry.Root.transform.position = pos;
                    if (portalMat != null)
                    {
                        entry.RuneQuad.sharedMaterial = portalMat;
                        entry.BeaconQuad.sharedMaterial = portalMat;
                    }
                    entry.BeaconBillboard.targetCamera = cam;
                    entry.Root.SetActive(true);
                }
            }

            for (int i = portalIdx; i < _portalPool.Count; i++)
            {
                _portalPool[i].Root.SetActive(false);
            }
        }

        private void CollectMapLightPositions()
        {
            _allLightPositions.Clear();
            if (_cartographyData == null)
            {
                return;
            }

            IReadOnlyList<CartographyPropPlacement> props = _cartographyData.Props;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Kind == CartographyPropKind.TorchBrazier)
                {
                    _allLightPositions.Add(props[i].WorldPosition);
                }
            }

            IReadOnlyList<CartographyLanternSource> lanterns = _cartographyData.Lanterns;
            for (int i = 0; i < lanterns.Count; i++)
            {
                CartographyLanternSource l = lanterns[i];
                bool nearExisting = false;
                for (int j = 0; j < _allLightPositions.Count; j++)
                {
                    float dx = _allLightPositions[j].x - l.WorldXZ.x;
                    float dz = _allLightPositions[j].z - l.WorldXZ.y;
                    if (dx * dx + dz * dz < 1.44f)
                    {
                        nearExisting = true;
                        break;
                    }
                }

                if (!nearExisting)
                {
                    CartographyWorldData.WorldToGrid(new Vector3(l.WorldXZ.x, 0f, l.WorldXZ.y), out int gx, out int gy);
                    float elev = _cartographyData.GetElevation(gx, gy);
                    _allLightPositions.Add(new Vector3(l.WorldXZ.x, elev, l.WorldXZ.y));
                }
            }
        }

        private void UpdateWindowTorches(GridCoord center, bool isDungeon)
        {
            _selectedLightPositions.Clear();
            _selectedLightDistSq.Clear();

            Vector3 centerWorld = CartographyWorldData.GridToWorld(center);
            const float maxSearchRadiusSq = 28f * 28f;

            for (int i = 0; i < _allLightPositions.Count; i++)
            {
                Vector3 lp = _allLightPositions[i];
                float dx = lp.x - centerWorld.x;
                float dz = lp.z - centerWorld.z;
                float distSq = dx * dx + dz * dz;
                if (distSq > maxSearchRadiusSq)
                {
                    continue;
                }

                if (_selectedLightPositions.Count < MaxActiveTorchLights)
                {
                    _selectedLightPositions.Add(lp);
                    _selectedLightDistSq.Add(distSq);
                }
                else
                {
                    int worstIdx = 0;
                    float worstDist = _selectedLightDistSq[0];
                    for (int k = 1; k < _selectedLightDistSq.Count; k++)
                    {
                        if (_selectedLightDistSq[k] > worstDist)
                        {
                            worstDist = _selectedLightDistSq[k];
                            worstIdx = k;
                        }
                    }

                    if (distSq < worstDist)
                    {
                        _selectedLightPositions[worstIdx] = lp;
                        _selectedLightDistSq[worstIdx] = distSq;
                    }
                }
            }

            float targetIntensity = isDungeon ? 3.1f : 0.95f;
            float targetRange = isDungeon ? 9.0f : 4.2f;
            float targetFlickerAmp = isDungeon ? 0.38f : 0.14f;

            for (int i = 0; i < _selectedLightPositions.Count; i++)
            {
                Vector3 lp = _selectedLightPositions[i];
                TorchLightEntry entry = GetTorchLightEntry(i);
                entry.Root.transform.position = new Vector3(lp.x, lp.y + 1.05f, lp.z - 0.30f);
                entry.PointLight.intensity = targetIntensity;
                entry.PointLight.range = targetRange;
                entry.Flicker.baseIntensity = targetIntensity;
                entry.Flicker.flickerAmplitude = targetFlickerAmp;
                entry.Root.SetActive(true);
            }

            for (int i = _selectedLightPositions.Count; i < _torchLightPool.Count; i++)
            {
                _torchLightPool[i].Root.SetActive(false);
            }
        }

        private Material ResolveRuntimeTerrainMaterial(bool isDungeon)
        {
            Material source = _catalog != null ? _catalog.cartographyTerrainMaterial : null;
            if (source == null)
            {
                return null;
            }

            if (_runtimeTerrainMat == null || _sourceTerrainMat != source)
            {
                DestroyResource(_runtimeTerrainMat);
                _sourceTerrainMat = source;
                _runtimeTerrainMat = new Material(source)
                {
                    name = source.name + "_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (isDungeon)
            {
                if (_runtimeTerrainMat.HasProperty(GroundValueScaleId)) _runtimeTerrainMat.SetFloat(GroundValueScaleId, 0.42f);
                if (_runtimeTerrainMat.HasProperty(RoadValueBoostId)) _runtimeTerrainMat.SetFloat(RoadValueBoostId, 0.46f);
                if (_runtimeTerrainMat.HasProperty(HorizonHazeColorId)) _runtimeTerrainMat.SetColor(HorizonHazeColorId, new Color(0.05f, 0.07f, 0.12f, 1f));
                if (_runtimeTerrainMat.HasProperty(HorizonHazeStrengthId)) _runtimeTerrainMat.SetFloat(HorizonHazeStrengthId, 0.22f);
                if (_runtimeTerrainMat.HasProperty(LanternPoolIntensityId)) _runtimeTerrainMat.SetFloat(LanternPoolIntensityId, 1.15f);
            }
            else
            {
                if (_runtimeTerrainMat.HasProperty(GroundValueScaleId)) _runtimeTerrainMat.SetFloat(GroundValueScaleId, 0.76f);
                if (_runtimeTerrainMat.HasProperty(RoadValueBoostId)) _runtimeTerrainMat.SetFloat(RoadValueBoostId, 0.86f);
                if (_runtimeTerrainMat.HasProperty(HorizonHazeColorId)) _runtimeTerrainMat.SetColor(HorizonHazeColorId, new Color(0.91f, 0.83f, 0.70f, 1f));
                if (_runtimeTerrainMat.HasProperty(HorizonHazeStrengthId)) _runtimeTerrainMat.SetFloat(HorizonHazeStrengthId, 0.09f);
                if (_runtimeTerrainMat.HasProperty(LanternPoolIntensityId)) _runtimeTerrainMat.SetFloat(LanternPoolIntensityId, 1.35f);
            }

            return _runtimeTerrainMat;
        }

        private Material ResolveRuntimeWaterMaterial(bool isDungeon)
        {
            Material source = _catalog != null
                ? (_catalog.cartographyWaterMaterial != null ? _catalog.cartographyWaterMaterial : _catalog.shallowWaterMaterial)
                : null;
            if (source == null)
            {
                return null;
            }

            if (_runtimeWaterMat == null || _sourceWaterMat != source)
            {
                DestroyResource(_runtimeWaterMat);
                _sourceWaterMat = source;
                _runtimeWaterMat = new Material(source)
                {
                    name = source.name + "_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (isDungeon)
            {
                if (_runtimeWaterMat.HasProperty(BaseColorId)) _runtimeWaterMat.SetColor(BaseColorId, new Color(0.08f, 0.16f, 0.24f, 1f));
                if (_runtimeWaterMat.HasProperty(ShallowColorId)) _runtimeWaterMat.SetColor(ShallowColorId, new Color(0.08f, 0.18f, 0.28f, 0.90f));
                if (_runtimeWaterMat.HasProperty(DeepColorId)) _runtimeWaterMat.SetColor(DeepColorId, new Color(0.03f, 0.08f, 0.14f, 0.98f));
            }
            else
            {
                if (_runtimeWaterMat.HasProperty(BaseColorId)) _runtimeWaterMat.SetColor(BaseColorId, new Color(0.16f, 0.38f, 0.46f, 1f));
                if (_runtimeWaterMat.HasProperty(ShallowColorId)) _runtimeWaterMat.SetColor(ShallowColorId, new Color(0.18f, 0.45f, 0.62f, 0.88f));
                if (_runtimeWaterMat.HasProperty(DeepColorId)) _runtimeWaterMat.SetColor(DeepColorId, new Color(0.08f, 0.24f, 0.40f, 0.96f));
            }

            return _runtimeWaterMat;
        }

        private Material ResolvePropMaterial(CartographyPropKind kind)
        {
            Material mat = _catalog != null ? _catalog.GetCartographyPropMaterial(kind) : null;
            if (mat != null)
            {
                return mat;
            }

            if (_fallbackPropMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                _fallbackPropMaterial = new Material(shader)
                {
                    name = "FallbackCartographyProp_Mat",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (_fallbackPropMaterial.HasProperty(BaseColorId)) _fallbackPropMaterial.SetColor(BaseColorId, new Color(0.45f, 0.55f, 0.40f, 1f));
            }

            return _fallbackPropMaterial;
        }

        private void ConfigureStoneBridge(StoneBridgeEntry entry, CartographyPropPlacement placement, bool isDungeon)
        {
            float w = Mathf.Max(1.0f, placement.SizeMeters.x);
            float l = Mathf.Max(1.2f, placement.SizeMeters.y);

            if (entry.DeckMesh == null || Mathf.Abs(entry.CachedWidth - w) > 0.01f || Mathf.Abs(entry.CachedLength - l) > 0.01f)
            {
                DestroyResource(entry.DeckMesh);
                DestroyResource(entry.CurbMesh);
                entry.DeckMesh = BuildBridgeDeckMesh(w * 0.86f, l);
                entry.CurbMesh = BuildBridgeParapetAndArchMesh(w, l);
                entry.DeckFilter.sharedMesh = entry.DeckMesh;
                entry.CurbFilter.sharedMesh = entry.CurbMesh;
                entry.CachedWidth = w;
                entry.CachedLength = l;
            }

            Material mat = ResolveRuntimeTerrainMaterial(isDungeon);
            if (mat != null)
            {
                entry.DeckRenderer.sharedMaterial = mat;
                entry.CurbRenderer.sharedMaterial = mat;
            }

            entry.Root.transform.position = placement.WorldPosition;
            entry.Root.SetActive(true);
        }

        private PropBillboardEntry GetPropEntry(int index)
        {
            while (_propPool.Count <= index)
            {
                GameObject go = new GameObject("PropBillboard_" + _propPool.Count);
                go.transform.SetParent(_propsContainer, false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _propBillboardQuadMesh;

                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;

                PixelBillboard bb = go.AddComponent<PixelBillboard>();
                go.SetActive(false);

                _propPool.Add(new PropBillboardEntry
                {
                    Root = go,
                    Filter = mf,
                    Renderer = mr,
                    Billboard = bb
                });
            }

            return _propPool[index];
        }

        private StoneBridgeEntry GetBridgeEntry(int index)
        {
            while (_bridgePool.Count <= index)
            {
                GameObject root = new GameObject("StoneBridge_" + _bridgePool.Count);
                root.transform.SetParent(_bridgesContainer, false);

                GameObject deckGo = new GameObject("BridgeCobbleDeck");
                deckGo.transform.SetParent(root.transform, false);
                MeshFilter deckMf = deckGo.AddComponent<MeshFilter>();
                MeshRenderer deckMr = deckGo.AddComponent<MeshRenderer>();
                deckMr.shadowCastingMode = ShadowCastingMode.On;
                deckMr.receiveShadows = true;

                GameObject curbGo = new GameObject("BridgeStoneParapets");
                curbGo.transform.SetParent(root.transform, false);
                MeshFilter curbMf = curbGo.AddComponent<MeshFilter>();
                MeshRenderer curbMr = curbGo.AddComponent<MeshRenderer>();
                curbMr.shadowCastingMode = ShadowCastingMode.On;
                curbMr.receiveShadows = true;

                root.SetActive(false);
                _bridgePool.Add(new StoneBridgeEntry
                {
                    Root = root,
                    DeckFilter = deckMf,
                    DeckRenderer = deckMr,
                    CurbFilter = curbMf,
                    CurbRenderer = curbMr
                });
            }

            return _bridgePool[index];
        }

        private TorchLightEntry GetTorchLightEntry(int index)
        {
            while (_torchLightPool.Count <= index)
            {
                GameObject go = new GameObject("TorchLight_" + _torchLightPool.Count);
                go.transform.SetParent(_torchLightContainer, false);

                Light pointLight = go.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = new Color(1.0f, 0.580f, 0.239f, 1f);
                pointLight.range = 7.5f;
                pointLight.intensity = 2.8f;
                pointLight.shadows = LightShadows.None;

                BywaterTorchFlicker flicker = go.AddComponent<BywaterTorchFlicker>();
                flicker.baseIntensity = 2.8f;
                flicker.flickerAmplitude = 0.35f;
                flicker.flickerSpeed = 6.5f;
                flicker.emberTransform = null;

                go.SetActive(false);
                _torchLightPool.Add(new TorchLightEntry
                {
                    Root = go,
                    PointLight = pointLight,
                    Flicker = flicker
                });
            }

            return _torchLightPool[index];
        }

        private PortalMarkerEntry GetPortalMarker(int index)
        {
            while (_portalPool.Count <= index)
            {
                GameObject root = new GameObject("PortalRune_" + _portalPool.Count);
                root.transform.SetParent(_portalContainer, false);

                GameObject runeGo = new GameObject("RuneFloor");
                runeGo.transform.SetParent(root.transform, false);
                runeGo.transform.localPosition = new Vector3(0f, 0.022f, 0f);
                runeGo.transform.localScale = new Vector3(0.86f, 1f, 0.86f);
                runeGo.AddComponent<MeshFilter>().sharedMesh = _groundQuadMesh;
                MeshRenderer runeRenderer = runeGo.AddComponent<MeshRenderer>();
                runeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                runeRenderer.receiveShadows = false;

                GameObject beaconGo = new GameObject("RuneBeacon");
                beaconGo.transform.SetParent(root.transform, false);
                beaconGo.transform.localPosition = new Vector3(-0.34f, 0.02f, 0.34f);
                beaconGo.transform.localScale = new Vector3(0.18f, 0.85f, 1f);
                beaconGo.AddComponent<MeshFilter>().sharedMesh = _propBillboardQuadMesh;
                MeshRenderer beaconRenderer = beaconGo.AddComponent<MeshRenderer>();
                beaconRenderer.shadowCastingMode = ShadowCastingMode.Off;
                beaconRenderer.receiveShadows = false;
                PixelBillboard bb = beaconGo.AddComponent<PixelBillboard>();

                root.SetActive(false);
                _portalPool.Add(new PortalMarkerEntry
                {
                    Root = root,
                    RuneQuad = runeRenderer,
                    BeaconQuad = beaconRenderer,
                    BeaconBillboard = bb
                });
            }

            return _portalPool[index];
        }

        private void WarmupLightAndPortalPools()
        {
            for (int i = _torchLightPool.Count; i < MaxActiveTorchLights; i++)
            {
                GetTorchLightEntry(i);
            }

            for (int i = _portalPool.Count; i < 8; i++)
            {
                GetPortalMarker(i);
            }
        }

        private void EnsureCampfireEntry()
        {
            if (_campfireEntry != null && _campfireEntry.Root != null)
            {
                return;
            }

            GameObject root = new GameObject("CampfireProp");
            root.transform.SetParent(_propsContainer, false);

            GameObject quadGo = new GameObject("CampfireBillboard");
            quadGo.transform.SetParent(root.transform, false);
            quadGo.transform.localScale = new Vector3(0.95f, 1.15f, 1f);
            quadGo.AddComponent<MeshFilter>().sharedMesh = _propBillboardQuadMesh;
            MeshRenderer mr = quadGo.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            PixelBillboard bb = quadGo.AddComponent<PixelBillboard>();

            GameObject lightGo = new GameObject("CampfireLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.95f, -0.25f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.56f, 0.20f, 1f);
            light.range = 6.5f;
            light.intensity = 3.2f;
            light.shadows = LightShadows.None;

            BywaterTorchFlicker flicker = lightGo.AddComponent<BywaterTorchFlicker>();
            flicker.baseIntensity = 3.2f;
            flicker.flickerAmplitude = 0.55f;
            flicker.flickerSpeed = 6.8f;

            root.SetActive(false);
            _campfireEntry = new CampfireEntry
            {
                Root = root,
                BillboardRenderer = mr,
                Billboard = bb,
                PointLight = light,
                Flicker = flicker
            };
        }

        private void EnsureArenaBorderLine()
        {
            if (_arenaBorderLine != null)
            {
                return;
            }

            GameObject go = new GameObject("CombatArenaBorder");
            go.transform.SetParent(_arenaContainer, false);
            _arenaBorderLine = go.AddComponent<LineRenderer>();
            _arenaBorderLine.useWorldSpace = true;
            _arenaBorderLine.loop = true;
            _arenaBorderLine.startWidth = 0.10f;
            _arenaBorderLine.endWidth = 0.10f;
            _arenaBorderLine.numCornerVertices = 2;
            _arenaBorderLine.shadowCastingMode = ShadowCastingMode.Off;
            _arenaBorderLine.receiveShadows = false;

            Color borderColor = new Color(0.98f, 0.78f, 0.22f, 0.85f);
            _arenaBorderLine.startColor = borderColor;
            _arenaBorderLine.endColor = borderColor;

            if (_arenaBorderMaterial == null)
            {
                if (_catalog != null && _catalog.overlayBaseMaterial != null)
                {
                    _arenaBorderMaterial = new Material(_catalog.overlayBaseMaterial);
                }
                else
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                    _arenaBorderMaterial = new Material(shader);
                }
                _arenaBorderMaterial.name = "CombatArenaBorder_Mat";
                _arenaBorderMaterial.hideFlags = HideFlags.HideAndDontSave;
                if (_arenaBorderMaterial.HasProperty(BaseColorId)) _arenaBorderMaterial.SetColor(BaseColorId, borderColor);
                if (_arenaBorderMaterial.HasProperty(ColorId)) _arenaBorderMaterial.SetColor(ColorId, borderColor);
            }

            _arenaBorderLine.sharedMaterial = _arenaBorderMaterial;
            go.SetActive(false);
        }

        private void EnsureContainers()
        {
            if (_propsContainer == null) _propsContainer = CreateChildContainer("Props");
            if (_bridgesContainer == null) _bridgesContainer = CreateChildContainer("Bridges");
            if (_torchLightContainer == null) _torchLightContainer = CreateChildContainer("TorchLights");
            if (_portalContainer == null) _portalContainer = CreateChildContainer("Portals");
            if (_arenaContainer == null) _arenaContainer = CreateChildContainer("CombatArenaFrame");
        }

        private void EnsureCartographyComponents()
        {
            if (_elevationBuilder == null)
            {
                Transform t = CreateChildContainer("CartographyTerrain");
                _elevationBuilder = t.GetComponent<BywaterElevationBuilder>() ?? t.gameObject.AddComponent<BywaterElevationBuilder>();
            }

            if (_foliageScatter == null)
            {
                Transform t = CreateChildContainer("CartographyFoliage");
                _foliageScatter = t.GetComponent<BywaterFoliageScatter>() ?? t.gameObject.AddComponent<BywaterFoliageScatter>();
            }

            if (_topDownLightmap == null)
            {
                Transform t = CreateChildContainer("CartographyTopDownLightmap");
                _topDownLightmap = t.GetComponent<BywaterTopDownLightmap>() ?? t.gameObject.AddComponent<BywaterTopDownLightmap>();
            }

            if (_heroAnchor == null)
            {
                _heroAnchor = CreateChildContainer("CartographyHeroAnchor");
            }

            if (_dioramaController == null)
            {
                Transform t = CreateChildContainer("CartographyDioramaController");
                _dioramaController = t.GetComponent<BywaterDioramaController>() ?? t.gameObject.AddComponent<BywaterDioramaController>();
                _dioramaController.enableDemoPatrol = false;
            }
        }

        private void EnsurePartyLantern()
        {
            if (_partyLantern != null)
            {
                return;
            }

            Transform existing = transform.Find("PartyLantern");
            if (existing != null)
            {
                _partyLantern = existing.GetComponent<Light>();
                if (_partyLantern != null)
                {
                    return;
                }
            }

            GameObject go = new GameObject("PartyLantern");
            go.transform.SetParent(transform, false);
            _partyLantern = go.AddComponent<Light>();
            _partyLantern.type = LightType.Point;
            _partyLantern.color = new Color(1.0f, 0.635f, 0.290f, 1f);
            _partyLantern.intensity = 2.8f;
            _partyLantern.range = 11.0f;
            _partyLantern.shadows = LightShadows.Soft;
            _partyLantern.shadowStrength = 0.65f;
        }

        private void EnsureSharedMeshes()
        {
            if (_propBillboardQuadMesh == null)
            {
                _propBillboardQuadMesh = new Mesh
                {
                    name = "Cartography_RuntimePropQuad",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _propBillboardQuadMesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, 0f, 0f),
                    new Vector3( 0.5f, 0f, 0f),
                    new Vector3( 0.5f, 1f, 0f),
                    new Vector3(-0.5f, 1f, 0f)
                });
                _propBillboardQuadMesh.SetNormals(new[]
                {
                    Vector3.back, Vector3.back, Vector3.back, Vector3.back
                });
                _propBillboardQuadMesh.SetUVs(0, new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                });
                _propBillboardQuadMesh.SetUVs(1, new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                });
                _propBillboardQuadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _propBillboardQuadMesh.RecalculateBounds();
            }

            if (_groundQuadMesh == null)
            {
                _groundQuadMesh = new Mesh
                {
                    name = "Cartography_RuntimeGroundQuad",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _groundQuadMesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3( 0.5f, 0f, -0.5f),
                    new Vector3( 0.5f, 0f,  0.5f),
                    new Vector3(-0.5f, 0f,  0.5f)
                });
                _groundQuadMesh.SetNormals(new[]
                {
                    Vector3.up, Vector3.up, Vector3.up, Vector3.up
                });
                _groundQuadMesh.SetUVs(0, new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                });
                _groundQuadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _groundQuadMesh.RecalculateBounds();
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

        private static Camera ResolveActiveCamera()
        {
            return Camera.main;
        }

        private static float EvalBridgeArchY(float zNorm)
        {
            float clamped = Mathf.Clamp(zNorm, -1f, 1f);
            float arch = Mathf.Cos(clamped * Mathf.PI * 0.5f);
            return 0.04f + 0.36f * arch;
        }

        private static float EvalBridgeUndersideY(float zNorm)
        {
            float az = Mathf.Abs(zNorm);
            if (az < 0.62f)
            {
                float t = az / 0.62f;
                return -0.52f + 0.66f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            }
            return -0.52f;
        }

        private static Mesh BuildBridgeDeckMesh(float roadWidth, float lengthMeters)
        {
            const int segs = 12;
            float halfW = roadWidth * 0.5f;
            float halfL = lengthMeters * 0.5f;

            List<Vector3> verts = new List<Vector3>(segs * 4);
            List<Vector2> uvs = new List<Vector2>(segs * 4);
            List<Vector2> uv1 = new List<Vector2>(segs * 4);
            List<Color> colors = new List<Color>(segs * 4);
            List<int> tris = new List<int>(segs * 6);
            Color cobbleColor = new Color(0f, 0f, 1f, 0f);

            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs * 2f - 1f;
                float t1 = (float)(i + 1) / segs * 2f - 1f;
                float z0 = t0 * halfL;
                float z1 = t1 * halfL;
                float y0 = EvalBridgeArchY(t0);
                float y1 = EvalBridgeArchY(t1);
                float v0 = (z0 + halfL) * 0.5f;
                float v1 = (z1 + halfL) * 0.5f;
                float u0 = 0f;
                float u1 = roadWidth * 0.5f;

                float ao0 = Mathf.Lerp(0.96f, 0.78f, Mathf.Abs(t0));
                float ao1 = Mathf.Lerp(0.96f, 0.78f, Mathf.Abs(t1));

                AddBridgeQuad(
                    verts, uvs, tris,
                    new Vector3(-halfW, y0, z0),
                    new Vector3(-halfW, y1, z1),
                    new Vector3( halfW, y1, z1),
                    new Vector3( halfW, y0, z0),
                    new Vector2(u0, v0),
                    new Vector2(u0, v1),
                    new Vector2(u1, v1),
                    new Vector2(u1, v0));

                colors.Add(cobbleColor);
                colors.Add(cobbleColor);
                colors.Add(cobbleColor);
                colors.Add(cobbleColor);

                uv1.Add(new Vector2(0f, ao0));
                uv1.Add(new Vector2(0f, ao1));
                uv1.Add(new Vector2(0f, ao1));
                uv1.Add(new Vector2(0f, ao0));
            }

            Mesh m = new Mesh { name = "Cartography_RuntimeBridgeDeck", hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetUVs(1, uv1);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildBridgeParapetAndArchMesh(float totalWidth, float lengthMeters)
        {
            const int segs = 12;
            float halfTotal = totalWidth * 0.5f;
            float halfL = lengthMeters * 0.5f;
            const float curbW = 0.24f;
            const float curbRise = 0.28f;

            List<Vector3> verts = new List<Vector3>(segs * 24 + 16);
            List<Vector2> uvs = new List<Vector2>(segs * 24 + 16);
            List<Vector2> uv1 = new List<Vector2>(segs * 24 + 16);
            List<Color> colors = new List<Color>(segs * 24 + 16);
            List<int> tris = new List<int>(segs * 36 + 24);

            void StampAttr(float aoBot, float aoTop)
            {
                colors.Add(Color.clear);
                colors.Add(Color.clear);
                colors.Add(Color.clear);
                colors.Add(Color.clear);
                uv1.Add(new Vector2(1f, aoBot));
                uv1.Add(new Vector2(1f, aoTop));
                uv1.Add(new Vector2(1f, aoTop));
                uv1.Add(new Vector2(1f, aoBot));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                float xOuter = side < 0 ? -halfTotal : halfTotal - curbW;
                float xInner = xOuter + curbW;
                float xMin = Mathf.Min(xOuter, xInner);
                float xMax = Mathf.Max(xOuter, xInner);

                for (int i = 0; i < segs; i++)
                {
                    float t0 = (float)i / segs * 2f - 1f;
                    float t1 = (float)(i + 1) / segs * 2f - 1f;
                    float z0 = t0 * halfL;
                    float z1 = t1 * halfL;
                    float yTop0 = EvalBridgeArchY(t0) + curbRise;
                    float yTop1 = EvalBridgeArchY(t1) + curbRise;
                    float yBot0 = EvalBridgeUndersideY(t0);
                    float yBot1 = EvalBridgeUndersideY(t1);
                    float v0 = (z0 + halfL) * 0.5f;
                    float v1 = (z1 + halfL) * 0.5f;

                    AddBridgeQuad(verts, uvs, tris,
                        new Vector3(xMin, yTop0, z0), new Vector3(xMin, yTop1, z1),
                        new Vector3(xMax, yTop1, z1), new Vector3(xMax, yTop0, z0),
                        new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.18f, v1), new Vector2(0.18f, v0));
                    StampAttr(0.95f, 0.95f);

                    AddBridgeQuad(verts, uvs, tris,
                        new Vector3(xMin, yBot1, z1), new Vector3(xMin, yTop1, z1),
                        new Vector3(xMin, yTop0, z0), new Vector3(xMin, yBot0, z0),
                        new Vector2(v1, 0f), new Vector2(v1, 0.5f), new Vector2(v0, 0.5f), new Vector2(v0, 0f));
                    StampAttr(0.46f, 0.94f);

                    AddBridgeQuad(verts, uvs, tris,
                        new Vector3(xMax, yBot0, z0), new Vector3(xMax, yTop0, z0),
                        new Vector3(xMax, yTop1, z1), new Vector3(xMax, yBot1, z1),
                        new Vector2(v0, 0f), new Vector2(v0, 0.5f), new Vector2(v1, 0.5f), new Vector2(v1, 0f));
                    StampAttr(0.46f, 0.94f);
                }

                float yEndTop = EvalBridgeArchY(-1f) + curbRise;
                AddBridgeQuad(verts, uvs, tris,
                    new Vector3(xMin, -0.52f, -halfL), new Vector3(xMin, yEndTop, -halfL),
                    new Vector3(xMax, yEndTop, -halfL), new Vector3(xMax, -0.52f, -halfL),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.2f, 0.5f), new Vector2(0.2f, 0f));
                StampAttr(0.52f, 0.94f);

                AddBridgeQuad(verts, uvs, tris,
                    new Vector3(xMax, -0.52f, halfL), new Vector3(xMax, yEndTop, halfL),
                    new Vector3(xMin, yEndTop, halfL), new Vector3(xMin, -0.52f, halfL),
                    new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.2f, 0.5f), new Vector2(0.2f, 0f));
                StampAttr(0.52f, 0.94f);
            }

            Mesh m = new Mesh { name = "Cartography_RuntimeBridgeCurbs", hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetUVs(1, uv1);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private static void AddBridgeQuad(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<int> tris,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            Vector2 uv3)
        {
            int idx = verts.Count;
            verts.Add(p0);
            verts.Add(p1);
            verts.Add(p2);
            verts.Add(p3);
            uvs.Add(uv0);
            uvs.Add(uv1);
            uvs.Add(uv2);
            uvs.Add(uv3);
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
            tris.Add(idx);
            tris.Add(idx + 2);
            tris.Add(idx + 3);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _bridgePool.Count; i++)
            {
                DestroyResource(_bridgePool[i].DeckMesh);
                DestroyResource(_bridgePool[i].CurbMesh);
            }

            DestroyResource(_propBillboardQuadMesh);
            _propBillboardQuadMesh = null;

            DestroyResource(_groundQuadMesh);
            _groundQuadMesh = null;

            DestroyResource(_runtimeTerrainMat);
            _runtimeTerrainMat = null;

            DestroyResource(_runtimeWaterMat);
            _runtimeWaterMat = null;

            DestroyResource(_fallbackPropMaterial);
            _fallbackPropMaterial = null;

            DestroyResource(_arenaBorderMaterial);
            _arenaBorderMaterial = null;
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
    }
}
