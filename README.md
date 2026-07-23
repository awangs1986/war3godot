# Diablo1Like — D1-like ARPG + War3 terrain/MDX (0.1)

## Goals
1. War3-similar terrain (ground / cliff split, classic MPQ-style pipeline)
2. Matching Godot terrain editor
3. Ship a Diablo-like game on that foundation

Details: [docs/GOALS.md](docs/GOALS.md) · Development notes: [docs/DEV_RETROSPECTIVE.md](docs/DEV_RETROSPECTIVE.md)

## Run
- **W3 sandbox (0.1 main):** `godot --path .` → `scenes/w3_sandbox.tscn`
- Classic greybox town/dungeon: open `scenes/town.tscn`

Requires **Godot 4.7**. CI runs the same engine line.

## War3 assets
See [docs/w3_0.1.md](docs/w3_0.1.md). Requires your own unpacked War3 files for textures/models; maps via `.w3x` (7z) or folder. **JASS not executed.**

```bash
godot --headless --path . -s res://tools/check_asset_root.gd
godot --headless --path . -s res://tools/w3_smoke.gd
godot --headless --path . -s res://tools/w3_regression.gd
godot --headless --path . -s res://tools/w3_level_runtime_smoke.gd
```

## Import pipeline

Convert a `.w3x`, `.w3m`, extracted map directory, or `war3map.w3e` into the
project-owned binary level format:

```bash
godot --headless --path . --script res://tools/w3_import_level.gd -- INPUT res://levels/map.res
```

Assign the resulting `W3LevelData` resource to the sandbox's `imported_level`,
or call `W3MapWorld.load_level_data(resource)`. Runtime terrain no longer reparses
W3E; stock textures/models still resolve from the separately configured War3 asset root.

## Controls (sandbox)
| Input | Action |
|-------|--------|
| LMB | Grid move (A* on W3E tiles, stick to height/cliff) |

## Greybox loop (town)
Town → Cathedral → fight → loot → merchant. Soft death returns to town.
