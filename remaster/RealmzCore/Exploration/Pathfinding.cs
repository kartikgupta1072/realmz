using System;
using System.Collections.Generic;
using RealmzCore.Data;

namespace RealmzCore.Exploration
{
    public sealed class PathResult
    {
        public PathResult(bool found, int totalCost, IReadOnlyList<GridCoord> steps)
        {
            Found = found;
            TotalCost = totalCost;
            Steps = steps ?? Array.Empty<GridCoord>();
        }

        public bool Found { get; }
        public int TotalCost { get; }
        public IReadOnlyList<GridCoord> Steps { get; }
    }

    public sealed class ReachableNode
    {
        public ReachableNode(GridCoord coord, int totalCost, IReadOnlyList<GridCoord> path)
        {
            Coord = coord;
            TotalCost = totalCost;
            Path = path ?? Array.Empty<GridCoord>();
        }

        public GridCoord Coord { get; }
        public int TotalCost { get; }
        public IReadOnlyList<GridCoord> Path { get; }
    }

    public sealed class AStarPathfinder
    {
        private static readonly int[] Dx8 = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy8 = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private static readonly bool[] IsDiag8 = { false, false, false, false, true, true, true, true };

        [ThreadStatic]
        private static AStarPathfinder? t_shared;

        private int[] _gScore;
        private int[] _parent;
        private int[] _visitStamp;
        private int[] _closedStamp;
        private int _stamp;

        private OpenEntry[] _heap;
        private int _heapCount;

        private readonly List<GridCoord> _reconstructBuffer;
        private readonly Dictionary<GridCoord, ReachableNode> _reachableCache;

        public AStarPathfinder()
            : this(90, 90)
        {
        }

        public AStarPathfinder(int initialWidth, int initialHeight)
            : this(ComputeInitialCapacity(initialWidth, initialHeight))
        {
        }

        public AStarPathfinder(int initialCapacity)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _gScore = new int[initialCapacity];
            _parent = new int[initialCapacity];
            _visitStamp = new int[initialCapacity];
            _closedStamp = new int[initialCapacity];
            _stamp = 0;

            int initialHeapCapacity = Math.Max(64, Math.Min(initialCapacity, 16384));
            _heap = new OpenEntry[initialHeapCapacity];
            _heapCount = 0;

            _reconstructBuffer = new List<GridCoord>(128);
            _reachableCache = new Dictionary<GridCoord, ReachableNode>(256);
        }

        public int BufferCapacity => _gScore.Length;

        public long SearchCount { get; private set; }

        private static AStarPathfinder Shared => t_shared ??= new AStarPathfinder();

        public static int GetStepCost(bool isDiagonal, int tileMoveCost, bool useOctileCost = true)
        {
            int clamped = tileMoveCost < 1 ? 1 : tileMoveCost;
            if (!useOctileCost)
            {
                return clamped;
            }

            return (isDiagonal ? 14 : 10) * clamped;
        }

        public static PathResult FindPath(
            TileMapData map,
            GridCoord start,
            GridCoord goal,
            bool allowDiagonal = true,
            bool hasBoat = false,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            return Shared.FindPathCore(
                map,
                start,
                goal,
                allowDiagonal: allowDiagonal,
                hasBoat: hasBoat,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        public static PathResult FindPath(
            int width,
            int height,
            GridCoord start,
            GridCoord goal,
            Func<int, int, bool> isPassable,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            return Shared.FindPathCore(
                width,
                height,
                start,
                goal,
                isPassable,
                getMoveCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        public static Dictionary<GridCoord, ReachableNode> FindReachableTiles(
            int width,
            int height,
            GridCoord start,
            int maxCost,
            Func<int, int, bool> isPassable,
            Func<int, int, bool> canStopOn,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            bool useOctileCost = false)
        {
            return Shared.FindReachableTilesCore(
                width,
                height,
                start,
                maxCost,
                isPassable,
                canStopOn,
                getMoveCost,
                allowDiagonal: allowDiagonal,
                useOctileCost: useOctileCost);
        }

        public bool FindPathSteps(
            TileMapData map,
            GridCoord start,
            GridCoord goal,
            List<GridCoord> destinationSteps,
            out int totalCost,
            bool allowDiagonal = true,
            bool hasBoat = false,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (destinationSteps == null)
            {
                throw new ArgumentNullException(nameof(destinationSteps));
            }

            return FindPathStepsCore(
                map.Width,
                map.Height,
                start,
                goal,
                map,
                hasBoat,
                isPassable: null,
                getMoveCost: null,
                destinationSteps,
                out totalCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        public bool FindPathSteps(
            int width,
            int height,
            GridCoord start,
            GridCoord goal,
            Func<int, int, bool> isPassable,
            Func<int, int, int> getMoveCost,
            List<GridCoord> destinationSteps,
            out int totalCost,
            bool allowDiagonal = true,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (isPassable == null)
            {
                throw new ArgumentNullException(nameof(isPassable));
            }

            if (getMoveCost == null)
            {
                throw new ArgumentNullException(nameof(getMoveCost));
            }

            if (destinationSteps == null)
            {
                throw new ArgumentNullException(nameof(destinationSteps));
            }

            return FindPathStepsCore(
                width,
                height,
                start,
                goal,
                map: null,
                hasBoat: false,
                isPassable,
                getMoveCost,
                destinationSteps,
                out totalCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        internal PathResult FindPathCore(
            TileMapData map,
            GridCoord start,
            GridCoord goal,
            bool allowDiagonal = true,
            bool hasBoat = false,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            bool found = FindPathStepsCore(
                map.Width,
                map.Height,
                start,
                goal,
                map,
                hasBoat,
                isPassable: null,
                getMoveCost: null,
                _reconstructBuffer,
                out int totalCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);

            if (!found || _reconstructBuffer.Count == 0)
            {
                return new PathResult(found, totalCost, Array.Empty<GridCoord>());
            }

            return new PathResult(true, totalCost, _reconstructBuffer.ToArray());
        }

        internal PathResult FindPathCore(
            int width,
            int height,
            GridCoord start,
            GridCoord goal,
            Func<int, int, bool> isPassable,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (isPassable == null)
            {
                throw new ArgumentNullException(nameof(isPassable));
            }

            if (getMoveCost == null)
            {
                throw new ArgumentNullException(nameof(getMoveCost));
            }

            bool found = FindPathStepsCore(
                width,
                height,
                start,
                goal,
                map: null,
                hasBoat: false,
                isPassable,
                getMoveCost,
                _reconstructBuffer,
                out int totalCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);

            if (!found || _reconstructBuffer.Count == 0)
            {
                return new PathResult(found, totalCost, Array.Empty<GridCoord>());
            }

            return new PathResult(true, totalCost, _reconstructBuffer.ToArray());
        }

        internal Dictionary<GridCoord, ReachableNode> FindReachableTilesCore(
            int width,
            int height,
            GridCoord start,
            int maxCost,
            Func<int, int, bool> isPassable,
            Func<int, int, bool> canStopOn,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            bool useOctileCost = false)
        {
            if (isPassable == null)
            {
                throw new ArgumentNullException(nameof(isPassable));
            }

            if (canStopOn == null)
            {
                throw new ArgumentNullException(nameof(canStopOn));
            }

            if (getMoveCost == null)
            {
                throw new ArgumentNullException(nameof(getMoveCost));
            }

            SearchCount++;
            _reachableCache.Clear();

            if (width <= 0 || height <= 0 || maxCost <= 0 || !InBounds(start, width, height))
            {
                return new Dictionary<GridCoord, ReachableNode>();
            }

            int totalCells = ComputeTotalCells(width, height);
            BeginSearch(totalCells);

            int startIdx = ToIndex(start.X, start.Y, width);
            _visitStamp[startIdx] = _stamp;
            _gScore[startIdx] = 0;
            _parent[startIdx] = -1;

            PushHeap(new OpenEntry(startIdx, 0, 0, start.X, start.Y));
            int dirCount = allowDiagonal ? 8 : 4;

            while (_heapCount > 0)
            {
                OpenEntry current = PopHeap();

                if (_closedStamp[current.Index] == _stamp)
                {
                    continue;
                }

                _closedStamp[current.Index] = _stamp;
                int cx = current.X;
                int cy = current.Y;
                int cg = _gScore[current.Index];

                if (current.Index != startIdx && canStopOn(cx, cy))
                {
                    GridCoord coord = new GridCoord(cx, cy);
                    ReconstructPath(startIdx, current.Index, width, _reconstructBuffer);
                    _reachableCache[coord] = new ReachableNode(coord, cg, _reconstructBuffer.ToArray());
                }

                for (int d = 0; d < dirCount; d++)
                {
                    int nx = cx + Dx8[d];
                    int ny = cy + Dy8[d];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int nIdx = ToIndex(nx, ny, width);
                    if (_closedStamp[nIdx] == _stamp || !isPassable(nx, ny))
                    {
                        continue;
                    }

                    bool isDiag = IsDiag8[d];
                    if (isDiag && !isPassable(cx + Dx8[d], cy) && !isPassable(cx, cy + Dy8[d]))
                    {
                        continue;
                    }

                    int stepCost = GetStepCost(isDiag, getMoveCost(nx, ny), useOctileCost);
                    long nextCostLong = (long)cg + stepCost;
                    if (nextCostLong > maxCost)
                    {
                        continue;
                    }

                    int nextCost = (int)nextCostLong;
                    int currentBest = _visitStamp[nIdx] == _stamp ? _gScore[nIdx] : int.MaxValue;
                    if (nextCost < currentBest)
                    {
                        _visitStamp[nIdx] = _stamp;
                        _gScore[nIdx] = nextCost;
                        _parent[nIdx] = current.Index;
                        int tieH = isDiag ? 1 : 0;
                        PushHeap(new OpenEntry(nIdx, nextCost, tieH, nx, ny));
                    }
                }
            }

            return new Dictionary<GridCoord, ReachableNode>(_reachableCache);
        }

        private bool FindPathStepsCore(
            int width,
            int height,
            GridCoord start,
            GridCoord goal,
            TileMapData? map,
            bool hasBoat,
            Func<int, int, bool>? isPassable,
            Func<int, int, int>? getMoveCost,
            List<GridCoord> destinationSteps,
            out int totalCost,
            bool allowDiagonal,
            int maxCost,
            bool useOctileCost)
        {
            destinationSteps.Clear();
            totalCost = 0;
            SearchCount++;

            if (!InBounds(start, width, height) || !InBounds(goal, width, height))
            {
                return false;
            }

            if (start == goal)
            {
                return true;
            }

            bool goalPassable = map != null ? map.IsPassable(goal.X, goal.Y, hasBoat) : isPassable!(goal.X, goal.Y);
            if (!goalPassable)
            {
                return false;
            }

            int totalCells = ComputeTotalCells(width, height);
            BeginSearch(totalCells);

            int startIdx = ToIndex(start.X, start.Y, width);
            int goalIdx = ToIndex(goal.X, goal.Y, width);
            _visitStamp[startIdx] = _stamp;
            _gScore[startIdx] = 0;
            _parent[startIdx] = -1;

            int startH = Heuristic(start, goal, allowDiagonal, useOctileCost);
            PushHeap(new OpenEntry(startIdx, startH, startH, start.X, start.Y));

            int dirCount = allowDiagonal ? 8 : 4;

            while (_heapCount > 0)
            {
                OpenEntry current = PopHeap();

                if (_closedStamp[current.Index] == _stamp)
                {
                    continue;
                }

                if (current.Index == goalIdx)
                {
                    totalCost = _gScore[goalIdx];
                    ReconstructPath(startIdx, goalIdx, width, destinationSteps);
                    return true;
                }

                _closedStamp[current.Index] = _stamp;
                int cx = current.X;
                int cy = current.Y;
                int currentG = _gScore[current.Index];

                for (int d = 0; d < dirCount; d++)
                {
                    int nx = cx + Dx8[d];
                    int ny = cy + Dy8[d];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int nIdx = ToIndex(nx, ny, width);
                    if (_closedStamp[nIdx] == _stamp)
                    {
                        continue;
                    }

                    bool neighborPassable = map != null ? map.IsPassable(nx, ny, hasBoat) : isPassable!(nx, ny);
                    if (!neighborPassable)
                    {
                        continue;
                    }

                    bool isDiag = IsDiag8[d];
                    if (isDiag)
                    {
                        bool adjXPassable = map != null ? map.IsPassable(cx + Dx8[d], cy, hasBoat) : isPassable!(cx + Dx8[d], cy);
                        if (!adjXPassable)
                        {
                            bool adjYPassable = map != null ? map.IsPassable(cx, cy + Dy8[d], hasBoat) : isPassable!(cx, cy + Dy8[d]);
                            if (!adjYPassable)
                            {
                                continue;
                            }
                        }
                    }

                    int tileMoveCost = map != null ? map.GetMoveCost(nx, ny) : getMoveCost!(nx, ny);
                    int stepCost = GetStepCost(isDiag, tileMoveCost, useOctileCost);
                    long tentativeLong = (long)currentG + stepCost;
                    if (tentativeLong > maxCost)
                    {
                        continue;
                    }

                    int tentativeG = (int)tentativeLong;
                    int currentBestG = _visitStamp[nIdx] == _stamp ? _gScore[nIdx] : int.MaxValue;
                    if (tentativeG < currentBestG)
                    {
                        _visitStamp[nIdx] = _stamp;
                        _gScore[nIdx] = tentativeG;
                        _parent[nIdx] = current.Index;
                        int h = Heuristic(new GridCoord(nx, ny), goal, allowDiagonal, useOctileCost);
                        int f = tentativeG + h;
                        PushHeap(new OpenEntry(nIdx, f, h, nx, ny));
                    }
                }
            }

            return false;
        }

        private void BeginSearch(int requiredCells)
        {
            if (requiredCells > _gScore.Length)
            {
                int doubled = _gScore.Length <= int.MaxValue / 2 ? _gScore.Length * 2 : int.MaxValue;
                int newCapacity = Math.Max(requiredCells, doubled);
                _gScore = new int[newCapacity];
                _parent = new int[newCapacity];
                _visitStamp = new int[newCapacity];
                _closedStamp = new int[newCapacity];
                _stamp = 1;
            }
            else if (_stamp == int.MaxValue)
            {
                Array.Clear(_visitStamp, 0, _visitStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _stamp = 1;
            }
            else
            {
                _stamp++;
            }

            _heapCount = 0;
        }

        private void PushHeap(OpenEntry entry)
        {
            if (_heapCount == _heap.Length)
            {
                int newCapacity = _heap.Length == 0 ? 64 : _heap.Length * 2;
                Array.Resize(ref _heap, newCapacity);
            }

            int idx = _heapCount++;
            while (idx > 0)
            {
                int parentIdx = (idx - 1) >> 1;
                if (CompareOpenEntries(entry, _heap[parentIdx]) >= 0)
                {
                    break;
                }

                _heap[idx] = _heap[parentIdx];
                idx = parentIdx;
            }

            _heap[idx] = entry;
        }

        private OpenEntry PopHeap()
        {
            OpenEntry min = _heap[0];
            int lastIdx = --_heapCount;
            if (lastIdx > 0)
            {
                OpenEntry last = _heap[lastIdx];
                int idx = 0;
                int half = lastIdx >> 1;
                while (idx < half)
                {
                    int left = (idx << 1) + 1;
                    int right = left + 1;
                    int smallest = left;
                    if (right < lastIdx && CompareOpenEntries(_heap[right], _heap[left]) < 0)
                    {
                        smallest = right;
                    }

                    if (CompareOpenEntries(last, _heap[smallest]) <= 0)
                    {
                        break;
                    }

                    _heap[idx] = _heap[smallest];
                    idx = smallest;
                }

                _heap[idx] = last;
            }

            return min;
        }

        private void ReconstructPath(int startIdx, int goalIdx, int width, List<GridCoord> destination)
        {
            destination.Clear();
            int cursor = goalIdx;
            while (cursor != -1 && cursor != startIdx)
            {
                int x = cursor % width;
                int y = cursor / width;
                destination.Add(new GridCoord(x, y));
                cursor = _parent[cursor];
            }

            destination.Reverse();
        }

        private static int ComputeInitialCapacity(int initialWidth, int initialHeight)
        {
            if (initialWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialWidth));
            }

            if (initialHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialHeight));
            }

            long capacityLong = (long)initialWidth * initialHeight;
            if (capacityLong > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(initialWidth), "Initial grid capacity exceeds Int32.MaxValue.");
            }

            return (int)capacityLong;
        }

        private static int ComputeTotalCells(int width, int height)
        {
            long totalCellsLong = (long)width * height;
            if (totalCellsLong > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Grid cell count exceeds Int32.MaxValue.");
            }

            return (int)totalCellsLong;
        }

        private static int Heuristic(GridCoord a, GridCoord b, bool allowDiagonal, bool useOctileCost)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            if (!useOctileCost)
            {
                return allowDiagonal ? Math.Max(dx, dy) : (dx + dy);
            }

            if (!allowDiagonal)
            {
                return 10 * (dx + dy);
            }

            int min = Math.Min(dx, dy);
            int max = Math.Max(dx, dy);
            return (14 * min) + (10 * (max - min));
        }

        private static bool InBounds(GridCoord coord, int width, int height)
        {
            return coord.X >= 0 && coord.X < width && coord.Y >= 0 && coord.Y < height;
        }

        private static int ToIndex(int x, int y, int width)
        {
            return (y * width) + x;
        }

        private static int CompareOpenEntries(in OpenEntry a, in OpenEntry b)
        {
            int cmp = a.F.CompareTo(b.F);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.H.CompareTo(b.H);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.Y.CompareTo(b.Y);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.X.CompareTo(b.X);
            if (cmp != 0)
            {
                return cmp;
            }

            return a.Index.CompareTo(b.Index);
        }

        private readonly struct OpenEntry
        {
            public OpenEntry(int index, int f, int h, int x, int y)
            {
                Index = index;
                F = f;
                H = h;
                X = x;
                Y = y;
            }

            public int Index { get; }
            public int F { get; }
            public int H { get; }
            public int X { get; }
            public int Y { get; }
        }
    }

    public static class AStarPathfinderExtensions
    {
        public static PathResult FindPath(
            this AStarPathfinder pathfinder,
            TileMapData map,
            GridCoord start,
            GridCoord goal,
            bool allowDiagonal = true,
            bool hasBoat = false,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (pathfinder == null)
            {
                throw new ArgumentNullException(nameof(pathfinder));
            }

            return pathfinder.FindPathCore(
                map,
                start,
                goal,
                allowDiagonal: allowDiagonal,
                hasBoat: hasBoat,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        public static PathResult FindPath(
            this AStarPathfinder pathfinder,
            int width,
            int height,
            GridCoord start,
            GridCoord goal,
            Func<int, int, bool> isPassable,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            int maxCost = int.MaxValue,
            bool useOctileCost = true)
        {
            if (pathfinder == null)
            {
                throw new ArgumentNullException(nameof(pathfinder));
            }

            return pathfinder.FindPathCore(
                width,
                height,
                start,
                goal,
                isPassable,
                getMoveCost,
                allowDiagonal: allowDiagonal,
                maxCost: maxCost,
                useOctileCost: useOctileCost);
        }

        public static Dictionary<GridCoord, ReachableNode> FindReachableTiles(
            this AStarPathfinder pathfinder,
            int width,
            int height,
            GridCoord start,
            int maxCost,
            Func<int, int, bool> isPassable,
            Func<int, int, bool> canStopOn,
            Func<int, int, int> getMoveCost,
            bool allowDiagonal = true,
            bool useOctileCost = false)
        {
            if (pathfinder == null)
            {
                throw new ArgumentNullException(nameof(pathfinder));
            }

            return pathfinder.FindReachableTilesCore(
                width,
                height,
                start,
                maxCost,
                isPassable,
                canStopOn,
                getMoveCost,
                allowDiagonal: allowDiagonal,
                useOctileCost: useOctileCost);
        }
    }
}
