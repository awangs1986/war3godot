# Local Warcraft data

The binary game data in this directory is ignored by Git.

## Resource version and provenance

Use **Warcraft III: Reforged 1.32.10.18820** as the resource baseline.
To reproduce the terrain work, use resource exports from the recorded installation. Do not select an arbitrary
newer release based on the previous `1.32.10+` wording: that was not a tested
compatibility range.

| Item | Recorded value |
| --- | --- |
| Version | **1.32.10.18820**, verified on 2026-10-10 |
| Patch / build | **1.32.10 / 18820** |

Installation paths are local configuration. Both CASC extractors require an
explicit `--install` argument pointing to your installation of this exact build.
Paths do not establish the patch version; verify the records below before exporting.

Read-only inspection of the source installation found three matching records:

| Evidence | Value |
| --- | --- |
| `.build.info`: active `w3` product, branch `cn` | `1.32.10.18820` |
| `_retail_/x86_64/Warcraft III.exe`: PE FileVersion / ProductVersion | `1.32.10.18820` / `1.32.10.18820` |
| `_retail_/x86_64/World Editor.exe`: PE FileVersion / ProductVersion | `1.32.10.18820` / `1.32.10.18820` |

CASC Build Key: `aa2b20f1ee251ee642794e21d9a09315`.
The inspection identifies the source installation; existing exports have no build manifest,
so their individual provenance cannot be proved solely from this check.
When refreshing exports, extract both resource trees from this same build.

## Required resource directories

- `game/`: extracted game data, including classic/SD terrain atlases,
  `TerrainArt/Terrain.slk`, `TerrainArt/CliffTypes.slk`, cliff and ramp models
  under `Doodads/Terrain/`, and their referenced textures.
- `reforged/`: extracted HD cache for the Reforged model/material paths,
  including the applicable diffuse, normal, ORM and emissive textures.
- `maps/LostTemple/source/`: extracted map folder (`war3map.*` files).
- `maps/LostTemple/lost_temple.res`: project-native imported map resource —
  this is what the game loads.
- `local_paths.example.json`: portable asset and preview defaults.
- `local_paths.json`: optional local overrides, ignored by Git.

Keep the game-relative directory structure in both exports. Runtime resource
loading uses extracted files, not the CASC installation directly.
The standalone editor locates `war3/game` and `war3/reforged` beneath the project
root; Godot loads `local_paths.json` when present and otherwise uses
`local_paths.example.json`. Copy the example to `local_paths.json` before adding
absolute paths for your machine. Keep local overrides out of commits.

**The terrain path still needs classic/SD assets from the Reforged install.**
Ground transition atlases prefer classic textures/PNG sidecars; replacing them
with HD `*_diffuse.dds` atlases changes the alpha masks and can reintroduce
square transitions.
HD-only exports are therefore insufficient for reproducing this terrain setup.

## Map pipeline (Reforged-only)

1. Extract a map's `war3map.*` files into a folder with any external tool
   (e.g. CascView / MPQ editor — outside this project).
2. Import the folder to a `.res` level with `tools/w3_import_level.gd`
   (headless Godot script; see `TEST_LOST_TEMPLE.BAT` for the runtime side).
3. Runtime loads only `.res` levels or extracted folders.

## Texture sidecars

Some classic BLP1 textures are JPEG-CMYK, which Godot cannot decode. Run the
offline converter once after refreshing `game/`:

```powershell
python tools/mpqtool/convert_cmyk_sidecars.py "war3/game"
```

It writes `.png` sidecars next to each affected `.blp` with the HiveWE
turbojpeg channel order. The runtime never shells out to converters.
