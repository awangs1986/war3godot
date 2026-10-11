# Project Rules

## Terrain and model bug escalation

- When debugging a terrain-rendering or model-rendering bug, count an unsuccessful user verification or a new attempted fix that does not resolve the same root issue as one round.
- If the same terrain/model bug remains unresolved after two rounds, stop making further implementation guesses and cross-check the relevant behavior and source code in both HiveWE and Warsmash before the next fix.
- Record which implementation details agree, which conflict, and how the local project differs.
- If HiveWE and Warsmash conflict, treat Warsmash as the authoritative behavior unless the user explicitly directs otherwise.
- Exception (2026-07-22, user directed): ground transition packing / `real_tile_texture` ownership follows **HiveWE** (`GroundLayout.RealTileTexture`, `w3_gpu_terrain._corner_tex`). Do not silently revert to Warsmash for this path. Seal: `docs/GROUND_TEX_HIVEWE_SEAL.md`.
