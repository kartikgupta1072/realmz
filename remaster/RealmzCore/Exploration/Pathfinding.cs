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

    public static class AStarPathfinder
    {
        private static readonly int[] Dx8 = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy8 = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private static readonly bool[] IsDiag8 = { false, false, false, false, true, true, true, true };

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
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            return FindPath(
                map.Width,
                map.Height,
                start,
                goal,
                (x, y) => map.IsPassable(x, y, hasBoat),
                (x, y) => map.GetMoveCost(x, y),
                allowDiagonal: allowDiagonal,
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

            if (!InBounds(start, width, height) || !InBounds(goal, width, height))
            {
                return new PathResult(false, 0, Array.Empty<GridCoord>());
            }

            if (start == goal)
            {
                return new PathResult(true, 0, Array.Empty<GridCoord>());
            }

            if (!isPassable(goal.X, goal.Y))
            {
                return new PathResult(false, 0, Array.Empty<GridCoord>());
            }

            int totalCells = width * height;
            int[] gScore = new int[totalCells];
            int[] parent = new int[totalCells];
            bool[] closed = new bool[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                gScore[i] = int.MaxValue;
                parent[i] = -1;
            }

            int startIdx = ToIndex(start.X, start.Y, width);
            int goalIdx = ToIndex(goal.X, goal.Y, width);
            gScore[startIdx] = 0;

            SortedSet<OpenEntry> open = new SortedSet<OpenEntry>(OpenEntryComparer.Instance);
            int startH = Heuristic(start, goal, allowDiagonal, useOctileCost);
            open.Add(new OpenEntry(startIdx, startH, startH, start.X, start.Y));

            int dirCount = allowDiagonal ? 8 : 4;

            while (open.Count > 0)
            {
                OpenEntry current = open.Min!;
                open.Remove(current);

                if (closed[current.Index])
                {
                    continue;
                }

                if (current.Index == goalIdx)
                {
                    List<GridCoord> steps = ReconstructPath(parent, startIdx, goalIdx, width);
                    return new PathResult(true, gScore[goalIdx], steps);
                }

                closed[current.Index] = true;
                int cx = current.X;
                int cy = current.Y;
                int currentG = gScore[current.Index];

                for (int d = 0; d < dirCount; d++)
                {
                    int nx = cx + Dx8[d];
                    int ny = cy + Dy8[d];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int nIdx = ToIndex(nx, ny, width);
                    if (closed[nIdx] || !isPassable(nx, ny))
                    {
                        continue;
                    }

                    bool isDiag = IsDiag8[d];
                    if (isDiag && !isPassable(cx + Dx8[d], cy) && !isPassable(cx, cy + Dy8[d]))
                    {
                        continue;
                    }

                    int stepCost = GetStepCost(isDiag, getMoveCost(nx, ny), useOctileCost);
                    long tentativeLong = (long)currentG + stepCost;
                    if (tentativeLong > maxCost)
                    {
                        continue;
                    }

                    int tentativeG = (int)tentativeLong;
                    if (tentativeG < gScore[nIdx])
                    {
                        gScore[nIdx] = tentativeG;
                        parent[nIdx] = current.Index;
                        int h = Heuristic(new GridCoord(nx, ny), goal, allowDiagonal, useOctileCost);
                        int f = tentativeG + h;
                        open.Add(new OpenEntry(nIdx, f, h, nx, ny));
                    }
                }
            }

            return new PathResult(false, 0, Array.Empty<GridCoord>());
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
            Dictionary<GridCoord, ReachableNode> result = new Dictionary<GridCoord, ReachableNode>();
            if (width <= 0 || height <= 0 || maxCost <= 0 || !InBounds(start, width, height))
            {
                return result;
            }

            int totalCells = width * height;
            int[] bestCost = new int[totalCells];
            int[] parent = new int[totalCells];
            bool[] settled = new bool[totalCells];
            for (int i = 0; i < totalCells; i++)
            {
                bestCost[i] = int.MaxValue;
                parent[i] = -1;
            }

            int startIdx = ToIndex(start.X, start.Y, width);
            bestCost[startIdx] = 0;

            SortedSet<OpenEntry> open = new SortedSet<OpenEntry>(OpenEntryComparer.Instance);
            open.Add(new OpenEntry(startIdx, 0, 0, start.X, start.Y));
            int dirCount = allowDiagonal ? 8 : 4;

            while (open.Count > 0)
            {
                OpenEntry current = open.Min!;
                open.Remove(current);

                if (settled[current.Index])
                {
                    continue;
                }

                settled[current.Index] = true;
                int cx = current.X;
                int cy = current.Y;
                int cg = bestCost[current.Index];

                if (current.Index != startIdx && canStopOn(cx, cy))
                {
                    GridCoord coord = new GridCoord(cx, cy);
                    List<GridCoord> path = ReconstructPath(parent, startIdx, current.Index, width);
                    result[coord] = new ReachableNode(coord, cg, path);
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
                    if (settled[nIdx] || !isPassable(nx, ny))
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
                    if (nextCost < bestCost[nIdx])
                    {
                        bestCost[nIdx] = nextCost;
                        parent[nIdx] = current.Index;
                        int tieH = isDiag ? 1 : 0;
                        open.Add(new OpenEntry(nIdx, nextCost, tieH, nx, ny));
                    }
                }
            }

            return result;
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

        private static List<GridCoord> ReconstructPath(int[] parent, int startIdx, int goalIdx, int width)
        {
            List<GridCoord> reversed = new List<GridCoord>();
            int cursor = goalIdx;
            while (cursor != -1 && cursor != startIdx)
            {
                int x = cursor % width;
                int y = cursor / width;
                reversed.Add(new GridCoord(x, y));
                cursor = parent[cursor];
            }

            reversed.Reverse();
            return reversed;
        }

        private static bool InBounds(GridCoord coord, int width, int height)
        {
            return coord.X >= 0 && coord.X < width && coord.Y >= 0 && coord.Y < height;
        }

        private static int ToIndex(int x, int y, int width)
        {
            return (y * width) + x;
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

        private sealed class OpenEntryComparer : IComparer<OpenEntry>
        {
            public static readonly OpenEntryComparer Instance = new OpenEntryComparer();

            public int Compare(OpenEntry a, OpenEntry b)
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
        }
    }
}
