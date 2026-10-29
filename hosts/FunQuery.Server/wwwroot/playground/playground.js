(() => {
  "use strict";

  const $ = (id) => document.getElementById(id);
  const STATE_KEY = "funquery.playground.state.v1";
  const HISTORY_KEY = "funquery.playground.history.v1";

  const config = {
    exposeSourceNames: false,
    sources: [],
    examples: [],
  };

  function setStatus(text) {
    $("status").textContent = text;
  }

  function readJson(key, fallback) {
    try {
      const raw = localStorage.getItem(key);
      return raw ? JSON.parse(raw) : fallback;
    } catch {
      return fallback;
    }
  }

  function writeJson(key, value) {
    try {
      localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // Storage can be unavailable (private mode); the playground still works.
    }
  }

  function readHistory() {
    const value = readJson(HISTORY_KEY, []);
    return Array.isArray(value) ? value : [];
  }

  function persist() {
    writeJson(STATE_KEY, {
      query: $("query").value,
      source: $("source").value,
      variables: $("variables").value,
    });
    updateRequestPreview();
  }

  function restore() {
    const saved = readJson(STATE_KEY, null);
    if (!saved || typeof saved !== "object") return false;

    $("query").value = typeof saved.query === "string" ? saved.query : "";
    $("source").value = typeof saved.source === "string" ? saved.source : "";
    $("variables").value = typeof saved.variables === "string" ? saved.variables : "";

    return $("query").value.length > 0;
  }

  function addSourceOptions() {
    const list = $("source-list");
    list.replaceChildren();

    for (const name of config.sources) {
      const option = document.createElement("option");
      option.value = name;
      list.append(option);
    }
  }

  function addExampleOptions() {
    const select = $("examples");
    select.replaceChildren(new Option("Custom…", ""));

    config.examples.forEach((example, index) => {
      select.append(new Option(example.name, String(index)));
    });
  }

  function applyExample(index) {
    const example = config.examples[index];
    if (!example) return;

    $("query").value = example.query;
    $("source").value = example.source || "";
    $("variables").value = example.variablesJson || "";
    $("examples").value = String(index);
    persist();
  }

  function applyHistory(index) {
    const entry = readHistory()[index];
    if (!entry) return;

    $("query").value = entry.query || "";
    $("source").value = entry.source || "";
    $("variables").value = entry.variables ? JSON.stringify(entry.variables, null, 2) : "";
    $("history").value = "";
    persist();
  }

  function addHistoryOptions() {
    const select = $("history");
    select.replaceChildren(new Option("History…", ""));

    readHistory().forEach((entry, index) => {
      const query = typeof entry.query === "string" ? entry.query : "";
      const label = query.length > 64 ? query.slice(0, 61) + "…" : query;
      select.append(new Option(label, String(index)));
    });
  }

  function remember(body) {
    const entry = {
      query: body.query,
      source: body.source || "",
      variables: body.variables ?? null,
    };

    const same = (item) =>
      item.query === entry.query &&
      (item.source || "") === entry.source &&
      JSON.stringify(item.variables ?? null) === JSON.stringify(entry.variables ?? null);

    const next = [entry, ...readHistory().filter((item) => !same(item))].slice(0, 20);
    writeJson(HISTORY_KEY, next);
    addHistoryOptions();
  }

  function queryUrl() {
    return new URL("/query", window.location.href).href;
  }

  function buildRequest() {
    const query = $("query").value.trim();
    if (!query) {
      return { error: "Enter a query first." };
    }

    const body = { query };
    const source = $("source").value.trim();
    if (source) body.source = source;

    const variablesText = $("variables").value.trim();
    if (variablesText) {
      let variables;
      try {
        variables = JSON.parse(variablesText);
      } catch {
        return { error: "Variables must be valid JSON." };
      }

      if (!variables || typeof variables !== "object" || Array.isArray(variables)) {
        return { error: "Variables must be a JSON object." };
      }

      body.variables = variables;
    }

    return { body };
  }

  function updateRequestPreview(request = buildRequest()) {
    $("request-url").textContent = queryUrl();

    const body = $("request-body");
    if (request.error) {
      body.textContent = request.error;
      body.classList.add("invalid");
      return;
    }

    body.classList.remove("invalid");
    body.textContent = JSON.stringify(request.body, null, 2);
  }

  function shellQuote(value) {
    return "'" + value.replace(/'/g, "'\\''") + "'";
  }

  function curlCommand(request) {
    const body = JSON.stringify(request.body);
    return [
      "curl -X POST " + shellQuote(queryUrl()),
      "  -H 'Content-Type: application/json'",
      "  -d " + shellQuote(body),
    ].join(" \\\n");
  }

  function fallbackCopy(text) {
    const area = document.createElement("textarea");
    area.className = "clipboard-fallback";
    area.value = text;
    area.setAttribute("readonly", "");
    area.setAttribute("aria-hidden", "true");
    document.body.append(area);
    area.select();

    let copied = false;
    try {
      copied = document.execCommand("copy");
    } catch {
      copied = false;
    }

    area.remove();
    return copied;
  }

  async function copyText(text, label) {
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(text);
        setStatus(label + " copied");
        return;
      }
    } catch {
      // Fall through to the textarea fallback.
    }

    setStatus(fallbackCopy(text) ? label + " copied" : "Copy failed");
  }

  function copyCurrent(kind) {
    const request = buildRequest();
    if (request.error) {
      updateRequestPreview(request);
      showError({ code: "INVALID_REQUEST", message: request.error });
      return;
    }

    updateRequestPreview(request);
    const text = kind === "curl"
      ? curlCommand(request)
      : JSON.stringify(request.body, null, 2);

    copyText(text, kind === "curl" ? "curl" : "POST body");
  }

  function showResult(payload) {
    const output = $("result");
    output.classList.remove("error");
    output.textContent = JSON.stringify(payload, null, 2);
  }

  function showError(payload) {
    const output = $("result");
    output.classList.add("error");

    if (!payload || typeof payload !== "object") {
      output.textContent = String(payload);
      return;
    }

    const parts = [];
    if (payload.code) parts.push("[" + payload.code + "]");
    if (payload.message) parts.push(payload.message);

    if (typeof payload.position === "number") {
      const length = typeof payload.length === "number" ? payload.length : 0;
      parts.push("(position " + payload.position + (length ? ", length " + length : "") + ")");

      const query = $("query");
      query.focus();
      query.setSelectionRange(payload.position, payload.position + length);
    }

    output.textContent = parts.length ? parts.join(" ") : JSON.stringify(payload, null, 2);
  }

  async function run() {
    const request = buildRequest();
    if (request.error) {
      updateRequestPreview(request);
      showError({ code: "INVALID_REQUEST", message: request.error });
      return;
    }

    updateRequestPreview(request);
    setStatus("Running…");
    const started = performance.now();

    try {
      const response = await fetch("/query", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request.body),
      });

      const text = await response.text();
      const elapsed = Math.round(performance.now() - started);
      setStatus(response.status + " · " + elapsed + " ms");

      let payload;
      try {
        payload = text ? JSON.parse(text) : null;
      } catch {
        payload = text;
      }

      if (response.ok) {
        showResult(payload);
        remember(request.body);
      } else {
        showError(payload);
      }
    } catch (error) {
      setStatus("Network error");
      showError({ code: "NETWORK_ERROR", message: String(error) });
    }
  }

  async function loadConfig() {
    try {
      const response = await fetch("/playground/config");
      if (!response.ok) throw new Error("config unavailable");

      const payload = await response.json();
      config.exposeSourceNames = Boolean(payload.exposeSourceNames);
      config.sources = Array.isArray(payload.sources) ? payload.sources : [];
      config.examples = Array.isArray(payload.examples) ? payload.examples : [];
    } catch {
      config.exposeSourceNames = false;
      config.sources = [];
      config.examples = [];
    }

    addSourceOptions();
    addExampleOptions();
  }

  function wire() {
    $("run").addEventListener("click", run);

    $("clear").addEventListener("click", () => {
      $("result").textContent = "";
      setStatus("");
    });

    $("copy-curl").addEventListener("click", () => copyCurrent("curl"));
    $("copy-body").addEventListener("click", () => copyCurrent("body"));

    $("query").addEventListener("input", persist);
    $("source").addEventListener("input", persist);
    $("variables").addEventListener("input", persist);

    $("examples").addEventListener("change", (event) => {
      if (event.target.value === "") return;
      applyExample(Number(event.target.value));
    });

    $("history").addEventListener("change", (event) => {
      if (event.target.value === "") return;
      applyHistory(Number(event.target.value));
    });

    document.addEventListener("keydown", (event) => {
      if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
        event.preventDefault();
        run();
      }
    });
  }

  async function init() {
    wire();
    addHistoryOptions();
    await loadConfig();

    if (!restore()) {
      if (config.examples.length > 0) applyExample(0);
      else $("query").value = "$source([1, 2, 3]).$count()";
    }

    updateRequestPreview();
  }

  init();
})();
