extends RefCounted
## One war3map.w3e tile corner (tilepoint).

## Height in "layer fractions" as stored by mdx-m3-viewer: (raw-8192)/512.
var ground_height: float = 0.0
var water_height: float = 0.0
var map_edge: bool = false
var ramp: bool = false
var blight: bool = false
var water: bool = false
var boundary: bool = false
var ground_texture: int = 0
var cliff_variation: int = 0
var ground_variation: int = 0
var cliff_texture: int = 0
## Cliff layer 0..15 — world Y uses (ground_height + layer_height - 2) * TILE_SIZE.

var layer_height: int = 2


func world_y(tile_size: float = 128.0) -> float:
	return (ground_height + float(layer_height) - 2.0) * tile_size


func cliff_level() -> int:
	return layer_height
