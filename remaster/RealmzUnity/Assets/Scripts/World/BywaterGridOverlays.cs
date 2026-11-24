using System;
using System.Collections.Generic;
using Realmz.Data;
using RealmzCore.Combat;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    public struct EnemyIntentArcData
    {
        public GridCoord From;
        public GridCoord To;
        public bool IsSpell;
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
        private const float DefaultVfxDuration = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

        private static readonly Color ReachableNormalColor = new Color(0.176f, 0.831f, 0.749f, 0.88f); // #2DD4BF
        private static readonly Color ReachableAooColor = new Color(0.976f, 0.451f, 0.086f, 0.92f);    // #F97316
        private static readonly Color ThreatenedColor = new Color(0.937f, 0.267f, 0.267f, 0.22f);
        private static readonly Color HoverPathSafeColor = new Color(1.00f, 0.95f, 0.72f, 0.76f);
        private static readonly Color HoverPathAooColor = new Color(0.98f, 0.25f, 0.18f, 0.80f);
        private static readonly Color ExploreReachableColor = new Color(0.22f, 0.86f, 0.64f, 0.78f);
        private static readonly Color ExploreBlockedColor = new Color(0.88f, 0.24f, 0.22f, 0.78f);
        private static readonly Color LosClearColor = new Color(0.18f, 0.85f, 0.48f, 0.72f);
        private static readonly Color LosPartialColor = new Color(0.96f, 0.72f, 0.16f, 0.74f);
        private static readonly Color LosBlockedColor = new Color(0.95f, 0.22f, 0.22f, 0.78f);
        private static readonly Color AoeBeneficialColor = new Color(0.22f, 0.74f, 0.97f, 0.92f);
        private static readonly Color AoeHostileColor = new Color(0.96f, 0.32f, 0.18f, 0.94f);
        private static readonly Color AoeBlockedColor = new Color(0.22f, 0.22f, 0.25f, 0.78f);
        private static readonly Color FlankGoldColor = new Color(0.98f, 0.80f, 0.15f, 0.60f);
        private static readonly Color AttackTargetColor = new Color(0.937f, 0.267f, 0.267f, 0.92f);   // #EF4444
        private static readonly Color HoveredAttackTargetColor = new Color(0.98f, 0.34f, 0.24f, 0.95f);
        private static readonly Color TacticalSelectColor = new Color(0.98f, 0.84f, 0.24f, 0.92f);
        private static readonly Color CombatBoundsColor = new Color(0.98f, 0.78f, 0.22f, 0.52f);

        private static readonly Color ShoveSafeEmerald = new Color(0.06f, 0.73f, 0.51f, 0.78f);
        private static readonly Color ShoveLedgeGold = new Color(0.98f, 0.80f, 0.08f, 0.82f);
        private static readonly Color ShoveCrushCrimson = new Color(0.96f, 0.36f, 0.12f, 0.84f);
        private static readonly Color ShoveInvalidRed = new Color(0.84f, 0.20f, 0.20f, 0.52f);

        private static readonly Color IntentMeleeColor = new Color(0.937f, 0.267f, 0.267f, 0.92f); // #EF4444
        private static readonly Color IntentSpellColor = new Color(0.976f, 0.451f, 0.086f, 0.94f); // #F97316

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

        private BywaterAssetCatalog _catalog;
        private Func<int, int, float> _elevationResolver;
        private MaterialPropertyBlock _propBlock;

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

        private Transform _intentArcContainer;
        private readonly List<LineRenderer> _intentArcPool = new List<LineRenderer>(16);
        private readonly Vector3[] _arcPointScratch = new Vector3[IntentArcSegments + 1];
        private int _activeIntentArcCount;

        private readonly Dictionary<SurfaceType, Material> _surfaceMaterials = new Dictionary<SurfaceType, Material>(8);
        private readonly Dictionary<SurfaceType, Texture2D> _surfaceTextures = new Dictionary<SurfaceType, Texture2D>(8);
        private readonly Dictionary<SurfaceType, Color> _surfaceBaseColors = new Dictionary<SurfaceType, Color>(8);
        private readonly Dictionary<SurfaceType, bool> _surfaceIsStrip = new Dictionary<SurfaceType, bool>(8);
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

        private Material _shoveChevronMaterial;
        private Texture2D _shoveChevronTexture;
        private Material _intentMeleeMaterial;
        private Material _intentSpellMaterial;
        private Texture2D _intentDashTexture;
        private Material _bracketMaterial;
        private Texture2D _cornerBracketTexture;
        private Material _aoeFillMaterial;
        private Texture2D _outlineFillTexture;
        private Material _fallbackOverlayMaterial;
        private float _intentScrollOffset;
        private float _gridOpacity = 1.0f;

        public void SetGridOpacity(float opacity)
        {
            _gridOpacity = Mathf.Clamp01(opacity);
            ApplyOpacityToLayer(_combatBoundsLayer);
            ApplyOpacityToLayer(_combatReachableLayer);
            ApplyOpacityToLayer(_combatThreatenedLayer);
            ApplyOpacityToLayer(_attackTargetLayer);
            ApplyOpacityToLayer(_exploreHoverLayer);
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
                _intentScrollOffset = Mathf.Repeat(_intentScrollOffset - dt * 1.65f, 1f);
                if (_intentMeleeMaterial != null)
                {
                    SetMaterialTextureOffset(_intentMeleeMaterial, new Vector2(_intentScrollOffset, 0f));
                }
                if (_intentSpellMaterial != null)
                {
                    SetMaterialTextureOffset(_intentSpellMaterial, new Vector2(_intentScrollOffset * 1.25f, 0f));
                }
            }
        }

        private void OnDestroy()
        {
            DisposeSurfaceCache();
            DisposeVfxMaterialCache();

            DestroySafe(_vfxFallbackTexture);
            _vfxFallbackTexture = null;

            DestroySafe(_shoveChevronMaterial);
            _shoveChevronMaterial = null;
            DestroySafe(_shoveChevronTexture);
            _shoveChevronTexture = null;

            DestroySafe(_intentMeleeMaterial);
            _intentMeleeMaterial = null;
            DestroySafe(_intentSpellMaterial);
            _intentSpellMaterial = null;
            DestroySafe(_intentDashTexture);
            _intentDashTexture = null;

            DestroySafe(_bracketMaterial);
            _bracketMaterial = null;
            DestroySafe(_cornerBracketTexture);
            _cornerBracketTexture = null;

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
            HideLayer(_losLayer);
            HideLayer(_flankLayer);
            HideLayer(_shoveLayer);
            ClearEnemyIntentArcs();
        }

        public void SetSurfaceCells(IReadOnlyList<CombatGridCell> cells)
        {
            EnsureLayers();
            _surfaceLayer.ActiveCount = 0;

            if (cells != null)
            {
                float t = Application.isPlaying ? Time.unscaledTime : 0f;
                for (int i = 0; i < cells.Count; i++)
                {
                    CombatGridCell cell = cells[i];
                    if (cell == null || cell.Surface == SurfaceType.None)
                    {
                        continue;
                    }

                    Material mat = GetOrCreateSurfaceMaterial(cell.Surface);
                    Color baseCol = _surfaceBaseColors.TryGetValue(cell.Surface, out Color cachedCol)
                        ? cachedCol
                        : GetSurfaceBaseColor(cell.Surface);
                    float y = GetTileElevation(cell.X, cell.Y) + 0.085f;
                    Vector3 pos = new Vector3(cell.X + 0.5f, y, -(cell.Y + 0.5f));
                    PlaceCustomQuad(_surfaceLayer, pos, 0.96f, 0.96f, 0f, mat, baseCol);
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
        }

        public void ShowShovePreview(GridCoord actor, GridCoord target, TacticalShovePreview preview)
        {
            EnsureLayers();
            _shoveLayer.ActiveCount = 0;

            if (preview == null)
            {
                TrimLayer(_shoveLayer);
                return;
            }

            Color previewColor;
            if (!preview.CanExecute)
            {
                previewColor = ShoveInvalidRed;
            }
            else if (preview.HitsWallOrBounds || !string.IsNullOrEmpty(preview.CollidingUnitId))
            {
                previewColor = ShoveCrushCrimson;
            }
            else if (preview.PushesOffLedge)
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
            float destY = GetTileElevation(highlightTile.X, highlightTile.Y) + 0.025f + shoveLayerOffset;
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

            TrimLayer(_shoveLayer);
        }

        public void ClearShovePreview()
        {
            EnsureLayers();
            HideLayer(_shoveLayer);
        }

        public void ShowEnemyIntentArcs(IReadOnlyList<EnemyIntentArcData> arcs)
        {
            EnsureLayers();
            _activeIntentArcCount = 0;

            if (arcs != null)
            {
                for (int i = 0; i < arcs.Count; i++)
                {
                    EnemyIntentArcData arc = arcs[i];
                    if (arc.From == arc.To)
                    {
                        continue;
                    }

                    LineRenderer lr = GetOrCreateIntentArc(_activeIntentArcCount++);
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

                    Color col = arc.IsSpell ? IntentSpellColor : IntentMeleeColor;
                    lr.sharedMaterial = GetOrCreateIntentArcMaterial(arc.IsSpell);
                    lr.startColor = col;
                    lr.endColor = col;
                    lr.startWidth = arc.IsSpell ? 0.130f : 0.115f;
                    lr.endWidth = arc.IsSpell ? 0.095f : 0.085f;
                    lr.textureMode = LineTextureMode.Tile;

                    float approxDist = Vector3.Distance(p0, p2);
                    lr.textureScale = new Vector2(Mathf.Max(1.5f, approxDist * 1.4f), 1f);
                    lr.positionCount = IntentArcSegments + 1;
                    lr.SetPositions(_arcPointScratch);
                    lr.gameObject.SetActive(true);
                }
            }

            for (int i = _activeIntentArcCount; i < _intentArcPool.Count; i++)
            {
                _intentArcPool[i].gameObject.SetActive(false);
            }
        }

        public void ClearEnemyIntentArcs()
        {
            _activeIntentArcCount = 0;
            for (int i = 0; i < _intentArcPool.Count; i++)
            {
                _intentArcPool[i].gameObject.SetActive(false);
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
                    PlaceQuadWithBaseColor(_exploreHoverLayer, step.X + 0.5f, y, -(step.Y + 0.5f), 0.34f, 0.34f, baseColor, fadedColor);
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

        public void SetCombatHoverPath(IReadOnlyList<GridCoord> path, bool provokesAoO)
        {
            EnsureLayers();
            _combatHoverLayer.ActiveCount = 0;

            if (path != null)
            {
                Color color = provokesAoO ? HoverPathAooColor : HoverPathSafeColor;
                for (int i = 0; i < path.Count; i++)
                {
                    GridCoord step = path[i];
                    float y = GetTileElevation(step.X, step.Y) + 0.025f + 0.010f;
                    PlaceQuad(_combatHoverLayer, step.X + 0.5f, y, -(step.Y + 0.5f), 0.42f, 0.42f, color);
                }
            }

            TrimLayer(_combatHoverLayer);
        }

        public void SetLosPreview(LosResult los, GridCoord from, GridCoord to)
        {
            EnsureLayers();
            _losLayer.ActiveCount = 0;

            if (los != null)
            {
                Color rayColor = los.Kind == LosKind.Clear
                    ? LosClearColor
                    : (los.Kind == LosKind.PartialCover ? LosPartialColor : LosBlockedColor);

                IReadOnlyList<GridCoord> cells = los.RayCells;
                if (cells != null)
                {
                    for (int i = 0; i < cells.Count; i++)
                    {
                        GridCoord c = cells[i];
                        if (c == from)
                        {
                            continue;
                        }

                        float y = GetTileElevation(c.X, c.Y) + 0.025f + 0.012f;
                        PlaceQuad(_losLayer, c.X + 0.5f, y, -(c.Y + 0.5f), 0.34f, 0.34f, rayColor);
                    }
                }

                if (los.BlockedBy.HasValue)
                {
                    GridCoord blocker = los.BlockedBy.Value;
                    float blockerY = GetTileElevation(blocker.X, blocker.Y) + 0.025f + 0.013f;
                    PlaceQuad(_losLayer, blocker.X + 0.5f, blockerY, -(blocker.Y + 0.5f), 0.80f, 0.80f, LosBlockedColor);
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
            const float thickness = 0.10f;
            const float span = 0.94f;
            float offset = (span - thickness) * 0.5f;
            float cx = coord.X + 0.5f;
            float cz = -(coord.Y + 0.5f);
            float y = GetTileElevation(coord.X, coord.Y) + 0.025f + layerOffset;

            PlaceQuad(layer, cx, y, cz + offset, span, thickness, color);
            PlaceQuad(layer, cx, y, cz - offset, span, thickness, color);
            PlaceQuad(layer, cx - offset, y, cz, thickness, span - thickness * 2f, color);
            PlaceQuad(layer, cx + offset, y, cz, thickness, span - thickness * 2f, color);
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
            if (_intentDashTexture == null)
            {
                _intentDashTexture = BuildDashedLineTexture();
            }

            if (isSpell)
            {
                if (_intentSpellMaterial == null)
                {
                    _intentSpellMaterial = CreateTransparentOverlayMaterial("IntentArcSpell", _intentDashTexture, IntentSpellColor);
                }
                return _intentSpellMaterial;
            }

            if (_intentMeleeMaterial == null)
            {
                _intentMeleeMaterial = CreateTransparentOverlayMaterial("IntentArcMelee", _intentDashTexture, IntentMeleeColor);
            }
            return _intentMeleeMaterial;
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
            Color32 hairline = new Color32(255, 255, 255, 115);

            const int armLen = 14;
            const int armThick = 3;

            for (int y = 0; y < size; y++)
            {
                bool inCornerY = y < armLen || y >= size - armLen;
                bool inEdgeBandY = y < armThick || y >= size - armThick;
                bool onHairlineY = y == 0 || y == size - 1;

                for (int x = 0; x < size; x++)
                {
                    bool inCornerX = x < armLen || x >= size - armLen;
                    bool inEdgeBandX = x < armThick || x >= size - armThick;
                    bool onHairlineX = x == 0 || x == size - 1;

                    bool isBracket = (inCornerX && inEdgeBandY) || (inCornerY && inEdgeBandX);
                    if (isBracket)
                    {
                        pixels[y * size + x] = bracket;
                    }
                    else if (onHairlineX || onHairlineY)
                    {
                        pixels[y * size + x] = hairline;
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

        private static Texture2D BuildDashedLineTexture()
        {
            const int w = 32;
            const int h = 8;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "IntentDashTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[w * h];
            Color32 dash = new Color32(255, 255, 255, 255);
            Color32 gap = new Color32(255, 255, 255, 28);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isDash = x < 20 && y >= 1 && y <= h - 2;
                    pixels[y * w + x] = isDash ? dash : gap;
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
