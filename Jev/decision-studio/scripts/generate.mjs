import { mkdir, writeFile } from 'node:fs/promises';
import { experimental_composeSpec } from '@json-render/core';
import { OpenRouter } from '@openrouter/sdk';
import { createOpenRouterEvaluator, model } from './openrouter-evaluator.mjs';
import { catalog, scenarios, sourceCommit } from '../src/catalog.mjs';

// This script runs only in CI/server context. Never import it from the browser.
if(!process.env.JEV_OPENROUTER_API_KEY) throw new Error('JEV_OPENROUTER_API_KEY is required server-side.');
const receipts=[];
const evaluate=createOpenRouterEvaluator(new OpenRouter({apiKey:process.env.JEV_OPENROUTER_API_KEY}),receipts);
const runs=[];
async function compose(scenario, prompt, initialSpec) {
  const events=[];
  for await (const event of experimental_composeSpec({catalog,candidates:scenario.candidates,prompt,context:scenario.context,initialSpec,evaluate,maxSteps:14,maxElements:12,maxDepth:3,signal:AbortSignal.timeout(90000)})) events.push(event);
  const result=events.at(-1);
  if(result?.type!=='complete'||result.stopReason!=='finish'||!result.spec) throw new Error('Composition did not finish; refusing to publish an incomplete recording.');
  if(!catalog.validate(result.spec).success) throw new Error('Invalid generated spec.');
  return {prompt,events,spec:result.spec,elapsedMs:result.elapsedMs,inputTokens:result.inputTokens,stopReason:result.stopReason};
}
for(const scenario of scenarios) {
  console.log('Composing scenario:',scenario.id);
  const original=await compose(scenario,scenario.prompt);
  const versions=[{id:'original',label:'Composición inicial',...original}];
  for(const edit of scenario.edits) versions.push({id:edit.id,label:edit.label,...await compose(scenario,edit.prompt,original.spec)});
  runs.push({id:scenario.id,versions});
}
await mkdir('public',{recursive:true});
await writeFile('public/runs.json',JSON.stringify({model,transport:'OpenRouter Decisions',generatedAt:new Date().toISOString(),sourceCommit,dataKind:'synthetic',mode:'recorded',receipts,runs},null,2));
console.log('Generated',runs.length,'scenarios with original + edited real Jev compositions.');
