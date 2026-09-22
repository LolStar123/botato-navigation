export const W = 80,
    H = 50;
const id = (x, y) => y * W + x;
export const point = (k) => [k % W, Math.floor(k / W)];
export function inside(x, y, polygon) {
    let hit = false;
    for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
        const a = polygon[i],
            b = polygon[j];
        if (
            a[1] > y !== b[1] > y &&
            x < ((b[0] - a[0]) * (y - a[1])) / (b[1] - a[1]) + a[0]
        )
            hit = !hit;
    }
    return hit;
}
export function makeMap(name = "quarry") {
    const shapes = [];
    const rock = (x, y, rx, ry, seed) =>
        shapes.push(
            Array.from({ length: 10 }, (_, i) => {
                const a = (i / 10) * Math.PI * 2,
                    r = 1 + 0.18 * Math.sin(i * 2.4 + seed);
                return [x + Math.cos(a) * rx * r, y + Math.sin(a) * ry * r];
            }),
        );
    if (name === "quarry")
        for (const a of [
            [15, 12, 5, 7, 1],
            [33, 8, 7, 4, 2],
            [52, 15, 5, 8, 3],
            [68, 7, 5, 3, 4],
            [24, 30, 8, 5, 5],
            [43, 32, 5, 7, 6],
            [64, 35, 7, 5, 7],
            [11, 42, 4, 3, 8],
            [43, 47, 8, 2, 9],
        ])
            rock(...a);
    if (name === "ravine")
        for (let i = 0; i < 6; i++) {
            rock(23 + i * 3, 6 + i * 7, 8, 4, i);
            rock(49 + i * 3, 6 + i * 7, 8, 4, i + 3);
        }
    if (name === "ruins") {
        for (const [x, y, w, h] of [
            [14, 0, 3, 34],
            [14, 42, 3, 8],
            [32, 15, 3, 35],
            [50, 0, 3, 33],
            [68, 17, 3, 33],
            [16, 23, 8, 3],
            [53, 30, 8, 3],
        ])
            shapes.push([
                [x, y],
                [x + w, y],
                [x + w, y + h],
                [x, y + h],
            ]);
    }
    const blocked = new Set();
    for (let y = 0; y < H; y++)
        for (let x = 0; x < W; x++)
            if (
                x === 0 ||
                y === 0 ||
                x === W - 1 ||
                y === H - 1 ||
                shapes.some((p) => inside(x + 0.5, y + 0.5, p))
            )
                blocked.add(id(x, y));
    return { name, shapes, blocked, start: [5, 25], goal: [75, 25] };
}
export function clearance(blocked) {
    const d = new Float64Array(W * H).fill(99),
        q = [];
    for (const k of blocked) {
        d[k] = 0;
        q.push(k);
    }
    for (let i = 0; i < q.length; i++) {
        const k = q[i],
            [x, y] = point(k);
        for (const [dx, dy] of [
            [1, 0],
            [-1, 0],
            [0, 1],
            [0, -1],
        ]) {
            const a = x + dx,
                b = y + dy,
                n = id(a, b);
            if (a >= 0 && b >= 0 && a < W && b < H && d[n] > d[k] + 1) {
                d[n] = d[k] + 1;
                q.push(n);
            }
        }
    }
    return d;
}
const valid = (p, blocked) =>
    p.length === 2 &&
    p.every(Number.isInteger) &&
    p[0] >= 0 &&
    p[0] < W &&
    p[1] >= 0 &&
    p[1] < H &&
    !blocked.has(id(...p));
export function lineCells(a, b, blocked) {
    const steps = Math.max(
            1,
            Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) * 4),
        ),
        cells = [a];
    let prev = a;
    for (let i = 0; i <= steps; i++) {
        const p = [
            Math.round(a[0] + ((b[0] - a[0]) * i) / steps),
            Math.round(a[1] + ((b[1] - a[1]) * i) / steps),
        ];
        if (!valid(p, blocked)) return null;
        const dx = p[0] - prev[0],
            dy = p[1] - prev[1];
        if (
            dx &&
            dy &&
            (!valid([prev[0] + dx, prev[1]], blocked) ||
                !valid([prev[0], prev[1] + dy], blocked))
        )
            return null;
        if (dx || dy) cells.push(p);
        prev = p;
    }
    return cells;
}
export function route(blocked, start, goal, margin = 3, weight = 2) {
    const field = clearance(blocked),
        penalty = (p) =>
            1 +
            (weight * Math.max(0, margin - field[id(...p)])) /
                Math.max(1, margin);
    if (!valid(start, blocked) || !valid(goal, blocked))
        return { path: [], raw: [], visited: [], cost: null, field };
    const open = [{ p: start, g: 0, f: 0 }],
        best = new Map([[id(...start), 0]]),
        parents = new Map(),
        closed = new Set(),
        visited = [];
    let end = null;
    while (open.length) {
        open.sort((a, b) => a.f - b.f);
        const n = open.shift(),
            k = id(...n.p);
        if (closed.has(k)) continue;
        closed.add(k);
        visited.push(n.p);
        if (k === id(...goal)) {
            end = n;
            break;
        }
        for (let dy = -1; dy <= 1; dy++)
            for (let dx = -1; dx <= 1; dx++) {
                if (!dx && !dy) continue;
                const p = [n.p[0] + dx, n.p[1] + dy];
                if (!valid(p, blocked) || closed.has(id(...p))) continue;
                if (
                    dx &&
                    dy &&
                    (!valid([n.p[0] + dx, n.p[1]], blocked) ||
                        !valid([n.p[0], n.p[1] + dy], blocked))
                )
                    continue;
                const g = n.g + Math.hypot(dx, dy) * penalty(p),
                    key = id(...p);
                if (g >= (best.get(key) ?? Infinity)) continue;
                best.set(key, g);
                parents.set(key, k);
                open.push({
                    p,
                    g,
                    f: g + Math.hypot(goal[0] - p[0], goal[1] - p[1]),
                });
            }
    }
    if (!end) return { path: [], raw: [], visited, cost: null, field };
    const raw = [];
    for (let k = id(...goal); k !== undefined; k = parents.get(k))
        raw.unshift(point(k));
    const cost = (points) =>
        points
            .slice(1)
            .reduce(
                (n, p, i) =>
                    n +
                    Math.hypot(p[0] - points[i][0], p[1] - points[i][1]) *
                        penalty(p),
                0,
            );
    const path = [raw[0]];
    let a = 0;
    while (a < raw.length - 1) {
        let chosen = a + 1;
        for (let b = raw.length - 1; b > a + 1; b--) {
            const cells = lineCells(raw[a], raw[b], blocked);
            if (cells && cost(cells) <= cost(raw.slice(a, b + 1)) * 1.03) {
                chosen = b;
                break;
            }
        }
        path.push(raw[chosen]);
        a = chosen;
    }
    return { path, raw, visited, cost: end.g, field };
}
export function paint(blocked, x, y, radius, erase = false) {
    for (
        let b = Math.max(1, y - radius);
        b < Math.min(H - 1, y + radius + 1);
        b++
    )
        for (
            let a = Math.max(1, x - radius);
            a < Math.min(W - 1, x + radius + 1);
            a++
        )
            if (Math.hypot(a - x, b - y) <= radius)
                erase ? blocked.delete(id(a, b)) : blocked.add(id(a, b));
}
