using System.Numerics;
using ExileCore.PoEMemory.MemoryObjects;

namespace PoeBot.Core.World;

/// <summary>
/// Clearance-aware terrain router over PoE's walkability grid (<c>IngameData.RawPathfindingData</c>,
/// row-major <c>[y][x]</c>, 0 = blocked wall/water/chasm, &gt;0 = walkable).
///
/// The route is not just the shortest walkable path- it deliberately keeps its DISTANCE FROM WALLS so the
/// character glides through the middle of open space instead of scraping corners. Three stages, all bounded
/// to a window around the start+goal so it stays cheap enough to run every movement tick:
///
///   1. CLEARANCE FIELD  - a two-pass chamfer distance transform gives every walkable cell its distance to
///                         the nearest wall (capped). O(cells), sub-millisecond over the window.
///   2. WEIGHTED A*       - 8-directional, no corner-cutting, with an added cost for cells whose clearance is
///                         below the desired margin. This bends the path away from walls while still
///                         minimising travel- the weight trades the two off.
///   3. CLEARANCE SMOOTH  - a funnel/string-pull that shortcuts nodes only through comfortably-clear cells,
///                         removing the A* zig-zag without re-introducing wall-hugging.
///
/// Buffers are reused between calls to avoid per-tick allocations. Decoupled from GameController (it takes a
/// data provider) so it can be exercised headlessly against a live game.
/// </summary>
public sealed class Pathfinder
{
    private readonly Func<IngameData?> _data;
    private readonly Func<int> _wallClearance;    // desired grid cells of clearance from walls
    private readonly Func<float> _clearanceWeight; // penalty per missing clearance cell (avoid-walls strength)

    private const float Diag = 1.41421356f;
    private const int WindowCap = 900; // max window dimension (grid cells) - bounds cost of a full re-plan

    // Reusable clearance-field buffers + the window frame they were computed over.
    private float[] _dist = Array.Empty<float>();
    private float[] _clear = Array.Empty<float>();
    private int _bx0, _by0, _bw, _bh;

    // Reusable A* state.
    private readonly PriorityQueue<long, float> _open = new();
    private readonly Dictionary<long, float> _g = new(4096);
    private readonly Dictionary<long, long> _came = new(4096);

    public Pathfinder(Func<IngameData?> data, Func<int> wallClearance, Func<float> clearanceWeight)
    {
        _data = data;
        _wallClearance = wallClearance;
        _clearanceWeight = clearanceWeight;
    }

    // ---------------------------------------------------------------- grid basics

    private int[][]? Grid()
    {
        try { return _data()?.RawPathfindingData; }
        catch { return null; }
    }

    /// <summary>Raw pathfinding value at (x,y). -1 if OOB/unreadable. 0 = blocked, &gt;0 = walkable.</summary>
    public int RawValueAt(int x, int y)
    {
        var g = Grid();
        return g is null ? -1 : CellValue(g, x, y);
    }

    public bool IsWalkable(int x, int y)
    {
        var g = Grid();
        return g is not null && CellWalkable(g, x, y);
    }

    private static int CellValue(int[][] g, int x, int y)
    {
        if (y < 0 || y >= g.Length) return -1;
        var row = g[y];
        if (row is null || x < 0 || x >= row.Length) return -1;
        return row[x];
    }

    private static bool CellWalkable(int[][] g, int x, int y) => CellValue(g, x, y) > 0;

    /// <summary>Grid dimensions (width X, height Y). (0,0) if unreadable.</summary>
    public (int w, int h) GridDims()
    {
        var g = Grid();
        if (g is null) return (0, 0);
        var h = g.Length;
        var w = h > 0 && g[0] is not null ? g[0].Length : 0;
        return (w, h);
    }

    public Vector3 GridToWorld(Vector2 grid)
    {
        var d = _data();
        if (d is null) return default;
        try { return d.ToWorldWithTerrainHeight(grid); }
        catch { return default; }
    }

    /// <summary>Average (x,y) of every walkable cell (CellWalkable &gt; 0) in grid coords- the arena's walkable
    /// CENTROID. `default` if the grid is unreadable or has zero walkable cells. This is an O(cells) full-grid
    /// scan; that is fine because the CALLER caches it (MovementService.ArenaCenterGrid), so it runs at most
    /// once per area. Used to seek the arena centre where the Simulacrum wave-device sits- see ExploreInward.</summary>
    public Vector2 WalkableCentroid()
    {
        var g = Grid();
        if (g is null) return default;
        double sx = 0, sy = 0;
        long n = 0;
        for (int y = 0; y < g.Length; y++)
        {
            var row = g[y];
            if (row is null) continue;
            for (int x = 0; x < row.Length; x++)
                if (row[x] > 0) { sx += x; sy += y; n++; }
        }
        if (n == 0) return default;
        return new Vector2((float)(sx / n), (float)(sy / n));
    }

    /// <summary>Nearest walkable cell to <paramref name="target"/>, ring-searching outward up to
    /// <paramref name="maxRadius"/> grid cells. Returns the target itself if it is already walkable, else the
    /// first walkable cell found on an expanding square ring, else `default`. Guards the non-convex-arena case
    /// where the raw walkable centroid can land on a blocked cell (a wall or pit near the middle).</summary>
    public Vector2 NearestWalkable(Vector2 target, int maxRadius)
    {
        var g = Grid();
        if (g is null) return default;
        int tx = (int)MathF.Round(target.X), ty = (int)MathF.Round(target.Y);
        if (CellWalkable(g, tx, ty)) return new Vector2(tx, ty);
        for (int r = 1; r <= maxRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue; // ring perimeter only
                    if (CellWalkable(g, tx + dx, ty + dy)) return new Vector2(tx + dx, ty + dy);
                }
        return default;
    }

    // ---------------------------------------------------------------- geodesic farthest point

    /// <summary>Farthest walkable cell from <paramref name="start"/>, measured by BFS HOP COUNT over the walkable
    /// grid (not Euclidean)- the true geodesic farthest point respecting walls. Because the flood fill only ever
    /// visits cells connected to start through walkable terrain, the result is CONNECTED-WALKABLE from start:
    /// a deep exploration target that provably sits on the walkable map, which a guessed heading cannot offer.
    /// `default` if the grid is unreadable, start cannot be snapped to a walkable cell, or there are no walkable
    /// cells at all. O(cells)- this is the CALLER's job to cache (see MovementService.FarthestReachableGrid),
    /// same contract as WalkableCentroid.
    ///
    /// CONNECTIVITY matches FindPath's step predicate EXACTLY- 8-directional with the identical no-corner-cutting
    /// guard (a diagonal needs both shared orthogonal cells open)- so every hop this fill takes is one A* would
    /// also accept.
    ///
    /// HONEST LIMIT (verify pass 2026-07-17): connected-walkable is NOT the same as "FindPath can always route
    /// to it in one plan". FindPath is WINDOW-BOUNDED- it plans inside a box around the start->goal line plus a
    /// margin (~110 cells). If the only walkable route to a far cell detours outside that window (large
    /// serpentine / U-shaped geometry), A* returns no route even though the cell is genuinely reachable, and the
    /// caller's mover falls back. This does not bite small near-convex arenas (Simulacrum), but CAN on the large
    /// arenas future callers will explore; the general fix is iterative stepping toward nearer reachable
    /// waypoints. Do not restate the earlier "beeline never fires" as an unconditional guarantee- it was wrong.</summary>
    public Vector2 GeodesicFarthest(Vector2 start)
    {
        var g = Grid();
        if (g is null) return default;
        var (w, h) = GridDims();
        if (w == 0 || h == 0) return default;

        int sx = (int)start.X, sy = (int)start.Y;
        // Defensive- the player always stands on a walkable cell (Diagnose ground truth), but the caller could
        // hand us a stale/rounded position; snap to the nearest walkable cell within a small radius, else bail.
        if (!CellWalkable(g, sx, sy) && !TrySnap(g, ref sx, ref sy, 10))
            return default;

        // Local buffers, not fields: this runs ONCE per area (the caller caches the single Vector2 result the
        // same way MovementService.ArenaCenterGrid caches its centroid), so a per-area O(cells) bool[] is cheap
        // and keeps Pathfinder's state surface unchanged (no new ctor args / fields).
        var visited = new bool[w * h];
        var queue = new Queue<int>();
        int Idx(int x, int y) => y * w + x;
        var startIdx = Idx(sx, sy);
        visited[startIdx] = true;
        queue.Enqueue(startIdx);

        int farIdx = startIdx;
        int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            farIdx = cur; // BFS dequeues in non-decreasing hop-distance order, so the LAST one out is a farthest.
            int cx = cur % w, cy = cur / w;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + dxs[k], ny = cy + dys[k];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (!CellWalkable(g, nx, ny)) continue;
                // Same no-corner-cutting rule as FindPath: a diagonal needs both shared orthogonal cells open.
                if (k >= 4 && (!CellWalkable(g, cx + dxs[k], cy) || !CellWalkable(g, cx, cy + dys[k]))) continue;
                var ni = Idx(nx, ny);
                if (visited[ni]) continue;
                visited[ni] = true;
                queue.Enqueue(ni);
            }
        }
        return new Vector2(farIdx % w, farIdx / w);
    }

    // ---------------------------------------------------------------- line checks

    /// <summary>Bresenham ray: true if every cell between the two grid points is walkable (no clearance
    /// requirement). Used by the combat reachability gate ("can I reach this target in a straight walk?").</summary>
    public bool HasLineOfWalk(int x0, int y0, int x1, int y1)
    {
        var g = Grid();
        if (g is null) return false;
        return RayForEach(x0, y0, x1, y1, (x, y) => CellWalkable(g, x, y));
    }

    /// <summary>Ray that must stay walkable AND keep at least <paramref name="minClear"/> clearance from
    /// walls the whole way. Used to shortcut/smooth without cutting corners. Clearance comes from the field
    /// built by the most recent <see cref="FindPath"/>; outside that window clearance reads as fully open.</summary>
    public bool HasClearLineOfWalk(int x0, int y0, int x1, int y1, float minClear)
    {
        var g = Grid();
        if (g is null) return false;
        return RayForEach(x0, y0, x1, y1, (x, y) => CellWalkable(g, x, y) && Clearance(x, y) >= minClear);
    }

    private static bool RayForEach(int x0, int y0, int x1, int y1, Func<int, int, bool> ok)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy, x = x0, y = y0;
        var guard = dx + dy + 4;
        while (guard-- > 0)
        {
            if (!ok(x, y)) return false;
            if (x == x1 && y == y1) return true;
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
        return false;
    }

    // ---------------------------------------------------------------- clearance field

    private void BuildClearanceField(int[][] g, int minX, int minY, int maxX, int maxY, float cap)
    {
        int w = maxX - minX + 1, h = maxY - minY + 1;
        int n = w * h;
        if (_dist.Length < n) { _dist = new float[n]; _clear = new float[n]; }
        _bx0 = minX; _by0 = minY; _bw = w; _bh = h;

        float inf = cap + 10f;

        // seed: blocked cells 0, walkable cells +inf
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _dist[y * w + x] = CellWalkable(g, minX + x, minY + y) ? inf : 0f;

        // forward pass (upper-left neighbours)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float d = _dist[i];
                if (x > 0) d = Math.Min(d, _dist[i - 1] + 1f);
                if (y > 0) d = Math.Min(d, _dist[i - w] + 1f);
                if (x > 0 && y > 0) d = Math.Min(d, _dist[i - w - 1] + Diag);
                if (x < w - 1 && y > 0) d = Math.Min(d, _dist[i - w + 1] + Diag);
                _dist[i] = d;
            }

        // backward pass (lower-right neighbours) + cap into _clear
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                float d = _dist[i];
                if (x < w - 1) d = Math.Min(d, _dist[i + 1] + 1f);
                if (y < h - 1) d = Math.Min(d, _dist[i + w] + 1f);
                if (x < w - 1 && y < h - 1) d = Math.Min(d, _dist[i + w + 1] + Diag);
                if (x > 0 && y < h - 1) d = Math.Min(d, _dist[i + w - 1] + Diag);
                _dist[i] = d;
                _clear[i] = Math.Min(d, cap);
            }
    }

    /// <summary>Clearance (grid distance to nearest wall) at a cell from the last-built field. Cells outside
    /// the window read as fully open so callers never over-restrict on stale data.</summary>
    private float Clearance(int x, int y)
    {
        int lx = x - _bx0, ly = y - _by0;
        if (lx < 0 || ly < 0 || lx >= _bw || ly >= _bh) return float.MaxValue;
        return _clear[ly * _bw + lx];
    }

    // ---------------------------------------------------------------- pathfind

    /// <summary>Clearance-aware route: smoothed grid waypoints from (excluding) start to goal, or empty if no
    /// route within budget / the grid is dead. The caller then steps toward the first waypoints.</summary>
    public List<Vector2> FindPath(Vector2 startF, Vector2 goalF, int maxExpanded, Action<string>? log)
    {
        var result = new List<Vector2>();
        var g = Grid();
        if (g is null) { log?.Invoke("pathfind: grid dead"); return result; }

        var (gw, gh) = GridDims();
        if (gw == 0) return result;

        int sx = (int)startF.X, sy = (int)startF.Y, gx = (int)goalF.X, gy = (int)goalF.Y;
        if (!CellWalkable(g, sx, sy)) TrySnap(g, ref sx, ref sy, 6);
        if (!CellWalkable(g, gx, gy) && !TrySnap(g, ref gx, ref gy, 10))
        {
            log?.Invoke($"pathfind: goal ({gx},{gy}) unwalkable, no snap");
            return result;
        }

        int target = Math.Max(0, _wallClearance());
        float weight = Math.Max(0f, _clearanceWeight());
        float cap = target + 6f;
        // Generous margin so the window contains a real DETOUR (around water/walls), not just the straight
        // start->goal rectangle- otherwise A* can't see the long way round and the caller falls back to a
        // straight nudge into the obstacle.
        int margin = Math.Max(target + 8, 110);

        // Bound the working window. If the goal is far, aim at a sub-goal on the way so the window (and cost)
        // stays capped- incremental re-pathing each tick carries us the rest of the distance.
        float ddx = gx - sx, ddy = gy - sy;
        float dlen = MathF.Sqrt(ddx * ddx + ddy * ddy);
        float maxReach = WindowCap - 2 * margin;
        if (dlen > maxReach && dlen > 1e-3f)
        {
            gx = sx + (int)(ddx / dlen * maxReach);
            gy = sy + (int)(ddy / dlen * maxReach);
            if (!CellWalkable(g, gx, gy)) TrySnap(g, ref gx, ref gy, 12);
        }

        int minX = Math.Max(0, Math.Min(sx, gx) - margin);
        int minY = Math.Max(0, Math.Min(sy, gy) - margin);
        int maxX = Math.Min(gw - 1, Math.Max(sx, gx) + margin);
        int maxY = Math.Min(gh - 1, Math.Max(sy, gy) + margin);

        BuildClearanceField(g, minX, minY, maxX, maxY, cap);

        // ----- weighted A* -----
        long Key(int x, int y) => ((long)(uint)x << 32) | (uint)y;
        var start = Key(sx, sy);
        var goal = Key(gx, gy);

        _open.Clear(); _g.Clear(); _came.Clear();
        _g[start] = 0f;
        _open.Enqueue(start, 0f);

        int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int expanded = 0;
        bool found = false;

        while (_open.Count > 0 && expanded < maxExpanded)
        {
            var cur = _open.Dequeue();
            expanded++;
            if (cur == goal) { found = true; break; }

            int cx = (int)(cur >> 32), cy = (int)(uint)cur;
            var curG = _g[cur];

            for (int k = 0; k < 8; k++)
            {
                int nx = cx + dxs[k], ny = cy + dys[k];
                if (nx < minX || ny < minY || nx > maxX || ny > maxY) continue;
                if (!CellWalkable(g, nx, ny)) continue;
                // No corner cutting: a diagonal needs both shared orthogonal cells open.
                if (k >= 4 && (!CellWalkable(g, cx + dxs[k], cy) || !CellWalkable(g, cx, cy + dys[k]))) continue;

                float stepCost = k < 4 ? 1f : Diag;
                float penalty = Math.Max(0f, target - Clearance(nx, ny)) * weight;
                float tentative = curG + stepCost + penalty;

                var nk = Key(nx, ny);
                if (_g.TryGetValue(nk, out var old) && tentative >= old) continue;

                _g[nk] = tentative;
                _came[nk] = cur;
                float hx = nx - gx, hy = ny - gy;
                _open.Enqueue(nk, tentative + MathF.Sqrt(hx * hx + hy * hy));
            }
        }

        if (!found)
        {
            log?.Invoke($"pathfind: no route ({sx},{sy})->({gx},{gy}) expanded={expanded}/{maxExpanded}");
            return result;
        }

        // reconstruct raw path (start-exclusive)
        var raw = new List<Vector2>();
        var node = goal;
        while (node != start)
        {
            raw.Add(new Vector2((int)(node >> 32), (int)(uint)node));
            if (!_came.TryGetValue(node, out node)) break;
        }
        raw.Reverse();

        // ----- clearance-aware smoothing (funnel) -----
        float smoothClear = Math.Max(2f, target * 0.5f);
        Smooth(raw, sx, sy, smoothClear, result);
        log?.Invoke($"pathfind: route len={result.Count} (raw={raw.Count}) expanded={expanded} clr>={target}");
        return result;
    }

    private void Smooth(List<Vector2> raw, int fromX, int fromY, float minClear, List<Vector2> outPath)
    {
        outPath.Clear();
        if (raw.Count == 0) return;

        int ax = fromX, ay = fromY;
        int i = 0;
        while (i < raw.Count)
        {
            // farthest node still reachable from the current anchor through a clear straight line
            int j = raw.Count - 1;
            while (j > i)
            {
                var n = raw[j];
                if (HasClearLineOfWalk(ax, ay, (int)n.X, (int)n.Y, minClear)) break;
                j--;
            }
            var pick = raw[j];
            outPath.Add(pick);
            ax = (int)pick.X; ay = (int)pick.Y;
            i = j + 1;
        }
    }

    /// <summary>Farthest smoothed node within <paramref name="maxStepGrid"/> of the player that is reachable
    /// through a clear (wall-avoiding) straight line- the point we actually right-click this tick.</summary>
    public Vector2 PickReachableWaypoint(IReadOnlyList<Vector2> path, Vector2 from, float maxStepGrid)
    {
        if (path.Count == 0) return from;
        float minClear = Math.Max(2f, Math.Max(0, _wallClearance()) * 0.5f);
        int px = (int)from.X, py = (int)from.Y;
        var chosen = path[0];
        foreach (var node in path)
        {
            if (Vector2.Distance(from, node) > maxStepGrid) break;
            if (HasClearLineOfWalk(px, py, (int)node.X, (int)node.Y, minClear))
                chosen = node;
        }
        return chosen;
    }

    private static bool TrySnap(int[][] g, ref int gx, ref int gy, int radius)
    {
        for (var r = 1; r <= radius; r++)
            for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                    if (CellWalkable(g, gx + dx, gy + dy)) { gx += dx; gy += dy; return true; }
        return false;
    }

    // ---------------------------------------------------------------- diagnostics

    /// <summary>One-line ground-truth dump: grid alive?, dims, the raw value AT and AROUND the player (which
    /// always stands on a walkable cell, so its value reveals the walkable threshold and confirms [y][x]
    /// indexing), plus a ray of raw values toward a goal so a blocking strip (water/wall) shows as 0s.</summary>
    public string Diagnose(int px, int py, int gx, int gy)
    {
        var (w, h) = GridDims();
        if (w == 0) return $"grid=DEAD player({px},{py}) goal({gx},{gy})";

        var self = RawValueAt(px, py);
        var swapped = RawValueAt(py, px);
        var neigh = $"N={RawValueAt(px, py - 1)} S={RawValueAt(px, py + 1)} E={RawValueAt(px + 1, py)} W={RawValueAt(px - 1, py)}";

        var ray = new System.Text.StringBuilder();
        for (var i = 1; i <= 12; i++)
        {
            var t = i / 12f;
            ray.Append(RawValueAt((int)(px + (gx - px) * t), (int)(py + (gy - py) * t)));
            if (i < 12) ray.Append(',');
        }
        return $"grid={w}x{h} playerVal={self} (swapped={swapped}) {neigh} | ray[p->g]={ray}";
    }
}
