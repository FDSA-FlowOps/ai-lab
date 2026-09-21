import test from 'node:test';
import assert from 'node:assert/strict';
import {catalog,scenarios} from '../src/catalog.mjs';
test('Every prepared candidate respects the catalog and only Board may be root',()=>{
  for(const scene of scenarios) {
    assert.equal(new Set(scene.candidates.map(c=>c.id)).size,scene.candidates.length);
    assert.equal(scene.candidates.filter(c=>c.root).length,1);
    for(const c of scene.candidates) {
      assert.ok(catalog.data.components[c.element.type].props.safeParse(c.element.props).success,c.id);
      assert.equal(c.root,c.element.type==='Board');
    }
  }
});
test('No candidate may execute an action or inject HTML',()=>{
  for(const scene of scenarios) for(const c of scene.candidates) {
    assert.equal(c.element.on,undefined);
    assert.equal(c.element.props.dangerouslySetInnerHTML,undefined);
  }
});
