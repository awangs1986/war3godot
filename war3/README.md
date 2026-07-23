# Local Warcraft data

The binary game data in this directory is ignored by Git.

- `game/`: locally extracted Warcraft 1.32.10+ (Reforged) assets. This folder
  tree is the only supported asset source; MPQ archives are never opened at
  runtime or during import.
- `maps/LostTemple/source/`: extracted map folder (`war3map.*` files).
- `maps/LostTemple/lost_temple.res`: project-native imported map resource —
  this is what the game loads.
- `local_paths.json`: local source and preview paths.

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
