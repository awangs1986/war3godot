shader_type spatial;

/*
  Map-preview water (War3 W3E heights).
  Inspired by smallcableboi V2 (CC0): fresnel, depth absorption, shore foam.
  NO hint_screen_texture — that copies the whole opaque scene every frame and
  freezes flycam on DotA-sized maps. Depth texture alone is enough for shores.
*/

render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear, repeat_disable;

group_uniforms colours;
uniform vec4 water_color : source_color = vec4(0.10, 0.40, 0.48, 0.72);
uniform vec4 deep_color : source_color = vec4(0.02, 0.14, 0.24, 0.90);
uniform vec3 absorption_color : source_color = vec3(0.45, 0.72, 0.82);
uniform float absorption_mult : hint_range(0.0, 0.08, 0.001) = 0.014;
uniform vec3 foam_color : source_color = vec3(0.82, 0.90, 0.94);
uniform vec3 sky_reflect_tint : source_color = vec3(0.50, 0.64, 0.78);

group_uniforms waves;
uniform float wave_scale = 0.018;
uniform float wave_speed = 0.35;
uniform float normal_strength : hint_range(0.0, 2.0, 0.01) = 0.5;

group_uniforms shore;
uniform float edge_size : hint_range(1.0, 64.0, 0.5) = 16.0;
uniform float foam_strength : hint_range(0.0, 1.0, 0.01) = 0.8;
uniform float reflection_strength : hint_range(0.0, 1.0, 0.01) = 0.5;

varying vec3 v_world_pos;

float linear_depth(float nonlinear_depth, mat4 inv_projection_matrix) {
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	nonlinear_depth = nonlinear_depth * 2.0 - 1.0;
#endif
	return 1.0 / (nonlinear_depth * inv_projection_matrix[2].w + inv_projection_matrix[3].w);
}

float schlick_fresnel(vec3 view, vec3 norm) {
	float cos_i = clamp(dot(normalize(view), normalize(norm)), 0.0, 1.0);
	float r0 = 0.02;
	return clamp(r0 + (1.0 - r0) * pow(1.0 - cos_i, 5.0), 0.0, 1.0);
}

vec3 procedural_wave_normal(vec3 world_pos, float t) {
	vec2 p1 = world_pos.xz * wave_scale + vec2(t * 0.11, t * 0.07);
	vec2 p2 = world_pos.xz * (wave_scale * 1.9) - vec2(t * 0.08, t * 0.13);
	float dx = cos(p1.x * 6.283185) * cos(p1.y * 6.283185) * 0.55
			 + cos(p2.x * 6.283185 + 1.3) * cos(p2.y * 6.283185 + 0.8) * 0.45 * 1.9;
	float dz = -sin(p1.x * 6.283185) * sin(p1.y * 6.283185) * 0.55
			 - sin(p2.x * 6.283185 + 1.3) * sin(p2.y * 6.283185 + 0.8) * 0.45 * 1.9;
	return normalize(vec3(-dx * normal_strength, 1.0, -dz * normal_strength));
}

float foam_noise(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float t = TIME * wave_speed;
	vec3 wave_n = procedural_wave_normal(v_world_pos, t);
	vec3 world_n = normalize(mix(vec3(0.0, 1.0, 0.0), wave_n, 0.85));
	vec3 view_n = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	NORMAL = view_n;

	float self_depth = -VERTEX.z;
	float scene_depth = linear_depth(texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX);
	float depth_diff = max(scene_depth - self_depth, 0.0);

	float fres = schlick_fresnel(VIEW, view_n);
	vec3 absorb = pow(absorption_color, vec3(depth_diff * absorption_mult));
	vec3 body = mix(water_color.rgb, deep_color.rgb, clamp(depth_diff / 220.0, 0.0, 1.0));
	body *= absorb;
	vec3 col = mix(body, sky_reflect_tint, fres * reflection_strength);

	if (depth_diff < edge_size) {
		float shore = 1.0 - clamp(depth_diff / edge_size, 0.0, 1.0);
		float n = foam_noise(v_world_pos.xz * 0.08 + vec2(t * 0.2, -t * 0.15));
		float foam = smoothstep(0.35, 0.85, n + shore * 0.55) * shore * foam_strength;
		col = mix(col, foam_color, foam);
	}

	ALBEDO = col;
	ALPHA = mix(water_color.a, deep_color.a, clamp(depth_diff / 200.0, 0.0, 1.0) * 0.5 + fres * 0.25);
	METALLIC = 0.08;
	ROUGHNESS = 0.08;
	EMISSION = col * 0.12;
	RIM = 0.35;
	RIM_TINT = 0.55;
}
