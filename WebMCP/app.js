const supportBadge = document.querySelector("#support-badge");
const taskForm = document.querySelector("#task-form");
const taskInput = document.querySelector("#task-input");
const taskList = document.querySelector("#task-list");
const logEl = document.querySelector("#log");

let nextId = 1;
const tasks = [];
const toolRegistrations = [];

function writeLog(message) {
  const timestamp = new Date().toLocaleTimeString("es-ES", { hour12: false });
  logEl.textContent += `[${timestamp}] ${message}\n`;
  logEl.scrollTop = logEl.scrollHeight;
}

function renderTasks() {
  taskList.innerHTML = "";

  if (tasks.length === 0) {
    const empty = document.createElement("li");
    empty.textContent = "No hay tareas todavia.";
    taskList.appendChild(empty);
    return;
  }

  for (const task of tasks) {
    const li = document.createElement("li");
    li.className = `task-item${task.done ? " done" : ""}`;

    const title = document.createElement("span");
    title.className = "task-title";
    title.textContent = `#${task.id} ${task.title}`;

    li.appendChild(title);

    if (!task.done) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "small";
      btn.textContent = "Marcar hecha";
      btn.addEventListener("click", () => {
        completeTask(task.id);
      });
      li.appendChild(btn);
    }

    taskList.appendChild(li);
  }
}

function addTask(title) {
  const value = String(title || "").trim();
  if (!value) {
    throw new Error("El texto de la tarea esta vacio.");
  }

  const created = {
    id: nextId++,
    title: value,
    done: false
  };
  tasks.push(created);
  renderTasks();
  return created;
}

function completeTask(id) {
  const task = tasks.find((t) => t.id === Number(id));
  if (!task) {
    throw new Error(`No existe la tarea con id ${id}.`);
  }
  task.done = true;
  renderTasks();
  return task;
}

function listTasks() {
  return tasks.map((t) => ({ ...t }));
}

function toToolResult(text, extra = {}) {
  return {
    content: [{ type: "text", text }],
    ...extra
  };
}

function getTools() {
  return [
    {
      name: "add_task",
      description: "Crea una nueva tarea en la lista local.",
      inputSchema: {
        type: "object",
        properties: {
          title: {
            type: "string",
            description: "Texto corto de la tarea."
          }
        },
        required: ["title"],
        additionalProperties: false
      },
      async execute({ title }) {
        const created = addTask(title);
        writeLog(`Tool add_task -> creada tarea #${created.id}`);
        return toToolResult(`Tarea creada: #${created.id} "${created.title}"`, {
          structuredContent: created
        });
      }
    },
    {
      name: "list_tasks",
      description: "Devuelve todas las tareas con su estado actual.",
      inputSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      },
      async execute() {
        const snapshot = listTasks();
        writeLog(`Tool list_tasks -> ${snapshot.length} tareas`);
        return toToolResult(`Hay ${snapshot.length} tarea(s).`, {
          structuredContent: snapshot
        });
      }
    },
    {
      name: "complete_task",
      description: "Marca como completada una tarea por id.",
      inputSchema: {
        type: "object",
        properties: {
          id: {
            type: "number",
            description: "ID numerico de la tarea."
          }
        },
        required: ["id"],
        additionalProperties: false
      },
      async execute({ id }) {
        const done = completeTask(id);
        writeLog(`Tool complete_task -> tarea #${done.id} completada`);
        return toToolResult(`Tarea completada: #${done.id}`, {
          structuredContent: done
        });
      }
    }
  ];
}

function setSupport(available, message) {
  supportBadge.textContent = message;
  supportBadge.classList.remove("ok", "warn");
  supportBadge.classList.add(available ? "ok" : "warn");
}

function registerWebMcpTools() {
  try {
    const modelContext = navigator.modelContext;
    if (!modelContext) {
      setSupport(
        false,
        "Sin soporte nativo detectado. Usa navegador compatible o bridge WebMCP."
      );
      writeLog("WebMCP no esta disponible en navigator.modelContext.");
      return;
    }

    const tools = getTools();

    if (typeof modelContext.registerTool === "function") {
      for (const tool of tools) {
        const registration = modelContext.registerTool(tool);
        if (registration && typeof registration.unregister === "function") {
          toolRegistrations.push(registration);
        }
      }
      setSupport(true, "WebMCP activo mediante registerTool().");
      writeLog(`Registradas ${tools.length} herramientas con registerTool().`);
      return;
    }

    if (typeof modelContext.provideContext === "function") {
      modelContext.provideContext({ tools });
      setSupport(true, "WebMCP activo mediante provideContext().");
      writeLog(`Publicadas ${tools.length} herramientas con provideContext().`);
      return;
    }

    setSupport(false, "Se detecto modelContext, pero sin API de registro usable.");
    writeLog("modelContext presente, pero no expone registerTool/provideContext.");
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Error desconocido al registrar WebMCP.";
    setSupport(false, "Error al inicializar WebMCP (revisa bridge/cliente MCP).");
    writeLog(`Error WebMCP: ${message}`);
  }
}

taskForm.addEventListener("submit", (event) => {
  event.preventDefault();
  try {
    const created = addTask(taskInput.value);
    taskInput.value = "";
    writeLog(`UI -> creada tarea #${created.id}`);
  } catch (error) {
    writeLog(`UI error: ${error.message}`);
  }
});

window.addEventListener("beforeunload", () => {
  for (const registration of toolRegistrations) {
    registration.unregister();
  }
});

renderTasks();
registerWebMcpTools();
