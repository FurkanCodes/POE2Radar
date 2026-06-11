#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const API = "http://localhost:7777";

async function api(path, method = "GET", body = null) {
  const opts = { method, headers: {} };
  if (body) {
    opts.headers["Content-Type"] = "application/json";
    opts.body = JSON.stringify(body);
  }
  const r = await fetch(`${API}${path}`, opts);
  const text = await r.text();
  try {
    return JSON.parse(text);
  } catch {
    return { error: text || r.statusText, status: r.status };
  }
}

const server = new McpServer({ name: "poe2radar", version: "1.0.0" });

server.tool("game_state", "Current area, vitals, entity counts", {}, async () => {
  const s = await api("/state");
  return { content: [{ type: "text", text: JSON.stringify(s, null, 2) }] };
});

server.tool(
  "get_entities",
  "Live entities in the current zone",
  {
    category: z.string().optional(),
    alive: z.boolean().optional(),
    search: z.string().optional().describe("Filter metadata path client-side"),
    radius: z.number().optional(),
    limit: z.number().optional(),
  },
  async ({ category, alive, search, radius, limit }) => {
    let q = `?limit=${limit || 100}`;
    if (category) q += `&category=${encodeURIComponent(category)}`;
    if (alive) q += "&alive=true";
    if (radius) q += `&radius=${radius}`;
    let entities = await api(`/entities${q}`);
    if (!Array.isArray(entities)) entities = [];
    if (search) {
      const s = search.toLowerCase();
      entities = entities.filter((e) => (e.metadata || "").toLowerCase().includes(s));
    }
    return { content: [{ type: "text", text: JSON.stringify(entities, null, 2) }] };
  }
);

server.tool("search_database", "Search static GGPK entity paths (works without game running)", {
  query: z.string().describe("e.g. Waypoint, Ritual, Chest"),
  limit: z.number().optional(),
}, async ({ query, limit }) => {
  const db = await api("/api/database");
  const max = limit || 50;
  const q = query.toLowerCase();
  const list = Array.isArray(db) ? db : [];
  const results = list.filter((p) => p.toLowerCase().includes(q)).slice(0, max);
  return {
    content: [{ type: "text", text: `${results.length} matches:\n${results.join("\n")}` }],
  };
});

server.tool("get_display_rules", "Full display-rules ruleset", {}, async () => {
  const r = await api("/api/display-rules");
  return { content: [{ type: "text", text: JSON.stringify(r, null, 2) }] };
});

server.tool("update_display_rules", "Replace display rules or clear ruleset", {
  rules: z.array(z.record(z.any())).optional(),
  clear: z.boolean().optional(),
}, async ({ rules, clear }) => {
  const body = clear ? { clear: true } : { rules };
  const result = await api("/api/display-rules", "POST", body);
  return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
});

server.tool("get_nav", "Selected navigation targets", {}, async () => {
  const p = await api("/api/nav");
  return { content: [{ type: "text", text: JSON.stringify(p, null, 2) }] };
});

server.tool("set_nav", "Toggle or clear nav selection (draw-only)", {
  toggle: z.string().optional().describe("Target id e.g. e:123 or t:path"),
  clear: z.boolean().optional(),
}, async ({ toggle, clear }) => {
  const body = clear ? { clear: true } : { toggle };
  const result = await api("/api/nav", "POST", body);
  return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
});

server.tool("get_zone", "Static zone guide for current area", {}, async () => {
  const z = await api("/api/zone");
  return { content: [{ type: "text", text: JSON.stringify(z, null, 2) }] };
});

server.tool("get_settings", "Radar settings snapshot", {}, async () => {
  const s = await api("/api/settings");
  return { content: [{ type: "text", text: JSON.stringify(s, null, 2) }] };
});

server.tool("update_settings", "Update whitelisted radar settings", {
  settings: z.record(z.any()),
}, async ({ settings }) => {
  const result = await api("/api/settings", "POST", settings);
  return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
});

const transport = new StdioServerTransport();
await server.connect(transport);
