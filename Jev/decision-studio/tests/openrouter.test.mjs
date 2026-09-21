import test from 'node:test';
import assert from 'node:assert/strict';
import {createOpenRouterEvaluator,model} from '../scripts/openrouter-evaluator.mjs';
const request={state:{task:'test'},questions:{root:{type:'choice',instructions:'Pick',criteria:{board:'Root'}}},signal:new AbortController().signal};
test('Native Decisions response is mapped to composer answers and usage',async()=>{
 const receipts=[];
 const client={alpha:{decisions:{create:async r=>{
   assert.equal(r.decisionsRequest.model,model);
   assert.deepEqual(r.decisionsRequest.questions,request.questions);
   return {answers:{root:{type:'choice',choice:'board',confidence:.8}},model,usage:{inputTokens:123,cost:.00001}};
 }}}};
 const result=await createOpenRouterEvaluator(client,receipts)(request);
 assert.deepEqual(result,{answers:{root:{choice:'board',confidence:.8}},usage:{inputTokens:123}});
 assert.equal(receipts[0].cost,.00001);
});
test('Unknown choices are rejected rather than silently rendered',async()=>{
 const client={alpha:{decisions:{create:async()=>({answers:{root:{type:'choice',choice:'invented'}}})}}};
 await assert.rejects(createOpenRouterEvaluator(client)(request),/outside the offered/);
});
test('SDK errors never leak raw request or credential metadata',async()=>{
 const client={alpha:{decisions:{create:async()=>{throw Object.assign(new Error('sensitive request metadata'),{statusCode:403})}}}};
 await assert.rejects(createOpenRouterEvaluator(client)(request),e=>e.message.includes('403')&&!e.message.includes('sensitive'));
});
