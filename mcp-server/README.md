# POE2Radar MCP Server

Stdio MCP bridge to the overlay API at `http://localhost:7777`.

**Prerequisite:** `POE2Radar.Overlay.exe` running (API starts with the overlay).

## Setup

```bash
cd mcp-server
npm install
```

## Cursor `mcp.json`

```json
{
  "mcpServers": {
    "poe2radar": {
      "command": "node",
      "args": ["C:/Users/Furkan/Downloads/POE2Radar/mcp-server/index.js"]
    }
  }
}
```

Adjust the path to your clone.

## Tools

| Tool | API | Notes |
|------|-----|--------|
| `game_state` | `GET /state` | Live — needs game |
| `get_entities` | `GET /entities` | Live — filter/search |
| `search_database` | `GET /api/database` | **Static** — 6,692 GGPK paths, no game needed |
| `get_display_rules` | `GET /api/display-rules` | |
| `update_display_rules` | `POST /api/display-rules` | |
| `get_nav` / `set_nav` | `/api/nav` | Draw-only nav |
| `get_zone` | `GET /api/zone` | |
| `get_settings` / `update_settings` | `/api/settings` | |
