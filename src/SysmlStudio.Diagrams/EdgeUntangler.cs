using System.Numerics;

namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// Re-routes edges that cross other edges. MSAGL routes each edge on its own,
/// the shortest way round the boxes, so two edges cross wherever their shortest
/// ways happen to. Here a crossing is priced as <see cref="CrossingPenalty"/> of
/// extra length: an edge that crosses others is searched for again on a grid on
/// which stepping over another edge costs that much, and it takes the new way
/// only when that is cheaper, length and crossings counted together.
/// </para>
/// <para>
/// So a short detour round a crossing is taken and a long one is not: two
/// edges that cannot avoid each other still cross, by their shortest ways.
/// </para>
/// </summary>
public static class EdgeUntangler
{
    /// <summary>What one crossing costs, in the same units as an edge's length.</summary>
    public const double CrossingPenalty = 160;

    // Grid cell, and how far a route keeps off a box it does not connect. The
    // clearance matches MSAGL's polyline padding, which leaves room for the
    // rounded corners the canvas draws. A coarser cell is for estimates.
    private const double FineCell = 8;
    private const double Clearance = 10;

    // Running alongside another edge is allowed but not free, so that routes
    // spread out rather than share a line.
    private const double AlongsideCost = 4;

    private const int Passes = 3;

    /// <summary>One edge's route between two of the boxes, in the boxes' coordinates.</summary>
    public sealed class Route(int source, int target, List<(double X, double Y)> points)
    {
        /// <summary>Index of the box the edge leaves.</summary>
        public int Source { get; } = source;

        /// <summary>Index of the box the edge enters.</summary>
        public int Target { get; } = target;

        /// <summary>From the source box's boundary to the target box's.</summary>
        public List<(double X, double Y)> Points { get; set; } = points;

        /// <summary>Whether <see cref="Untangle"/> replaced the route it was given.</summary>
        public bool Changed { get; set; }
    }

    /// <summary>
    /// Replaces the routes of edges that cross others by cheaper ones where
    /// there are any. A replaced route is a polyline whose corners hug the
    /// boxes it goes round, and is marked <see cref="Route.Changed"/>.
    /// </summary>
    /// <param name="boxes">Every box on the diagram, which routes keep clear of.</param>
    /// <param name="routes">The routes, replaced in place.</param>
    /// <param name="cell">
    /// The grid's cell size. The default draws routes; a coarser cell is for
    /// estimating how a layout would route, several times faster.
    /// </param>
    public static void Untangle(IReadOnlyList<(double X, double Y, double Width, double Height)> boxes, IReadOnlyList<Route> routes, double cell = FineCell)
    {
        if (boxes.Count == 0 || routes.Count < 2)
            return;

        var grid = new Grid(boxes, routes, cell);
        for (var pass = 0; pass < Passes; pass++)
        {
            var crossed = Enumerable.Range(0, routes.Count)
                .Select(i => (Index: i, Count: CrossingsWithOthers(routes, i, routes[i].Points)))
                .Where(r => r.Count > 0)
                .OrderByDescending(r => r.Count)
                .ToList();

            var improved = false;
            foreach (var (index, _) in crossed)
            {
                var route = routes[index];
                if (route.Points.Count < 2 || route.Source == route.Target)
                    continue;

                grid.MarkRoutes(routes, index);
                var candidate = grid.Search(boxes, routes, index);
                if (candidate is null || Cost(routes, index, candidate) >= Cost(routes, index, route.Points) - 1)
                    continue;

                route.Points = candidate;
                route.Changed = true;
                improved = true;
            }

            if (!improved)
                break;
        }
    }

    /// <summary>Every place where two routes cross, counted once per pair of segments.</summary>
    public static int Crossings(IReadOnlyList<Route> routes)
    {
        var total = 0;
        for (var i = 0; i < routes.Count; i++)
        {
            for (var j = i + 1; j < routes.Count; j++)
                total += Crossings(routes[i].Points, routes[j].Points);
        }

        return total;
    }

    private static double Cost(IReadOnlyList<Route> routes, int index, List<(double X, double Y)> points)
        => Length(points) + (CrossingPenalty * CrossingsWithOthers(routes, index, points));

    private static int CrossingsWithOthers(IReadOnlyList<Route> routes, int index, List<(double X, double Y)> points)
    {
        var total = 0;
        for (var j = 0; j < routes.Count; j++)
        {
            if (j != index)
                total += Crossings(points, routes[j].Points);
        }

        return total;
    }

    private static int Crossings(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        var count = 0;
        for (var i = 0; i + 1 < a.Count; i++)
            count += Crossings(a[i], a[i + 1], b);
        return count;
    }

    private static int Crossings((double X, double Y) p, (double X, double Y) q, List<(double X, double Y)> b)
    {
        var count = 0;
        for (var j = 0; j + 1 < b.Count; j++)
        {
            if (SegmentsCross(p, q, b[j], b[j + 1]))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Whether the segments cross. A point on the other segment's line counts
    /// as being on one side of it, so a route that passes through a corner of
    /// another, or through the other at a corner of its own, crosses it once
    /// and not zero or two times; lines that only overlap do not cross.
    /// </summary>
    private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
            || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y))
        {
            return false;
        }

        var d1 = Orientation(c, d, a);
        var d2 = Orientation(c, d, b);
        var d3 = Orientation(a, b, c);
        var d4 = Orientation(a, b, d);
        if (d1 == 0 && d2 == 0)
            return false;
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    private static int Orientation((double X, double Y) from, (double X, double Y) to, (double X, double Y) point)
    {
        var cross = ((to.X - from.X) * (point.Y - from.Y)) - ((to.Y - from.Y) * (point.X - from.X));
        return Math.Abs(cross) < 1e-9 ? 0 : Math.Sign(cross);
    }

    private static double Length(List<(double X, double Y)> points)
    {
        var length = 0.0;
        for (var i = 0; i + 1 < points.Count; i++)
            length += Distance(points[i], points[i + 1]);
        return length;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>
    /// The picture cut into cells: which box, if any, each cell belongs to, and
    /// which other edges pass through it.
    /// </summary>
    private sealed class Grid
    {
        private const int Free = -1;
        private const int Crowded = -2;

        private readonly double _left;
        private readonly double _cell;
        private readonly double _top;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _owner;
        private readonly ulong[] _edges;

        public Grid(IReadOnlyList<(double X, double Y, double Width, double Height)> boxes, IReadOnlyList<Route> routes, double cell)
        {
            _cell = cell;
            const double margin = 80;
            var points = routes.SelectMany(r => r.Points).ToList();
            var left = Math.Min(boxes.Min(b => b.X), points.Count == 0 ? double.MaxValue : points.Min(p => p.X)) - margin;
            var top = Math.Min(boxes.Min(b => b.Y), points.Count == 0 ? double.MaxValue : points.Min(p => p.Y)) - margin;
            var right = Math.Max(boxes.Max(b => b.X + b.Width), points.Count == 0 ? double.MinValue : points.Max(p => p.X)) + margin;
            var bottom = Math.Max(boxes.Max(b => b.Y + b.Height), points.Count == 0 ? double.MinValue : points.Max(p => p.Y)) + margin;

            _left = left;
            _top = top;
            _columns = (int)Math.Ceiling((right - left) / _cell) + 1;
            _rows = (int)Math.Ceiling((bottom - top) / _cell) + 1;
            _owner = new int[_columns * _rows];
            _edges = new ulong[_columns * _rows];
            Array.Fill(_owner, Free);

            for (var b = 0; b < boxes.Count; b++)
            {
                var grown = Grow(boxes[b], Clearance);
                var (c0, r0) = CellOf((grown.X, grown.Y));
                var (c1, r1) = CellOf((grown.X + grown.Width, grown.Y + grown.Height));
                for (var r = Math.Max(0, r0); r <= Math.Min(_rows - 1, r1); r++)
                {
                    for (var c = Math.Max(0, c0); c <= Math.Min(_columns - 1, c1); c++)
                    {
                        if (!Inside(Centre(c, r), grown))
                            continue;
                        var i = (r * _columns) + c;
                        _owner[i] = _owner[i] == Free ? b : Crowded;
                    }
                }
            }
        }

        /// <summary>
        /// Marks the cells every route but <paramref name="except"/> passes
        /// through, as a line no step can slip past diagonally. Routes share
        /// bits beyond the 64th, which only makes crossing them cost less.
        /// </summary>
        public void MarkRoutes(IReadOnlyList<Route> routes, int except)
        {
            Array.Clear(_edges);
            for (var e = 0; e < routes.Count; e++)
            {
                if (e == except)
                    continue;

                var bit = 1UL << (e % 64);
                var points = routes[e].Points;
                for (var k = 0; k + 1 < points.Count; k++)
                {
                    var (a, b) = (points[k], points[k + 1]);
                    var steps = Math.Max(1, (int)Math.Ceiling(Distance(a, b) / (_cell / 4)));
                    var previous = CellOf(a);
                    for (var s = 0; s <= steps; s++)
                    {
                        var cell = CellOf(Lerp(a, b, (double)s / steps));
                        Mark(cell, bit);
                        if (cell.Column != previous.Column && cell.Row != previous.Row)
                            Mark((previous.Column, cell.Row), bit);
                        previous = cell;
                    }
                }
            }
        }

        /// <summary>
        /// The cheapest way from the route's source box to its target box, with
        /// its corners pulled tight and its ends cut back to the boxes' edges;
        /// null if there is no way.
        /// </summary>
        public List<(double X, double Y)>? Search(IReadOnlyList<(double X, double Y, double Width, double Height)> boxes, IReadOnlyList<Route> routes, int index)
        {
            var route = routes[index];
            var source = boxes[route.Source];
            var target = boxes[route.Target];
            var from = (source.X + (source.Width / 2), source.Y + (source.Height / 2));
            var to = (target.X + (target.Width / 2), target.Y + (target.Height / 2));
            var start = Index(CellOf(from));
            var goal = Index(CellOf(to));
            if (start < 0 || goal < 0)
                return null;

            bool Open(int i) => _owner[i] == Free || _owner[i] == route.Source || _owner[i] == route.Target;
            bool Own(int i) => _owner[i] == route.Source || _owner[i] == route.Target;

            var cost = new double[_owner.Length];
            var came = new int[_owner.Length];
            Array.Fill(cost, double.PositiveInfinity);
            Array.Fill(came, -1);
            var queue = new PriorityQueue<int, double>();
            cost[start] = 0;
            queue.Enqueue(start, 0);
            var goalCentre = Centre(goal % _columns, goal / _columns);

            while (queue.TryDequeue(out var here, out var priority))
            {
                if (here == goal)
                    break;
                var (column, row) = (here % _columns, here / _columns);
                if (priority > cost[here] + Distance(Centre(column, row), goalCentre) + 1e-6)
                    continue;

                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0)
                            continue;
                        var (c, r) = (column + dc, row + dr);
                        if (c < 0 || r < 0 || c >= _columns || r >= _rows)
                            continue;
                        var next = (r * _columns) + c;
                        if (!Open(next))
                            continue;
                        if (dr != 0 && dc != 0 && !(Open((row * _columns) + c) && Open((r * _columns) + column)))
                            continue;

                        var step = dr != 0 && dc != 0 ? _cell * Math.Sqrt(2) : _cell;
                        if (!Own(next))
                        {
                            // Only other edges this step newly steps onto are crossed.
                            var entered = _edges[next] & ~_edges[here];
                            step += (CrossingPenalty * BitOperations.PopCount(entered))
                                    + (AlongsideCost * BitOperations.PopCount(_edges[next]));
                        }

                        var total = cost[here] + step;
                        if (total >= cost[next])
                            continue;
                        cost[next] = total;
                        came[next] = here;
                        queue.Enqueue(next, total + Distance(Centre(c, r), goalCentre));
                    }
                }
            }

            if (came[goal] < 0 && goal != start)
                return null;

            var path = new List<(double X, double Y)>();
            for (var i = goal; i >= 0; i = came[i])
                path.Add(Centre(i % _columns, i / _columns));
            path.Reverse();
            path[0] = from;
            path[^1] = to;
            if (path.Count < 2)
                path.Add(to);

            var tight = PullTight(path, boxes, routes, index);
            return CutBack(tight, source, target);
        }

        /// <summary>
        /// Replaces runs of cells by straight lines, as long as a line keeps
        /// clear of the boxes and crosses no more edges than the run it replaces.
        /// </summary>
        private static List<(double X, double Y)> PullTight(
            List<(double X, double Y)> path,
            IReadOnlyList<(double X, double Y, double Width, double Height)> boxes,
            IReadOnlyList<Route> routes,
            int index)
        {
            var route = routes[index];
            var others = routes.Where((_, i) => i != index).Select(r => r.Points).ToList();
            var walls = boxes.Where((_, i) => i != route.Source && i != route.Target)
                .Select(b => Grow(b, Clearance - 1)).ToList();

            // Crossings made by the run of cells up to each point.
            var crossedBefore = new int[path.Count];
            for (var k = 1; k < path.Count; k++)
                crossedBefore[k] = crossedBefore[k - 1] + others.Sum(o => Crossings(path[k - 1], path[k], o));

            bool Straight(int i, int j)
                => walls.All(w => Clip(path[i], path[j], w) is not { } inside || inside.Leave - inside.Enter < 1e-9)
                   && others.Sum(o => Crossings(path[i], path[j], o)) <= crossedBefore[j] - crossedBefore[i];

            var tight = new List<(double X, double Y)> { path[0] };
            var from = 0;
            while (from < path.Count - 1)
            {
                var to = from + 1;
                while (to + 1 < path.Count && Straight(from, to + 1))
                    to++;
                tight.Add(path[to]);
                from = to;
            }

            return tight;
        }

        /// <summary>The part of the route between the two boxes' edges; null if it never leaves the source.</summary>
        private static List<(double X, double Y)>? CutBack(
            List<(double X, double Y)> path,
            (double X, double Y, double Width, double Height) source,
            (double X, double Y, double Width, double Height) target)
        {
            var first = -1;
            (double X, double Y) exit = default;
            for (var k = 0; k + 1 < path.Count; k++)
            {
                if (Inside(path[k + 1], source))
                    continue;
                if (Clip(path[k], path[k + 1], source) is { } inside && Inside(path[k], source))
                    exit = Lerp(path[k], path[k + 1], inside.Leave);
                else
                    exit = path[k];
                first = k + 1;
                break;
            }

            if (first < 0)
                return null;

            var last = -1;
            (double X, double Y) entry = default;
            for (var k = path.Count - 1; k > first - 1; k--)
            {
                if (Inside(path[k - 1], target))
                    continue;
                if (Clip(path[k - 1], path[k], target) is { } inside && Inside(path[k], target))
                    entry = Lerp(path[k - 1], path[k], inside.Enter);
                else
                    entry = path[k];
                last = k - 1;
                break;
            }

            if (last < first - 1)
                return null;

            var cut = new List<(double X, double Y)> { exit };
            for (var k = first; k <= last; k++)
                cut.Add(path[k]);
            cut.Add(entry);
            return cut;
        }

        private static (double X, double Y, double Width, double Height) Grow((double X, double Y, double Width, double Height) box, double by)
            => (box.X - by, box.Y - by, box.Width + (2 * by), box.Height + (2 * by));

        private static bool Inside((double X, double Y) p, (double X, double Y, double Width, double Height) box)
            => p.X > box.X && p.X < box.X + box.Width && p.Y > box.Y && p.Y < box.Y + box.Height;

        /// <summary>
        /// Where the segment from <paramref name="a"/> to <paramref name="b"/> is
        /// inside the box, as parameters along it (Liang–Barsky); null if nowhere.
        /// </summary>
        private static (double Enter, double Leave)? Clip((double X, double Y) a, (double X, double Y) b, (double X, double Y, double Width, double Height) box)
        {
            double enter = 0, leave = 1;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            foreach (var (p, q) in new[] { (-dx, a.X - box.X), (dx, box.X + box.Width - a.X), (-dy, a.Y - box.Y), (dy, box.Y + box.Height - a.Y) })
            {
                if (Math.Abs(p) < 1e-12)
                {
                    if (q < 0)
                        return null;
                    continue;
                }

                var t = q / p;
                if (p < 0)
                    enter = Math.Max(enter, t);
                else
                    leave = Math.Min(leave, t);
                if (enter > leave)
                    return null;
            }

            return (enter, leave);
        }

        private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t)
            => (a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        private void Mark((int Column, int Row) cell, ulong bit)
        {
            var i = Index(cell);
            if (i >= 0)
                _edges[i] |= bit;
        }

        private (int Column, int Row) CellOf((double X, double Y) p)
            => ((int)Math.Floor((p.X - _left) / _cell), (int)Math.Floor((p.Y - _top) / _cell));

        private int Index((int Column, int Row) cell)
            => cell.Column < 0 || cell.Row < 0 || cell.Column >= _columns || cell.Row >= _rows ? -1 : (cell.Row * _columns) + cell.Column;

        private (double X, double Y) Centre(int column, int row)
            => (_left + ((column + 0.5) * _cell), _top + ((row + 0.5) * _cell));
    }
}
