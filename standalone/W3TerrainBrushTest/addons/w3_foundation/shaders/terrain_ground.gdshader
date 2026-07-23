shader_type spatial;
// Editor foundation colors must remain visible even before a scene lighting
// rig is authored. The final game terrain uses the separate PBR renderer.
render_mode unshaded, cull_disabled, depth_draw_opaque;

// One R8 texel per terrain corner. The byte value is a palette index.
uniform sampler2D ground_index : filter_nearest, repeat_disable;
uniform sampler2D palette_tex : source_color, filter_nearest, repeat_disable;
uniform ivec2 map_tiles = ivec2(32, 32);
uniform ivec2 map_corners = ivec2(33, 33);
uniform float grid_strength : hint_range(0.0, 1.0) = 0.08;
uniform bool debug_flat_lighting = false;
uniform bool debug_material_ids = false;

varying vec2 terrain_coord;

void vertex() {
	terrain_coord = UV * vec2(map_tiles);
}

int material_at(ivec2 corner) {
	corner = clamp(corner, ivec2(0), map_corners - ivec2(1));
	return int(round(texelFetch(ground_index, corner, 0).r * 255.0));
}

vec3 palette_color(int index) {
	int width = textureSize(palette_tex, 0).x;
	return texelFetch(palette_tex, ivec2(clamp(index, 0, width - 1), 0), 0).rgb;
}

vec3 id_color(int index) {
	float value = float(index);
	return 0.35 + 0.65 * fract(sin(vec3(value + 1.0, value + 17.0, value + 41.0) * vec3(12.9898, 78.233, 37.719)) * 43758.5453);
}

float detail_noise(vec2 p) {
	vec2 cell = floor(p);
	return fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 bounded = clamp(terrain_coord, vec2(0.0), vec2(map_tiles) - vec2(0.0001));
	ivec2 tile = ivec2(floor(bounded));
	vec2 local = fract(bounded);
	// Smooth cubic weights remove the hard square seam when corner materials differ.
	vec2 blend = local * local * (3.0 - 2.0 * local);

	vec3 c00 = debug_material_ids ? id_color(material_at(tile)) : palette_color(material_at(tile));
	vec3 c10 = debug_material_ids ? id_color(material_at(tile + ivec2(1, 0))) : palette_color(material_at(tile + ivec2(1, 0)));
	vec3 c01 = debug_material_ids ? id_color(material_at(tile + ivec2(0, 1))) : palette_color(material_at(tile + ivec2(0, 1)));
	vec3 c11 = debug_material_ids ? id_color(material_at(tile + ivec2(1, 1))) : palette_color(material_at(tile + ivec2(1, 1)));
	vec3 color = mix(mix(c00, c10, blend.x), mix(c01, c11, blend.x), blend.y);

	float macro = detail_noise(terrain_coord * 3.0) - 0.5;
	float edge = min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y));
	float grid = 1.0 - smoothstep(0.0, 0.018, edge);
	ALBEDO = color * (1.0 + macro * 0.055) * (1.0 - grid * grid_strength);
	if (debug_flat_lighting) {
		EMISSION = ALBEDO;
		ALBEDO = vec3(0.0);
	}
	ROUGHNESS = 0.88;
	METALLIC = 0.0;
}
