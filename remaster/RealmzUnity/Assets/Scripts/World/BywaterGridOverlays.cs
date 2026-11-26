using System;
using System.Collections.Generic;
using Realmz.Data;
using Realmz.Rendering;
using RealmzCore.Combat;
using RealmzCore.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Realmz.World
{
    public struct EnemyIntentArcData
    {
        public GridCoord From;
        public GridCoord To;
        public bool IsSpell;
    }

    public struct ShoveHazardVisualHint
    {
        public bool TriggersKegExplosion;
        public bool IgnitesOnBrazier;
        public bool CrushesWall;
        public bool PushesOffLedge;
        public bool DestroysCover;
        public SurfaceType EntersSurface;
        public string HazardBadgeText;
    }

    /// <summary>
    /// Pooled translucent tactical, exploration, elemental surface, shove preview, and enemy intent
    /// arc overlays aligned to canonical Cartography world coordinates and dynamic tile elevations.
    /// </summary>
    public sealed class BywaterGridOverlays : MonoBehaviour
    {
        private const int IntentArcSegments = 12;
        private const int SurfaceTextureSize = 64;
        private const int MaxCombatVfxPool = 8;
        private const int MaxHoverPathPips = 24;
        private const float DefaultVfxDuration = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

        private static readonly Color ReachableNormalColor = new Color(0.220f, 0.741f, 0.973f, 0.20f); // #38BDF8 @ 20%
        private static readonly Color ReachableAooColor = new Color(0.980f, 0.800f, 0.082f, 0.20f);    // #FACC15 @ 20%
        private static readonly Color ThreatenedColor = new Color(0.937f, 0.267f, 0.267f, 0.20f);      // #EF4444 @ 20%
        private static readonly Color HoverPathSafeColor = new Color(0.220f, 0.741f, 0.973f, 0.56f);
        private static readonly Color HoverPathGoldColor = new Color(0.980f, 0.800f, 0.082f, 0.62f);
        private static readonly Color HoverPathAooColor = new Color(0.98f, 0.25f, 0.18f, 0.66f);
        private static readonly Color ExploreReachableColor = new Color(0.22f, 0.86f, 0.64f, 0.58f);
        private static readonly Color ExploreBlockedColor = new Color(0.88f, 0.24f, 0.22f, 0.64f);
        private static readonly Color LosClearColor = new Color(0.18f, 0.85f, 0.48f, 0.72f);
        private static readonly Color LosPartialColor = new Color(0.96f, 0.72f, 0.16f, 0.74f);
        private static readonly Color LosBlockedColor = new Color(0.95f, 0.22f, 0.22f, 0.78f);
        private static readonly Color AoeBeneficialColor = new Color(0.22f, 0.74f, 0.97f, 0.92f);
        private static readonly Color AoeHostileColor = new Color(0.96f, 0.32f, 0.18f, 0.94f);
        private static readonly Color AoeBlockedColor = new Color(0.22f, 0.22f, 0.25f, 0.78f);
        private static readonly Color FlankGoldColor = new Color(0.98f, 0.80f, 0.15f, 0.60f);
        private static readonly Color AttackTargetColor = new Color(0.937f, 0.267f, 0.267f, 0.20f);    // #EF4444 @ 20%
        private static readonly Color HoveredAttackTargetColor = new Color(0.98f, 0.34f, 0.24f, 0.95f);
        private static readonly Color TacticalSelectColor = new Color(0.98f, 0.84f, 0.24f, 0.92f);
        private static readonly Color CombatBoundsColor = new Color(0.98f, 0.78f, 0.22f, 0.52f);
        private static readonly Color DeploymentZoneFillColor = new Color(0.16f, 0.85f, 0.96f, 0.28f);
        private static readonly Color DeploymentZoneBorderColor = new Color(0.16f, 0.85f, 0.96f, 0.90f);
        private static readonly Color DeploymentSelectedFillColor = new Color(0.28f, 0.94f, 1.00f, 0.56f);
        private static readonly Color DeploymentSelectedFrameColor = new Color(1.00f, 0.84f, 0.22f, 0.96f);

        private static readonly Color ShoveSafeEmerald = new Color(0.06f, 0.73f, 0.51f, 0.78f);
        private static readonly Color ShoveLedgeGold = new Color(0.98f, 0.80f, 0.08f, 0.82f);
        private static readonly Color ShoveCrushCrimson = new Color(0.96f, 0.36f, 0.12f, 0.84f);
        private static readonly Color ShoveInvalidRed = new Color(0.84f, 0.20f, 0.20f, 0.52f);

        private static readonly Color IntentMeleeColor = new Color(0.95f, 0.16f, 0.28f, 0.96f); // Threat crimson-magenta
        private static readonly Color IntentSpellColor = new Color(0.98f, 0.45f, 0.08f, 0.96f); // Threat amber-orange

        private sealed class OverlayLayer
        {
            public readonly Transform Container;
            public readonly List<MeshRenderer> Quads = new List<MeshRenderer>(32);
            public readonly List<Color> BaseColors = new List<Color>(32);
            public int ActiveCount;

            public OverlayLayer(Transform parent, string name)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                Container = go.transform;
            }
        }

        private sealed class WorldBadgeInstance
        {
            public GameObject Root;
            public Image Border;
            public Image Background;
            public TextMeshProUGUI Label;
        }

        private BywaterAssetCatalog _catalog;
        private Func<int, int, float> _elevationResolver;
        private Func<int, int, bool> _waterTileResolver;
        private float _waterSurfaceHeight = Realmz.Cartography.CartographyWorldData.TileMapWaterSurfaceElevation;
        private MaterialPropertyBlock _propBlock;
        private BywaterCombatVfxDirector _vfxDirector;
        private readonly Vector3[] _lightningTwoHopScratch = new Vector3[2];
        private readonly Dictionary<GridCoord, SurfaceType> _surfaceByCoord = new Dictionary<GridCoord, SurfaceType>(64);

        private OverlayLayer _surfaceLayer;
        private OverlayLayer _combatBoundsLayer;
        private OverlayLayer _combatReachableLayer;
        private OverlayLayer _combatThreatenedLayer;
        private OverlayLayer _attackTargetLayer;
        private OverlayLayer _hoveredAttackTargetLayer;
        private OverlayLayer _spellAoeLayer;
        private OverlayLayer _tacticalSelectLayer;
        private OverlayLayer _exploreHoverLayer;
        private OverlayLayer _combatHoverLayer;
        private OverlayLayer _losLayer;
        private OverlayLayer _flankLayer;
        private OverlayLayer _shoveLayer;
        private OverlayLayer _deploymentZoneLayer;

        private Transform _intentArcContainer;
        private readonly List<LineRenderer> _intentArcPool = new List<LineRenderer>(16);
        private readonly List<MeshRenderer> _intentArrowheadPool = new List<MeshRenderer>(16);
        private readonly List<MeshRenderer> _intentThreatRingPool = new List<MeshRenderer>(16);
        private readonly List<Vector3> _intentArrowheadTips = new List<Vector3>(16);
        private readonly List<Vector3> _intentArrowheadTangents = new List<Vector3>(16);
        private readonly List<Color> _intentThreatRingBaseColors = new List<Color>(16);
        private readonly Vector3[] _arcPointScratch = new Vector3[IntentArcSegments + 1];
        private int _activeIntentArcCount;

        private readonly Dictionary<SurfaceType, Material> _surfaceMaterials = new Dictionary<SurfaceType, Material>(8);
        private readonly Dictionary<SurfaceType, Texture2D> _surfaceTextures = new Dictionary<SurfaceType, Texture2D>(8);
        private readonly Dictionary<SurfaceType, Color> _surfaceBaseColors = new Dictionary<SurfaceType, Color>(8);
        private readonly Dictionary<SurfaceType, bool> _surfaceIsStrip = new Dictionary<SurfaceType, bool>(8);
        private readonly Dictionary<SurfaceType, Texture2D> _surfaceStripSources = new Dictionary<SurfaceType, Texture2D>(8);
        private readonly Dictionary<SurfaceType, Color32[]> _surfaceStripPixels = new Dictionary<SurfaceType, Color32[]>(8);
        private readonly Dictionary<int, Texture2D> _surfaceEdgeTextures = new Dictionary<int, Texture2D>(32);
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private const int SurfaceEdgeNorth = 1;
        private const int SurfaceEdgeEast = 2;
        private const int SurfaceEdgeSouth = 4;
        private const int SurfaceEdgeWest = 8;
        private const int SurfaceEdgeAll = 15;
        private readonly SurfaceType[] _animatedSurfaceTypes =
        {
            SurfaceType.Water,
            SurfaceType.Oil,
            SurfaceType.Fire,
            SurfaceType.Ice,
            SurfaceType.Poison,
            SurfaceType.ElectrifiedWater,
            SurfaceType.SteamCloud
        };

        private sealed class CombatVfxInstance
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public bool Active;
            public string VfxKey;
            public Vector3 StartPos;
            public Vector3 TargetPos;
            public bool IsProjectile;
            public float Elapsed;
            public float Duration;
            public float BaseScale;
        }

        private Transform _vfxContainer;
        private readonly List<CombatVfxInstance> _vfxPool = new List<CombatVfxInstance>(MaxCombatVfxPool);
        private readonly Dictionary<string, Material> _vfxMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private MaterialPropertyBlock _vfxPropBlock;
        private Texture2D _vfxFallbackTexture;
        private int _vfxRoundRobinCursor;

        private Transform _hoverPipContainer;
        private readonly List<WorldBadgeInstance> _hoverPipPool = new List<WorldBadgeInstance>(MaxHoverPathPips);

        private Transform _shoveExtrasContainer;
        private LineRenderer _shoveArrowLine;
        private MeshRenderer _shoveArrowhead;
        private WorldBadgeInstance _shoveHazardBadge;
        private Material _shoveLineMaterial;
        private readonly Vector3[] _shoveLinePoints = new Vector3[2];

        private Material _shoveChevronMaterial;
        private Texture2D _shoveChevronTexture;
        private Material _intentMeleeMaterial;
        private Material _intentSpellMaterial;
        private Texture2D _intentChevronTexture;
        private Material _intentArrowheadMaterial;
        private Texture2D _intentArrowheadTexture;
        private Material _intentThreatRingMaterial;
        private Texture2D _intentThreatRingTexture;
        private Material _bracketMaterial;
        private Texture2D _cornerBracketTexture;
        private Material _pathDotMaterial;
        private Texture2D _pathDotTexture;
        private Material _aoeFillMaterial;
        private Texture2D _outlineFillTexture;
        private Material _losBlockerXMaterial;
        private Texture2D _losBlockerXTexture;
        private Material _fallbackOverlayMaterial;
        private float _intentScrollOffset;
        private float _intentSpellScrollOffset;
        private float _gridOpacity = 1.0f;

        public int ActiveIntentArrowheadCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _intentArrowheadPool.Count; i++)
                {
                    if (_intentArrowheadPool[i] != null && _intentArrowheadPool[i].gameObject.activeSelf)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public void SetGridOpacity(float opacity)
        {
            _gridOpacity = Mathf.Clamp01(opacity);
            ApplyOpacityToLayer(_combatBoundsLayer);
            ApplyOpacityToLayer(_combatReachableLayer);
            ApplyOpacityToLayer(_combatThreatenedLayer);
            ApplyOpacityToLayer(_attackTargetLayer);
            ApplyOpacityToLayer(_exploreHoverLayer);
            ApplyOpacityToLayer(_deploymentZoneLayer);
        }

        public void Initialize(BywaterAssetCatalog catalog)
        {
            if (_catalog != catalog)
            {
                DisposeSurfaceCache();
                DisposeVfxMaterialCache();
            }
            _catalog = catalog;
            EnsureLayers();
        }

        public void SetElevationResolver(Func<int, int, float> resolver)
        {
            _elevationResolver = resolver;
        }

        public void SetWaterTileResolver(Func<int, int, bool> resolver)
        {
            _waterTileResolver = resolver;
        }

        public void SetWaterSurfaceHeight(float waterSurfaceHeight)
        {
            _waterSurfaceHeight = waterSurfaceHeight;
        }

        public float GetTileElevation(int x, int y)
        {
            return _elevationResolver != null ? _elevationResolver(x, y) : 0f;
        }

        private void Update()
        {
            float t = Application.isPlaying ? Time.unscaledTime : 0f;
            float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;

            if (_surfaceLayer != null && _surfaceLayer.ActiveCount > 0)
            {
                StepSurfaceAnimations(t);
            }

            StepCombatVfx(dt);

            if (_activeIntentArcCount > 0 && Application.isPlaying)
            {
                // Decreasing U offset shifts repeating +U chevrons forward along the line from From toward To.
                _intentScrollOffset = Mathf.Repeat(_intentScrollOffset - dt * 1.9f, 1f);
                _intentSpellScrollOffset = Mathf.Repeat(_intentSpellScrollOffset - dt * 2.35f, 1f);
                if (_intentMeleeMaterial != null)
                {
                    SetMaterialTextureOffset(_intentMeleeMaterial, new Vector2(_intentScrollOffset, 0f));
                }
                if (_intentSpellMaterial != null)
                {
                    SetMaterialTextureOffset(_intentSpellMaterial, new Vector2(_intentSpellScrollOffset, 0f));
                }

                RefreshIntentArrowheadOrientations();
                StepIntentThreatRingPulse(t);
            }
        }

        private void OnDestroy()
        {
            ClearAll();
            ClearCombatVfx();

            DisposeSurfaceCache();
            DisposeVfxMaterialCache();

            DestroySafe(_vfxFallbackTexture);
            _vfxFallbackTexture = null;

            DestroySafe(_shoveChevronMaterial);
            _shoveChevronMaterial = null;
            DestroySafe(_shoveChevronTexture);
            _shoveChevronTexture = null;
            DestroySafe(_shoveLineMaterial);
            _shoveLineMaterial = null;

            DestroySafe(_intentMeleeMaterial);
            _intentMeleeMaterial = null;
            DestroySafe(_intentSpellMaterial);
            _intentSpellMaterial = null;
            DestroySafe(_intentChevronTexture);
            _intentChevronTexture = null;
            DestroySafe(_intentArrowheadMaterial);
            _intentArrowheadMaterial = null;
            DestroySafe(_intentArrowheadTexture);
            _intentArrowheadTexture = null;
            DestroySafe(_intentThreatRingMaterial);
            _intentThreatRingMaterial = null;
            DestroySafe(_intentThreatRingTexture);
            _intentThreatRingTexture = null;

            DestroySafe(_bracketMaterial);
            _bracketMaterial = null;
            DestroySafe(_cornerBracketTexture);
            _cornerBracketTexture = null;
            DestroySafe(_pathDotMaterial);
            _pathDotMaterial = null;
            DestroySafe(_pathDotTexture);
            _pathDotTexture = null;

            DestroySafe(_aoeFillMaterial);
            _aoeFillMaterial = null;
            DestroySafe(_outlineFillTexture);
            _outlineFillTexture = null;

            DestroySafe(_fallbackOverlayMaterial);
            _fallbackOverlayMaterial = null;
        }

        private void DisposeSurfaceCache()
        {
            foreach (KeyValuePair<SurfaceType, Material> kv in _surfaceMaterials)
            {
                DestroySafe(kv.Value);
            }
            _surfaceMaterials.Clear();
            _surfaceBaseColors.Clear();
            _surfaceIsStrip.Clear();

            foreach (KeyValuePair<SurfaceType, Texture2D> kv in _surfaceTextures)
            {
                DestroySafe(kv.Value);
            }
            _surfaceTextures.Clear();

            foreach (KeyValuePair<int, Texture2D> kv in _surfaceEdgeTextures)
            {
                DestroySafe(kv.Value);
            }
            _surfaceEdgeTextures.Clear();
            _surfaceStripSources.Clear();
            _surfaceStripPixels.Clear();
        }

        private void DisposeVfxMaterialCache()
        {
            foreach (KeyValuePair<string, Material> kv in _vfxMaterials)
            {
                DestroySafe(kv.Value);
            }
            _vfxMaterials.Clear();
        }

        public void ClearAll()
        {
            EnsureLayers();
            _surfaceByCoord.Clear();
            HideLayer(_surfaceLayer);
            HideLayer(_combatBoundsLayer);
            HideLayer(_combatReachableLayer);
            HideLayer(_combatThreatenedLayer);
            HideLayer(_attackTargetLayer);
            HideLayer(_hoveredAttackTargetLayer);
            HideLayer(_spellAoeLayer);
            HideLayer(_tacticalSelectLayer);
            HideLayer(_exploreHoverLayer);
            HideLayer(_combatHoverLayer);
            HideAllHoverPips();
            HideLayer(_losLayer);
            HideLayer(_flankLayer);
            HideLayer(_shoveLayer);
            HideShoveExtras();
            HideLayer(_deploymentZoneLayer);
            ClearEnemyIntentArcs();
        }

        public void SetSurfaceCells(IReadOnlyList<CombatGridCell> cells)
        {
            EnsureLayers();
            _surfaceLayer.ActiveCount = 0;
            _surfaceByCoord.Clear();

            if (cells != null)
            {
                float t = Application.isPlaying ? Time.unscaledTime : 0f;

                // Register every surface first so edge masks can see neighbors later in the list.
                for (int i = 0; i < cells.Count; i++)
                {
                    CombatGridCell cell = cells[i];
                    if (cell != null && cell.Surface != SurfaceType.None)
                    {
                        _surfaceByCoord[cell.Coord] = cell.Surface;
                    }
                }

                for (int i = 0; i < cells.Count; i++)
                {
                    CombatGridCell cell = cells[i];
                    if (cell == null || cell.Surface == SurfaceType.None)
                    {
                        continue;
                    }

                    float elev = GetTileElevation(cell.X, cell.Y);
                    bool isRealRiverTile = (_waterTileResolver != null && _waterTileResolver(cell.X, cell.Y)) ||
                                           elev <= _waterSurfaceHeight - 0.04f;
                    if (cell.Surface == SurfaceType.Water && isRealRiverTile)
                    {
                        continue;
                    }

                    Material mat = GetOrCreateSurfaceMaterial(cell.Surface);
                    Color baseCol = _surfaceBaseColors.TryGetValue(cell.Surface, out Color cachedCol)
                        ? cachedCol
                        : GetSurfaceBaseColor(cell.Surface);
                    float y = (cell.Surface == SurfaceType.ElectrifiedWater ||
                               cell.Surface == SurfaceType.Ice ||
                               cell.Surface == SurfaceType.SteamCloud ||
                               cell.Surface == SurfaceType.Fire ||
                               cell.Surface == SurfaceType.Oil ||
                               cell.Surface == SurfaceType.Water)
                        ? Mathf.Max(elev, _waterSurfaceHeight) + 0.025f
                        : Mathf.Max(elev + 0.085f, _waterSurfaceHeight + 0.045f);
                    Vector3 pos = new Vector3(cell.X + 0.5f, y, -(cell.Y + 0.5f));
                    Texture2D edgeTex = null;
                    int neighborBits = 0;
                    if (UsesSurfaceEdgeMask(cell.Surface) &&
                        _surfaceIsStrip.TryGetValue(cell.Surface, out bool isStripSurface) && isStripSurface)
                    {
                        neighborBits = ComputeSurfaceNeighborBits(cell.X, cell.Y, cell.Surface);
                        edgeTex = neighborBits == SurfaceEdgeAll ? null : GetOrCreateSurfaceEdgeTexture(cell.Surface, neighborBits);
                    }

                    bool joinsNeighbors = edgeTex != null || neighborBits == SurfaceEdgeAll;
                    float quadSize = joinsNeighbors ? 1.002f : 0.96f;
                    PlaceCustomQuad(_surfaceLayer, pos, quadSize, quadSize, 0f, mat, baseCol);
                    if (edgeTex != null)
                    {
                        MeshRenderer placed = _surfaceLayer.Quads[_surfaceLayer.ActiveCount - 1];
                        _propBlock.Clear();
                        _propBlock.SetColor(BaseColorId, baseCol);
                        _propBlock.SetColor(ColorId, baseCol);
                        _propBlock.SetTexture(BaseMapId, edgeTex);
                        _propBlock.SetTexture(MainTexId, edgeTex);
                        placed.SetPropertyBlock(_propBlock);
                    }
                }

                if (_surfaceLayer.ActiveCount > 0)
                {
                    StepSurfaceAnimations(t);
                }
            }

            TrimLayer(_surfaceLayer);
        }

        public void ClearSurfaceCells()
        {
            EnsureLayers();
            _surfaceByCoord.Clear();
            HideLayer(_surfaceLayer);
        }

        public void PlayCombatVfx(string vfxKey, Vector3 startWorldPos, Vector3 targetWorldPos, bool isProjectile = false)
        {
            if (string.IsNullOrWhiteSpace(vfxKey))
            {
                return;
            }

            EnsureLayers();
            CombatVfxInstance inst = AcquireVfxInstance();
            inst.Active = true;
            inst.VfxKey = vfxKey;
            inst.StartPos = startWorldPos;
            inst.TargetPos = targetWorldPos;
            inst.IsProjectile = isProjectile || string.Equals(vfxKey, "arrow_bolt", StringComparison.OrdinalIgnoreCase);
            inst.Elapsed = 0f;
            inst.Duration = DefaultVfxDuration;
            inst.BaseScale = ResolveVfxBaseScale(vfxKey);
            inst.Renderer.sharedMaterial = GetOrCreateVfxMaterial(vfxKey);

            UpdateCombatVfxInstanceVisual(inst);
            inst.Root.SetActive(true);

            RouteVfxToDirector(vfxKey, startWorldPos, targetWorldPos, inst.IsProjectile);
        }

        public void ClearCombatVfx()
        {
            for (int i = 0; i < _vfxPool.Count; i++)
            {
                CombatVfxInstance inst = _vfxPool[i];
                if (inst == null)
                {
                    continue;
                }

                inst.Active = false;
                if (inst.Root != null)
                {
                    inst.Root.SetActive(false);
                }
            }

            if (_vfxDirector == null)
            {
                _vfxDirector = GetComponent<BywaterCombatVfxDirector>();
            }
            if (_vfxDirector != null)
            {
                _vfxDirector.ClearAll();
            }
        }

        public void ShowShovePreview(GridCoord actor, GridCoord target, TacticalShovePreview preview)
        {
            ShowShovePreview(actor, target, preview, default(ShoveHazardVisualHint));
        }

        public void ShowShovePreview(
            GridCoord actor,
            GridCoord target,
            TacticalShovePreview preview,
            ShoveHazardVisualHint hazardHint)
        {
            EnsureLayers();
            _shoveLayer.ActiveCount = 0;

            if (preview == null)
            {
                HideShoveExtras();
                TrimLayer(_shoveLayer);
                return;
            }

            bool hasKeg = hazardHint.TriggersKegExplosion;
            bool hasBrazier = hazardHint.IgnitesOnBrazier;
            bool hasCrush = hazardHint.CrushesWall || preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId);
            bool hasLedge = hazardHint.PushesOffLedge || preview.PushesOffLedge;
            SurfaceType enteredSurface = hazardHint.EntersSurface != SurfaceType.None
                ? hazardHint.EntersSurface
                : preview.DestinationSurface;

            Color previewColor;
            if (!preview.CanExecute)
            {
                previewColor = ShoveInvalidRed;
            }
            else if (hasKeg || hasBrazier || hasCrush || enteredSurface == SurfaceType.Fire || enteredSurface == SurfaceType.ElectrifiedWater)
            {
                previewColor = ShoveCrushCrimson;
            }
            else if (hasLedge || hazardHint.DestroysCover)
            {
                previewColor = ShoveLedgeGold;
            }
            else
            {
                previewColor = ShoveSafeEmerald;
            }

            const float shoveLayerOffset = 0.014f;
            GridCoord dest = preview.Destination;

            // Highlight destination tile (or target tile if invalid)
            GridCoord highlightTile = preview.CanExecute ? dest : target;
            float destElev = GetTileElevation(highlightTile.X, highlightTile.Y);
            float destY = destElev + 0.025f + shoveLayerOffset;
            PlaceQuad(
                _shoveLayer,
                highlightTile.X + 0.5f,
                destY,
                -(highlightTile.Y + 0.5f),
                0.90f,
                0.90f,
                previewColor);

            // Push direction chevron quad placed between target and destination
            GridCoord dir = preview.PushDirection;
            if (dir.X != 0 || dir.Y != 0)
            {
                float targetCenterX = target.X + 0.5f;
                float targetCenterZ = -(target.Y + 0.5f);
                float worldDirX = dir.X;
                float worldDirZ = -dir.Y;
                Vector2 dirNorm = new Vector2(worldDirX, worldDirZ).normalized;
                float yawDeg = Mathf.Atan2(dirNorm.x, dirNorm.y) * Mathf.Rad2Deg;

                float chevronX = targetCenterX + dirNorm.x * 0.50f;
                float chevronZ = targetCenterZ + dirNorm.y * 0.50f;
                float chevronY = Mathf.Max(
                    GetTileElevation(target.X, target.Y),
                    GetTileElevation(dest.X, dest.Y)) + 0.025f + shoveLayerOffset + 0.002f;

                Material chevronMat = GetOrCreateShoveChevronMaterial();
                PlaceCustomQuad(
                    _shoveLayer,
                    new Vector3(chevronX, chevronY, chevronZ),
                    0.72f,
                    0.72f,
                    yawDeg,
                    chevronMat,
                    previewColor);
            }

            UpdateShoveTrajectoryAndBadge(target, highlightTile, dir, preview.CanExecute, previewColor, hazardHint, preview);
            TrimLayer(_shoveLayer);
        }

        public void ClearShovePreview()
        {
            EnsureLayers();
            HideLayer(_shoveLayer);
            HideShoveExtras();
        }

        public void ShowEnemyIntentArcs(IReadOnlyList<EnemyIntentArcData> arcs)
        {
            EnsureLayers();
            _activeIntentArcCount = 0;
            _intentArrowheadTips.Clear();
            _intentArrowheadTangents.Clear();
            _intentThreatRingBaseColors.Clear();

            if (arcs != null)
            {
                _propBlock ??= new MaterialPropertyBlock();

                for (int i = 0; i < arcs.Count; i++)
                {
                    EnemyIntentArcData arc = arcs[i];
                    if (arc.From == arc.To)
                    {
                        continue;
                    }

                    int arcIndex = _activeIntentArcCount++;
                    LineRenderer lr = GetOrCreateIntentArc(arcIndex);
                    float startY = GetTileElevation(arc.From.X, arc.From.Y) + 0.65f;
                    float endY = GetTileElevation(arc.To.X, arc.To.Y) + 0.65f;

                    Vector3 p0 = new Vector3(arc.From.X + 0.5f, startY, -(arc.From.Y + 0.5f));
                    Vector3 p2 = new Vector3(arc.To.X + 0.5f, endY, -(arc.To.Y + 0.5f));

                    float peakY = Mathf.Max(startY, endY) + 1.55f;
                    float controlY = 2f * peakY - 0.5f * (startY + endY);
                    Vector3 p1 = new Vector3((p0.x + p2.x) * 0.5f, controlY, (p0.z + p2.z) * 0.5f);

                    for (int s = 0; s <= IntentArcSegments; s++)
                    {
                        float u = s / (float)IntentArcSegments;
                        float oneMinusU = 1f - u;
                        _arcPointScratch[s] =
                            (oneMinusU * oneMinusU) * p0 +
                            (2f * oneMinusU * u) * p1 +
                            (u * u) * p2;
                    }

                    Vector3 tangent = p2 - _arcPointScratch[IntentArcSegments - 1];
                    if (tangent.sqrMagnitude < 0.0001f)
                    {
                        tangent = p2 - p0;
                    }
                    tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;

                    // End the line shaft inside the arrowhead base so the sharp tip reaches p2 cleanly.
                    _arcPointScratch[IntentArcSegments] = p2 - tangent * 0.14f;

                    Color col = arc.IsSpell ? IntentSpellColor : IntentMeleeColor;
                    lr.sharedMaterial = GetOrCreateIntentArcMaterial(arc.IsSpell);
                    lr.startColor = col;
                    lr.endColor = col;
                    lr.startWidth = arc.IsSpell ? 0.165f : 0.150f;
                    lr.endWidth = arc.IsSpell ? 0.110f : 0.100f;
                    lr.textureMode = LineTextureMode.Tile;

                    float approxDist = Vector3.Distance(p0, p2);
                    lr.textureScale = new Vector2(Mathf.Max(1.8f, approxDist * 1.5f), 1f);
                    lr.positionCount = IntentArcSegments + 1;
                    lr.SetPositions(_arcPointScratch);
                    lr.gameObject.SetActive(true);

                    Vector3 arrowPos = p2 - tangent * 0.18f;
                    _intentArrowheadTips.Add(arrowPos);
                    _intentArrowheadTangents.Add(tangent);

                    MeshRenderer arrowMr = GetOrCreateIntentArrowhead(arcIndex);
                    OrientIntentArrowhead(arrowMr.transform, arrowPos, tangent);
                    arrowMr.transform.localScale = arc.IsSpell
                        ? new Vector3(0.44f, 0.50f, 1f)
                        : new Vector3(0.40f, 0.46f, 1f);
                    arrowMr.sharedMaterial = GetOrCreateIntentArrowheadMaterial();
                    _propBlock.Clear();
                    _propBlock.SetColor(BaseColorId, col);
                    _propBlock.SetColor(ColorId, col);
                    arrowMr.SetPropertyBlock(_propBlock);
                    arrowMr.gameObject.SetActive(true);

                    MeshRenderer ringMr = GetOrCreateIntentThreatRing(arcIndex);
                    float ringY = GetTileElevation(arc.To.X, arc.To.Y) + 0.042f;
                    ringMr.transform.position = new Vector3(arc.To.X + 0.5f, ringY, -(arc.To.Y + 0.5f));
                    ringMr.transform.rotation = Quaternion.Euler(90f, 45f, 0f);
                    ringMr.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                    ringMr.sharedMaterial = GetOrCreateIntentThreatRingMaterial();
                    _intentThreatRingBaseColors.Add(col);
                    _propBlock.Clear();
                    _propBlock.SetColor(BaseColorId, col);
                    _propBlock.SetColor(ColorId, col);
                    ringMr.SetPropertyBlock(_propBlock);
                    ringMr.gameObject.SetActive(true);
                }
            }

            DisableUnusedIntentArcItems();
        }

        public void ClearEnemyIntentArcs()
        {
            _activeIntentArcCount = 0;
            _intentArrowheadTips.Clear();
            _intentArrowheadTangents.Clear();
            _intentThreatRingBaseColors.Clear();
            DisableUnusedIntentArcItems();
        }

        private void DisableUnusedIntentArcItems()
        {
            for (int i = _activeIntentArcCount; i < _intentArcPool.Count; i++)
            {
                if (_intentArcPool[i] != null)
                {
                    _intentArcPool[i].gameObject.SetActive(false);
                }
            }

            for (int i = _activeIntentArcCount; i < _intentArrowheadPool.Count; i++)
            {
                if (_intentArrowheadPool[i] != null)
                {
                    _intentArrowheadPool[i].gameObject.SetActive(false);
                }
            }

            for (int i = _activeIntentArcCount; i < _intentThreatRingPool.Count; i++)
            {
                if (_intentThreatRingPool[i] != null)
                {
                    _intentThreatRingPool[i].gameObject.SetActive(false);
                }
            }
        }

        public void SetExplorationHoverPath(IReadOnlyList<GridCoord> path, GridCoord hoverCoord, bool reachable)
        {
            EnsureLayers();
            _exploreHoverLayer.ActiveCount = 0;

            Color baseColor = reachable ? ExploreReachableColor : ExploreBlockedColor;
            Color fadedColor = ScaleAlpha(baseColor, _gridOpacity);

            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    GridCoord step = path[i];
                    if (step == hoverCoord)
                    {
                        continue;
                    }

                    float y = GetTileElevation(step.X, step.Y) + 0.025f + 0.008f;
                    PlaceCustomQuadWithBaseColor(
                        _exploreHoverLayer,
                        new Vector3(step.X + 0.5f, y, -(step.Y + 0.5f)),
                        0.24f,
                        0.24f,
                        0f,
                        GetOrCreatePathDotMaterial(),
                        baseColor,
                        fadedColor);
                }
            }

            if (hoverCoord.X >= 0 && hoverCoord.Y >= 0)
            {
                float hoverY = GetTileElevation(hoverCoord.X, hoverCoord.Y) + 0.025f + 0.009f;
                Material bracketMat = GetOrCreateBracketMaterial();
                PlaceCustomQuadWithBaseColor(
                    _exploreHoverLayer,
                    new Vector3(hoverCoord.X + 0.5f, hoverY, -(hoverCoord.Y + 0.5f)),
                    0.92f,
                    0.92f,
                    0f,
                    bracketMat,
                    baseColor,
                    fadedColor);
            }

            TrimLayer(_exploreHoverLayer);
        }

        public void SetCombatReachable(IReadOnlyList<ReachableTileInfo> tiles)
        {
            EnsureLayers();
            _combatReachableLayer.ActiveCount = 0;

            if (tiles != null)
            {
                Material bracketMat = GetOrCreateBracketMaterial();
                for (int i = 0; i < tiles.Count; i++)
                {
                    ReachableTileInfo tile = tiles[i];
                    Color baseColor = tile.ProvokesAoO ? ReachableAooColor : ReachableNormalColor;
                    Color color = ScaleAlpha(baseColor, _gridOpacity);
                    float y = GetTileElevation(tile.Coord.X, tile.Coord.Y) + 0.025f;
                    PlaceCustomQuadWithBaseColor(
                        _combatReachableLayer,
                        new Vector3(tile.Coord.X + 0.5f, y, -(tile.Coord.Y + 0.5f)),
                        0.94f,
                        0.94f,
                        0f,
                        bracketMat,
                        baseColor,
                        color);
                }
            }

            TrimLayer(_combatReachableLayer);
        }

        public void SetCombatThreatened(IReadOnlyList<GridCoord> tiles)
        {
            EnsureLayers();
            _combatThreatenedLayer.ActiveCount = 0;

            if (tiles != null)
            {
                Material bracketMat = GetOrCreateBracketMaterial();
                Color color = ScaleAlpha(ThreatenedColor, _gridOpacity);
                for (int i = 0; i < tiles.Count; i++)
                {
                    GridCoord tile = tiles[i];
                    float y = GetTileElevation(tile.X, tile.Y) + 0.025f + 0.002f;
                    PlaceCustomQuadWithBaseColor(
                        _combatThreatenedLayer,
                        new Vector3(tile.X + 0.5f, y, -(tile.Y + 0.5f)),
                        0.90f,
                        0.90f,
                        0f,
                        bracketMat,
                        ThreatenedColor,
                        color);
                }
            }

            TrimLayer(_combatThreatenedLayer);
        }

        public void SetAttackTargets(IReadOnlyList<GridCoord> tiles)
        {
            EnsureLayers();
            _attackTargetLayer.ActiveCount = 0;

            if (tiles != null)
            {
                Material bracketMat = GetOrCreateBracketMaterial();
                Color color = ScaleAlpha(AttackTargetColor, _gridOpacity);
                for (int i = 0; i < tiles.Count; i++)
                {
                    GridCoord tile = tiles[i];
                    float y = GetTileElevation(tile.X, tile.Y) + 0.025f + 0.004f;
                    PlaceCustomQuadWithBaseColor(
                        _attackTargetLayer,
                        new Vector3(tile.X + 0.5f, y, -(tile.Y + 0.5f)),
                        0.92f,
                        0.92f,
                        0f,
                        bracketMat,
                        AttackTargetColor,
                        color);
                }
            }

            TrimLayer(_attackTargetLayer);
        }

        public void SetHoveredAttackTarget(GridCoord? coord)
        {
            EnsureLayers();
            _hoveredAttackTargetLayer.ActiveCount = 0;

            if (coord.HasValue)
            {
                GridCoord c = coord.Value;
                float y = GetTileElevation(c.X, c.Y) + 0.025f + 0.005f;
                PlaceCustomQuad(
                    _hoveredAttackTargetLayer,
                    new Vector3(c.X + 0.5f, y, -(c.Y + 0.5f)),
                    0.94f,
                    0.94f,
                    0f,
                    GetOrCreateAoeFillMaterial(),
                    HoveredAttackTargetColor);
            }

            TrimLayer(_hoveredAttackTargetLayer);
        }

        public void SetHoveredCombatTarget(GridCoord? coord)
        {
            SetHoveredAttackTarget(coord);
        }

        public void SetSelectedTile(GridCoord? coord, Color? colorOverride = null)
        {
            EnsureLayers();
            _tacticalSelectLayer.ActiveCount = 0;

            if (coord.HasValue)
            {
                GridCoord c = coord.Value;
                float y = GetTileElevation(c.X, c.Y) + 0.025f + 0.015f;
                PlaceCustomQuad(
                    _tacticalSelectLayer,
                    new Vector3(c.X + 0.5f, y, -(c.Y + 0.5f)),
                    0.94f,
                    0.94f,
                    0f,
                    GetOrCreateAoeFillMaterial(),
                    colorOverride ?? TacticalSelectColor);
            }

            TrimLayer(_tacticalSelectLayer);
        }

        public void SetCombatBounds(int originX, int originY, int width, int height, bool active)
        {
            EnsureLayers();
            _combatBoundsLayer.ActiveCount = 0;

            if (active && width > 0 && height > 0)
            {
                Color color = ScaleAlpha(CombatBoundsColor, _gridOpacity);
                for (int x = originX; x < originX + width; x++)
                {
                    int topY = originY;
                    int botY = originY + height - 1;
                    PlaceQuadWithBaseColor(_combatBoundsLayer, x + 0.5f, GetTileElevation(x, topY) + 0.025f + 0.001f, -(topY + 0.06f), 1.0f, 0.10f, CombatBoundsColor, color);
                    PlaceQuadWithBaseColor(_combatBoundsLayer, x + 0.5f, GetTileElevation(x, botY) + 0.025f + 0.001f, -(botY + 0.94f), 1.0f, 0.10f, CombatBoundsColor, color);
                }

                for (int y = originY; y < originY + height; y++)
                {
                    int leftX = originX;
                    int rightX = originX + width - 1;
                    PlaceQuadWithBaseColor(_combatBoundsLayer, leftX + 0.06f, GetTileElevation(leftX, y) + 0.025f + 0.001f, -(y + 0.5f), 0.10f, 1.0f, CombatBoundsColor, color);
                    PlaceQuadWithBaseColor(_combatBoundsLayer, rightX + 0.94f, GetTileElevation(rightX, y) + 0.025f + 0.001f, -(y + 0.5f), 0.10f, 1.0f, CombatBoundsColor, color);
                }
            }

            TrimLayer(_combatBoundsLayer);
        }

        public void SetDeploymentZone(IReadOnlyList<GridCoord> worldCoords, GridCoord? selectedHeroWorld = null)
        {
            EnsureLayers();
            _deploymentZoneLayer.ActiveCount = 0;

            if (worldCoords != null && worldCoords.Count > 0)
            {
                Material bracketMat = GetOrCreateBracketMaterial();
                Material outlineMat = GetOrCreateAoeFillMaterial();
                Color fillDisplay = ScaleAlpha(DeploymentZoneFillColor, _gridOpacity);
                Color borderDisplay = ScaleAlpha(DeploymentZoneBorderColor, _gridOpacity);
                bool selectedInZone = false;

                for (int i = 0; i < worldCoords.Count; i++)
                {
                    GridCoord tile = worldCoords[i];
                    float baseY = GetTileElevation(tile.X, tile.Y) + 0.025f;

                    PlaceQuadWithBaseColor(
                        _deploymentZoneLayer,
                        tile.X + 0.5f,
                        baseY + 0.003f,
                        -(tile.Y + 0.5f),
                        0.92f,
                        0.92f,
                        DeploymentZoneFillColor,
                        fillDisplay);

                    PlaceCustomQuadWithBaseColor(
                        _deploymentZoneLayer,
                        new Vector3(tile.X + 0.5f, baseY + 0.0045f, -(tile.Y + 0.5f)),
                        0.94f,
                        0.94f,
                        0f,
                        bracketMat,
                        DeploymentZoneBorderColor,
                        borderDisplay);

                    if (selectedHeroWorld.HasValue && tile == selectedHeroWorld.Value)
                    {
                        selectedInZone = true;
                    }
                }

                if (selectedInZone)
                {
                    GridCoord sel = selectedHeroWorld.Value;
                    float selBaseY = GetTileElevation(sel.X, sel.Y) + 0.025f;
                    Color selFillDisplay = ScaleAlpha(DeploymentSelectedFillColor, _gridOpacity);
                    Color selFrameDisplay = ScaleAlpha(DeploymentSelectedFrameColor, _gridOpacity);

                    PlaceCustomQuadWithBaseColor(
                        _deploymentZoneLayer,
                        new Vector3(sel.X + 0.5f, selBaseY + 0.012f, -(sel.Y + 0.5f)),
                        0.94f,
                        0.94f,
                        0f,
                        outlineMat,
                        DeploymentSelectedFillColor,
                        selFillDisplay);

                    PlaceTileBorderWithOpacity(
                        _deploymentZoneLayer,
                        sel,
                        0.015f,
                        DeploymentSelectedFrameColor,
                        selFrameDisplay);
                }
            }

            TrimLayer(_deploymentZoneLayer);
        }

        public void ClearDeploymentZone()
        {
            EnsureLayers();
            HideLayer(_deploymentZoneLayer);
        }

        public void SetCombatHoverPath(IReadOnlyList<GridCoord> path, bool provokesAoO)
        {
            SetCombatHoverPath(path, provokesAoO, null);
        }

        public void SetCombatHoverPath(IReadOnlyList<GridCoord> path, bool provokesAoO, IReadOnlyList<int> stepApCosts)
        {
            EnsureLayers();
            _combatHoverLayer.ActiveCount = 0;
            int activePips = 0;

            if (path != null)
            {
                Material dotMat = GetOrCreatePathDotMaterial();
                Material bracketMat = GetOrCreateBracketMaterial();
                TMPro.TMP_FontAsset font = _catalog != null ? _catalog.uiFont : null;

                for (int i = 0; i < path.Count; i++)
                {
                    GridCoord step = path[i];
                    float elev = GetTileElevation(step.X, step.Y);
                    float y = elev + 0.025f + 0.010f;

                    int stepCost = (stepApCosts != null && i < stepApCosts.Count)
                        ? Mathf.Max(0, stepApCosts[i])
                        : InferStepApCost(path, i);

                    bool isDestination = i == path.Count - 1;
                    bool highCostOrAoo = stepCost >= 2 || (provokesAoO && isDestination);
                    Color dotColor = provokesAoO
                        ? HoverPathAooColor
                        : (highCostOrAoo ? HoverPathGoldColor : HoverPathSafeColor);
                    float dotSize = isDestination ? 0.30f : 0.22f;

                    PlaceCustomQuadWithBaseColor(
                        _combatHoverLayer,
                        new Vector3(step.X + 0.5f, y, -(step.Y + 0.5f)),
                        dotSize,
                        dotSize,
                        0f,
                        dotMat,
                        dotColor,
                        dotColor);

                    if (isDestination)
                    {
                        Color bracketColor = ScaleAlpha(dotColor, 0.65f);
                        PlaceCustomQuadWithBaseColor(
                            _combatHoverLayer,
                            new Vector3(step.X + 0.5f, y + 0.001f, -(step.Y + 0.5f)),
                            0.92f,
                            0.92f,
                            0f,
                            bracketMat,
                            bracketColor,
                            bracketColor);
                    }

                    if (stepCost > 0 && activePips < MaxHoverPathPips)
                    {
                        WorldBadgeInstance pip = GetOrCreateHoverPip(activePips++);
                        pip.Root.transform.position = new Vector3(step.X + 0.5f, elev + 0.34f, -(step.Y + 0.5f));
                        if (font != null && pip.Label.font != font)
                        {
                            pip.Label.font = font;
                        }

                        pip.Label.text = stepCost == 1 ? "1 AP" : (stepCost == 2 ? "2 AP" : stepCost + " AP");
                        Color accent = highCostOrAoo
                            ? new Color(0.980f, 0.800f, 0.082f, 0.96f)
                            : new Color(0.220f, 0.741f, 0.973f, 0.96f);
                        pip.Border.color = accent;
                        pip.Label.color = highCostOrAoo
                            ? new Color(1.00f, 0.90f, 0.45f, 1f)
                            : new Color(0.84f, 0.96f, 1.00f, 1f);
                        pip.Root.SetActive(true);
                    }
                }
            }

            for (int i = activePips; i < _hoverPipPool.Count; i++)
            {
                if (_hoverPipPool[i]?.Root != null)
                {
                    _hoverPipPool[i].Root.SetActive(false);
                }
            }

            TrimLayer(_combatHoverLayer);
        }

        public void SetLosPreview(LosResult los, GridCoord from, GridCoord to, bool hasDisadvantagePenalty = false)
        {
            EnsureLayers();
            _losLayer.ActiveCount = 0;

            if (los != null)
            {
                bool blocked = los.Kind == LosKind.Blocked;
                bool penalized = !blocked && (los.Kind == LosKind.PartialCover || hasDisadvantagePenalty);
                Color rayColor = blocked
                    ? LosBlockedColor
                    : (penalized ? LosPartialColor : LosClearColor);

                GridCoord endCoord = blocked && los.BlockedBy.HasValue ? los.BlockedBy.Value : to;
                Vector2 start2 = new Vector2(from.X + 0.5f, -(from.Y + 0.5f));
                Vector2 end2 = new Vector2(endCoord.X + 0.5f, -(endCoord.Y + 0.5f));
                float segLen = Vector2.Distance(start2, end2);

                Material dotMat = GetOrCreatePathDotMaterial();
                if (segLen > 0.2f)
                {
                    const float dotSpacing = 0.42f;
                    int dotCount = Mathf.Clamp(Mathf.FloorToInt(segLen / dotSpacing), 2, 42);
                    for (int i = 1; i <= dotCount; i++)
                    {
                        float u = i / (float)dotCount;
                        float px = Mathf.Lerp(start2.x, end2.x, u);
                        float pz = Mathf.Lerp(start2.y, end2.y, u);
                        int gx = Mathf.FloorToInt(px);
                        int gy = Mathf.FloorToInt(-pz);
                        float py = GetTileElevation(gx, gy) + 0.025f + 0.015f;
                        PlaceCustomQuad(
                            _losLayer,
                            new Vector3(px, py, pz),
                            0.24f,
                            0.24f,
                            0f,
                            dotMat,
                            rayColor);
                    }
                }

                if (los.BlockedBy.HasValue)
                {
                    GridCoord blocker = los.BlockedBy.Value;
                    float blockerY = GetTileElevation(blocker.X, blocker.Y) + 0.025f + 0.017f;
                    Vector3 blockerPos = new Vector3(blocker.X + 0.5f, blockerY, -(blocker.Y + 0.5f));
                    PlaceCustomQuad(
                        _losLayer,
                        blockerPos,
                        0.82f,
                        0.82f,
                        0f,
                        GetOrCreateLosBlockerXMaterial(),
                        LosBlockedColor);
                }
            }

            TrimLayer(_losLayer);
        }

        public void SetSpellAoeFootprint(
            SpellAoeFootprint footprint,
            ICollection<GridCoord> allyCoords,
            ICollection<GridCoord> enemyCoords,
            bool isBeneficial)
        {
            EnsureLayers();
            _spellAoeLayer.ActiveCount = 0;

            if (footprint != null)
            {
                Material aoeMat = GetOrCreateAoeFillMaterial();
                IReadOnlyList<GridCoord> affected = footprint.AffectedTiles;
                if (affected != null)
                {
                    for (int i = 0; i < affected.Count; i++)
                    {
                        GridCoord tile = affected[i];
                        Color color;
                        if (allyCoords != null && allyCoords.Contains(tile))
                        {
                            color = AoeBeneficialColor;
                        }
                        else if (enemyCoords != null && enemyCoords.Contains(tile))
                        {
                            color = AoeHostileColor;
                        }
                        else
                        {
                            color = isBeneficial ? AoeBeneficialColor : AoeHostileColor;
                        }

                        float y = GetTileElevation(tile.X, tile.Y) + 0.025f + 0.006f;
                        PlaceCustomQuad(_spellAoeLayer, new Vector3(tile.X + 0.5f, y, -(tile.Y + 0.5f)), 0.92f, 0.92f, 0f, aoeMat, color);
                    }
                }

                IReadOnlyList<GridCoord> blocked = footprint.BlockedTiles;
                if (blocked != null)
                {
                    for (int i = 0; i < blocked.Count; i++)
                    {
                        GridCoord tile = blocked[i];
                        float y = GetTileElevation(tile.X, tile.Y) + 0.025f + 0.007f;
                        PlaceCustomQuad(_spellAoeLayer, new Vector3(tile.X + 0.5f, y, -(tile.Y + 0.5f)), 0.90f, 0.90f, 0f, aoeMat, AoeBlockedColor);
                    }
                }
            }

            TrimLayer(_spellAoeLayer);
        }

        public void SetFlankIndicator(GridCoord? targetPos, IReadOnlyList<GridCoord> flankingAllies)
        {
            EnsureLayers();
            _flankLayer.ActiveCount = 0;

            if (targetPos.HasValue)
            {
                PlaceTileBorder(_flankLayer, targetPos.Value, 0.015f, FlankGoldColor);
            }

            if (flankingAllies != null)
            {
                for (int i = 0; i < flankingAllies.Count; i++)
                {
                    PlaceTileBorder(_flankLayer, flankingAllies[i], 0.015f, FlankGoldColor);
                }
            }

            TrimLayer(_flankLayer);
        }

        private void PlaceTileBorder(OverlayLayer layer, GridCoord coord, float layerOffset, Color color)
        {
            PlaceTileBorderWithOpacity(layer, coord, layerOffset, color, color);
        }

        private void PlaceTileBorderWithOpacity(
            OverlayLayer layer,
            GridCoord coord,
            float layerOffset,
            Color baseColor,
            Color displayColor)
        {
            const float thickness = 0.10f;
            const float span = 0.94f;
            float offset = (span - thickness) * 0.5f;
            float cx = coord.X + 0.5f;
            float cz = -(coord.Y + 0.5f);
            float y = GetTileElevation(coord.X, coord.Y) + 0.025f + layerOffset;

            PlaceQuadWithBaseColor(layer, cx, y, cz + offset, span, thickness, baseColor, displayColor);
            PlaceQuadWithBaseColor(layer, cx, y, cz - offset, span, thickness, baseColor, displayColor);
            PlaceQuadWithBaseColor(layer, cx - offset, y, cz, thickness, span - thickness * 2f, baseColor, displayColor);
            PlaceQuadWithBaseColor(layer, cx + offset, y, cz, thickness, span - thickness * 2f, baseColor, displayColor);
        }

        private void PlaceQuad(OverlayLayer layer, float x, float y, float z, float scaleX, float scaleZ, Color color)
        {
            PlaceQuadWithBaseColor(layer, x, y, z, scaleX, scaleZ, color, color);
        }

        private void PlaceQuadWithBaseColor(
            OverlayLayer layer,
            float x,
            float y,
            float z,
            float scaleX,
            float scaleZ,
            Color baseColor,
            Color displayColor)
        {
            Material mat = _catalog != null && _catalog.overlayBaseMaterial != null
                ? _catalog.overlayBaseMaterial
                : GetOrCreateFallbackOverlayMaterial();
            PlaceCustomQuadWithBaseColor(
                layer,
                new Vector3(x, y, z),
                scaleX,
                scaleZ,
                0f,
                mat,
                baseColor,
                displayColor);
        }

        private void PlaceCustomQuad(
            OverlayLayer layer,
            Vector3 worldPos,
            float scaleX,
            float scaleZ,
            float yawDeg,
            Material material,
            Color color)
        {
            PlaceCustomQuadWithBaseColor(layer, worldPos, scaleX, scaleZ, yawDeg, material, color, color);
        }

        private void PlaceCustomQuadWithBaseColor(
            OverlayLayer layer,
            Vector3 worldPos,
            float scaleX,
            float scaleZ,
            float yawDeg,
            Material material,
            Color baseColor,
            Color displayColor)
        {
            int idx = layer.ActiveCount++;
            MeshRenderer quad = GetOrCreateQuad(layer, idx);
            while (layer.BaseColors.Count <= idx)
            {
                layer.BaseColors.Add(Color.white);
            }
            layer.BaseColors[idx] = baseColor;

            quad.transform.position = worldPos;
            quad.transform.rotation = Quaternion.Euler(90f, yawDeg, 0f);
            quad.transform.localScale = new Vector3(scaleX, scaleZ, 1f);
            if (material != null)
            {
                quad.sharedMaterial = material;
            }

            _propBlock ??= new MaterialPropertyBlock();
            _propBlock.Clear();
            _propBlock.SetColor(BaseColorId, displayColor);
            _propBlock.SetColor(ColorId, displayColor);
            quad.SetPropertyBlock(_propBlock);
            quad.gameObject.SetActive(true);
        }

        private void ApplyOpacityToLayer(OverlayLayer layer)
        {
            if (layer == null)
            {
                return;
            }

            _propBlock ??= new MaterialPropertyBlock();
            int limit = Mathf.Min(layer.ActiveCount, layer.Quads.Count, layer.BaseColors.Count);
            for (int i = 0; i < limit; i++)
            {
                MeshRenderer quad = layer.Quads[i];
                if (quad == null)
                {
                    continue;
                }

                Color col = ScaleAlpha(layer.BaseColors[i], _gridOpacity);
                _propBlock.Clear();
                _propBlock.SetColor(BaseColorId, col);
                _propBlock.SetColor(ColorId, col);
                quad.SetPropertyBlock(_propBlock);
            }
        }

        private static Color ScaleAlpha(Color col, float opacity)
        {
            col.a *= Mathf.Clamp01(opacity);
            return col;
        }

        private static MeshRenderer GetOrCreateQuad(OverlayLayer layer, int index)
        {
            while (layer.Quads.Count <= index)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "OverlayQuad_" + layer.Quads.Count;
                go.transform.SetParent(layer.Container, false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

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

                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                layer.Quads.Add(renderer);
            }

            return layer.Quads[index];
        }

        private LineRenderer GetOrCreateIntentArc(int index)
        {
            while (_intentArcPool.Count <= index)
            {
                GameObject go = new GameObject("IntentArc_" + _intentArcPool.Count);
                go.transform.SetParent(_intentArcContainer, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.alignment = LineAlignment.View;
                go.SetActive(false);
                _intentArcPool.Add(lr);
            }

            return _intentArcPool[index];
        }

        private MeshRenderer GetOrCreateIntentArrowhead(int index)
        {
            while (_intentArrowheadPool.Count <= index)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "IntentArrowhead_" + _intentArrowheadPool.Count;
                go.transform.SetParent(_intentArcContainer, false);

                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    DestroySafe(col);
                }

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _intentArrowheadPool.Add(mr);
            }

            return _intentArrowheadPool[index];
        }

        private MeshRenderer GetOrCreateIntentThreatRing(int index)
        {
            while (_intentThreatRingPool.Count <= index)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "IntentThreatRing_" + _intentThreatRingPool.Count;
                go.transform.SetParent(_intentArcContainer, false);

                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    DestroySafe(col);
                }

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _intentThreatRingPool.Add(mr);
            }

            return _intentThreatRingPool[index];
        }

        private static void OrientIntentArrowhead(Transform arrowTf, Vector3 worldPos, Vector3 tangent)
        {
            arrowTf.position = worldPos;
            Vector3 dir = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;

            Camera cam = Camera.main;
            Vector3 toCam = cam != null ? (cam.transform.position - worldPos) : Vector3.up;
            if (toCam.sqrMagnitude < 0.0001f)
            {
                toCam = Vector3.up;
            }

            Vector3 right = Vector3.Cross(toCam.normalized, dir);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.up, dir);
                if (right.sqrMagnitude < 0.0001f)
                {
                    right = Vector3.right;
                }
            }
            right.Normalize();

            Vector3 faceNormal = Vector3.Cross(dir, right).normalized;
            arrowTf.rotation = Quaternion.LookRotation(-faceNormal, dir);
        }

        private void RefreshIntentArrowheadOrientations()
        {
            int limit = Mathf.Min(_activeIntentArcCount, _intentArrowheadPool.Count, _intentArrowheadTips.Count, _intentArrowheadTangents.Count);
            for (int i = 0; i < limit; i++)
            {
                MeshRenderer mr = _intentArrowheadPool[i];
                if (mr != null && mr.gameObject.activeSelf)
                {
                    OrientIntentArrowhead(mr.transform, _intentArrowheadTips[i], _intentArrowheadTangents[i]);
                }
            }
        }

        private void StepIntentThreatRingPulse(float timeSec)
        {
            int limit = Mathf.Min(_activeIntentArcCount, _intentThreatRingPool.Count, _intentThreatRingBaseColors.Count);
            if (limit <= 0)
            {
                return;
            }

            _propBlock ??= new MaterialPropertyBlock();
            float wave = 0.5f + 0.5f * Mathf.Sin(timeSec * 5.5f);
            float scale = Mathf.Lerp(0.88f, 0.98f, wave);
            float alphaMul = Mathf.Lerp(0.76f, 1.00f, wave);

            for (int i = 0; i < limit; i++)
            {
                MeshRenderer ringMr = _intentThreatRingPool[i];
                if (ringMr == null || !ringMr.gameObject.activeSelf)
                {
                    continue;
                }

                ringMr.transform.localScale = new Vector3(scale, scale, 1f);
                Color c = _intentThreatRingBaseColors[i];
                c.a *= alphaMul;
                _propBlock.Clear();
                _propBlock.SetColor(BaseColorId, c);
                _propBlock.SetColor(ColorId, c);
                ringMr.SetPropertyBlock(_propBlock);
            }
        }

        private void StepSurfaceAnimations(float timeSec)
        {
            for (int i = 0; i < _animatedSurfaceTypes.Length; i++)
            {
                SurfaceType st = _animatedSurfaceTypes[i];
                if (!_surfaceMaterials.TryGetValue(st, out Material mat) || mat == null)
                {
                    continue;
                }

                Color baseCol = _surfaceBaseColors[st];
                bool isStrip = _surfaceIsStrip.TryGetValue(st, out bool stripFlag) && stripFlag;
                if (isStrip)
                {
                    float fps;
                    float pulse;
                    switch (st)
                    {
                        case SurfaceType.Fire:
                            fps = 7.0f;
                            pulse = 0.94f + 0.06f * Mathf.Sin(timeSec * 7.2f);
                            break;
                        case SurfaceType.ElectrifiedWater:
                            fps = 8.0f;
                            pulse = 0.92f + 0.08f * Mathf.Abs(Mathf.Sin(timeSec * 11.0f));
                            break;
                        case SurfaceType.Poison:
                            fps = 4.5f;
                            pulse = 0.92f + 0.08f * Mathf.Sin(timeSec * 3.4f);
                            break;
                        case SurfaceType.SteamCloud:
                            fps = 4.0f;
                            pulse = 0.90f + 0.10f * Mathf.Sin(timeSec * 2.1f);
                            break;
                        case SurfaceType.Oil:
                            fps = 3.0f;
                            pulse = 0.96f + 0.04f * Mathf.Sin(timeSec * 1.8f);
                            break;
                        case SurfaceType.Ice:
                            fps = 2.0f;
                            pulse = 0.95f + 0.05f * Mathf.Sin(timeSec * 2.6f);
                            break;
                        default: // Water
                            fps = 5.0f;
                            pulse = 0.94f + 0.06f * Mathf.Sin(timeSec * 2.8f);
                            break;
                    }

                    int frame = Mathf.FloorToInt(timeSec * fps) & 3;
                    SetMaterialTextureScale(mat, new Vector2(0.25f, 1f));
                    SetMaterialTextureOffset(mat, new Vector2(frame * 0.25f, 0f));
                    Color pulsedStrip = new Color(
                        Mathf.Clamp01(baseCol.r * pulse),
                        Mathf.Clamp01(baseCol.g * pulse),
                        Mathf.Clamp01(baseCol.b * pulse),
                        baseCol.a);
                    SetMaterialColor(mat, pulsedStrip);
                    continue;
                }

                Vector2 offset;
                float procPulse;

                switch (st)
                {
                    case SurfaceType.Fire:
                        offset = new Vector2(Mathf.Sin(timeSec * 1.4f) * 0.08f, -timeSec * 0.36f);
                        procPulse = 0.90f + 0.12f * Mathf.Sin(timeSec * 7.2f);
                        break;
                    case SurfaceType.ElectrifiedWater:
                        offset = new Vector2(timeSec * 0.28f, Mathf.Sin(timeSec * 3.2f) * 0.10f);
                        procPulse = 0.86f + 0.16f * Mathf.Abs(Mathf.Sin(timeSec * 11.0f));
                        break;
                    case SurfaceType.Poison:
                        offset = new Vector2(timeSec * 0.08f, -timeSec * 0.14f);
                        procPulse = 0.88f + 0.12f * Mathf.Sin(timeSec * 3.4f);
                        break;
                    case SurfaceType.SteamCloud:
                        offset = new Vector2(timeSec * 0.16f, timeSec * 0.10f);
                        procPulse = 0.84f + 0.14f * Mathf.Sin(timeSec * 2.1f);
                        break;
                    case SurfaceType.Oil:
                        offset = new Vector2(timeSec * 0.04f, timeSec * 0.03f);
                        procPulse = 0.94f + 0.06f * Mathf.Sin(timeSec * 1.8f);
                        break;
                    case SurfaceType.Ice:
                        offset = Vector2.zero;
                        procPulse = 0.92f + 0.08f * Mathf.Sin(timeSec * 2.6f);
                        break;
                    default: // Water
                        offset = new Vector2(timeSec * 0.14f, timeSec * 0.07f);
                        procPulse = 0.90f + 0.08f * Mathf.Sin(timeSec * 2.8f);
                        break;
                }

                SetMaterialTextureOffset(mat, new Vector2(Mathf.Repeat(offset.x, 1f), Mathf.Repeat(offset.y, 1f)));
                Color pulsed = new Color(
                    Mathf.Clamp01(baseCol.r * procPulse),
                    Mathf.Clamp01(baseCol.g * procPulse),
                    Mathf.Clamp01(baseCol.b * procPulse),
                    baseCol.a);
                SetMaterialColor(mat, pulsed);
            }
        }

        private Material GetOrCreateSurfaceMaterial(SurfaceType surface)
        {
            if (_surfaceMaterials.TryGetValue(surface, out Material existing) && existing != null)
            {
                return existing;
            }

            if (_catalog != null)
            {
                string key = GetSurfaceCatalogKey(surface);
                var stripEntry = !string.IsNullOrEmpty(key) ? _catalog.GetCombatSurfaceEntry(key) : null;
                if (stripEntry == null)
                {
                    stripEntry = _catalog.GetCombatSurfaceEntry(surface.ToString());
                }
                if (stripEntry != null && stripEntry.albedo != null)
                {
                    Color stripBase = new Color(1f, 1f, 1f, GetSurfaceStripAlpha(surface));
                    _surfaceBaseColors[surface] = stripBase;
                    _surfaceIsStrip[surface] = true;

                    Material stripMat = CreateTransparentOverlayMaterial("SurfaceStrip_" + surface, stripEntry.albedo, stripBase);
                    SetMaterialTextureScale(stripMat, new Vector2(0.25f, 1f));
                    SetMaterialTextureOffset(stripMat, Vector2.zero);
                    _surfaceMaterials[surface] = stripMat;
                    _surfaceStripSources[surface] = stripEntry.albedo;
                    return stripMat;
                }
            }

            Texture2D tex = BuildSurfacePixelTexture(surface);
            _surfaceTextures[surface] = tex;

            Color baseCol = GetSurfaceBaseColor(surface);
            _surfaceBaseColors[surface] = baseCol;
            _surfaceIsStrip[surface] = false;

            Material mat = CreateTransparentOverlayMaterial("Surface_" + surface, tex, baseCol);
            _surfaceMaterials[surface] = mat;
            return mat;
        }

        private static bool UsesSurfaceEdgeMask(SurfaceType surface)
        {
            // Full-bleed liquid strips read as hard square stickers; fire and steam already have organic alpha.
            return surface == SurfaceType.Oil ||
                   surface == SurfaceType.Poison ||
                   surface == SurfaceType.Ice ||
                   surface == SurfaceType.Water ||
                   surface == SurfaceType.ElectrifiedWater;
        }

        private bool HasSameSurface(int x, int y, SurfaceType surface)
        {
            return _surfaceByCoord.TryGetValue(new GridCoord(x, y), out SurfaceType other) && other == surface;
        }

        private int ComputeSurfaceNeighborBits(int x, int y, SurfaceType surface)
        {
            // Grid y grows southward; world z = -(y + 0.5), so texture top (+V) faces grid north (y - 1).
            int bits = 0;
            if (HasSameSurface(x, y - 1, surface)) bits |= SurfaceEdgeNorth;
            if (HasSameSurface(x + 1, y, surface)) bits |= SurfaceEdgeEast;
            if (HasSameSurface(x, y + 1, surface)) bits |= SurfaceEdgeSouth;
            if (HasSameSurface(x - 1, y, surface)) bits |= SurfaceEdgeWest;
            return bits;
        }

        private Texture2D GetOrCreateSurfaceEdgeTexture(SurfaceType surface, int neighborBits)
        {
            int key = ((int)surface << 4) | (neighborBits & SurfaceEdgeAll);
            if (_surfaceEdgeTextures.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            if (!_surfaceStripSources.TryGetValue(surface, out Texture2D src) || src == null)
            {
                return null;
            }

            Color32[] srcPixels = GetSurfaceStripPixels(surface, src);
            if (srcPixels == null)
            {
                return null;
            }

            int w = src.width;
            int h = src.height;
            int frame = Mathf.Max(1, h);
            Color32[] pixels = new Color32[srcPixels.Length];
            Array.Copy(srcPixels, pixels, srcPixels.Length);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!IsInsideSurfaceEdgeMask(x % frame, y, frame, neighborBits))
                    {
                        pixels[y * w + x].a = 0;
                    }
                }
            }

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = $"SurfaceEdge_{surface}_{neighborBits}",
                filterMode = FilterMode.Point,
                wrapMode = src.wrapMode,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            _surfaceEdgeTextures[key] = tex;
            return tex;
        }

        private Color32[] GetSurfaceStripPixels(SurfaceType surface, Texture2D src)
        {
            if (_surfaceStripPixels.TryGetValue(surface, out Color32[] cached) && cached != null)
            {
                return cached;
            }

            Color32[] pixels = null;
            try
            {
                if (src.isReadable)
                {
                    pixels = src.GetPixels32();
                }
                else
                {
                    // Catalog strips are not CPU-readable; copy once through a temporary sRGB render target.
                    RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    RenderTexture prevActive = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    Texture2D readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
                    readable.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0, false);
                    readable.Apply(false, false);
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(rt);
                    pixels = readable.GetPixels32();
                    DestroySafe(readable);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BywaterGridOverlays] Surface edge mask unavailable for {surface}: {ex.Message}");
                pixels = null;
            }

            _surfaceStripPixels[surface] = pixels;
            return pixels;
        }

        /// <summary>
        /// Ragged, pixel-quantized puddle edge on sides without a same-surface neighbor, with rounded outer
        /// corners. Edge noise is periodic per side so neighboring tiles' boundaries line up seamlessly.
        /// </summary>
        private static bool IsInsideSurfaceEdgeMask(int fx, int fy, int frame, int neighborBits)
        {
            bool hasN = (neighborBits & SurfaceEdgeNorth) != 0;
            bool hasE = (neighborBits & SurfaceEdgeEast) != 0;
            bool hasS = (neighborBits & SurfaceEdgeSouth) != 0;
            bool hasW = (neighborBits & SurfaceEdgeWest) != 0;

            // Quantize to 2x2 texel blocks so the edge stays on the 64 px/m art grid.
            int qx = fx & ~1;
            int qy = fy & ~1;
            float px = qx + 1f;
            float py = qy + 1f;
            float f = frame;

            const float PhaseN = 0.00f;
            const float PhaseE = 2.10f;
            const float PhaseS = 1.30f;
            const float PhaseW = 3.70f;

            // Texture row 0 is the bottom edge (grid south); top rows face grid north.
            if (!hasS && py < SurfaceEdgeInset(qx, frame, PhaseS)) return false;
            if (!hasN && (f - py) < SurfaceEdgeInset(qx, frame, PhaseN)) return false;
            if (!hasW && px < SurfaceEdgeInset(qy, frame, PhaseW)) return false;
            if (!hasE && (f - px) < SurfaceEdgeInset(qy, frame, PhaseE)) return false;

            float r = f * 0.17f;
            if (!hasS && !hasW && !IsInsideRoundedCorner(px, py, SurfaceEdgeInset(0, frame, PhaseW) + r, SurfaceEdgeInset(0, frame, PhaseS) + r, r, -1f, -1f)) return false;
            if (!hasS && !hasE && !IsInsideRoundedCorner(px, py, f - SurfaceEdgeInset(0, frame, PhaseE) - r, SurfaceEdgeInset(0, frame, PhaseS) + r, r, 1f, -1f)) return false;
            if (!hasN && !hasW && !IsInsideRoundedCorner(px, py, SurfaceEdgeInset(0, frame, PhaseW) + r, f - SurfaceEdgeInset(0, frame, PhaseN) - r, r, -1f, 1f)) return false;
            if (!hasN && !hasE && !IsInsideRoundedCorner(px, py, f - SurfaceEdgeInset(0, frame, PhaseE) - r, f - SurfaceEdgeInset(0, frame, PhaseN) - r, r, 1f, 1f)) return false;
            return true;
        }

        private static bool IsInsideRoundedCorner(float px, float py, float cx, float cy, float r, float dirX, float dirY)
        {
            bool inCornerX = dirX < 0f ? px < cx : px > cx;
            bool inCornerY = dirY < 0f ? py < cy : py > cy;
            if (!inCornerX || !inCornerY)
            {
                return true;
            }

            float dx = px - cx;
            float dy = py - cy;
            return (dx * dx) + (dy * dy) <= r * r;
        }

        private static float SurfaceEdgeInset(int along, int frame, float phase)
        {
            // Periodic in 'along' over one frame, so insets at 0 and frame match across tile seams.
            float u = (along / (float)frame) * Mathf.PI * 2f;
            float n = 0.55f * Mathf.Sin(2f * u + phase) +
                      0.30f * Mathf.Sin(5f * u + phase * 1.7f) +
                      0.15f * Mathf.Sin(11f * u + phase * 2.3f);
            float inset = frame * (0.085f + 0.045f * n);
            return Mathf.Round(inset * 0.5f) * 2f;
        }

        private static string GetSurfaceCatalogKey(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.Water: return "water";
                case SurfaceType.Oil: return "oil";
                case SurfaceType.Fire: return "fire";
                case SurfaceType.Ice: return "ice";
                case SurfaceType.Poison: return "poison";
                case SurfaceType.ElectrifiedWater: return "electrified_water";
                case SurfaceType.SteamCloud: return "steam_cloud";
                default: return string.Empty;
            }
        }

        private static float GetSurfaceStripAlpha(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.Water: return 0.80f;
                case SurfaceType.SteamCloud: return 0.78f;
                case SurfaceType.Fire: return 0.92f;
                case SurfaceType.ElectrifiedWater: return 0.90f;
                default: return 0.86f;
            }
        }

        private Material GetOrCreateShoveChevronMaterial()
        {
            if (_shoveChevronMaterial != null)
            {
                return _shoveChevronMaterial;
            }

            _shoveChevronTexture = BuildChevronPixelTexture();
            _shoveChevronMaterial = CreateTransparentOverlayMaterial("ShoveChevron", _shoveChevronTexture, Color.white);
            return _shoveChevronMaterial;
        }

        private Material GetOrCreateIntentArcMaterial(bool isSpell)
        {
            if (_intentChevronTexture == null)
            {
                _intentChevronTexture = BuildDirectionalChevronArcTexture();
            }

            if (isSpell)
            {
                if (_intentSpellMaterial == null)
                {
                    _intentSpellMaterial = CreateTransparentOverlayMaterial("IntentArcSpell", _intentChevronTexture, IntentSpellColor);
                }
                return _intentSpellMaterial;
            }

            if (_intentMeleeMaterial == null)
            {
                _intentMeleeMaterial = CreateTransparentOverlayMaterial("IntentArcMelee", _intentChevronTexture, IntentMeleeColor);
            }
            return _intentMeleeMaterial;
        }

        private Material GetOrCreateIntentArrowheadMaterial()
        {
            if (_intentArrowheadMaterial != null)
            {
                return _intentArrowheadMaterial;
            }

            _intentArrowheadTexture = BuildIntentArrowheadTexture();
            _intentArrowheadMaterial = CreateTransparentOverlayMaterial("IntentArrowhead", _intentArrowheadTexture, Color.white);
            return _intentArrowheadMaterial;
        }

        private Material GetOrCreateIntentThreatRingMaterial()
        {
            if (_intentThreatRingMaterial != null)
            {
                return _intentThreatRingMaterial;
            }

            _intentThreatRingTexture = BuildIntentThreatRingTexture();
            _intentThreatRingMaterial = CreateTransparentOverlayMaterial("IntentThreatRing", _intentThreatRingTexture, Color.white);
            return _intentThreatRingMaterial;
        }

        private Material GetOrCreateBracketMaterial()
        {
            if (_bracketMaterial != null)
            {
                return _bracketMaterial;
            }

            _cornerBracketTexture = BuildCornerBracketTexture();
            _bracketMaterial = CreateTransparentOverlayMaterial("GridCornerBracket", _cornerBracketTexture, Color.white);
            return _bracketMaterial;
        }

        private Material GetOrCreatePathDotMaterial()
        {
            if (_pathDotMaterial != null)
            {
                return _pathDotMaterial;
            }

            _pathDotTexture = BuildPathDotTexture();
            _pathDotMaterial = CreateTransparentOverlayMaterial("GridPathDot", _pathDotTexture, Color.white);
            return _pathDotMaterial;
        }

        private Material GetOrCreateAoeFillMaterial()
        {
            if (_aoeFillMaterial != null)
            {
                return _aoeFillMaterial;
            }

            _outlineFillTexture = BuildOutlineFillTexture();
            _aoeFillMaterial = CreateTransparentOverlayMaterial("GridOutlineFill", _outlineFillTexture, Color.white);
            return _aoeFillMaterial;
        }

        private Material GetOrCreateLosBlockerXMaterial()
        {
            if (_losBlockerXMaterial != null)
            {
                return _losBlockerXMaterial;
            }

            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GridLosBlockerX",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int d1 = Mathf.Abs(x - y);
                    int d2 = Mathf.Abs((size - 1 - x) - y);
                    bool inMargin = x >= 4 && x <= size - 5 && y >= 4 && y <= size - 5;
                    bool onCross = inMargin && (d1 <= 2 || d2 <= 2);
                    bool onBorder = (x <= 1 || x >= size - 2 || y <= 1 || y >= size - 2) &&
                                    ((x + y) & 1) == 0;
                    pixels[y * size + x] = (onCross || onBorder)
                        ? new Color32(255, 255, 255, 245)
                        : new Color32(0, 0, 0, 0);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _losBlockerXTexture = tex;
            _losBlockerXMaterial = CreateTransparentOverlayMaterial("GridLosBlockerX", tex, Color.white);
            return _losBlockerXMaterial;
        }

        private Material GetOrCreateFallbackOverlayMaterial()
        {
            if (_fallbackOverlayMaterial == null)
            {
                _fallbackOverlayMaterial = CreateTransparentOverlayMaterial("FallbackOverlay", Texture2D.whiteTexture, Color.white);
            }
            return _fallbackOverlayMaterial;
        }

        private Material CreateTransparentOverlayMaterial(string matName, Texture2D tex, Color baseColor)
        {
            Material mat;
            if (_catalog != null && _catalog.overlayBaseMaterial != null)
            {
                mat = new Material(_catalog.overlayBaseMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                mat = new Material(shader);
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
            }

            mat.name = matName;
            mat.hideFlags = HideFlags.HideAndDontSave;
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty(BaseMapId))
                {
                    mat.SetTexture(BaseMapId, tex);
                }
            }
            SetMaterialColor(mat, baseColor);
            return mat;
        }

        private static Color GetSurfaceBaseColor(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.Water:
                    return new Color(0.22f, 0.72f, 0.96f, 0.62f);
                case SurfaceType.Oil:
                    return new Color(0.18f, 0.14f, 0.12f, 0.78f);
                case SurfaceType.Fire:
                    return new Color(1.00f, 0.44f, 0.12f, 0.80f);
                case SurfaceType.Ice:
                    return new Color(0.70f, 0.94f, 1.00f, 0.74f);
                case SurfaceType.Poison:
                    return new Color(0.32f, 0.90f, 0.24f, 0.72f);
                case SurfaceType.ElectrifiedWater:
                    return new Color(0.28f, 0.86f, 1.00f, 0.76f);
                case SurfaceType.SteamCloud:
                    return new Color(0.90f, 0.94f, 0.98f, 0.56f);
                default:
                    return Color.white;
            }
        }

        private static Texture2D BuildSurfacePixelTexture(SurfaceType surface)
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SurfaceTex_" + surface,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int bx = x >> 1;
                    int by = y >> 1;
                    int h = DeterministicHash(bx, by, (int)surface * 97 + 13);
                    float n = (h & 0xFF) / 255f;
                    Color c = Color.clear;

                    switch (surface)
                    {
                        case SurfaceType.Water:
                        {
                            float wave = Mathf.Sin((x / (float)size) * Mathf.PI * 4f + (y / (float)size) * Mathf.PI * 2f);
                            bool foam = wave > 0.68f || (n > 0.91f && ((bx + by) % 3 == 0));
                            c = foam
                                ? new Color(0.86f, 0.97f, 1.0f, 0.92f)
                                : Color.Lerp(new Color(0.26f, 0.66f, 0.92f, 0.74f), new Color(0.42f, 0.80f, 0.98f, 0.80f), n);
                            break;
                        }

                        case SurfaceType.Oil:
                        {
                            float swirl = Mathf.Sin((bx * 0.45f) + Mathf.Cos(by * 0.40f) * 2f);
                            bool sheen = swirl > 0.76f;
                            c = sheen
                                ? new Color(0.46f, 0.34f, 0.58f, 0.90f)
                                : Color.Lerp(new Color(0.14f, 0.12f, 0.11f, 0.86f), new Color(0.24f, 0.21f, 0.18f, 0.88f), n);
                            break;
                        }

                        case SurfaceType.Fire:
                        {
                            float tongue = Mathf.Sin(bx * 0.6f) * 0.35f + Mathf.Cos(by * 0.5f) * 0.35f + n * 0.45f;
                            if (tongue > 0.72f)
                            {
                                c = new Color(1.0f, 0.92f, 0.42f, 0.96f);
                            }
                            else if (tongue > 0.35f)
                            {
                                c = new Color(1.0f, 0.52f, 0.12f, 0.88f);
                            }
                            else
                            {
                                c = new Color(0.86f, 0.18f, 0.08f, 0.74f);
                            }
                            break;
                        }

                        case SurfaceType.Ice:
                        {
                            bool crack = ((bx + by * 2) % 11 == 0) || ((bx * 2 - by + 32) % 13 == 0);
                            c = crack
                                ? new Color(0.95f, 0.99f, 1.0f, 0.95f)
                                : Color.Lerp(new Color(0.64f, 0.90f, 0.99f, 0.78f), new Color(0.80f, 0.95f, 1.0f, 0.84f), n);
                            break;
                        }

                        case SurfaceType.Poison:
                        {
                            bool bubble = ((bx % 7 == 3) && (by % 7 == 3)) || (n > 0.90f);
                            c = bubble
                                ? new Color(0.72f, 1.0f, 0.36f, 0.95f)
                                : Color.Lerp(new Color(0.18f, 0.66f, 0.16f, 0.78f), new Color(0.32f, 0.84f, 0.22f, 0.84f), n);
                            break;
                        }

                        case SurfaceType.ElectrifiedWater:
                        {
                            bool spark = ((bx ^ by) % 9 == 0) || (n > 0.92f);
                            c = spark
                                ? new Color(0.98f, 0.96f, 0.42f, 0.98f)
                                : Color.Lerp(new Color(0.22f, 0.72f, 0.96f, 0.78f), new Color(0.45f, 0.92f, 1.0f, 0.86f), n);
                            break;
                        }

                        case SurfaceType.SteamCloud:
                        {
                            float dx = (x - size * 0.5f) / (size * 0.5f);
                            float dy = (y - size * 0.5f) / (size * 0.5f);
                            float puff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 0.78f + (n - 0.5f) * 0.25f);
                            c = new Color(0.92f, 0.95f, 1.0f, Mathf.Clamp01(puff * 0.72f));
                            break;
                        }
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildChevronPixelTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ShoveChevronTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 240);
            Color32 outline = new Color32(30, 30, 30, 220);

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size; // 0 = bottom, 1 = top (+dir)
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / size) - 0.5f) * 2f; // 0 = center, 1 = edge

                    bool inHead = ny >= 0.45f && ny <= 0.92f && nx <= (0.92f - ny) * 1.45f;
                    bool inShaft = ny >= 0.10f && ny < 0.48f && nx <= 0.22f;
                    bool borderHead = ny >= 0.42f && ny <= 0.96f && nx <= (0.96f - ny) * 1.52f;
                    bool borderShaft = ny >= 0.06f && ny < 0.48f && nx <= 0.28f;

                    if (inHead || inShaft)
                    {
                        pixels[y * size + x] = white;
                    }
                    else if (borderHead || borderShaft)
                    {
                        pixels[y * size + x] = outline;
                    }
                    else
                    {
                        pixels[y * size + x] = clear;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildCornerBracketTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GridCornerBracketTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 bracket = new Color32(255, 255, 255, 255);

            const int armLen = 14;
            const int armThick = 3;

            for (int y = 0; y < size; y++)
            {
                bool inCornerY = y < armLen || y >= size - armLen;
                bool inEdgeBandY = y < armThick || y >= size - armThick;

                for (int x = 0; x < size; x++)
                {
                    bool inCornerX = x < armLen || x >= size - armLen;
                    bool inEdgeBandX = x < armThick || x >= size - armThick;

                    bool isBracket = (inCornerX && inEdgeBandY) || (inCornerY && inEdgeBandX);
                    pixels[y * size + x] = isBracket ? bracket : clear;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildPathDotTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GridPathDotTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            float invRadius = 1.0f / (size * 0.44f);

            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) * invRadius;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) * invRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= 1.0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Smooth alpha edge and subtle dark outer rim framing a bright tintable interior
                    float alpha = Mathf.Clamp01((1.0f - dist) / 0.16f);
                    float rimT = Mathf.Clamp01((dist - 0.62f) / 0.24f);
                    byte rgb = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 52f, rimT));
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[y * size + x] = new Color32(rgb, rgb, rgb, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildOutlineFillTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GridOutlineFillTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color32 border = new Color32(255, 255, 255, 245);
            Color32 innerFill = new Color32(255, 255, 255, 56); // ~0.22 translucent interior fill

            for (int y = 0; y < size; y++)
            {
                bool isBorderY = y < 2 || y >= size - 2;
                for (int x = 0; x < size; x++)
                {
                    bool isBorderX = x < 2 || x >= size - 2;
                    pixels[y * size + x] = (isBorderX || isBorderY) ? border : innerFill;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildDirectionalChevronArcTexture()
        {
            const int w = 32;
            const int h = 16;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "IntentDirectionalChevronTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[w * h];
            Color32 chevronCore = new Color32(255, 255, 255, 255);
            Color32 chevronEdge = new Color32(42, 14, 20, 195);
            Color32 ribbon = new Color32(255, 255, 255, 44);
            Color32 clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < h; y++)
            {
                float ny = Mathf.Abs(((y + 0.5f) / h) - 0.5f) * 2f; // 0 = centerline, 1 = edge
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w; // 0 = tail (-U), 1 = forward apex (+U)
                    float chevronCenter = 0.78f - ny * 0.42f;
                    float dist = Mathf.Abs(nx - chevronCenter);

                    if (ny <= 0.86f && dist <= 0.145f)
                    {
                        pixels[y * w + x] = chevronCore;
                    }
                    else if (ny <= 0.94f && dist <= 0.21f)
                    {
                        pixels[y * w + x] = chevronEdge;
                    }
                    else if (ny <= 0.52f)
                    {
                        pixels[y * w + x] = ribbon;
                    }
                    else
                    {
                        pixels[y * w + x] = clear;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildIntentArrowheadTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "IntentArrowheadTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color32 core = new Color32(255, 255, 255, 255);
            Color32 edge = new Color32(28, 10, 14, 230);
            Color32 clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size; // 0 = back base, 1 = sharp tip (+Y)
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / size) - 0.5f) * 2f; // 0 = centerline, 1 = side
                    float notchBase = 0.14f + (1f - nx) * 0.18f;
                    bool inCore = ny >= notchBase && ny <= 0.93f && nx <= (0.93f - ny) * 1.15f;
                    float edgeNotchBase = notchBase - 0.045f;
                    bool inEdge = ny >= edgeNotchBase && ny <= 0.98f && nx <= (0.98f - ny) * 1.24f;

                    if (inCore)
                    {
                        pixels[y * size + x] = core;
                    }
                    else if (inEdge)
                    {
                        pixels[y * size + x] = edge;
                    }
                    else
                    {
                        pixels[y * size + x] = clear;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D BuildIntentThreatRingTexture()
        {
            const int size = SurfaceTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "IntentThreatRingTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            Color32 ring = new Color32(255, 255, 255, 235);
            Color32 edge = new Color32(30, 10, 14, 185);
            Color32 innerTint = new Color32(255, 255, 255, 26);
            Color32 clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < size; y++)
            {
                float dy = (((y + 0.5f) / size) - 0.5f) * 2f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (((x + 0.5f) / size) - 0.5f) * 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool isNotch = (Mathf.Abs(dx) <= 0.09f || Mathf.Abs(dy) <= 0.09f) && r >= 0.56f && r <= 0.95f;
                    bool inRing = (r >= 0.72f && r <= 0.88f) || isNotch;
                    bool inEdge = (r >= 0.68f && r <= 0.92f) || ((Mathf.Abs(dx) <= 0.13f || Mathf.Abs(dy) <= 0.13f) && r >= 0.52f && r <= 0.98f);

                    if (inRing)
                    {
                        pixels[y * size + x] = ring;
                    }
                    else if (inEdge)
                    {
                        pixels[y * size + x] = edge;
                    }
                    else if (r < 0.68f)
                    {
                        pixels[y * size + x] = innerTint;
                    }
                    else
                    {
                        pixels[y * size + x] = clear;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void SetMaterialTextureOffset(Material mat, Vector2 offset)
        {
            mat.mainTextureOffset = offset;
            if (mat.HasProperty(BaseMapId))
            {
                mat.SetTextureOffset(BaseMapId, offset);
            }
        }

        private static void SetMaterialTextureScale(Material mat, Vector2 scale)
        {
            mat.mainTextureScale = scale;
            if (mat.HasProperty(BaseMapId))
            {
                mat.SetTextureScale(BaseMapId, scale);
            }
        }

        private CombatVfxInstance AcquireVfxInstance()
        {
            for (int i = 0; i < _vfxPool.Count; i++)
            {
                if (!_vfxPool[i].Active)
                {
                    return _vfxPool[i];
                }
            }

            if (_vfxPool.Count < MaxCombatVfxPool)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "CombatVfxQuad_" + _vfxPool.Count;
                go.transform.SetParent(_vfxContainer, false);

                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    DestroySafe(col);
                }

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);

                CombatVfxInstance created = new CombatVfxInstance
                {
                    Root = go,
                    Renderer = mr
                };
                _vfxPool.Add(created);
                return created;
            }

            CombatVfxInstance recycled = _vfxPool[_vfxRoundRobinCursor % _vfxPool.Count];
            _vfxRoundRobinCursor = (_vfxRoundRobinCursor + 1) % _vfxPool.Count;
            return recycled;
        }

        private void StepCombatVfx(float dt)
        {
            if (_vfxPool.Count == 0 || !Application.isPlaying)
            {
                return;
            }

            for (int i = 0; i < _vfxPool.Count; i++)
            {
                CombatVfxInstance inst = _vfxPool[i];
                if (inst == null || !inst.Active)
                {
                    continue;
                }

                inst.Elapsed += dt;
                if (inst.Elapsed >= inst.Duration)
                {
                    inst.Active = false;
                    if (inst.Root != null)
                    {
                        inst.Root.SetActive(false);
                    }
                    continue;
                }

                UpdateCombatVfxInstanceVisual(inst);
            }
        }

        private void UpdateCombatVfxInstanceVisual(CombatVfxInstance inst)
        {
            float t = Mathf.Clamp01(inst.Elapsed / Mathf.Max(0.01f, inst.Duration));
            int frame = Mathf.Clamp(Mathf.FloorToInt(t * 4f), 0, 3);

            Vector3 pos;
            if (inst.IsProjectile)
            {
                pos = Vector3.Lerp(inst.StartPos, inst.TargetPos, t);
                pos.y += 0.72f + Mathf.Sin(t * Mathf.PI) * 0.24f;
            }
            else
            {
                pos = inst.TargetPos + new Vector3(0f, 0.78f, 0f);
            }

            inst.Root.transform.position = pos;

            Camera cam = Camera.main;
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.Euler(45f, 0f, 0f);
            if (inst.IsProjectile)
            {
                Vector3 dir = inst.TargetPos - inst.StartPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f && cam != null)
                {
                    Vector3 right = cam.transform.right;
                    Vector3 up = cam.transform.up;
                    float dx = Vector3.Dot(dir, right);
                    float dy = Vector3.Dot(dir, up);
                    float rollDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    inst.Root.transform.rotation = camRot * Quaternion.Euler(0f, 0f, rollDeg);
                }
                else
                {
                    inst.Root.transform.rotation = camRot;
                }
            }
            else
            {
                inst.Root.transform.rotation = camRot;
            }

            float pop = inst.IsProjectile ? 1.0f : (0.92f + 0.24f * Mathf.Sin(t * Mathf.PI));
            float scale = inst.BaseScale * pop;
            inst.Root.transform.localScale = new Vector3(scale, scale, 1f);

            float alpha = t > 0.72f ? Mathf.Lerp(1f, 0f, (t - 0.72f) / 0.28f) : 1f;
            Color tint = new Color(1f, 1f, 1f, alpha);
            Vector4 st = new Vector4(0.25f, 1f, frame * 0.25f, 0f);

            _vfxPropBlock ??= new MaterialPropertyBlock();
            _vfxPropBlock.Clear();
            _vfxPropBlock.SetVector(BaseMapStId, st);
            _vfxPropBlock.SetVector(MainTexStId, st);
            _vfxPropBlock.SetColor(BaseColorId, tint);
            _vfxPropBlock.SetColor(ColorId, tint);
            inst.Renderer.SetPropertyBlock(_vfxPropBlock);
        }

        private Material GetOrCreateVfxMaterial(string vfxKey)
        {
            if (_vfxMaterials.TryGetValue(vfxKey, out Material existing) && existing != null)
            {
                return existing;
            }

            Texture2D tex = null;
            if (_catalog != null)
            {
                var entry = _catalog.GetCombatVfxEntry(vfxKey);
                if (entry != null && entry.albedo != null)
                {
                    tex = entry.albedo;
                }
            }

            if (tex == null)
            {
                if (_vfxFallbackTexture == null)
                {
                    _vfxFallbackTexture = BuildVfxFallbackTexture();
                }
                tex = _vfxFallbackTexture;
            }

            Material mat = CreateTransparentOverlayMaterial("CombatVfx_" + vfxKey, tex, Color.white);
            SetMaterialTextureScale(mat, new Vector2(0.25f, 1f));
            SetMaterialTextureOffset(mat, Vector2.zero);
            _vfxMaterials[vfxKey] = mat;
            return mat;
        }

        private static float ResolveVfxBaseScale(string vfxKey)
        {
            if (string.Equals(vfxKey, "fireball_impact", StringComparison.OrdinalIgnoreCase)) return 1.45f;
            if (string.Equals(vfxKey, "heal_sparkle", StringComparison.OrdinalIgnoreCase)) return 1.25f;
            if (string.Equals(vfxKey, "slash_arc", StringComparison.OrdinalIgnoreCase)) return 1.25f;
            if (string.Equals(vfxKey, "arrow_bolt", StringComparison.OrdinalIgnoreCase)) return 0.95f;
            return 1.15f;
        }

        private static Texture2D BuildVfxFallbackTexture()
        {
            const int w = 64;
            const int h = 16;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "CombatVfxFallbackTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[w * h];
            for (int f = 0; f < 4; f++)
            {
                float radius = 0.25f + f * 0.16f;
                for (int y = 0; y < h; y++)
                {
                    float ny = ((y + 0.5f) / h - 0.5f) * 2f;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        float nx = ((lx + 0.5f) / 16f - 0.5f) * 2f;
                        float d = Mathf.Sqrt(nx * nx + ny * ny);
                        float a = Mathf.Clamp01(1f - d / Mathf.Max(0.15f, radius));
                        pixels[y * w + f * 16 + lx] = new Color(1f, 0.92f, 0.65f, a);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty(BaseColorId))
            {
                mat.SetColor(BaseColorId, color);
            }
            if (mat.HasProperty(ColorId))
            {
                mat.SetColor(ColorId, color);
            }
        }

        private static int DeterministicHash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7FFFFFFF;
            }
        }

        private static void HideLayer(OverlayLayer layer)
        {
            if (layer == null)
            {
                return;
            }

            layer.ActiveCount = 0;
            TrimLayer(layer);
        }

        private static void TrimLayer(OverlayLayer layer)
        {
            for (int i = layer.ActiveCount; i < layer.Quads.Count; i++)
            {
                layer.Quads[i].gameObject.SetActive(false);
            }
        }

        private void RouteVfxToDirector(string vfxKey, Vector3 startWorldPos, Vector3 targetWorldPos, bool isProjectile)
        {
            if (_vfxDirector == null)
            {
                _vfxDirector = GetComponent<BywaterCombatVfxDirector>();
            }
            if (_vfxDirector == null)
            {
                return;
            }

            if (vfxKey.IndexOf("keg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _vfxDirector.PlayKegExplosion(targetWorldPos);
                return;
            }

            if (vfxKey.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _vfxDirector.PlayFireball(startWorldPos, targetWorldPos);
                return;
            }

            if (vfxKey.IndexOf("lightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lightningTwoHopScratch[0] = startWorldPos;
                _lightningTwoHopScratch[1] = targetWorldPos;
                _vfxDirector.PlayChainLightning(_lightningTwoHopScratch);
                return;
            }

            if (vfxKey.IndexOf("frost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("cold", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _vfxDirector.PlayFrostShatter(targetWorldPos);
                return;
            }

            if (isProjectile ||
                vfxKey.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                vfxKey.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _vfxDirector.PlayArrowArc(startWorldPos, targetWorldPos);
            }
        }

        private int InferStepApCost(IReadOnlyList<GridCoord> path, int index)
        {
            GridCoord step = path[index];
            if (_surfaceByCoord.TryGetValue(step, out SurfaceType surf) &&
                (surf == SurfaceType.Water ||
                 surf == SurfaceType.ElectrifiedWater ||
                 surf == SurfaceType.Oil ||
                 surf == SurfaceType.Poison))
            {
                return 2;
            }

            if (index > 0)
            {
                GridCoord prev = path[index - 1];
                float elevDelta = Mathf.Abs(GetTileElevation(step.X, step.Y) - GetTileElevation(prev.X, prev.Y));
                if (elevDelta >= 0.30f)
                {
                    return 2;
                }
            }

            return 1;
        }

        private WorldBadgeInstance GetOrCreateHoverPip(int index)
        {
            while (_hoverPipPool.Count <= index)
            {
                WorldBadgeInstance created = CreateWorldBadge(
                    _hoverPipContainer,
                    "HoverApPip_" + _hoverPipPool.Count,
                    new Vector2(84f, 30f),
                    0.0052f,
                    16f,
                    45);
                created.Root.SetActive(false);
                _hoverPipPool.Add(created);
            }

            return _hoverPipPool[index];
        }

        private void HideAllHoverPips()
        {
            for (int i = 0; i < _hoverPipPool.Count; i++)
            {
                if (_hoverPipPool[i]?.Root != null)
                {
                    _hoverPipPool[i].Root.SetActive(false);
                }
            }
        }

        private void UpdateShoveTrajectoryAndBadge(
            GridCoord target,
            GridCoord highlightTile,
            GridCoord pushDir,
            bool canExecute,
            Color previewColor,
            ShoveHazardVisualHint hazardHint,
            TacticalShovePreview preview)
        {
            EnsureShoveExtras();

            float startElev = GetTileElevation(target.X, target.Y);
            float endElev = GetTileElevation(highlightTile.X, highlightTile.Y);
            Vector3 startPos = new Vector3(target.X + 0.5f, startElev + 0.34f, -(target.Y + 0.5f));
            Vector3 endPos = new Vector3(highlightTile.X + 0.5f, endElev + 0.34f, -(highlightTile.Y + 0.5f));

            Vector3 delta = endPos - startPos;
            Vector3 flatDir = new Vector3(delta.x, 0f, delta.z);
            if (flatDir.sqrMagnitude < 0.0001f && (pushDir.X != 0 || pushDir.Y != 0))
            {
                flatDir = new Vector3(pushDir.X, 0f, -pushDir.Y);
                endPos = startPos + flatDir.normalized * 0.75f;
                delta = endPos - startPos;
            }

            if (flatDir.sqrMagnitude > 0.0001f)
            {
                Vector3 dirNorm = delta.normalized;
                Vector3 lineEnd = endPos - dirNorm * 0.22f;

                _shoveLinePoints[0] = startPos;
                _shoveLinePoints[1] = lineEnd;
                Color arrowCol = new Color(previewColor.r, previewColor.g, previewColor.b, 0.96f);
                _shoveArrowLine.sharedMaterial = GetOrCreateShoveLineMaterial();
                _shoveArrowLine.startColor = arrowCol;
                _shoveArrowLine.endColor = arrowCol;
                _shoveArrowLine.positionCount = 2;
                _shoveArrowLine.SetPositions(_shoveLinePoints);
                _shoveArrowLine.gameObject.SetActive(true);

                Vector3 arrowPos = endPos - dirNorm * 0.14f;
                OrientIntentArrowhead(_shoveArrowhead.transform, arrowPos, dirNorm);
                _shoveArrowhead.transform.localScale = new Vector3(0.42f, 0.48f, 1f);
                _shoveArrowhead.sharedMaterial = GetOrCreateIntentArrowheadMaterial();
                _propBlock ??= new MaterialPropertyBlock();
                _propBlock.Clear();
                _propBlock.SetColor(BaseColorId, arrowCol);
                _propBlock.SetColor(ColorId, arrowCol);
                _shoveArrowhead.SetPropertyBlock(_propBlock);
                _shoveArrowhead.gameObject.SetActive(true);
            }
            else
            {
                _shoveArrowLine.positionCount = 0;
                _shoveArrowLine.gameObject.SetActive(false);
                _shoveArrowhead.gameObject.SetActive(false);
            }

            string badgeText = ResolveShoveHazardBadgeText(canExecute, hazardHint, preview);
            if (!string.IsNullOrEmpty(badgeText))
            {
                TMPro.TMP_FontAsset font = _catalog != null ? _catalog.uiFont : null;
                if (font != null && _shoveHazardBadge.Label.font != font)
                {
                    _shoveHazardBadge.Label.font = font;
                }

                _shoveHazardBadge.Label.text = badgeText;
                Color borderCol = new Color(previewColor.r, previewColor.g, previewColor.b, 0.98f);
                _shoveHazardBadge.Border.color = borderCol;
                _shoveHazardBadge.Label.color = new Color(1.00f, 0.94f, 0.72f, 1f);
                _shoveHazardBadge.Root.transform.position = new Vector3(
                    highlightTile.X + 0.5f,
                    Mathf.Max(endElev, _waterSurfaceHeight) + 1.18f,
                    -(highlightTile.Y + 0.5f));
                _shoveHazardBadge.Root.SetActive(true);
            }
            else
            {
                _shoveHazardBadge.Root.SetActive(false);
            }
        }

        private void HideShoveExtras()
        {
            if (_shoveArrowLine != null)
            {
                _shoveArrowLine.positionCount = 0;
                _shoveArrowLine.gameObject.SetActive(false);
            }

            if (_shoveArrowhead != null)
            {
                _shoveArrowhead.gameObject.SetActive(false);
            }

            if (_shoveHazardBadge?.Root != null)
            {
                _shoveHazardBadge.Root.SetActive(false);
            }
        }

        private static string ResolveShoveHazardBadgeText(
            bool canExecute,
            ShoveHazardVisualHint hazardHint,
            TacticalShovePreview preview)
        {
            if (!string.IsNullOrWhiteSpace(hazardHint.HazardBadgeText))
            {
                return hazardHint.HazardBadgeText;
            }

            if (!canExecute)
            {
                return string.Empty;
            }

            if (hazardHint.TriggersKegExplosion)
            {
                return "KEG BLAST 2d6!";
            }

            if (hazardHint.IgnitesOnBrazier)
            {
                return "BRAZIER +1d6 FIRE";
            }

            if (hazardHint.CrushesWall || preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId))
            {
                return "WALL CRUSH +1d4";
            }

            if (hazardHint.PushesOffLedge || preview.PushesOffLedge)
            {
                return "LEDGE FALL +1d6";
            }

            SurfaceType entered = hazardHint.EntersSurface != SurfaceType.None
                ? hazardHint.EntersSurface
                : preview.DestinationSurface;
            if (entered == SurfaceType.ElectrifiedWater)
            {
                return "WATER SHOCK!";
            }

            if (hazardHint.DestroysCover)
            {
                return "COVER SMASH!";
            }

            return entered != SurfaceType.None ? ResolveSurfaceHazardBadge(entered) : string.Empty;
        }

        private static string ResolveSurfaceHazardBadge(SurfaceType surface)
        {
            switch (surface)
            {
                case SurfaceType.Fire: return "FIRE +1d4";
                case SurfaceType.Poison: return "POISON CLOUD";
                case SurfaceType.Ice: return "ICE SLIP";
                case SurfaceType.Oil: return "OIL SLICK";
                case SurfaceType.Water: return "SHALLOW WATER";
                default: return string.Empty;
            }
        }

        private Material GetOrCreateShoveLineMaterial()
        {
            if (_shoveLineMaterial == null)
            {
                _shoveLineMaterial = CreateTransparentOverlayMaterial("ShoveTrajectoryLine", Texture2D.whiteTexture, Color.white);
            }
            return _shoveLineMaterial;
        }

        private void EnsureShoveExtras()
        {
            if (_shoveExtrasContainer == null)
            {
                GameObject containerGo = new GameObject("ShoveTrajectoryAndBadges");
                containerGo.transform.SetParent(transform, false);
                _shoveExtrasContainer = containerGo.transform;
            }

            if (_shoveArrowLine == null)
            {
                GameObject lineGo = new GameObject("ShoveTrajectoryArrow");
                lineGo.transform.SetParent(_shoveExtrasContainer, false);
                _shoveArrowLine = lineGo.AddComponent<LineRenderer>();
                _shoveArrowLine.useWorldSpace = true;
                _shoveArrowLine.shadowCastingMode = ShadowCastingMode.Off;
                _shoveArrowLine.receiveShadows = false;
                _shoveArrowLine.startWidth = 0.14f;
                _shoveArrowLine.endWidth = 0.11f;
                _shoveArrowLine.numCapVertices = 2;
                _shoveArrowLine.numCornerVertices = 2;
                _shoveArrowLine.alignment = LineAlignment.View;
                lineGo.SetActive(false);
            }

            if (_shoveArrowhead == null)
            {
                GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                headGo.name = "ShoveArrowhead";
                headGo.transform.SetParent(_shoveExtrasContainer, false);
                Collider col = headGo.GetComponent<Collider>();
                if (col != null)
                {
                    DestroySafe(col);
                }

                _shoveArrowhead = headGo.GetComponent<MeshRenderer>();
                _shoveArrowhead.shadowCastingMode = ShadowCastingMode.Off;
                _shoveArrowhead.receiveShadows = false;
                headGo.SetActive(false);
            }

            if (_shoveHazardBadge == null)
            {
                _shoveHazardBadge = CreateWorldBadge(
                    _shoveExtrasContainer,
                    "ShoveHazardBadge",
                    new Vector2(210f, 40f),
                    0.0060f,
                    15.5f,
                    48);
                _shoveHazardBadge.Root.SetActive(false);
            }
        }

        private WorldBadgeInstance CreateWorldBadge(
            Transform parent,
            string badgeName,
            Vector2 sizeDelta,
            float worldScale,
            float fontSize,
            int sortingOrder)
        {
            GameObject rootGo = new GameObject(badgeName, typeof(RectTransform), typeof(Canvas));
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localScale = Vector3.one * worldScale;
            rootGo.AddComponent<PixelBillboard>();

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;

            RectTransform rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.sizeDelta = sizeDelta;

            Image borderImg = rootGo.AddComponent<Image>();
            borderImg.color = new Color(0.220f, 0.741f, 0.973f, 0.95f);
            borderImg.raycastTarget = false;

            GameObject innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(rootGo.transform, false);
            RectTransform innerRt = innerGo.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(2f, 2f);
            innerRt.offsetMax = new Vector2(-2f, -2f);

            Image bgImg = innerGo.GetComponent<Image>();
            bgImg.color = new Color(0.055f, 0.075f, 0.125f, 0.92f);
            bgImg.raycastTarget = false;

            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(innerGo.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(4f, 1f);
            textRt.offsetMax = new Vector2(-4f, -1f);

            TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
            if (_catalog != null && _catalog.uiFont != null)
            {
                tmp.font = _catalog.uiFont;
            }
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return new WorldBadgeInstance
            {
                Root = rootGo,
                Border = borderImg,
                Background = bgImg,
                Label = tmp
            };
        }

        private void EnsureLayers()
        {
            if (_surfaceLayer == null) _surfaceLayer = new OverlayLayer(transform, "ElementalSurfaces");
            if (_combatBoundsLayer == null) _combatBoundsLayer = new OverlayLayer(transform, "CombatBounds");
            if (_combatReachableLayer == null) _combatReachableLayer = new OverlayLayer(transform, "CombatReachable");
            if (_combatThreatenedLayer == null) _combatThreatenedLayer = new OverlayLayer(transform, "CombatThreatened");
            if (_attackTargetLayer == null) _attackTargetLayer = new OverlayLayer(transform, "AttackTargets");
            if (_hoveredAttackTargetLayer == null) _hoveredAttackTargetLayer = new OverlayLayer(transform, "HoveredAttackTarget");
            if (_spellAoeLayer == null) _spellAoeLayer = new OverlayLayer(transform, "SpellAoeFootprint");
            if (_tacticalSelectLayer == null) _tacticalSelectLayer = new OverlayLayer(transform, "TacticalSelection");
            if (_exploreHoverLayer == null) _exploreHoverLayer = new OverlayLayer(transform, "ExplorationHoverPath");
            if (_combatHoverLayer == null) _combatHoverLayer = new OverlayLayer(transform, "CombatHoverPath");
            if (_losLayer == null) _losLayer = new OverlayLayer(transform, "LosPreview");
            if (_flankLayer == null) _flankLayer = new OverlayLayer(transform, "FlankIndicator");
            if (_shoveLayer == null) _shoveLayer = new OverlayLayer(transform, "ShovePreview");
            if (_deploymentZoneLayer == null) _deploymentZoneLayer = new OverlayLayer(transform, "DeploymentZone");

            if (_intentArcContainer == null)
            {
                GameObject go = new GameObject("EnemyIntentArcs");
                go.transform.SetParent(transform, false);
                _intentArcContainer = go.transform;
            }

            if (_vfxContainer == null)
            {
                GameObject go = new GameObject("CombatVfxPool");
                go.transform.SetParent(transform, false);
                _vfxContainer = go.transform;
            }

            if (_hoverPipContainer == null)
            {
                GameObject go = new GameObject("CombatHoverPathPips");
                go.transform.SetParent(transform, false);
                _hoverPipContainer = go.transform;
            }

            EnsureShoveExtras();
        }

        private static void DestroySafe(UnityEngine.Object obj)
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
