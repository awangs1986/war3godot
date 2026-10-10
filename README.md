# Diablo1Like — D1-like ARPG + War3 terrain/MDX (0.1)

## Goals
1. War3-similar terrain (ground / cliff split, classic MPQ-style pipeline)
2. Matching Godot terrain editor
3. Ship a Diablo-like game on that foundation

Details: [docs/GOALS.md](docs/GOALS.md) · Roadmap: [docs/ROADMAP_P0_P5.md](docs/ROADMAP_P0_P5.md) · Development notes: [docs/DEV_RETROSPECTIVE.md](docs/DEV_RETROSPECTIVE.md)

## Run
- **W3 sandbox (0.1 main):** `godot --path .` → `scenes/w3_sandbox.tscn`
- Classic greybox town/dungeon: open `scenes/town.tscn`

Requires **Godot 4.7**. CI runs the same engine line.

## War3 assets
Resource version and directory requirements: [war3/README.md](war3/README.md).
The recorded source install is `C:\Warcraft.III.R`;
the owner recalls **Reforged 2.0.4**, but its exact version/build is pending
verification. The old `1.32.10+` wording is not a verified compatibility range.

See [docs/w3_0.1.md](docs/w3_0.1.md). Requires your own unpacked War3 files for textures/models; maps via `.w3x` (7z) or folder. **JASS not executed.**

```bash
godot --headless --path . -s res://tools/check_asset_root.gd
godot --headless --path . res://tools/w3_ground_crosscheck.tscn -- --asset-free
godot --headless --path . res://tools/w3_regression.tscn
godot --headless --path . res://tools/w3_smoke.tscn
godot --headless --path . res://tools/w3_level_runtime_smoke.tscn
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
