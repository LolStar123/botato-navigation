export const defaults = {
  goalX: 22,
  goalY: 12,
  extraWall: false,
  width: 25,
  height: 16,
  start: [2, 3],
  rocks: [
    [7, 2, 3, 8],
    [13, 6, 4, 3],
    [18, 1, 3, 4],
    [4, 12, 8, 2],
  ],
};
export const controls = [
  {
    key: "goalX",
    label: "Destination x",
    type: "number",
    min: 0,
    max: 24,
    step: 1,
  },
  {
    key: "goalY",
    label: "Destination y",
    type: "number",
    min: 0,
    max: 15,
    step: 1,
  },
  { key: "extraWall", label: "Block the central passage", type: "checkbox" },
];
export function astar(width, height, blocked, start, goal) {
  const key = (x, y) => x + "," + y,
    valid = ([x, y]) =>
      Number.isInteger(x) &&
      Number.isInteger(y) &&
      x >= 0 &&
      y >= 0 &&
      x < width &&
      y < height &&
      !blocked.has(key(x, y));
  if (!valid(start) || !valid(goal)) return { path: [], expanded: 0 };
  const nodes = new Map(),
    open = [{ p: start, g: 0, f: 0, parent: null }],
    closed = new Set();
  nodes.set(key(...start), open[0]);
  let expanded = 0;
  while (open.length) {
    open.sort((a, b) => a.f - b.f || a.g - b.g);
    const node = open.shift(),
      id = key(...node.p);
    if (closed.has(id)) continue;
    closed.add(id);
    expanded++;
    if (id === key(...goal)) {
      const path = [];
      for (let n = node; n; n = n.parent) path.unshift(n.p);
      return { path, expanded };
    }
    for (let dx = -1; dx <= 1; dx++)
      for (let dy = -1; dy <= 1; dy++) {
        if (!dx && !dy) continue;
        const p = [node.p[0] + dx, node.p[1] + dy];
        if (!valid(p) || closed.has(key(...p))) continue;
        if (
          dx &&
          dy &&
          (!valid([node.p[0] + dx, node.p[1]]) ||
            !valid([node.p[0], node.p[1] + dy]))
        )
          continue;
        const g = node.g + Math.hypot(dx, dy),
          old = nodes.get(key(...p));
        if (old && g >= old.g) continue;
        const next = {
          p,
          g,
          f: g + Math.hypot(goal[0] - p[0], goal[1] - p[1]),
          parent: node,
        };
        nodes.set(key(...p), next);
        open.push(next);
      }
  }
  return { path: [], expanded };
}
export function run(i) {
  const { width: w, height: h } = i;
  if (
    !Number.isInteger(w) ||
    !Number.isInteger(h) ||
    w < 1 ||
    h < 1 ||
    w * h > 10000
  )
    throw Error("Use a bounded positive integer grid.");
  const blocks = new Set();
  for (const [x, y, dx, dy] of i.rocks)
    for (let a = x; a < x + dx; a++)
      for (let b = y; b < y + dy; b++) blocks.add(a + "," + b);
  if (i.extraWall) for (let y = 0; y < h; y++) blocks.add("11," + y);
  const r = astar(w, h, blocks, i.start, [i.goalX, i.goalY]);
  return {
    summary: r.path.length
      ? "A traversable route to the destination"
      : "No route: the destination is blocked or disconnected",
    metrics: {
      "route steps": Math.max(0, r.path.length - 1),
      "nodes explored": r.expanded,
      "blocked cells": blocks.size,
    },
    columns: ["step", "x", "y"],
    rows: r.path.map((p, n) => [n, ...p]),
    grid: {
      width: w,
      height: h,
      blocked: [...blocks].map((s) => s.split(",").map(Number)),
      start: i.start,
      goal: [i.goalX, i.goalY],
      path: r.path,
    },
    steps: [
      "Read occupied terrain cells",
      "Search by travelled cost plus distance to target",
      "Reject diagonal corner cuts",
      "Recalculate when the route becomes blocked",
    ],
    artifact: r,
  };
}
