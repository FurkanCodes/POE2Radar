# Shipped default config

These JSON files are copied into every release zip as `config/`, so new installs start with the
tuned defaults instead of empty first-run seeds.

| File | Purpose |
|------|---------|
| `radar_settings.json` | Overlay toggles, helpers, atlas, hotkeys, cadence |
| `display_rules.json` | Entity display / hide / navigable rules |
| `watched_entities.json` | Watched metadata patterns |
| `hidden_entities.json` | Globally hidden entity tokens |
| `zone_entity_overrides.json` | Per-area type overrides |

Price caches (`poe_ninja_prices.json`, `ritual_prices.json`) are intentionally **not** shipped —
they refresh at runtime.

To refresh defaults from a local tuned build:

```powershell
Copy-Item src\POE2Radar.Overlay\bin\Release\net10.0-windows\config\radar_settings.json `
          src\POE2Radar.Overlay\Config\Defaults\radar_settings.json -Force
# …repeat for the other four files…
```
