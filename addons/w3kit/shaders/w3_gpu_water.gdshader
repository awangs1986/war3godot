shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;

/*
  HiveWE-style GPU water: one MultiMesh.
  Instance transform = tile origin; local xz 0..1 * tile_size; Y from water height.
*/

uniform sampler2D water_height_tex : hint_default_black, filter_nearest;
uniform sampler2D water_exists_tex : hint_default_black, filter_nearest;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear, repeat_disable;

uniform ivec2 map_tiles = ivec2(64, 64);
uniform ivec2 map_corners = ivec2(65, 65);
uniform float tile_size = 128.0;
uniform float water_height_offset = -0.7;

uniform vec4 water_color : source_color = vec4(0.10, 0.40, 0.48, 0.72);
uniform vec4 deep_color : source_color = vec4(0.02, 0.14, 0.24, 0.90);
uniform vec3 sky_reflect_tint : source_color = vec3(0.50, 0.64, 0.78);
uniform float edge_size = 16.0;
uniform float wave_speed = 0.35;

varying vec3 v_world_pos;

float linear_depth(float nonlinear_depth, mat4 inv_projection_matrix) {
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	nonlinear_depth = nonlinear_depth * 2.0 - 1.0;
#endif
	return 1.0 / (nonlinear_depth * inv_projection_matrix[2].w + inv_projection_matrix[3].w);
}

float sample_wh(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	vec2 uv = (vec2(float(corner.x), float(corner.y)) + vec2(0.5)) / vec2(float(map_corners.x), float(map_corners.y));
	return textureLod(water_height_tex, uv, 0.0).r;
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
	float wy = (wh + water_height_offset) * tile_size;

	if (exists < 0.5) {
		VERTEX = vec3(local.x * tile_size, -100000.0, local.y * tile_size);
	} else {
		VERTEX = vec3(local.x * tile_size, wy, local.y * tile_size);
	}
	NORMAL = vec3(0.0, 1.0, 0.0);
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float t = TIME * wave_speed;
	float n = sin(v_world_pos.x * 0.02 + t) * cos(v_world_pos.z * 0.02 - t * 0.7);
	vec3 view_n = normalize(NORMAL + vec3(n * 0.15, 0.0, n * 0.12));
	NORMAL = view_n;

	float self_depth = -VERTEX.z;
	float scene_depth = linear_depth(texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX);
	float depth_diff = max(scene_depth - self_depth, 0.0);

	float fres = pow(1.0 - clamp(dot(normalize(VIEW), view_n), 0.0, 1.0), 3.0);
	vec3 body = mix(water_color.rgb, deep_color.rgb, clamp(depth_diff / 220.0, 0.0, 1.0));
	vec3 col = mix(body, sky_reflect_tint, fres * 0.5);
	if (depth_diff < edge_size) {
		float shore = 1.0 - clamp(depth_diff / edge_size, 0.0, 1.0);
		col = mix(col, vec3(0.85, 0.92, 0.95), shore * 0.7);
	}
	ALBEDO = col;
	ALPHA = mix(water_color.a, deep_color.a, clamp(depth_diff / 200.0, 0.0, 1.0) * 0.4 + fres * 0.2);
	ROUGHNESS = 0.08;
	METALLIC = 0.08;
	EMISSION = col * 0.1;
}
