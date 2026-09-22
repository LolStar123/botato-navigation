import test from "node:test";
import assert from "node:assert/strict";
import { makeMap, route, lineCells, paint, W, H } from "./model.mjs";
test("all terrain presets have a real collision-free route", () => {
    for (const name of ["quarry", "ravine", "ruins"]) {
        const m = makeMap(name),
            r = route(m.blocked, m.start, m.goal);
        assert.ok(r.path.length > 1, name);
        for (let i = 1; i < r.path.length; i++)
            assert.ok(lineCells(r.path[i - 1], r.path[i], m.blocked), name);
        assert.deepEqual(r.path.at(-1), m.goal);
    }
});
test("unreachable and blocked destinations never claim success", () => {
    const b = new Set();
    for (let y = 0; y < H; y++) b.add(y * W + 40);
    assert.equal(route(b, [5, 25], [75, 25]).path.length, 0);
    b.add(25 * W + 5);
    assert.equal(route(b, [5, 25], [5, 25]).path.length, 0);
});
test("no diagonal corner cutting during smoothing", () => {
    assert.equal(lineCells([5, 5], [6, 6], new Set([5 * W + 6])), null);
});
test("paint and erase change actual occupancy and route", () => {
    const m = makeMap("quarry");
    paint(m.blocked, ...m.goal, 2);
    assert.equal(route(m.blocked, m.start, m.goal).path.length, 0);
    paint(m.blocked, ...m.goal, 2, true);
    assert.ok(route(m.blocked, m.start, m.goal).path.length);
});
