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

function matchesAny(filePath, patterns) {
  return patterns.some((pattern) => globToRegExp(pattern).test(filePath));
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

const changedFilesPath = process.argv[2];
const catalogPath = process.argv[3];

if (!changedFilesPath || !catalogPath) {
  console.error("Usage: select-agents.js <changed-files.txt> <catalog.json>");
  process.exit(1);
}

const changedFiles = fs
  .readFileSync(changedFilesPath, "utf8")
  .split(/\r?\n/)
  .map((line) => line.trim())
  .filter(Boolean);

const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const selectedAgents = (catalog.agents || [])
  .filter((agent) => matchesAnyForAgent(agent, changedFiles))
  .sort((a, b) => (b.priority || 0) - (a.priority || 0));

function matchesAnyForAgent(agent, files) {
  const patterns = Array.isArray(agent.match) ? agent.match : [];
  if (patterns.length === 0) {
    return false;
  }

  return files.some((file) => matchesAny(file, patterns));
}

const output = {
  catalog_version: catalog.version || 1,
  changed_files_count: changedFiles.length,
  detected_areas: inferAreas(changedFiles),
  selected_agents: selectedAgents.map((agent) => ({
    id: agent.id,
    description: agent.description || "",
    priority: agent.priority || 0,
    reason: `Matched patterns: ${(agent.match || []).join(", ")}`
  }))
};

fs.writeFileSync("selected-agents.json", `${JSON.stringify(output, null, 2)}\n`);

const lines = [];
lines.push("# Selected Agents");
lines.push(`Changed files: ${changedFiles.length}`);
lines.push(
  `Detected areas: ${output.detected_areas.length > 0 ? output.detected_areas.join(", ") : "none"}`
);
lines.push("");

if (output.selected_agents.length === 0) {
  lines.push("- No agents matched the changed files.");
} else {
  for (const agent of output.selected_agents) {
    lines.push(`- ${agent.id} (priority ${agent.priority}): ${agent.description}`);
    lines.push(`  ${agent.reason}`);
  }
}

fs.writeFileSync("selected-agents.md", `${lines.join("\n")}\n`);
