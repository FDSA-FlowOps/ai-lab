import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {catalog,scenarios,sourceCommit} from '../src/catalog.mjs';
const data=JSON.parse(await readFile('public/runs.json','utf8'));
assert.equal(data.model,'typesafe/jev-1.13');
assert.equal(data.sourceCommit,sourceCommit);
for(const scene of scenarios){
  const run=data.runs.find(r=>r.id===scene.id);
  assert.ok(run,scene.id);
  assert.equal(run.versions.length,scene.edits.length+1);
  for(const v of run.versions){
    assert.equal(v.stopReason,'finish');
    assert.ok(catalog.validate(v.spec).success);
    assert.ok(Object.keys(v.spec.elements).length>1,'Root-only output is not a useful dashboard');
    assert.ok(v.events.some(e=>e.type==='step'));
    assert.ok(v.elapsedMs>0);
  }
}
console.log('Validated 3 real recordings and their edits.');
