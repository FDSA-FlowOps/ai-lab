// json-render's model-neutral evaluator backed by OpenRouter's native Decisions API.
export const model = 'typesafe/jev-1.13';
export function createOpenRouterEvaluator(client, receipts = []) {
  return async ({state, questions, signal}) => {
    signal.throwIfAborted();
    let result;
    try {
      result = await client.alpha.decisions.create({decisionsRequest:{model,state,questions}}, {
        signal:AbortSignal.any([signal,AbortSignal.timeout(30000)]), retries:{strategy:'none'},
      });
    } catch(error) {
      // SDK error objects may contain request metadata. Never print the object.
      throw new Error(`OpenRouter Decisions failed (status ${Number(error.statusCode)||'unavailable'}; ${error.name||'request error'}).`);
    }
    const answers={};
    for(const [name,question] of Object.entries(questions)) {
      const answer=result.answers[name];
      if(answer?.type!=='choice'||!Object.hasOwn(question.criteria,answer.choice)) throw new Error('OpenRouter returned an answer outside the offered choices.');
      const confidence=answer.confidence;
      if(confidence!==undefined&&(!Number.isFinite(confidence)||confidence<0||confidence>1)) throw new Error('Invalid decision confidence.');
      answers[name]={choice:answer.choice,...(confidence===undefined?{}:{confidence})};
    }
    receipts.push({id:result.id??null,model:result.model,provider:result.provider??null,inputTokens:result.usage.inputTokens,cost:result.usage.cost??null});
    return {answers,usage:{inputTokens:result.usage.inputTokens}};
  };
}
