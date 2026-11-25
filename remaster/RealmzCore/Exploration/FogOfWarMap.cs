using System;
using RealmzCore.Data;

namespace RealmzCore.Exploration
{
    public enum FogVisibilityState : byte
    {
        Unseen = 0,
        Explored = 1,
        CurrentlyVisible = 2
    }

    public enum WeatherState
    {
        Clear = 0,
        LightRain = 1,
        Storm = 2
    }

    public sealed class FogOfWarMap
    {
        private readonly FogVisibilityState[] _states;

        public FogOfWarMap(string mapId, int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            MapId = mapId ?? string.Empty;
            Width = width;
            Height = height;
            _states = new FogVisibilityState[width * height];
        }

        public string MapId { get; }
        public int Width { get; }
        public int Height { get; }
        public int CurrentVisionRadius { get; private set; }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public FogVisibilityState GetState(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return FogVisibilityState.Unseen;
            }

            return _states[y * Width + x];
        }

        public bool IsExplored(int x, int y)
        {
            return GetState(x, y) != FogVisibilityState.Unseen;
        }

        public bool IsVisible(int x, int y)
        {
            return GetState(x, y) == FogVisibilityState.CurrentlyVisible;
        }

        public void RecomputeVisibility(TileMapData map, GridCoord origin, int visionRadius)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            int clampedRadius = Math.Max(0, visionRadius);

            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == FogVisibilityState.CurrentlyVisible)
                {
                    _states[i] = FogVisibilityState.Explored;
                }
            }

            CurrentVisionRadius = clampedRadius;

            if (!InBounds(origin.X, origin.Y))
            {
                return;
            }

            _states[origin.Y * Width + origin.X] = FogVisibilityState.CurrentlyVisible;
            if (clampedRadius == 0)
            {
                return;
            }

            int radiusSq = clampedRadius * clampedRadius;
            int minX = Math.Max(0, origin.X - clampedRadius);
            int maxX = Math.Min(Width - 1, origin.X + clampedRadius);
            int minY = Math.Max(0, origin.Y - clampedRadius);
            int maxY = Math.Min(Height - 1, origin.Y + clampedRadius);

            for (int ty = minY; ty <= maxY; ty++)
            {
                int dy = ty - origin.Y;
                for (int tx = minX; tx <= maxX; tx++)
                {
                    int dx = tx - origin.X;
                    if (dx * dx + dy * dy > radiusSq)
                    {
                        continue;
                    }

                    if (tx == origin.X && ty == origin.Y)
                    {
                        continue;
                    }

                    TraceVisibilityRay(map, origin.X, origin.Y, tx, ty);
                }
            }
        }

        private void TraceVisibilityRay(TileMapData map, int x0, int y0, int x1, int y1)
        {
            int adx = Math.Abs(x1 - x0);
            int ady = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = adx - ady;

            int cx = x0;
            int cy = y0;

            while (cx != x1 || cy != y1)
            {
                int prevX = cx;
                int prevY = cy;
                int e2 = 2 * err;
                bool stepX = e2 > -ady;
                bool stepY = e2 < adx;

                if (stepX)
                {
                    err -= ady;
                    cx += sx;
                }

                if (stepY)
                {
                    err += adx;
                    cy += sy;
                }

                // Prevent diagonal corner clipping when both orthogonal neighbors block LOS.
                if (stepX && stepY)
                {
                    if (map.BlocksLineOfSight(prevX + sx, prevY) &&
                        map.BlocksLineOfSight(prevX, prevY + sy))
                    {
                        break;
                    }
                }

                if (!InBounds(cx, cy))
                {
                    break;
                }

                _states[cy * Width + cx] = FogVisibilityState.CurrentlyVisible;

                if (cx == x1 && cy == y1)
                {
                    break;
                }

                if (map.BlocksLineOfSight(cx, cy))
                {
                    break;
                }
            }
        }

        public byte[] ExportVisibilityStates()
        {
            byte[] export = new byte[_states.Length];
            for (int i = 0; i < _states.Length; i++)
            {
                export[i] = (byte)_states[i];
            }

            return export;
        }

        public string ExportExploredBitsetBase64()
        {
            int byteCount = (_states.Length + 7) / 8;
            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] != FogVisibilityState.Unseen)
                {
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
            }

            return Convert.ToBase64String(bytes);
        }

        public void ImportExploredBitsetBase64(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return;
            }

            byte[] data;
            try
            {
                data = Convert.FromBase64String(base64!.Trim());
            }
            catch (FormatException)
            {
                return;
            }

            for (int i = 0; i < _states.Length; i++)
            {
                int byteIndex = i >> 3;
                if (byteIndex >= data.Length)
                {
                    break;
                }

                if ((data[byteIndex] & (1 << (i & 7))) != 0 &&
                    _states[i] == FogVisibilityState.Unseen)
                {
                    _states[i] = FogVisibilityState.Explored;
                }
            }
        }
    }
}
