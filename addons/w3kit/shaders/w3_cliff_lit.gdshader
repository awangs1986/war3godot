shader_type spatial;
// HiveWE and Warsmash disable blending while drawing cliff meshes. Keeping
// these models in Godot's opaque/depth-writing pass prevents adjacent cliff
// tiles from sorting over one another and exposing the culled ground below.
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool flat_lighting = false;
uniform sampler2D shd_tex : hint_default_black, filter_linear, repeat_disable;
uniform bool shd_enabled = false;
uniform vec2 map_origin = vec2(0.0);
uniform vec2 map_size = vec2(1.0);

varying vec3 v_world_pos;


void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}


void fragment() {
	vec4 tex = texture(albedo_tex, UV);
	float shd = 0.0;
	if (!flat_lighting && shd_enabled && map_size.x > 0.0 && map_size.y > 0.0) {
		vec2 suv = clamp((v_world_pos.xz - map_origin) / map_size, vec2(0.0), vec2(1.0));
		shd = texture(shd_tex, suv).r;
	}
	if (flat_lighting) {
		ALBEDO = vec3(0.0);
		EMISSION = tex.rgb;
		AO = 1.0;
		METALLIC = 0.0;
		SPECULAR = 0.0;
		ROUGHNESS = 1.0;
	} else {
		ALBEDO = tex.rgb;
		ROUGHNESS = 0.88;
		METALLIC = 0.0;
		SPECULAR = 0.22;
		AO = clamp(1.0 - shd * 0.80, 0.50, 1.0);
		AO_LIGHT_AFFECT = 0.20;
	}
}
