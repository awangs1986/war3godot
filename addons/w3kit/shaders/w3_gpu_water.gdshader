shader_type spatial;
// Warsmash Terrain.renderWater(): glDepthMask(false) — water tests depth but
// never writes it. depth_draw_always here z-fought with the terrain at shallow
// shores and flickered while the camera moved.
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

/*
  HiveWE-style GPU water: one MultiMesh.
  Instance transform = tile origin; local xz 0..1 * tile_size; Y from water height.
*/

uniform sampler2D water_height_tex : hint_default_black, filter_nearest;
uniform sampler2D ground_height_tex : hint_default_black, filter_nearest;
uniform sampler2D water_exists_tex : hint_default_black, filter_nearest;
uniform sampler2DArray water_textures : filter_linear_mipmap, repeat_disable;

uniform ivec2 map_tiles = ivec2(64, 64);
uniform ivec2 map_corners = ivec2(65, 65);
uniform float tile_size = 128.0;
uniform float water_height_offset = -0.7;
uniform int water_texture_count = 0;
uniform float water_texture_rate = 0.0;
uniform vec4 shallow_color_min : source_color = vec4(0.0, 0.45, 0.55, 0.50);
uniform vec4 shallow_color_max : source_color = vec4(0.0, 0.35, 0.45, 0.65);
uniform vec4 deep_color_min : source_color = vec4(0.0, 0.20, 0.35, 0.75);
uniform vec4 deep_color_max : source_color = vec4(0.0, 0.10, 0.25, 0.85);

varying float v_water_depth;

float sample_wh(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(float(corner.x), float(corner.y)) + vec2(0.5)) / vec2(float(map_corners.x), float(map_corners.y));
	return textureLod(water_height_tex, uv, 0.0).r;
}

float sample_gh(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(float(corner.x), float(corner.y)) + vec2(0.5)) / vec2(float(map_corners.x), float(map_corners.y));
	return textureLod(ground_height_tex, uv, 0.0).r;
}

void vertex() {
	int id = INSTANCE_ID;
	int tx = map_tiles.x;
	int tile_x = id % tx;
	int tile_y = id / tx;
	vec2 local = VERTEX.xz;

	vec2 euv = (vec2(float(tile_x), float(tile_y)) + vec2(0.5)) / vec2(float(map_tiles.x), float(map_tiles.y));
	float exists = textureLod(water_exists_tex, euv, 0.0).r;

	float w00 = sample_wh(ivec2(tile_x, tile_y));
	float w10 = sample_wh(ivec2(tile_x + 1, tile_y));
	float w01 = sample_wh(ivec2(tile_x, tile_y + 1));
	float w11 = sample_wh(ivec2(tile_x + 1, tile_y + 1));
	float wh = mix(mix(w00, w10, local.x), mix(w01, w11, local.x), local.y);
	float g00 = sample_gh(ivec2(tile_x, tile_y));
	float g10 = sample_gh(ivec2(tile_x + 1, tile_y));
	float g01 = sample_gh(ivec2(tile_x, tile_y + 1));
	float g11 = sample_gh(ivec2(tile_x + 1, tile_y + 1));
	float gh = mix(mix(g00, g10, local.x), mix(g01, g11, local.x), local.y);
	float wy = (wh + water_height_offset) * tile_size;
	v_water_depth = max(wh + water_height_offset - gh, 0.0);

	if (exists < 0.5) {
		VERTEX = vec3(local.x * tile_size, -100000.0, local.y * tile_size);
	} else {
		VERTEX = vec3(local.x * tile_size, wy, local.y * tile_size);
	}
	NORMAL = vec3(0.0, 1.0, 0.0);
}

void fragment() {
	const float min_depth = 10.0 / 128.0;
	const float deep_level = 64.0 / 128.0;
	const float max_depth = 72.0 / 128.0;
	vec4 tint;
	if (v_water_depth <= deep_level) {
		float value = max(0.0, v_water_depth - min_depth) / (deep_level - min_depth);
		tint = mix(shallow_color_min, shallow_color_max, clamp(value, 0.0, 1.0));
	} else {
		float value = clamp(v_water_depth - deep_level, 0.0, max_depth - deep_level) / (max_depth - deep_level);
		tint = mix(deep_color_min, deep_color_max, value);
	}
	vec4 frame = vec4(1.0);
	if (water_texture_count > 0) {
		float layer = mod(floor(TIME * water_texture_rate), float(water_texture_count));
		frame = texture(water_textures, vec3(UV.x, 1.0 - UV.y, layer));
	}
	vec4 color = frame * tint;
	ALBEDO = color.rgb;
	ALPHA = color.a;
	ROUGHNESS = 0.08;
	METALLIC = 0.06;
	SPECULAR = 0.82;
	// Water reflects scene light; it must not glow independently at night.
	EMISSION = vec3(0.0);
}
