using System.Collections.Generic;
using Realmz.Cartography;
using Realmz.Data;
using Realmz.Rendering;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

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
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int TerrainArrayId = Shader.PropertyToID("_TerrainArray");
        private static readonly int GroundValueScaleId = Shader.PropertyToID("_GroundValueScale");
        private static readonly int RoadValueBoostId = Shader.PropertyToID("_RoadValueBoost");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int HorizonHazeColorId = Shader.PropertyToID("_HorizonHazeColor");
        private static readonly int HorizonHazeStrengthId = Shader.PropertyToID("_HorizonHazeStrength");
        private static readonly int LanternPoolIntensityId = Shader.PropertyToID("_LanternPoolIntensity");
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int HeroWorldPosId = Shader.PropertyToID("_HeroWorldPos");

        private const float OccludedPropAlpha = 0.42f;
        private const float TreeFadeLerpSpeed = 8f;

        private sealed class PropBillboardEntry
        {
            public GameObject Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public PixelBillboard Billboard;
            public Material OriginalMaterial;
            public CartographyPropKind Kind;
            public Vector3 WorldPosition;
            public Vector2 SizeMeters;
            public float CurrentAlpha = 1f;
            public float LastAppliedAlpha = 1f;
            public bool IsAnimated4Frame;
            public bool IsFloorDecal;
            public bool WasTransparentFaded;
            public bool HasInitialPropertyBlock;
            public int LastAnimFrame = -1;
        }

        private sealed class NpcUnitEntry
        {
            public CartographyNpcPlacement Placement;
            public BywaterUnitView View;
            public GameObject PromptRoot;
            public TMPro.TMP_Text PromptText;
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
            public bool IsAnimated4Frame;
            public GameObject TentRoot;
            public MeshRenderer TentRenderer;
            public PixelBillboard TentBillboard;
        }

        private BywaterAssetCatalog _catalog;
        private TileMapData _currentMap;
        private CartographyWorldData _cartographyData;
        private GridCoord _focusCenter;
        private bool _hasRendered;

        private BywaterElevationBuilder _elevationBuilder;
        private BywaterFoliageScatter _foliageScatter;
        private BywaterFoliageScatter _dungeonFoliageScatter;
        private BywaterTopDownLightmap _topDownLightmap;
        private BywaterDioramaController _dioramaController;
        private Transform _heroAnchor;

        private Transform _propsContainer;
        private Transform _npcContainer;
        private Transform _bridgesContainer;
        private Transform _torchLightContainer;
        private Transform _portalContainer;
        private Transform _arenaContainer;

        private readonly List<PropBillboardEntry> _propPool = new List<PropBillboardEntry>(256);
        private readonly List<NpcUnitEntry> _npcPool = new List<NpcUnitEntry>(8);
        private readonly Dictionary<Material, Material> _fadedTransparentMaterialCache = new Dictionary<Material, Material>(16);
        private int _activeNpcCount;
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
        private Material _portalRuneMaterial;
        private Texture2D _portalRuneTexture;
        private Material _portalBeaconMaterial;
        private Texture2D _portalBeaconTexture;
        private Vector3 _lastPropBillboardFwdXZ = Vector3.zero;
        private bool _hasLastPropBillboardFwd;
        private MaterialPropertyBlock _propFadeBlock;
        private MaterialPropertyBlock _animatedPropBlock;
        private MaterialPropertyBlock _portalBeaconBlock;
        private int _activePropCount;
        private int _activePortalCount;
        private int _lastAnimatedPropFrame = -1;
        private float _terrainGridHintStrength;
        private bool _townNpcsHiddenForCombat;

        public CartographyWorldData CartographyData => _cartographyData;
        public Collider TerrainCollider => _elevationBuilder != null ? _elevationBuilder.terrainMeshCollider : null;
        public Transform PropsRoot => _propsContainer;
        public bool AreTownNpcsHiddenForCombat => _townNpcsHiddenForCombat;

        public int VisibleTownNpcCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
                {
                    NpcUnitEntry entry = _npcPool[i];
                    if (entry != null && entry.View != null && entry.View.gameObject.activeInHierarchy)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

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
                RebuildCartographyNpcs(map);
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
                if (_campfireEntry.TentRoot != null)
                {
                    _campfireEntry.TentRoot.SetActive(false);
                }
                return;
            }

            Camera cam = ResolveActiveCamera();
            _campfireEntry.Root.transform.position = worldPos;
            Material campfireMat = _catalog != null ? _catalog.GetCartographyPropMaterial(CartographyPropKind.Campfire4f) : null;
            bool is4f = campfireMat != null;
            if (!is4f)
            {
                campfireMat = ResolvePropMaterial(CartographyPropKind.TorchBrazier);
            }
            _campfireEntry.IsAnimated4Frame = is4f;

            if (_campfireEntry.BillboardRenderer != null && campfireMat != null)
            {
                _campfireEntry.BillboardRenderer.sharedMaterial = campfireMat;
                if (is4f)
                {
                    int frame = Application.isPlaying ? ((int)(Time.time * 6f) % 4) : 0;
                    ApplyAnimated4FramePropertyBlock(_campfireEntry.BillboardRenderer, 1f, frame);
                }
                else
                {
                    _campfireEntry.BillboardRenderer.SetPropertyBlock(null);
                }
            }
            if (_campfireEntry.Billboard != null)
            {
                _campfireEntry.Billboard.targetCamera = cam;
            }

            _campfireEntry.Root.SetActive(true);

            if (_campfireEntry.TentRoot != null)
            {
                Material tentMat = _catalog != null ? _catalog.GetCartographyPropMaterial(CartographyPropKind.CampTent) : null;
                if (tentMat != null)
                {
                    _campfireEntry.TentRoot.transform.position = ResolveCampTentPosition(worldPos);
                    if (_campfireEntry.TentRenderer != null)
                    {
                        _campfireEntry.TentRenderer.sharedMaterial = tentMat;
                    }
                    if (_campfireEntry.TentBillboard != null)
                    {
                        _campfireEntry.TentBillboard.targetCamera = cam;
                    }
                    _campfireEntry.TentRoot.SetActive(true);
                }
                else
                {
                    _campfireEntry.TentRoot.SetActive(false);
                }
            }
        }

        private Vector3 ResolveCampTentPosition(Vector3 campfireWorldPos)
        {
            if (_currentMap == null || _cartographyData == null)
            {
                return new Vector3(campfireWorldPos.x - 1.65f, campfireWorldPos.y, campfireWorldPos.z - 0.35f);
            }

            CartographyWorldData.WorldToGrid(campfireWorldPos, out int cx, out int cy);
            float campElev = _cartographyData.GetElevation(cx, cy);

            // Heroes sit in a northern semicircle (-dy in grid, +Z in world), so check clear tiles beside/south first.
            int[] dxs = { -2, 2, -1, 1, 0, -2, 2, 0 };
            int[] dys = { 0, 0, 1, 1, 1, 1, 1, 2 };

            for (int i = 0; i < dxs.Length; i++)
            {
                int tx = cx + dxs[i];
                int ty = cy + dys[i];
                if (!_currentMap.InBounds(tx, ty))
                {
                    continue;
                }

                TileRule rule = _currentMap.GetTileRule(_currentMap.GetTile(tx, ty));
                if (!rule.Passable || rule.NeedsBoat)
                {
                    continue;
                }

                CartographyTile tile = _cartographyData.GetTile(tx, ty);
                if (tile.IsWater || Mathf.Abs(tile.Elevation - campElev) > 0.45f)
                {
                    continue;
                }

                return CartographyWorldData.GridToWorld(tx, ty, tile.Elevation);
            }

            return new Vector3(
                Mathf.Clamp(campfireWorldPos.x - 1.5f, 0.75f, _currentMap.Width - 0.75f),
                campfireWorldPos.y,
                Mathf.Clamp(campfireWorldPos.z - 0.5f, -_currentMap.Height + 0.75f, -0.75f));
        }

        public void SetTerrainGridHint(float strength)
        {
            _terrainGridHintStrength = Mathf.Max(0f, strength);
        }

        public int ActiveNpcCount => _activeNpcCount;

        public IReadOnlyList<CartographyNpcPlacement> ActiveNpcs =>
            _cartographyData != null ? _cartographyData.Npcs : System.Array.Empty<CartographyNpcPlacement>();

        public bool TryGetNpcAtCoord(GridCoord coord, out CartographyNpcPlacement npc)
        {
            for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
            {
                CartographyNpcPlacement p = _npcPool[i].Placement;
                if (p.TileX == coord.X && p.TileY == coord.Y)
                {
                    npc = p;
                    return true;
                }
            }

            npc = default;
            return false;
        }

        public bool TryFindNearestNpc(GridCoord origin, out CartographyNpcPlacement npc, out int distanceTiles)
        {
            npc = default;
            distanceTiles = int.MaxValue;
            bool found = false;

            for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
            {
                CartographyNpcPlacement p = _npcPool[i].Placement;
                int dist = Mathf.Max(Mathf.Abs(p.TileX - origin.X), Mathf.Abs(p.TileY - origin.Y));
                if (dist < distanceTiles)
                {
                    distanceTiles = dist;
                    npc = p;
                    found = true;
                }
            }

            return found;
        }

        public void SetTownNpcsHiddenForCombat(bool hidden)
        {
            _townNpcsHiddenForCombat = hidden;
            if (_npcContainer != null)
            {
                _npcContainer.gameObject.SetActive(!hidden);
            }

            for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
            {
                NpcUnitEntry entry = _npcPool[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry.View != null)
                {
                    entry.View.gameObject.SetActive(!hidden);
                }

                if (hidden && entry.PromptRoot != null)
                {
                    entry.PromptRoot.SetActive(false);
                }
            }
        }

        public void UpdateNpcProximityPrompts(GridCoord heroCoord, Vector3 heroWorld, bool hidePrompts = false)
        {
            if (_townNpcsHiddenForCombat)
            {
                for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
                {
                    NpcUnitEntry entry = _npcPool[i];
                    if (entry != null && entry.PromptRoot != null)
                    {
                        entry.PromptRoot.SetActive(false);
                    }
                }
                return;
            }

            for (int i = 0; i < _activeNpcCount && i < _npcPool.Count; i++)
            {
                NpcUnitEntry entry = _npcPool[i];
                if (entry == null || entry.View == null)
                {
                    continue;
                }

                float dxWorld = heroWorld.x - entry.View.transform.position.x;
                if (Mathf.Abs(dxWorld) > 0.08f)
                {
                    entry.View.SetFacingRight(dxWorld >= 0f);
                }

                int dist = Mathf.Max(Mathf.Abs(entry.Placement.TileX - heroCoord.X), Mathf.Abs(entry.Placement.TileY - heroCoord.Y));
                if (entry.PromptRoot != null)
                {
                    entry.PromptRoot.SetActive(!hidePrompts && dist <= 2);
                }
            }
        }

        public void UpdateTreeOcclusionFade(IReadOnlyList<Vector3> unitWorldPositions, bool fadeCenterBuildingsInCombat = false)
        {
            if (_activePropCount <= 0)
            {
                return;
            }

            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 1f;
            float lerpFactor = Application.isPlaying ? Mathf.Clamp01(TreeFadeLerpSpeed * dt) : 1f;
            _propFadeBlock ??= new MaterialPropertyBlock();

            int limit = Mathf.Min(_activePropCount, _propPool.Count);
            const float maxCheckDistSq = 18.0f;

            for (int i = 0; i < limit; i++)
            {
                PropBillboardEntry entry = _propPool[i];
                if (entry == null || entry.Renderer == null || entry.Root == null || !entry.Root.activeSelf)
                {
                    continue;
                }

                bool occludes = false;
                bool canOcclude = IsOccludingPropKind(entry.Kind, fadeCenterBuildingsInCombat);
                if (canOcclude && unitWorldPositions != null && unitWorldPositions.Count > 0)
                {
                    bool checkBounds = entry.CurrentAlpha < 0.995f || entry.WasTransparentFaded;
                    if (!checkBounds)
                    {
                        for (int u = 0; u < unitWorldPositions.Count; u++)
                        {
                            Vector3 unitPos = unitWorldPositions[u];
                            float dx = unitPos.x - entry.WorldPosition.x;
                            float dz = unitPos.z - entry.WorldPosition.z;
                            if ((dx * dx + dz * dz) <= maxCheckDistSq)
                            {
                                checkBounds = true;
                                break;
                            }
                        }
                    }

                    if (checkBounds)
                    {
                        float halfWidth = Mathf.Clamp(entry.SizeMeters.x * 0.38f, 0.55f, 1.75f);
                        float maxDepth = Mathf.Clamp(entry.SizeMeters.y * 0.72f, 0.90f, 2.75f);

                        for (int u = 0; u < unitWorldPositions.Count; u++)
                        {
                            Vector3 unitPos = unitWorldPositions[u];
                            float dz = unitPos.z - entry.WorldPosition.z;
                            if (dz >= 0.05f && dz <= maxDepth && Mathf.Abs(unitPos.x - entry.WorldPosition.x) <= halfWidth)
                            {
                                occludes = true;
                                break;
                            }
                        }
                    }
                }

                if (!entry.IsAnimated4Frame &&
                    !occludes &&
                    Mathf.Abs(entry.CurrentAlpha - 1f) < 0.004f &&
                    !entry.WasTransparentFaded &&
                    entry.HasInitialPropertyBlock &&
                    Mathf.Abs(entry.LastAppliedAlpha - 1f) < 0.001f)
                {
                    continue;
                }

                float targetAlpha = occludes ? OccludedPropAlpha : 1f;
                if (!Application.isPlaying || Mathf.Abs(entry.CurrentAlpha - targetAlpha) < 0.004f)
                {
                    entry.CurrentAlpha = targetAlpha;
                }
                else
                {
                    entry.CurrentAlpha = Mathf.Lerp(entry.CurrentAlpha, targetAlpha, lerpFactor);
                }

                bool isTransparentFaded = entry.CurrentAlpha < 0.995f;
                if (entry.OriginalMaterial != null &&
                    (isTransparentFaded != entry.WasTransparentFaded || !entry.HasInitialPropertyBlock))
                {
                    Material targetMat = isTransparentFaded
                        ? GetOrCreateFadedTransparentMaterial(entry.OriginalMaterial)
                        : entry.OriginalMaterial;
                    if (targetMat != null && !ReferenceEquals(entry.Renderer.sharedMaterial, targetMat))
                    {
                        entry.Renderer.sharedMaterial = targetMat;
                    }
                }

                if (entry.IsAnimated4Frame)
                {
                    int frame = Application.isPlaying ? ((int)(Time.time * 6f) % 4) : 0;
                    if (!entry.HasInitialPropertyBlock ||
                        frame != entry.LastAnimFrame ||
                        Mathf.Abs(entry.CurrentAlpha - entry.LastAppliedAlpha) >= 0.001f)
                    {
                        ApplyAnimated4FramePropertyBlock(entry.Renderer, entry.CurrentAlpha, frame);
                        entry.LastAnimFrame = frame;
                        entry.LastAppliedAlpha = entry.CurrentAlpha;
                    }
                }
                else
                {
                    if (!entry.HasInitialPropertyBlock ||
                        isTransparentFaded != entry.WasTransparentFaded ||
                        Mathf.Abs(entry.CurrentAlpha - entry.LastAppliedAlpha) >= 0.001f)
                    {
                        Color tint = new Color(1f, 1f, 1f, entry.CurrentAlpha);
                        _propFadeBlock.Clear();
                        _propFadeBlock.SetColor(TintColorId, tint);
                        _propFadeBlock.SetColor(ColorId, tint);
                        entry.Renderer.SetPropertyBlock(_propFadeBlock);
                        entry.LastAppliedAlpha = entry.CurrentAlpha;
                    }
                }

                entry.WasTransparentFaded = isTransparentFaded;
                entry.HasInitialPropertyBlock = true;
            }
        }

        private void ApplyAnimated4FramePropertyBlock(MeshRenderer renderer, float alpha, int frame)
        {
            if (renderer == null)
            {
                return;
            }

            _animatedPropBlock ??= new MaterialPropertyBlock();
            _animatedPropBlock.Clear();
            Color tint = new Color(1f, 1f, 1f, alpha);
            Vector4 st = new Vector4(0.25f, 1f, (frame & 3) * 0.25f, 0f);
            _animatedPropBlock.SetColor(TintColorId, tint);
            _animatedPropBlock.SetColor(ColorId, tint);
            _animatedPropBlock.SetColor(BaseColorId, tint);
            _animatedPropBlock.SetVector(MainTexStId, st);
            _animatedPropBlock.SetVector(BaseMapStId, st);
            renderer.SetPropertyBlock(_animatedPropBlock);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SyncPropBillboardsIfCameraRotated();

            int frame = (int)(Time.time * 6f) % 4;
            if (frame != _lastAnimatedPropFrame)
            {
                _lastAnimatedPropFrame = frame;
                int propLimit = Mathf.Min(_activePropCount, _propPool.Count);
                for (int i = 0; i < propLimit; i++)
                {
                    PropBillboardEntry entry = _propPool[i];
                    if (entry != null && entry.IsAnimated4Frame && entry.Renderer != null && entry.Root != null && entry.Root.activeSelf)
                    {
                        ApplyAnimated4FramePropertyBlock(entry.Renderer, entry.CurrentAlpha, frame);
                        entry.LastAnimFrame = frame;
                        entry.LastAppliedAlpha = entry.CurrentAlpha;
                        entry.HasInitialPropertyBlock = true;
                    }
                }

                if (_campfireEntry != null && _campfireEntry.IsAnimated4Frame &&
                    _campfireEntry.Root != null && _campfireEntry.Root.activeSelf &&
                    _campfireEntry.BillboardRenderer != null)
                {
                    ApplyAnimated4FramePropertyBlock(_campfireEntry.BillboardRenderer, 1f, frame);
                }
            }

            if (_activePortalCount <= 0)
            {
                return;
            }

            _portalBeaconBlock ??= new MaterialPropertyBlock();
            float t = Time.unscaledTime;
            int limit = Mathf.Min(_activePortalCount, _portalPool.Count);
            for (int i = 0; i < limit; i++)
            {
                PortalMarkerEntry entry = _portalPool[i];
                if (entry == null || entry.BeaconQuad == null || entry.Root == null || !entry.Root.activeSelf)
                {
                    continue;
                }

                float pulse = 0.34f + 0.10f * Mathf.Sin(t * 2.2f + i * 1.3f);
                Color col = new Color(0.22f, 0.74f, 0.97f, pulse);
                _portalBeaconBlock.Clear();
                _portalBeaconBlock.SetColor(BaseColorId, col);
                _portalBeaconBlock.SetColor(ColorId, col);
                entry.BeaconQuad.SetPropertyBlock(_portalBeaconBlock);
            }
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
            map.SetBlockedPropFootprints(_cartographyData.ExportBlockedPropGridCoords());
            bool isDungeon = map.IsDungeon;
            Camera cam = ResolveActiveCamera();

            // 1. Elevation Terrain + MeshCollider + ShallowWaterPlane
            Material terrainMat = ResolveRuntimeTerrainMaterial(isDungeon);
            Material waterMat = ResolveRuntimeWaterMaterial(isDungeon);
            _elevationBuilder.terrainMaterial = terrainMat;
            _elevationBuilder.waterMaterial = waterMat;
            _elevationBuilder.Rebuild(_cartographyData);

            // 2. Top-Down Lightmap (disable dark daytime cloud blotches in underground dungeons)
            if (isDungeon)
            {
                _topDownLightmap.cloudStrength = 0.0f;
                _topDownLightmap.cloudShadowColor = Color.white;
                _topDownLightmap.canopyShadowColor = new Color(0.72f, 0.76f, 0.88f, 1f);
            }
            else
            {
                _topDownLightmap.cloudStrength = 0.82f;
                _topDownLightmap.cloudShadowColor = new Color(0.56f, 0.62f, 0.74f, 1f);
                _topDownLightmap.canopyShadowColor = new Color(0.48f, 0.54f, 0.66f, 1f);
            }

            _topDownLightmap.ConfigureFromWorldData(_cartographyData);
            _topDownLightmap.UpdateLightmap(Application.isPlaying ? Time.time : 0f);

            // 3. Instanced Foliage Scatter (outdoor grass/marsh + sparse dungeon cave scatter on separate child)
            _foliageScatter.foliageMaterial = _catalog != null ? _catalog.cartographyFoliageMaterial : null;
            _foliageScatter.marshFoliageMaterial = _catalog != null ? _catalog.cartographyMarshFoliageMaterial : null;
            _foliageScatter.targetCamera = cam;
            _foliageScatter.RebuildScatter(_cartographyData);
            _foliageScatter.gameObject.SetActive(!isDungeon);

            if (_dungeonFoliageScatter != null)
            {
                if (isDungeon)
                {
                    Material dungeonFoliageMat = _catalog != null ? _catalog.cartographyDungeonFoliageMaterial : null;
                    _dungeonFoliageScatter.targetCamera = cam;
                    _dungeonFoliageScatter.RebuildDungeonScatter(_cartographyData, dungeonFoliageMat);
                    _dungeonFoliageScatter.gameObject.SetActive(dungeonFoliageMat != null);
                }
                else
                {
                    _dungeonFoliageScatter.gameObject.SetActive(false);
                }
            }

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
                        fwd.Normalize();
                        initialYaw = Quaternion.LookRotation(fwd, Vector3.up);
                        _lastPropBillboardFwdXZ = fwd;
                        _hasLastPropBillboardFwd = true;
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
                    float sizeX = Mathf.Max(0.25f, prop.SizeMeters.x);
                    float sizeY = Mathf.Max(0.25f, prop.SizeMeters.y);
                    bool isFloorDecal = IsFloorDecalPropKind(prop.Kind);
                    bool isAnimated4f = IsAnimated4FramePropKind(prop.Kind);

                    entry.Kind = prop.Kind;
                    entry.IsFloorDecal = isFloorDecal;
                    entry.IsAnimated4Frame = isAnimated4f;
                    entry.SizeMeters = new Vector2(sizeX, sizeY);
                    entry.CurrentAlpha = 1f;
                    entry.LastAppliedAlpha = 1f;
                    entry.WasTransparentFaded = false;
                    entry.HasInitialPropertyBlock = true;
                    entry.OriginalMaterial = ResolvePropMaterial(prop.Kind);
                    entry.Renderer.sharedMaterial = entry.OriginalMaterial;
                    entry.Billboard.targetCamera = cam;

                    if (isFloorDecal)
                    {
                        Vector3 decalPos = new Vector3(prop.WorldPosition.x, prop.WorldPosition.y + 0.04f, prop.WorldPosition.z);
                        entry.WorldPosition = decalPos;
                        entry.Filter.sharedMesh = _groundQuadMesh;
                        entry.Billboard.enabled = false;
                        entry.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                        entry.Root.transform.position = decalPos;
                        entry.Root.transform.rotation = Quaternion.identity;
                        entry.Root.transform.localScale = new Vector3(sizeX, 1f, sizeY);
                    }
                    else
                    {
                        entry.WorldPosition = prop.WorldPosition;
                        entry.Filter.sharedMesh = _propBillboardQuadMesh;
                        // Prop yaw is synced centrally by BywaterWorldRenderer only when camera yaw changes.
                        entry.Billboard.enabled = false;
                        entry.Renderer.shadowCastingMode = ShouldPropCastShadow(prop.Kind)
                            ? ShadowCastingMode.On
                            : ShadowCastingMode.Off;
                        entry.Root.transform.position = prop.WorldPosition;
                        entry.Root.transform.rotation = initialYaw;
                        entry.Root.transform.localScale = new Vector3(sizeX, sizeY, 1f);
                    }

                    if (isAnimated4f)
                    {
                        int frame = Application.isPlaying ? ((int)(Time.time * 6f) % 4) : 0;
                        ApplyAnimated4FramePropertyBlock(entry.Renderer, 1f, frame);
                        entry.LastAnimFrame = frame;
                    }
                    else
                    {
                        entry.LastAnimFrame = -1;
                        _propFadeBlock ??= new MaterialPropertyBlock();
                        _propFadeBlock.Clear();
                        _propFadeBlock.SetColor(TintColorId, Color.white);
                        _propFadeBlock.SetColor(ColorId, Color.white);
                        entry.Renderer.SetPropertyBlock(_propFadeBlock);
                    }
                    entry.Root.SetActive(true);
                }
            }

            _activePropCount = propIdx;

            for (int i = propIdx; i < _propPool.Count; i++)
            {
                _propPool[i].Root.SetActive(false);
            }

            for (int i = bridgeIdx; i < _bridgePool.Count; i++)
            {
                _bridgePool[i].Root.SetActive(false);
            }
        }

        private void RebuildCartographyNpcs(TileMapData map)
        {
            int npcIdx = 0;
            if (!map.IsDungeon && _cartographyData != null && _cartographyData.Npcs != null)
            {
                IReadOnlyList<CartographyNpcPlacement> npcs = _cartographyData.Npcs;
                TMPro.TMP_FontAsset font = _catalog != null ? _catalog.uiFont : null;
                Color npcRingColor = new Color(0.95f, 0.78f, 0.30f, 1f);

                for (int i = 0; i < npcs.Count; i++)
                {
                    CartographyNpcPlacement npc = npcs[i];
                    NpcUnitEntry entry = GetNpcEntry(npcIdx++);
                    entry.Placement = npc;

                    Material npcMat = _catalog != null
                        ? _catalog.ResolveCartographyUnitMaterial(npc.NpcId, null)
                        : null;

                    entry.View.gameObject.SetActive(true);
                    entry.View.Configure(
                        npc.NpcId,
                        npc.DisplayName,
                        npcMat,
                        npcRingColor,
                        font,
                        spriteSize: 2.0f,
                        showHud: false,
                        isAllyOverride: true,
                        catalog: _catalog);
                    entry.View.SetWorldPosition(npc.WorldPosition);
                    entry.View.UpdateStats(1, 1, 0, 0, string.Empty, isCurrentTurn: false, isAlive: true);
                    if (entry.PromptText != null)
                    {
                        string actionVerb = string.Equals(npc.VenueKind, "temple", System.StringComparison.OrdinalIgnoreCase)
                            ? "Visit"
                            : "Trade";
                        entry.PromptText.text = $"[E / Click] {actionVerb}: {npc.DisplayName}";
                        if (font != null)
                        {
                            entry.PromptText.font = font;
                        }
                    }
                    if (entry.PromptRoot != null)
                    {
                        entry.PromptRoot.SetActive(false);
                    }

                    if (_townNpcsHiddenForCombat)
                    {
                        entry.View.gameObject.SetActive(false);
                    }
                }
            }

            _activeNpcCount = npcIdx;
            for (int i = npcIdx; i < _npcPool.Count; i++)
            {
                if (_npcPool[i]?.View != null)
                {
                    _npcPool[i].View.gameObject.SetActive(false);
                }
            }

            if (_npcContainer != null)
            {
                _npcContainer.gameObject.SetActive(!_townNpcsHiddenForCombat);
            }
        }

        private NpcUnitEntry GetNpcEntry(int index)
        {
            while (_npcPool.Count <= index)
            {
                GameObject go = new GameObject("TownNpc_" + _npcPool.Count);
                go.transform.SetParent(_npcContainer, false);
                BywaterUnitView view = go.AddComponent<BywaterUnitView>();

                GameObject promptGo = new GameObject("NpcInteractPrompt", typeof(RectTransform), typeof(Canvas));
                promptGo.transform.SetParent(go.transform, false);
                promptGo.transform.localPosition = new Vector3(0f, 1.92f, 0f);
                promptGo.transform.localScale = Vector3.one * 0.0066f;
                promptGo.AddComponent<PixelBillboard>();

                Canvas promptCanvas = promptGo.GetComponent<Canvas>();
                promptCanvas.renderMode = RenderMode.WorldSpace;
                promptCanvas.sortingOrder = 40;

                RectTransform promptRt = promptGo.GetComponent<RectTransform>();
                promptRt.sizeDelta = new Vector2(280f, 44f);

                Image outlineImg = promptGo.AddComponent<Image>();
                outlineImg.color = new Color(0.761f, 0.580f, 0.278f, 0.90f);
                outlineImg.raycastTarget = false;

                GameObject innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
                innerGo.transform.SetParent(promptGo.transform, false);
                RectTransform innerRt = innerGo.GetComponent<RectTransform>();
                innerRt.anchorMin = Vector2.zero;
                innerRt.anchorMax = Vector2.one;
                innerRt.offsetMin = new Vector2(2f, 2f);
                innerRt.offsetMax = new Vector2(-2f, -2f);
                Image innerImg = innerGo.GetComponent<Image>();
                innerImg.color = new Color(0.059f, 0.078f, 0.133f, 0.92f);
                innerImg.raycastTarget = false;

                GameObject textGo = new GameObject("PromptText", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                textGo.transform.SetParent(innerGo.transform, false);
                RectTransform textRt = textGo.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(8f, 3f);
                textRt.offsetMax = new Vector2(-8f, -3f);

                TMPro.TextMeshProUGUI tmp = textGo.GetComponent<TMPro.TextMeshProUGUI>();
                tmp.fontSize = 14.5f;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 11f;
                tmp.fontSizeMax = 15f;
                tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
                tmp.overflowMode = TMPro.TextOverflowModes.Truncate;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.color = new Color(0.98f, 0.88f, 0.48f, 1f);
                tmp.raycastTarget = false;

                promptGo.SetActive(false);

                go.SetActive(false);
                _npcPool.Add(new NpcUnitEntry
                {
                    View = view,
                    PromptRoot = promptGo,
                    PromptText = tmp
                });
            }

            return _npcPool[index];
        }

        private Material GetOrCreateFadedTransparentMaterial(Material sourceMat)
        {
            if (sourceMat == null)
            {
                return null;
            }

            if (_fadedTransparentMaterialCache.TryGetValue(sourceMat, out Material cached) && cached != null)
            {
                return cached;
            }

            Material faded = new Material(sourceMat)
            {
                name = sourceMat.name + "_FadeTransparent",
                hideFlags = HideFlags.HideAndDontSave
            };
            faded.SetOverrideTag("RenderType", "Transparent");
            faded.renderQueue = (int)RenderQueue.Transparent;
            if (faded.HasProperty(SrcBlendId)) faded.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            if (faded.HasProperty(DstBlendId)) faded.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            if (faded.HasProperty(ZWriteId)) faded.SetFloat(ZWriteId, 0f);
            _fadedTransparentMaterialCache[sourceMat] = faded;
            return faded;
        }

        private void RebuildMapPortals(TileMapData map)
        {
            int portalIdx = 0;
            IReadOnlyList<MapPortal> portals = map.Portals;
            if (portals != null)
            {
                Material runeMat = GetOrCreatePortalRuneMaterial();
                Material beaconMat = GetOrCreatePortalBeaconMaterial();
                Camera cam = ResolveActiveCamera();
                _portalBeaconBlock ??= new MaterialPropertyBlock();

                for (int i = 0; i < portals.Count; i++)
                {
                    MapPortal portal = portals[i];
                    float elev = GetTileElevation(portal.X, portal.Y);
                    Vector3 pos = CartographyWorldData.GridToWorld(portal.X, portal.Y, elev);

                    PortalMarkerEntry entry = GetPortalMarker(portalIdx++);
                    entry.Root.transform.position = pos;
                    if (runeMat != null)
                    {
                        entry.RuneQuad.sharedMaterial = runeMat;
                    }
                    if (beaconMat != null)
                    {
                        entry.BeaconQuad.sharedMaterial = beaconMat;
                    }

                    Color beaconCol = new Color(0.22f, 0.74f, 0.97f, 0.40f);
                    _portalBeaconBlock.Clear();
                    _portalBeaconBlock.SetColor(BaseColorId, beaconCol);
                    _portalBeaconBlock.SetColor(ColorId, beaconCol);
                    entry.BeaconQuad.SetPropertyBlock(_portalBeaconBlock);

                    entry.BeaconBillboard.targetCamera = cam;
                    entry.Root.SetActive(true);
                }
            }

            _activePortalCount = portalIdx;

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
                CartographyPropKind k = props[i].Kind;
                if (k == CartographyPropKind.TorchBrazier ||
                    k == CartographyPropKind.Campfire4f ||
                    k == CartographyPropKind.DungeonWallSconce4f)
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
            bool hasDedicatedDungeonMat = isDungeon && _catalog != null && _catalog.cartographyDungeonTerrainMaterial != null;
            Material source = _catalog != null
                ? (hasDedicatedDungeonMat ? _catalog.cartographyDungeonTerrainMaterial : _catalog.cartographyTerrainMaterial)
                : null;
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

            if (_runtimeTerrainMat.HasProperty(LayerCountId) &&
                _runtimeTerrainMat.HasProperty(TerrainArrayId) &&
                _runtimeTerrainMat.GetTexture(TerrainArrayId) is Texture2DArray terrainArr)
            {
                _runtimeTerrainMat.SetFloat(LayerCountId, terrainArr.depth);
            }

            if (isDungeon)
            {
                float groundVal = hasDedicatedDungeonMat ? 0.78f : 0.42f;
                float roadVal = hasDedicatedDungeonMat ? 0.86f : 0.46f;
                if (_runtimeTerrainMat.HasProperty(GroundValueScaleId)) _runtimeTerrainMat.SetFloat(GroundValueScaleId, groundVal);
                if (_runtimeTerrainMat.HasProperty(RoadValueBoostId)) _runtimeTerrainMat.SetFloat(RoadValueBoostId, roadVal);
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
            bool hasDedicatedDungeonWater = isDungeon && _catalog != null && _catalog.cartographyDungeonWaterMaterial != null;
            Material source = _catalog != null
                ? (hasDedicatedDungeonWater
                    ? _catalog.cartographyDungeonWaterMaterial
                    : (_catalog.cartographyWaterMaterial != null ? _catalog.cartographyWaterMaterial : _catalog.shallowWaterMaterial))
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
                if (!hasDedicatedDungeonWater)
                {
                    if (_runtimeWaterMat.HasProperty(BaseColorId)) _runtimeWaterMat.SetColor(BaseColorId, new Color(0.08f, 0.16f, 0.24f, 1f));
                    if (_runtimeWaterMat.HasProperty(ShallowColorId)) _runtimeWaterMat.SetColor(ShallowColorId, new Color(0.08f, 0.18f, 0.28f, 0.90f));
                    if (_runtimeWaterMat.HasProperty(DeepColorId)) _runtimeWaterMat.SetColor(DeepColorId, new Color(0.03f, 0.08f, 0.14f, 0.98f));
                }
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

        private void SyncPropBillboardsIfCameraRotated()
        {
            if (_activePropCount <= 0)
            {
                return;
            }

            Camera cam = ResolveActiveCamera();
            if (cam == null)
            {
                return;
            }

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                return;
            }

            fwd.Normalize();
            if (_hasLastPropBillboardFwd && (_lastPropBillboardFwdXZ - fwd).sqrMagnitude < 1e-6f)
            {
                return;
            }

            _lastPropBillboardFwdXZ = fwd;
            _hasLastPropBillboardFwd = true;
            Quaternion yawRot = Quaternion.LookRotation(fwd, Vector3.up);
            int limit = Mathf.Min(_activePropCount, _propPool.Count);
            for (int i = 0; i < limit; i++)
            {
                PropBillboardEntry entry = _propPool[i];
                if (entry != null && !entry.IsFloorDecal && entry.Root != null && entry.Root.activeSelf)
                {
                    entry.Root.transform.rotation = yawRot;
                }
            }
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
                beaconGo.transform.localScale = new Vector3(0.12f, 0.55f, 1f);
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

            GameObject tentRoot = new GameObject("CampTentProp");
            tentRoot.transform.SetParent(_propsContainer, false);
            GameObject tentQuadGo = new GameObject("CampTentBillboard");
            tentQuadGo.transform.SetParent(tentRoot.transform, false);
            tentQuadGo.transform.localScale = new Vector3(1.85f, 1.85f, 1f);
            tentQuadGo.AddComponent<MeshFilter>().sharedMesh = _propBillboardQuadMesh;
            MeshRenderer tentMr = tentQuadGo.AddComponent<MeshRenderer>();
            tentMr.shadowCastingMode = ShadowCastingMode.On;
            tentMr.receiveShadows = true;
            PixelBillboard tentBb = tentQuadGo.AddComponent<PixelBillboard>();
            tentRoot.SetActive(false);

            _campfireEntry = new CampfireEntry
            {
                Root = root,
                BillboardRenderer = mr,
                Billboard = bb,
                PointLight = light,
                Flicker = flicker,
                TentRoot = tentRoot,
                TentRenderer = tentMr,
                TentBillboard = tentBb
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
            if (_npcContainer == null) _npcContainer = CreateChildContainer("TownNpcs");
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

            if (_dungeonFoliageScatter == null)
            {
                Transform t = CreateChildContainer("CartographyDungeonFoliage");
                _dungeonFoliageScatter = t.GetComponent<BywaterFoliageScatter>() ?? t.gameObject.AddComponent<BywaterFoliageScatter>();
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

            DestroyResource(_portalRuneMaterial);
            _portalRuneMaterial = null;

            DestroyResource(_portalRuneTexture);
            _portalRuneTexture = null;

            DestroyResource(_portalBeaconMaterial);
            _portalBeaconMaterial = null;

            DestroyResource(_portalBeaconTexture);
            _portalBeaconTexture = null;

            foreach (Material fadedMat in _fadedTransparentMaterialCache.Values)
            {
                DestroyResource(fadedMat);
            }
            _fadedTransparentMaterialCache.Clear();
        }

        private static bool IsFloorDecalPropKind(CartographyPropKind kind)
        {
            return kind == CartographyPropKind.DungeonIronGrate ||
                   kind == CartographyPropKind.DungeonSpikeTrap;
        }

        private static bool ShouldPropCastShadow(CartographyPropKind kind)
        {
            return !IsFloorDecalPropKind(kind);
        }

        private static bool IsAnimated4FramePropKind(CartographyPropKind kind)
        {
            return kind == CartographyPropKind.Campfire4f ||
                   kind == CartographyPropKind.DungeonWallSconce4f;
        }

        private static bool IsOccludingPropKind(CartographyPropKind kind, bool fadeCenterBuildingsInCombat)
        {
            switch (kind)
            {
                case CartographyPropKind.TreeOakA:
                case CartographyPropKind.TreeOakB:
                case CartographyPropKind.TreePineA:
                case CartographyPropKind.TreePineB:
                case CartographyPropKind.TreeWillowA:
                case CartographyPropKind.BywaterInn:
                case CartographyPropKind.Guildhall:
                case CartographyPropKind.Blacksmith:
                case CartographyPropKind.Apothecary:
                case CartographyPropKind.Temple:
                case CartographyPropKind.CottageB:
                case CartographyPropKind.CottageC:
                case CartographyPropKind.Watchtower:
                    return true;
                default:
                    return false;
            }
        }

        private Material GetOrCreatePortalRuneMaterial()
        {
            if (_portalRuneMaterial != null)
            {
                return _portalRuneMaterial;
            }

            if (_portalRuneTexture == null)
            {
                _portalRuneTexture = BuildPortalRuneTexture();
            }

            _portalRuneMaterial = CreateTransparentUnlitMaterial("PortalRuneFloor_Mat", _portalRuneTexture, Color.white);
            return _portalRuneMaterial;
        }

        private Material GetOrCreatePortalBeaconMaterial()
        {
            if (_portalBeaconMaterial != null)
            {
                return _portalBeaconMaterial;
            }

            if (_portalBeaconTexture == null)
            {
                _portalBeaconTexture = BuildPortalBeaconTexture();
            }

            _portalBeaconMaterial = CreateTransparentUnlitMaterial(
                "PortalBeacon_Mat",
                _portalBeaconTexture,
                new Color(0.22f, 0.74f, 0.97f, 0.40f));
            return _portalBeaconMaterial;
        }

        private static Material CreateTransparentUnlitMaterial(string matName, Texture2D tex, Color baseColor)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            Material mat = new Material(shader)
            {
                name = matName,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;

            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty(BaseMapId))
                {
                    mat.SetTexture(BaseMapId, tex);
                }
            }

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, baseColor);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, baseColor);
            return mat;
        }

        private static Texture2D BuildPortalRuneTexture()
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PortalRuneSigilTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color stoneRim = new Color(0.32f, 0.34f, 0.40f, 0.90f);
            Color stoneRimDark = new Color(0.16f, 0.17f, 0.22f, 0.92f);
            Color tealRune = new Color(0.22f, 0.74f, 0.97f, 1.0f) * 0.82f;
            tealRune.a = 0.88f;

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size - 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 0.48f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color col;
                    if (r > 0.45f)
                    {
                        float edgeAlpha = Mathf.Clamp01((0.48f - r) / 0.03f) * 0.75f;
                        col = new Color(stoneRimDark.r, stoneRimDark.g, stoneRimDark.b, edgeAlpha);
                    }
                    else if (r > 0.38f)
                    {
                        // Outer carved stone ring with subtle top-left highlight
                        float bevel = Mathf.Clamp01(0.5f - (nx - ny) * 0.8f);
                        col = Color.Lerp(stoneRimDark, stoneRim, bevel);
                    }
                    else if (r > 0.31f)
                    {
                        // Concentric arcane teal rune trim with 8 glyph notches
                        float angle = Mathf.Atan2(ny, nx);
                        float notchWave = Mathf.Abs(Mathf.Sin(angle * 4f));
                        bool isRunePixel = (r > 0.325f && r < 0.365f) && (notchWave > 0.35f || r < 0.345f);
                        col = isRunePixel
                            ? Color.Lerp(stoneRimDark, tealRune, 0.82f)
                            : new Color(0.18f, 0.20f, 0.25f, 0.88f);
                    }
                    else
                    {
                        // Inner carved stone stairs (4 descending treads from top to bottom)
                        float stepCoord = Mathf.Clamp01((ny + 0.31f) / 0.62f);
                        int stepIndex = Mathf.Clamp(Mathf.FloorToInt(stepCoord * 4f), 0, 3);
                        float stepFrac = (stepCoord * 4f) - stepIndex;
                        bool isRiser = stepFrac < 0.18f;

                        float depthT = stepIndex / 3f;
                        Color warmAmberTop = new Color(0.76f, 0.60f, 0.38f, 0.92f);
                        Color warmAmberDeep = new Color(0.30f, 0.23f, 0.16f, 0.90f);
                        col = Color.Lerp(warmAmberDeep, warmAmberTop, depthT);

                        if (isRiser)
                        {
                            col = new Color(col.r * 0.52f, col.g * 0.50f, col.b * 0.55f, 0.92f);
                        }

                        // Subtle inner teal arcane trace along side edges of the stairwell
                        if (Mathf.Abs(nx) > 0.21f && Mathf.Abs(nx) < 0.25f && !isRiser)
                        {
                            col = Color.Lerp(col, tealRune, 0.55f);
                        }

                        // Soft radial vignette toward center sigil
                        float centerGlow = Mathf.Clamp01(1f - r / 0.14f) * 0.35f;
                        col = Color.Lerp(col, tealRune, centerGlow);
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildPortalBeaconTexture()
        {
            const int w = 16;
            const int h = 32;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "PortalBeaconTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float vy = (y + 0.5f) / h;
                float vertFade = Mathf.Sin(Mathf.Clamp01(vy) * Mathf.PI) * (1f - vy * 0.45f);
                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / w) - 0.5f) * 2f;
                    float horizFade = Mathf.Clamp01(1f - nx * nx);
                    float alpha = Mathf.Clamp01(vertFade * horizFade * 0.85f);
                    pixels[y * w + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
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
