using System.Collections.Generic;
using Realmz.Data;
using RealmzCore.Combat;
using RealmzCore.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Realmz.World
{
    /// <summary>
    /// Pooled translucent tactical and exploration grid overlays using MaterialPropertyBlock
    /// over BywaterAssetCatalog.overlayBaseMaterial so zero material instances are leaked.
    /// </summary>
    public sealed class BywaterGridOverlays : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Color ReachableNormalColor = new Color(0.15f, 0.78f, 0.72f, 0.34f);
        private static readonly Color ReachableAooColor = new Color(0.95f, 0.42f, 0.12f, 0.48f);
        private static readonly Color ThreatenedColor = new Color(0.95f, 0.25f, 0.18f, 0.28f);
        private static readonly Color HoverPathSafeColor = new Color(1.00f, 0.95f, 0.72f, 0.76f);
        private static readonly Color HoverPathAooColor = new Color(0.98f, 0.25f, 0.18f, 0.80f);
        private static readonly Color ExploreReachableColor = new Color(0.22f, 0.86f, 0.64f, 0.56f);
        private static readonly Color ExploreBlockedColor = new Color(0.88f, 0.24f, 0.22f, 0.56f);
        private static readonly Color LosClearColor = new Color(0.18f, 0.85f, 0.48f, 0.72f);
        private static readonly Color LosPartialColor = new Color(0.96f, 0.72f, 0.16f, 0.74f);
        private static readonly Color LosBlockedColor = new Color(0.95f, 0.22f, 0.22f, 0.78f);
        private static readonly Color AoeBeneficialColor = new Color(0.22f, 0.74f, 0.97f, 0.48f);
        private static readonly Color AoeHostileColor = new Color(0.96f, 0.32f, 0.18f, 0.52f);
        private static readonly Color AoeBlockedColor = new Color(0.22f, 0.22f, 0.25f, 0.45f);
        private static readonly Color FlankGoldColor = new Color(0.98f, 0.80f, 0.15f, 0.60f);

        private sealed class OverlayLayer
        {
            public readonly Transform Container;
            public readonly List<MeshRenderer> Quads = new List<MeshRenderer>(32);
            public int ActiveCount;

            public OverlayLayer(Transform parent, string name)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                Container = go.transform;
            }
        }

        private BywaterAssetCatalog _catalog;
        private MaterialPropertyBlock _propBlock;

        private OverlayLayer _combatReachableLayer;
        private OverlayLayer _combatThreatenedLayer;
        private OverlayLayer _spellAoeLayer;
        private OverlayLayer _exploreHoverLayer;
        private OverlayLayer _combatHoverLayer;
        private OverlayLayer _losLayer;
        private OverlayLayer _flankLayer;

        public void Initialize(BywaterAssetCatalog catalog)
        {
            _catalog = catalog;
            EnsureLayers();
        }

        public void ClearAll()
        {
            EnsureLayers();
            HideLayer(_combatReachableLayer);
            HideLayer(_combatThreatenedLayer);
            HideLayer(_spellAoeLayer);
            HideLayer(_exploreHoverLayer);
            HideLayer(_combatHoverLayer);
            HideLayer(_losLayer);
            HideLayer(_flankLayer);
        }

        public void SetExplorationHoverPath(IReadOnlyList<GridCoord> path, GridCoord hoverCoord, bool reachable)
        {
            EnsureLayers();
            _exploreHoverLayer.ActiveCount = 0;

            Color color = reachable ? ExploreReachableColor : ExploreBlockedColor;

            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    GridCoord step = path[i];
                    if (step == hoverCoord)
                    {
                        continue;
                    }

                    PlaceQuad(_exploreHoverLayer, step.X, 0.030f, -step.Y, 0.38f, 0.38f, color);
                }
            }

            if (hoverCoord.X >= 0 && hoverCoord.Y >= 0)
            {
                PlaceQuad(_exploreHoverLayer, hoverCoord.X, 0.031f, -hoverCoord.Y, 0.90f, 0.90f, color);
            }

            TrimLayer(_exploreHoverLayer);
        }

        public void SetCombatReachable(IReadOnlyList<ReachableTileInfo> tiles)
        {
            EnsureLayers();
            _combatReachableLayer.ActiveCount = 0;

            if (tiles != null)
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    ReachableTileInfo tile = tiles[i];
                    Color color = tile.ProvokesAoO ? ReachableAooColor : ReachableNormalColor;
                    PlaceQuad(_combatReachableLayer, tile.Coord.X, 0.022f, -tile.Coord.Y, 0.94f, 0.94f, color);
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
                for (int i = 0; i < tiles.Count; i++)
                {
                    GridCoord tile = tiles[i];
                    PlaceQuad(_combatThreatenedLayer, tile.X, 0.024f, -tile.Y, 0.90f, 0.90f, ThreatenedColor);
                }
            }

            TrimLayer(_combatThreatenedLayer);
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
                    PlaceQuad(_combatHoverLayer, step.X, 0.032f, -step.Y, 0.42f, 0.42f, color);
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

                        PlaceQuad(_losLayer, c.X, 0.034f, -c.Y, 0.34f, 0.34f, rayColor);
                    }
                }

                if (los.BlockedBy.HasValue)
                {
                    GridCoord blocker = los.BlockedBy.Value;
                    PlaceQuad(_losLayer, blocker.X, 0.035f, -blocker.Y, 0.80f, 0.80f, LosBlockedColor);
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

                        PlaceQuad(_spellAoeLayer, tile.X, 0.027f, -tile.Y, 0.92f, 0.92f, color);
                    }
                }

                IReadOnlyList<GridCoord> blocked = footprint.BlockedTiles;
                if (blocked != null)
                {
                    for (int i = 0; i < blocked.Count; i++)
                    {
                        GridCoord tile = blocked[i];
                        PlaceQuad(_spellAoeLayer, tile.X, 0.028f, -tile.Y, 0.90f, 0.90f, AoeBlockedColor);
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
                PlaceTileBorder(_flankLayer, targetPos.Value, 0.037f, FlankGoldColor);
            }

            if (flankingAllies != null)
            {
                for (int i = 0; i < flankingAllies.Count; i++)
                {
                    PlaceTileBorder(_flankLayer, flankingAllies[i], 0.037f, FlankGoldColor);
                }
            }

            TrimLayer(_flankLayer);
        }

        private void PlaceTileBorder(OverlayLayer layer, GridCoord coord, float y, Color color)
        {
            const float thickness = 0.10f;
            const float span = 0.94f;
            float offset = (span - thickness) * 0.5f;

            PlaceQuad(layer, coord.X, y, -coord.Y + offset, span, thickness, color);
            PlaceQuad(layer, coord.X, y, -coord.Y - offset, span, thickness, color);
            PlaceQuad(layer, coord.X - offset, y, -coord.Y, thickness, span - thickness * 2f, color);
            PlaceQuad(layer, coord.X + offset, y, -coord.Y, thickness, span - thickness * 2f, color);
        }

        private void PlaceQuad(OverlayLayer layer, float x, float y, float z, float scaleX, float scaleZ, Color color)
        {
            int idx = layer.ActiveCount++;
            MeshRenderer quad = GetOrCreateQuad(layer, idx);

            quad.transform.position = new Vector3(x, y, z);
            quad.transform.localScale = new Vector3(scaleX, scaleZ, 1f);

            if (_catalog != null && _catalog.overlayBaseMaterial != null)
            {
                quad.sharedMaterial = _catalog.overlayBaseMaterial;
            }

            _propBlock ??= new MaterialPropertyBlock();
            _propBlock.Clear();
            _propBlock.SetColor(BaseColorId, color);
            quad.SetPropertyBlock(_propBlock);
            quad.gameObject.SetActive(true);
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
            if (_combatReachableLayer == null) _combatReachableLayer = new OverlayLayer(transform, "CombatReachable");
            if (_combatThreatenedLayer == null) _combatThreatenedLayer = new OverlayLayer(transform, "CombatThreatened");
            if (_spellAoeLayer == null) _spellAoeLayer = new OverlayLayer(transform, "SpellAoeFootprint");
            if (_exploreHoverLayer == null) _exploreHoverLayer = new OverlayLayer(transform, "ExplorationHoverPath");
            if (_combatHoverLayer == null) _combatHoverLayer = new OverlayLayer(transform, "CombatHoverPath");
            if (_losLayer == null) _losLayer = new OverlayLayer(transform, "LosPreview");
            if (_flankLayer == null) _flankLayer = new OverlayLayer(transform, "FlankIndicator");
        }
    }
}
