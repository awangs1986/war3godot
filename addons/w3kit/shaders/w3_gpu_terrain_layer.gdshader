shader_type spatial;
// open-realm ground layer: one atlas Texture2D + SetTileUV mask cell + GPU alpha blend.
render_mode unshaded, cull_disabled, specular_disabled, blend_mix, depth_draw_opaque;

uniform sampler2D height_tex : hint_default_black, filter_nearest;
uniform sampler2D exists_tex : hint_default_white, filter_nearest;
uniform sampler2D ground_atlas : source_color, filter_linear, repeat_disable;
uniform sampler2D shd_tex : hint_default_black, filter_linear, repeat_disable;

uniform ivec2 map_tiles = ivec2(64, 64);
uniform ivec2 map_corners = ivec2(65, 65);
uniform float tile_size = 128.0;
uniform vec2 map_origin = vec2(0.0);
uniform vec2 map_size = vec2(1.0);
uniform bool shd_enabled = true;
uniform bool is_base_layer = true;
uniform float layer_y_bias = 0.0;
uniform vec3 light_dir = vec3(0.35, 0.85, 0.35);
uniform float light_ambient = 0.35;
uniform float light_scale = 1.1;

varying vec2 v_local;
varying flat int v_variation;
varying vec3 v_world_pos;
varying vec3 v_normal;

float sample_height(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(float(corner.x), float(corner.y)) + vec2(0.5)) / vec2(float(map_corners.x), float(map_corners.y));
	return textureLod(height_tex, uv, 0.0).r;
}

// open-realm SetTileUV on normalized 512x256 atlas (8x4 cells of 64).
vec2 atlas_uv(vec2 local, int variation) {
	float cell_x;
	float cell_y;
	if (variation < 16) {
		cell_x = float(variation % 4);
		cell_y = float(variation / 4);
	} else {
		int v2 = variation - 16;
		cell_x = 4.0 + float(v2 % 4);
		cell_y = float(v2 / 4);
	}
	float cell_w = 0.125;
	float cell_h = 0.25;
	float u = local.x;
	float v = 1.0 - local.y;
	u = mix(u, 0.5, 0.05);
	v = mix(v, 0.5, 0.05);
	float px = 1.0 / 512.0;
	float py = 1.0 / 256.0;
	vec2 outuv = vec2((cell_x + u) * cell_w, (cell_y + v) * cell_h);
	vec2 minu = vec2(cell_x * cell_w + px, cell_y * cell_h + py);
	vec2 maxu = vec2((cell_x + 1.0) * cell_w - px, (cell_y + 1.0) * cell_h - py);
	return clamp(outuv, minu, maxu);
}

float open_realm_lighting(vec3 n, vec3 ldir) {
	float ndotl = max(dot(n, normalize(ldir)), 0.0);
	return min(1.0, mix(light_ambient, 1.0, ndotl) * light_scale);
}

void vertex() {
	int id = INSTANCE_ID;
	int tx = map_tiles.x;
	int tile_x = id % tx;
	int tile_y = id / tx;
	// INSTANCE_CUSTOM.r = variation 0..31 stored as value/31
	v_variation = int(INSTANCE_CUSTOM.r * 31.0 + 0.5);
	vec2 local = VERTEX.xz;
	v_local = local;

	vec2 euv = (vec2(float(tile_x), float(tile_y)) + vec2(0.5)) / vec2(float(map_tiles.x), float(map_tiles.y));
	float exists = textureLod(exists_tex, euv, 0.0).r;
	// variation 255 sentinel (stored as >1) → hide instance
	if (INSTANCE_CUSTOM.a > 0.5 || exists < 0.5) {
		VERTEX = vec3(local.x * tile_size, -100000.0, local.y * tile_size);
		v_world_pos = vec3(0.0);
		v_normal = vec3(0.0, 1.0, 0.0);
		return;
	}

	float h00 = sample_height(ivec2(tile_x, tile_y));
	float h10 = sample_height(ivec2(tile_x + 1, tile_y));
	float h01 = sample_height(ivec2(tile_x, tile_y + 1));
	float h11 = sample_height(ivec2(tile_x + 1, tile_y + 1));
	float h = mix(mix(h00, h10, local.x), mix(h01, h11, local.x), local.y) + layer_y_bias;
	VERTEX = vec3(local.x * tile_size, h, local.y * tile_size);

	float hL = sample_height(ivec2(tile_x + int(local.x + 0.5) - 1, tile_y + int(local.y + 0.5)));
	float hR = sample_height(ivec2(tile_x + int(local.x + 0.5) + 1, tile_y + int(local.y + 0.5)));
	float hD = sample_height(ivec2(tile_x + int(local.x + 0.5), tile_y + int(local.y + 0.5) - 1));
	float hU = sample_height(ivec2(tile_x + int(local.x + 0.5), tile_y + int(local.y + 0.5) + 1));
	vec3 n = normalize(vec3(hL - hR, 2.0 * tile_size, hD - hU));
	NORMAL = n;
	v_normal = n;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (INSTANCE_CUSTOM.a > 0.5) {
		discard;
	}
	vec4 tex = textureLod(ground_atlas, atlas_uv(v_local, v_variation), 0.0);
	if (!is_base_layer && tex.a < 0.004) {
		discard;
	}
	float lighting = open_realm_lighting(normalize(v_normal), light_dir);
	float shd = 0.0;
	if (shd_enabled && map_size.x > 0.0 && map_size.y > 0.0) {
		vec2 suv = (v_world_pos.xz - map_origin) / map_size;
		suv.y = 1.0 - suv.y;
		suv = clamp(suv, vec2(0.0), vec2(1.0));
		shd = texture(shd_tex, suv).r;
	}
	ALBEDO = tex.rgb * lighting * (1.0 - shd);
	ALPHA = is_base_layer ? 1.0 : tex.a;
}
