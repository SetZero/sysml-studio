namespace SysmlStudio.Diagrams;

/// <summary>
/// <para>
/// Reorders the boxes within each row of a layered layout when that makes
/// fewer edges cross. MSAGL orders each layer by a heuristic that settles on
/// the first order it cannot improve locally, and a person moving a box or two
/// often does better. This tries what that person would: each box at each
/// place in its row, keeping a move when the routed picture crosses less.
/// </para>
/// <para>
/// What counts is how the edges would be routed, not the straight lines
/// between centres: the router takes an edge round a crossing where the
/// detour is short, so an order that looks worse drawn straight can route
/// cleaner. Each candidate is routed roughly, on a coarse grid, and the
/// straight lines only rule out moves that are plainly worse.
/// </para>
/// </summary>
public static class LayerOrder
{
    private const int MaxSweeps = 4;

    // How many candidate orders are routed per group, at a few milliseconds
    // each, and how much worse drawn straight a candidate may be and still be
    // routed to find out.
    private const int MaxEstimates = 40;
    private const int StraightSlack = 2;

    // The grid the estimate routes on; the drawn routes use a finer one.
    private const double EstimateCell = 16;

    /// <summary>
    /// Reorders the rows of <paramref name="nodes"/> in place; true when any
    /// row changed. A changed row keeps its left and right ends and spreads
    /// its boxes evenly between them; the other rows do not move.
    /// </summary>
    public static bool ReduceCrossings(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges)
    {
        var members = nodes.ToHashSet();
        var lines = edges.Where(e => members.Contains(e.Source) && members.Contains(e.Target) && !ReferenceEquals(e.Source, e.Target)).ToList();
        if (lines.Count < 2)
            return false;

        var rows = nodes
            .GroupBy(n => Math.Round(n.Y + (n.Height / 2)))
            .Select(g => g.OrderBy(n => n.X).ToList())
            .Where(r => r.Count > 1)
            .ToList();
        if (rows.Count == 0)
            return false;

        var search = new Search(nodes, lines);
        var changed = false;
        for (var sweep = 0; sweep < MaxSweeps && !search.Done; sweep++)
        {
            var improved = false;
            foreach (var row in rows)
            {
                // A copy: moving a box reorders the row being walked.
                List<DiagramNode> order = [.. row];
                foreach (var node in order.TakeWhile(_ => !search.Done))
                {
                    var from = row.IndexOf(node);
                    var to = search.BestPlace(row, from);
                    if (to == from)
                        continue;
                    Move(row, from, to);
                    improved = changed = true;
                }
            }

            if (!improved)
                break;
        }

        return changed;
    }

    /// <summary>The search's state: the best estimate so far, and how many estimates it may still make.</summary>
    private sealed class Search(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> lines)
    {
        private int _best = Estimate(nodes, lines);
        private int _budget = MaxEstimates;

        public bool Done => _best == 0 || _budget == 0;

        /// <summary>Where in its row the box at <paramref name="from"/> routes best; <paramref name="from"/> when nowhere is better.</summary>
        public int BestPlace(List<DiagramNode> row, int from)
        {
            var straight = Cost(nodes, lines);
            var candidates = Enumerable.Range(0, row.Count)
                .Where(to => to != from)
                .Select(to => (To: to, Straight: Try(row, from, to, () => Cost(nodes, lines))))
                .Where(c => c.Straight <= straight + StraightSlack)
                .OrderBy(c => c.Straight)
                .ToList();

            var place = from;
            foreach (var (to, _) in candidates.TakeWhile(_ => _budget > 0))
            {
                _budget--;
                var estimate = Try(row, from, to, () => Estimate(nodes, lines));
                if (estimate < _best)
                {
                    _best = estimate;
                    place = to;
                }
            }

            return place;
        }

        /// <summary>Measures the row with the box at <paramref name="from"/> moved to <paramref name="to"/>, then puts the row back exactly.</summary>
        private static int Try(List<DiagramNode> row, int from, int to, Func<int> measure)
        {
            var placed = row.ConvertAll(n => n.X);
            var node = row[from];
            Move(row, from, to);
            try
            {
                return measure();
            }
            finally
            {
                row.RemoveAt(to);
                row.Insert(from, node);
                for (var i = 0; i < row.Count; i++)
                    row[i].X = placed[i];
            }
        }
    }

    /// <summary>Crossings plus edges through boxes, drawn as straight lines between the box centres.</summary>
    public static int Cost(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges)
    {
        var cost = 0;
        for (var i = 0; i < edges.Count; i++)
        {
            var a = edges[i];
            var (p, q) = (Centre(a.Source), Centre(a.Target));
            for (var j = i + 1; j < edges.Count; j++)
            {
                var b = edges[j];
                if (!Shares(a, b) && Cross(p, q, Centre(b.Source), Centre(b.Target)))
                    cost++;
            }

            cost += nodes.Count(box => !ReferenceEquals(box, a.Source) && !ReferenceEquals(box, a.Target) && Through(p, q, box));
        }

        return cost;
    }

    /// <summary>
    /// Crossings plus edges through boxes once the edges are routed roughly:
    /// straight, then taken round crossings the way <see cref="EdgeUntangler"/> does.
    /// </summary>
    public static int Estimate(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges)
    {
        var index = new Dictionary<DiagramNode, int>();
        for (var i = 0; i < nodes.Count; i++)
            index[nodes[i]] = i;

        var routes = edges
            .Select(e => new EdgeUntangler.Route(index[e.Source], index[e.Target], [Centre(e.Source), Centre(e.Target)]))
            .ToList();
        EdgeUntangler.Untangle([.. nodes.Select(n => (n.X, n.Y, n.Width, n.Height))], routes, EstimateCell);

        var through = 0;
        foreach (var route in routes)
        {
            for (var k = 0; k + 1 < route.Points.Count; k++)
            {
                var (p, q) = (route.Points[k], route.Points[k + 1]);
                through += nodes.Where((_, i) => i != route.Source && i != route.Target).Count(box => Through(p, q, box));
            }
        }

        return EdgeUntangler.Crossings(routes) + through;
    }

    /// <summary>Moves the box at <paramref name="from"/> to <paramref name="to"/> and spreads the row evenly between its ends.</summary>
    private static void Move(List<DiagramNode> row, int from, int to)
    {
        var left = row.Min(n => n.X);
        var right = row.Max(n => n.X + n.Width);
        var gap = (right - left - row.Sum(n => n.Width)) / (row.Count - 1);

        var node = row[from];
        row.RemoveAt(from);
        row.Insert(to, node);

        var x = left;
        foreach (var box in row)
        {
            box.X = x;
            x += box.Width + gap;
        }
    }

    private static bool Shares(DiagramEdge a, DiagramEdge b)
        => ReferenceEquals(a.Source, b.Source) || ReferenceEquals(a.Source, b.Target)
           || ReferenceEquals(a.Target, b.Source) || ReferenceEquals(a.Target, b.Target);

    private static (double X, double Y) Centre(DiagramNode n) => (n.X + (n.Width / 2), n.Y + (n.Height / 2));

    private static bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Side((double X, double Y) o, (double X, double Y) p, (double X, double Y) q)
            => ((p.X - o.X) * (q.Y - o.Y)) - ((p.Y - o.Y) * (q.X - o.X));

        return Side(c, d, a) * Side(c, d, b) < 0 && Side(a, b, c) * Side(a, b, d) < 0;
    }

    /// <summary>Whether the line runs through the box, not merely past a corner of it.</summary>
    private static bool Through((double X, double Y) a, (double X, double Y) b, DiagramNode box)
    {
        const double inset = 4;
        double enter = 0, leave = 1;
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        foreach (var (p, q) in new[]
                 {
                     (-dx, a.X - (box.X + inset)), (dx, box.X + box.Width - inset - a.X),
                     (-dy, a.Y - (box.Y + inset)), (dy, box.Y + box.Height - inset - a.Y),
                 })
        {
            if (Math.Abs(p) < 1e-12)
            {
                if (q < 0)
                    return false;
                continue;
            }

            var t = q / p;
            if (p < 0)
                enter = Math.Max(enter, t);
            else
                leave = Math.Min(leave, t);
            if (enter >= leave)
                return false;
        }

        return true;
    }
}
