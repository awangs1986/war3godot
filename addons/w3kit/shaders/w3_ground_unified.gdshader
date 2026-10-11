shader_type spatial;
// HiveWE/Warsmash true-source ground: single pass, Texture2DArray, per-tile uvec4.
// Each tile carries up to 4 array layers (base + 3 overlays), composited high→base.
render_mode unshaded, cull_disabled, specular_disabled;

// Per-tile data: RGBAF, each channel = global array layer (tileset*STRIDE + sublayer),
// or EMPTY sentinel. R = base (lowest priority), A = highest. filter_nearest + texelFetch.
uniform sampler2D tile_data : filter_nearest;
uniform sampler2D height_tex : hint_default_black, filter_nearest;
uniform sampler2D exists_tex : hint_default_white, filter_nearest;
// One 64x64 layer per ground sub-image; layer = tileset_index*STRIDE + sub (0..15 blend, 16..31 deco).
uniform sampler2DArray ground_array : source_color, filter_linear, repeat_disable;
uniform sampler2D shd_tex : hint_default_black, filter_linear, repeat_disable;

uniform ivec2 map_tiles = ivec2(64, 64);
uniform ivec2 map_corners = ivec2(65, 65);
uniform float tile_size = 128.0;
uniform vec2 map_origin = vec2(0.0);
uniform vec2 map_size = vec2(1.0);
uniform bool shd_enabled = true;
uniform vec3 light_dir = vec3(0.45, 0.85, 0.28);
uniform float light_ambient = 0.35;
uniform float light_scale = 1.1;
/// Dye overlay alpha by corner bitset: BR=red BL=green TR=blue TL=yellow.
uniform bool soft_tint = false;

const float EMPTY = 60000.0; // sentinel above any real layer (max ~16*32)
const float LAYER_STRIDE = 32.0;

varying vec2 v_local;
varying flat ivec2 v_tile;
varying vec3 v_world_pos;
varying vec3 v_normal;

float sample_height(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(corner) + vec2(0.5)) / vec2(map_corners);
	return textureLod(height_tex, uv, 0.0).r;
}

// Sample one sub-image layer; EMPTY → fully transparent (contributes nothing).
// DotA soft-tint QA (H): BR tiles showed at TL, BL↔TR, TR↔BL, TL↔BR → 180° off
// under uv=(1-x,y). Rotate: (x, 1-y) matches Warsmash vertex UV in Godot.
vec4 sample_layer(float layer_f, vec2 local) {
	if (layer_f > EMPTY - 1.0) {
		return vec4(0.0);
	}
	vec2 uv = vec2(local.x, 1.0 - local.y);
	return texture(ground_array, vec3(uv, layer_f));
}

float ground_lighting(vec3 n, vec3 ldir) {
	float ndotl = max(dot(n, normalize(ldir)), 0.0);
	return min(1.0, mix(light_ambient, 1.0, ndotl) * light_scale);
}

vec3 bitset_tint(int bits) {
	vec3 c = vec3(0.0);
	float n = 0.0;
	if ((bits & 1) != 0) { c += vec3(1.0, 0.12, 0.12); n += 1.0; } // BR
	if ((bits & 2) != 0) { c += vec3(0.12, 1.0, 0.12); n += 1.0; } // BL
	if ((bits & 4) != 0) { c += vec3(0.15, 0.55, 1.0); n += 1.0; } // TR
	if ((bits & 8) != 0) { c += vec3(1.0, 0.95, 0.12); n += 1.0; } // TL
	return n > 0.0 ? c / n : vec3(1.0, 0.0, 1.0);
}

// Overlay contribution + bitset-colored tint weight for QA.
void accum_soft(float layer_f, vec2 local, inout float soft_a, inout vec3 soft_rgb) {
	if (layer_f > EMPTY - 1.0) {
		return;
	}
	int sub = int(mod(layer_f, LAYER_STRIDE));
	if (sub < 1 || sub > 15) {
		return; // base deco / empty shape, not a blend bitset
	}
	float a = sample_layer(layer_f, local).a;
	if (a <= soft_a) {
		return;
	}
	soft_a = a;
	soft_rgb = bitset_tint(sub);
}

void vertex() {
	int id = INSTANCE_ID;
	int tile_x = id % map_tiles.x;
	int tile_y = id / map_tiles.x;
	v_tile = ivec2(tile_x, tile_y);
	vec2 local = VERTEX.xz;
	v_local = local;

	vec2 euv = (vec2(v_tile) + vec2(0.5)) / vec2(map_tiles);
	float exists = textureLod(exists_tex, euv, 0.0).r;

	float h00 = sample_height(ivec2(tile_x, tile_y));
	float h10 = sample_height(ivec2(tile_x + 1, tile_y));
	float h01 = sample_height(ivec2(tile_x, tile_y + 1));
	float h11 = sample_height(ivec2(tile_x + 1, tile_y + 1));
	float h = mix(mix(h00, h10, local.x), mix(h01, h11, local.x), local.y);

	if (exists < 0.5) {
		VERTEX = vec3(local.x * tile_size, -100000.0, local.y * tile_size);
	} else {
		VERTEX = vec3(local.x * tile_size, h, local.y * tile_size);
	}

	ivec2 cc = v_tile + ivec2(int(local.x + 0.5), int(local.y + 0.5));
	float hL = sample_height(cc + ivec2(-1, 0));
	float hR = sample_height(cc + ivec2(1, 0));
	float hD = sample_height(cc + ivec2(0, -1));
	float hU = sample_height(cc + ivec2(0, 1));
	vec3 n = normalize(vec3(hL - hR, 2.0 * tile_size, hD - hU));
	NORMAL = n;
	v_normal = n;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float exists = texelFetch(exists_tex, v_tile, 0).r;
	if (exists < 0.5) {
		discard;
	}
	// Per-tile 4 layers: R=base(opaque), then G,B,A stacked above.
	vec4 ti = texelFetch(tile_data, v_tile, 0);
	// Composite high→base: color = mix(lower, color, color.a).
	vec4 col = sample_layer(ti.a, v_local);
	col = mix(sample_layer(ti.b, v_local), col, col.a);
	col = mix(sample_layer(ti.g, v_local), col, col.a);
	col = mix(sample_layer(ti.r, v_local), col, col.a); // base: a==1 → fully replaces

	float lighting = ground_lighting(normalize(v_normal), light_dir);
	float shd = 0.0;
	if (shd_enabled && map_size.x > 0.0 && map_size.y > 0.0) {
		vec2 suv = (v_world_pos.xz - map_origin) / map_size;
		suv.y = 1.0 - suv.y;
		suv = clamp(suv, vec2(0.0), vec2(1.0));
		shd = texture(shd_tex, suv).r;
	}
	ALBEDO = col.rgb * lighting * (1.0 - shd);
	if (soft_tint) {
		float soft_a = 0.0;
		vec3 soft_rgb = vec3(1.0, 0.0, 1.0);
		accum_soft(ti.g, v_local, soft_a, soft_rgb);
		accum_soft(ti.b, v_local, soft_a, soft_rgb);
		accum_soft(ti.a, v_local, soft_a, soft_rgb);
		// Punchy: any soft alpha paints solid bitset color (ignore weak attenuation).
		if (soft_a > 0.04) {
			ALBEDO = mix(ALBEDO, soft_rgb, clamp(soft_a * 1.6, 0.55, 1.0));
		}
	}
}
