# W3 Foundation Terrain — first editor slice

This C# add-on is the reusable terrain and imported-map preview foundation. It
has no dependency on `diablolike` gameplay, old MPQ runtime loaders, or gameplay
scene signal names.

## Test with Godot 4.7.1-rc2 .NET

1. Open the project with the .NET/Mono build of Godot 4.7.1-rc2.
2. Build the C# solution if the editor asks.
3. Open `res://addons/w3_foundation/lab/TerrainLab.tscn`.
4. Select `TerrainWorld` and use the terrain palette dock to paint.
5. Run the scene to use the foundation preview diagnostics.

Preview controls:

- `WASD`, `Q/E`, right mouse and mouse wheel: fly camera.
- Backtick: command input. Commands include `/debug`, `/debugon`, `/debugoff`,
  `/grid`, `/chunks`, `/help`, and `x,y` tile focus.
- `G`: tile grid; `B`: chunk bounds; `Tab`: flat lighting; `H`: material-ID
  colors; `F`: water visibility.

## Preview performance contracts

- Tile labels are created lazily and reused. Complete coordinates are visible
  only within 650 world units and inside the camera frustum.
- Animated placement previews update their proximity budget every 0.2 seconds.
  Animation starts within 2200 units, remains active until 2800 units, and is
  capped at the nearest 48 targets. Rendering stops after 4200 units.
- Static placement previews are distance/frustum checked every 0.15 seconds and
  stop rendering after 5200 units. The future model renderer can use the same
  policy while packing visible transforms into MultiMesh batches.

The first terrain slice intentionally uses palette colors instead of Warcraft
textures. Texture changes update an R8 GPU data texture; geometry changes
rebuild only intersecting 16x16 chunks. CASC 1.32.10 textures and exact Warcraft
transition masks attach to this same document/renderer boundary in a later slice.

## Ownership boundary

`addons/w3_foundation` may be consumed by disposable test templates. It must
never import or reference `templates/diablolike` or gameplay types.
