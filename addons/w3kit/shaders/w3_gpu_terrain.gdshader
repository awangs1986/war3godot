shader_type spatial;
// Classic MPQ ground (open-realm atlas UV + Warsmash 4-layer blend).
render_mode unshaded, cull_disabled, specular_disabled;

/*
  open-realm SetTileUV: each tileset is one 2D atlas layer in Texture2DArray.
  cell_w = 1/(width/64), cell_h = 1/(height/64); extended uses ux=0.5 for vars.
  We normalize all atlases to 512x256 (square padded left) so cell_w=0.125 always.

  Warsmash blend: A→B→G→R with color*a + next*(1-a). Empty tex=255.
*/

uniform sampler2D height_tex : hint_default_black, filter_nearest;
uniform sampler2D exists_tex : hint_default_white, filter_nearest;
uniform sampler2D texlist0_tex : hint_default_black, filter_nearest;
uniform sampler2D texlist1_tex : hint_default_black, filter_nearest;
// One atlas sheet per tileset (normalized 512x256). No mipmaps.
uniform sampler2DArray ground_array : source_color, filter_linear, repeat_disable;
uniform sampler2D shd_tex : hint_default_black, filter_linear, repeat_disable;

uniform ivec2 map_tiles = ivec2(64, 64);
uniform ivec2 map_corners = ivec2(65, 65);
uniform float tile_size = 128.0;
uniform int ground_layer_count = 1;
uniform vec2 map_origin = vec2(0.0);
uniform vec2 map_size = vec2(1.0);
uniform bool shd_enabled = true;
uniform vec3 light_dir = vec3(0.35, 0.85, 0.35);
uniform float light_ambient = 0.35;
uniform float light_scale = 1.1;

varying vec2 v_local;
varying flat ivec2 v_tile;
varying vec3 v_world_pos;
varying vec3 v_normal;

float sample_height(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(float(corner.x), float(corner.y)) + vec2(0.5)) / vec2(float(map_corners.x), float(map_corners.y));
	return textureLod(height_tex, uv, 0.0).r;
}

// open-realm SetTileUV on normalized 512x256 (8x4 cells of 64).
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
	// open-realm 5% inset toward cell center
	float cx = cell_x + 0.5;
	float cy = cell_y + 0.5;
	u = mix(u, 0.5, 0.05);
	v = mix(v, 0.5, 0.05);
	float px = 1.0 / 512.0;
	float py = 1.0 / 256.0;
	vec2 outuv = vec2((cell_x + u) * cell_w, (cell_y + v) * cell_h);
	vec2 minu = vec2(cell_x * cell_w + px, cell_y * cell_h + py);
	vec2 maxu = vec2((cell_x + 1.0) * cell_w - px, (cell_y + 1.0) * cell_h - py);
	return clamp(outuv, minu, maxu);
}

vec4 sample_ground(int tex_idx, int variation, vec2 local) {
	if (tex_idx >= 255) {
		return vec4(0.0);
	}
	tex_idx = clamp(tex_idx, 0, max(ground_layer_count - 1, 0));
	vec2 uv = atlas_uv(local, variation);
	return textureLod(ground_array, vec3(uv, float(tex_idx)), 0.0);
}

vec4 composite_under(vec4 color, int tex_idx, int variation) {
	if (tex_idx >= 255) {
		return color;
	}
	vec4 frag = sample_ground(tex_idx, variation, v_local);
	return color * color.a + frag * (1.0 - color.a);
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
	v_tile = ivec2(tile_x, tile_y);
	vec2 local = VERTEX.xz;
	v_local = local;

	vec2 euv = (vec2(float(tile_x), float(tile_y)) + vec2(0.5)) / vec2(float(map_tiles.x), float(map_tiles.y));
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
	vec2 luv = (vec2(float(v_tile.x), float(v_tile.y)) + vec2(0.5)) / vec2(float(map_tiles.x), float(map_tiles.y));
	float exists = textureLod(exists_tex, luv, 0.0).r;
	if (exists < 0.5) {
		discard;
	}
	vec4 p0 = textureLod(texlist0_tex, luv, 0.0) * 255.0;
	vec4 p1 = textureLod(texlist1_tex, luv, 0.0) * 255.0;
	int t0 = int(p0.r + 0.5);
	int v0 = int(p0.g + 0.5);
	int t1 = int(p0.b + 0.5);
	int v1 = int(p0.a + 0.5);
	int t2 = int(p1.r + 0.5);
	int v2 = int(p1.g + 0.5);
	int t3 = int(p1.b + 0.5);
	int v3 = int(p1.a + 0.5);

	vec4 col = vec4(0.0);
	col = composite_under(col, t3, v3);
	col = composite_under(col, t2, v2);
	col = composite_under(col, t1, v1);
	col = composite_under(col, t0, v0);

	float lighting = open_realm_lighting(normalize(v_normal), light_dir);
	float shd = 0.0;
	if (shd_enabled && map_size.x > 0.0 && map_size.y > 0.0) {
		vec2 suv = (v_world_pos.xz - map_origin) / map_size;
		suv.y = 1.0 - suv.y;
		suv = clamp(suv, vec2(0.0), vec2(1.0));
		shd = texture(shd_tex, suv).r;
	}
	ALBEDO = col.rgb * lighting * (1.0 - shd);
}
