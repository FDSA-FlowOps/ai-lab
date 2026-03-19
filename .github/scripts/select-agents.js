#!/usr/bin/env node

const fs = require("fs");

function globToRegExp(pattern) {
  let regex = "^";
  for (let i = 0; i < pattern.length; i += 1) {
    const ch = pattern[i];
    const next = pattern[i + 1];

    if (ch === "*") {
      if (next === "*") {
        regex += ".*";
        i += 1;
      } else {
        regex += "[^/]*";
      }
      continue;
    }

    if (ch === "?") {
      regex += ".";
      continue;
    }

    if ("\\.[]{}()+-^$|".includes(ch)) {
      regex += `\\${ch}`;
      continue;
    }

    regex += ch;
  }

  regex += "$";
  return new RegExp(regex);
}

function matchesAny(value, patterns) {
  return patterns.some((pattern) => globToRegExp(pattern).test(value));
}

function inferAreas(files) {
  const areas = new Set();

  for (const file of files) {
    if (/\.cs$|\.csproj$|\.sln$/i.test(file)) {
      areas.add("dotnet");
      areas.add("backend");
    }

    if (/^api\/|\/api\/|controllers?\/|endpoints?\//i.test(file)) {
      areas.add("api");
    }

    if (/^\.github\/workflows\/|^\.github\/scripts\//i.test(file)) {
      areas.add("automation");
    }

    if (/^MCP\/|^WebMCP\//i.test(file)) {
      areas.add("mcp");
    }

    if (/^RAG\//i.test(file)) {
      areas.add("rag");
    }
  }

  return [...areas].sort();
}

function readJsonIfExists(path) {
  if (!fs.existsSync(path)) {
    return null;
  }

  return JSON.parse(fs.readFileSync(path, "utf8"));
}

function classifySize(totalChangedLines, policy) {
  if (totalChangedLines <= (policy.tiny_max_changed_lines ?? 10)) {
    return "tiny";
  }

  if (totalChangedLines <= (policy.small_max_changed_lines ?? 40)) {
    return "small";
  }

  if (totalChangedLines <= (policy.medium_max_changed_lines ?? 200)) {
    return "medium";
  }

  return "large";
}

function getMaxAgents(size, policy) {
  const bySize = policy.max_agents_by_size || {};
  return bySize[size] ?? policy.max_agents_per_pr ?? 3;
}

function buildCandidate(agent, changedFiles, metrics, policy) {
  const matchPatterns = Array.isArray(agent.match) ? agent.match : [];
  const criticalPatterns = Array.isArray(agent.critical_paths) ? agent.critical_paths : [];
  const matchedByPatterns = changedFiles.some((file) => matchesAny(file, matchPatterns));
  const matchedCriticalPaths = changedFiles.filter((file) => matchesAny(file, criticalPatterns));
  const forcedByCritical = matchedCriticalPaths.length > 0;

  if (!matchedByPatterns && !forcedByCritical) {
    return { state: "discarded", id: agent.id, reason: "Sin coincidencia con patrones ni rutas criticas." };
  }

  if (agent.enabled === false) {
    return { state: "discarded", id: agent.id, reason: "Agente deshabilitado en la configuracion." };
  }

  const minLinesChanged = agent.min_lines_changed ?? 0;
  const minFilesChanged = agent.min_files_changed ?? 0;

  if (!forcedByCritical && metrics.total_changed_lines < minLinesChanged) {
    return {
      state: "discarded",
      id: agent.id,
      reason: `No alcanza el minimo de lineas modificadas (${metrics.total_changed_lines} < ${minLinesChanged}).`
    };
  }

  if (!forcedByCritical && metrics.files_changed < minFilesChanged) {
    return {
      state: "discarded",
      id: agent.id,
      reason: `No alcanza el minimo de ficheros modificados (${metrics.files_changed} < ${minFilesChanged}).`
    };
  }

  return {
    state: "candidate",
    id: agent.id,
    description: agent.description || "",
    priority: agent.priority || 0,
    group: agent.group || null,
    primary: agent.primary !== false,
    forced_by_critical: forcedByCritical,
    critical_matches: matchedCriticalPaths,
    reason: forcedByCritical
      ? `Seleccionado por rutas criticas: ${matchedCriticalPaths.join(", ")}`
      : `Coincide con patrones: ${matchPatterns.join(", ")}`
  };
}

const changedFilesPath = process.argv[2];
const catalogPath = process.argv[3];

if (!changedFilesPath || !catalogPath) {
  console.error("Uso: select-agents.js <changed-files.txt> <catalog.json>");
  process.exit(1);
}

const changedFiles = fs
  .readFileSync(changedFilesPath, "utf8")
  .split(/\r?\n/)
  .map((line) => line.trim())
  .filter(Boolean);

const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const policy = catalog.policy || {};
const rawMetrics = readJsonIfExists("change-metrics.json") || {};
const metrics = {
  files_changed: rawMetrics.files_changed ?? changedFiles.length,
  lines_added: rawMetrics.lines_added ?? 0,
  lines_deleted: rawMetrics.lines_deleted ?? 0,
  total_changed_lines: rawMetrics.total_changed_lines ?? 0
};
const prSize = classifySize(metrics.total_changed_lines, policy);
const areasDetectadas = inferAreas(changedFiles);

const appliedRules = [];
const discardedAgents = [];

let candidates = (catalog.agents || []).map((agent) => buildCandidate(agent, changedFiles, metrics, policy));
for (const item of candidates) {
  if (item.state === "discarded") {
    discardedAgents.push({ id: item.id, motivo: item.reason });
  }
}

candidates = candidates.filter((item) => item.state === "candidate");

if (prSize === "tiny" && policy.tiny_primary_only) {
  const before = candidates.length;
  candidates = candidates.filter((item) => item.primary || item.forced_by_critical);
  if (candidates.length !== before) {
    appliedRules.push("PR tiny: solo se mantienen agentes primarios o forzados por rutas criticas.");
  }
}

if (prSize === "small" && policy.small_primary_only) {
  const before = candidates.length;
  candidates = candidates.filter((item) => item.primary || item.forced_by_critical);
  if (candidates.length !== before) {
    appliedRules.push("PR small: solo se mantienen agentes primarios o forzados por rutas criticas.");
  }
}

candidates.sort((a, b) => {
  if (a.forced_by_critical !== b.forced_by_critical) {
    return a.forced_by_critical ? -1 : 1;
  }

  return b.priority - a.priority;
});

const byGroup = new Map();
for (const candidate of candidates) {
  const key = candidate.group || `__${candidate.id}`;
  const existing = byGroup.get(key);
  if (!existing) {
    byGroup.set(key, candidate);
    continue;
  }

  const winner =
    candidate.forced_by_critical && !existing.forced_by_critical
      ? candidate
      : !candidate.forced_by_critical && existing.forced_by_critical
        ? existing
        : candidate.priority > existing.priority
          ? candidate
          : existing;

  const loser = winner.id === candidate.id ? existing : candidate;
  discardedAgents.push({
    id: loser.id,
    motivo: `Descartado por exclusividad del grupo '${key}' frente a '${winner.id}'.`
  });
  byGroup.set(key, winner);
}

let selectedAgents = [...byGroup.values()].sort((a, b) => {
  if (a.forced_by_critical !== b.forced_by_critical) {
    return a.forced_by_critical ? -1 : 1;
  }

  return b.priority - a.priority;
});

const maxAgents = getMaxAgents(prSize, policy);
const forcedAgents = selectedAgents.filter((agent) => agent.forced_by_critical);
const normalAgents = selectedAgents.filter((agent) => !agent.forced_by_critical);

if (policy.critical_paths_override_limits && forcedAgents.length > maxAgents) {
  appliedRules.push(
    `Rutas criticas activas: se permite superar el limite normal de agentes (${maxAgents}) para conservar agentes forzados.`
  );
  selectedAgents = forcedAgents;
} else {
  const remainingSlots = Math.max(0, maxAgents - forcedAgents.length);
  const keptNormalAgents = normalAgents.slice(0, remainingSlots);
  const discardedByLimit = normalAgents.slice(remainingSlots);
  for (const agent of discardedByLimit) {
    discardedAgents.push({
      id: agent.id,
      motivo: `Descartado por limite maximo de agentes para PR ${prSize} (${maxAgents}).`
    });
  }
  selectedAgents = [...forcedAgents, ...keptNormalAgents];
}

appliedRules.push(`Tamano estimado del cambio: ${prSize}.`);
appliedRules.push(`Limite maximo de agentes aplicado: ${maxAgents}.`);

const output = {
  version_catalogo: catalog.version || 1,
  tamano_pr: prSize,
  metricas: metrics,
  areas_detectadas: areasDetectadas,
  reglas_aplicadas: appliedRules,
  agentes_descartados: discardedAgents,
  agentes_seleccionados: selectedAgents.map((agent) => ({
    id: agent.id,
    descripcion: agent.description,
    prioridad: agent.priority,
    grupo: agent.group,
    primario: agent.primary,
    forzado_por_ruta_critica: agent.forced_by_critical,
    motivo: agent.reason
  }))
};

fs.writeFileSync("selected-agents.json", `${JSON.stringify(output, null, 2)}\n`);

const lines = [];
lines.push("# Agentes seleccionados");
lines.push(`Tamano estimado de la PR: ${output.tamano_pr}`);
lines.push(`Ficheros modificados: ${metrics.files_changed}`);
lines.push(`Lineas anadidas: ${metrics.lines_added}`);
lines.push(`Lineas eliminadas: ${metrics.lines_deleted}`);
lines.push(`Lineas totales modificadas: ${metrics.total_changed_lines}`);
lines.push(`Areas detectadas: ${areasDetectadas.length > 0 ? areasDetectadas.join(", ") : "ninguna"}`);
lines.push("");
lines.push("## Reglas aplicadas");
for (const rule of appliedRules) {
  lines.push(`- ${rule}`);
}
lines.push("");
lines.push("## Agentes finales");
if (output.agentes_seleccionados.length === 0) {
  lines.push("- Ningun agente coincide con los ficheros modificados tras aplicar las restricciones.");
} else {
  for (const agent of output.agentes_seleccionados) {
    lines.push(`- ${agent.id} (prioridad ${agent.prioridad}): ${agent.descripcion}`);
    lines.push(`  ${agent.motivo}`);
  }
}
lines.push("");
lines.push("## Agentes descartados");
if (discardedAgents.length === 0) {
  lines.push("- Ninguno");
} else {
  for (const agent of discardedAgents) {
    lines.push(`- ${agent.id}: ${agent.motivo}`);
  }
}

fs.writeFileSync("selected-agents.md", `${lines.join("\n")}\n`);
