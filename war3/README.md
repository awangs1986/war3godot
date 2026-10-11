# Local War3 data (gitignored binaries)

- `game/` — extracted from your install MPQs (`TerrainArt`, `Units`, `Doodads`, …)
- `maps/Adrenaline/` — extracted `(4)Adrenaline.w3m`
- `local_paths.json` — default paths for the sandbox

Re-extract:

```bash
cd tools/mpqtool && node extract_into_project.cjs
```

Source install is configured in that script (`WAR3_INSTALL`).
