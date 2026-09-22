import test from 'node:test';
import assert from 'node:assert/strict';
import * as m from './model.mjs';
test('workflow invariants and boundary cases',()=>{
assert.equal(m.astar(3,3,new Set(['1,0','0,1']),[0,0],[2,2]).path.length,0);const r=m.astar(5,5,new Set(),[0,0],[4,4]);assert.equal(r.path.length,5);assert.deepEqual(r.path.at(-1),[4,4]);assert.equal(m.run({...m.defaults,extraWall:true}).artifact.path.length,0);assert.ok(m.run(m.defaults).artifact.path.length>0);
});
