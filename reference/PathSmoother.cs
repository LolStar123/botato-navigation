namespace Botato.Navigation;

/// <summary>Collision-checked greedy string pulling. A shortcut may never cross a blocked/avoided cell or cut a corner.</summary>
public static class PathSmoother
{
    public static IReadOnlyList<GridPoint> Smooth(NavigationGrid grid, IReadOnlyList<GridPoint> rawPath, AvoidanceOverlay avoidance, NavigationOptions options)
    {
        if (rawPath.Count <= 1) return [];
        var result = new List<GridPoint>();
        var anchor = 0;
        while (anchor < rawPath.Count - 1)
        {
            var selected = anchor + 1;
            for (var candidate = rawPath.Count - 1; candidate > anchor; candidate--)
            {
                if (!HasClearLine(grid, rawPath[anchor], rawPath[candidate], avoidance)) continue;
                var directCost = LineCost(grid, rawPath[anchor], rawPath[candidate], avoidance);
                var originalCost = PathCost(grid, rawPath, anchor, candidate, avoidance);
                if (directCost * 100 <= originalCost * (100 + options.MaxSmoothingCostIncreasePercent)) { selected = candidate; break; }
            }
            result.Add(rawPath[selected]);
            anchor = selected;
        }
        return result;
    }

    public static bool HasClearLine(NavigationGrid grid, GridPoint from, GridPoint to, AvoidanceOverlay avoidance)
    {
        var previous = from;
        var first = true;
        foreach (var point in Rasterize(from, to))
        {
            if (!grid.IsWalkable(point) || !avoidance.Allows(point)) return false;
            if (!first && !CanStep(grid, previous, point, avoidance)) return false;
            previous = point;
            first = false;
        }
        return !first;
    }

    private static bool CanStep(NavigationGrid grid, GridPoint from, GridPoint to, AvoidanceOverlay avoidance)
    {
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        if (dx > 1 || dy > 1 || (dx == 0 && dy == 0)) return false;
        if (dx == 1 && dy == 1)
        {
            var horizontal = new GridPoint(to.X, from.Y);
            var vertical = new GridPoint(from.X, to.Y);
            return grid.IsWalkable(horizontal) && avoidance.Allows(horizontal) && grid.IsWalkable(vertical) && avoidance.Allows(vertical);
        }
        return true;
    }

    private static IEnumerable<GridPoint> Rasterize(GridPoint from, GridPoint to)
    {
        var x = from.X;
        var y = from.Y;
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = from.X < to.X ? 1 : -1;
        var sy = from.Y < to.Y ? 1 : -1;
        var error = dx - dy;
        var guard = checked(Math.Max(dx, dy) + 1);
        while (guard-- > 0)
        {
            yield return new GridPoint(x, y);
            if (x == to.X && y == to.Y) yield break;
            var twiceError = error * 2;
            if (twiceError > -dy) { error -= dy; x += sx; }
            if (twiceError < dx) { error += dx; y += sy; }
        }
    }

    private static long LineCost(NavigationGrid grid, GridPoint from, GridPoint to, AvoidanceOverlay avoidance)
    {
        long cost = 0;
        GridPoint? previous = null;
        foreach (var point in Rasterize(from, to))
        {
            if (previous is not { } prior) { previous = point; continue; }
            var diagonal = prior.X != point.X && prior.Y != point.Y;
            cost = checked(cost + (diagonal ? 1_414L : 1_000L) * grid.TerrainCost(point) + avoidance.PenaltyAt(point));
            previous = point;
        }
        return cost;
    }

    private static long PathCost(NavigationGrid grid, IReadOnlyList<GridPoint> path, int from, int to, AvoidanceOverlay avoidance)
    {
        long cost = 0;
        for (var index = from + 1; index <= to; index++)
        {
            var diagonal = path[index - 1].X != path[index].X && path[index - 1].Y != path[index].Y;
            cost = checked(cost + (diagonal ? 1_414L : 1_000L) * grid.TerrainCost(path[index]) + avoidance.PenaltyAt(path[index]));
        }
        return cost;
    }
}
