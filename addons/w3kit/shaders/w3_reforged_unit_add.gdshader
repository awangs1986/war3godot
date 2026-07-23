shader_type spatial;
render_mode cull_disabled, blend_add, depth_draw_never, diffuse_burley, specular_schlick_ggx;

uniform sampler2D diffuse_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D emissive_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D team_color_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D environment_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer_alpha : hint_range(0.0, 1.0) = 1.0;
uniform int filter_mode = 3;
uniform float emissive_gain = 1.0;
uniform bool unshaded_layer = false;

void fragment() {
	vec4 diffuse_texel = texture(diffuse_map, UV);
	vec4 orm = texture(orm_map, UV);
	vec3 team_color = texture(team_color_map, UV).rgb;
	if (orm.a > 0.1) {
		diffuse_texel.rgb *= team_color * orm.a;
	}
	ALBEDO = diffuse_texel.rgb;
	ROUGHNESS = clamp(orm.g, 0.04, 1.0);
	METALLIC = clamp(orm.b, 0.0, 1.0);
	EMISSION = texture(emissive_map, UV).rgb * emissive_gain + diffuse_texel.rgb;
	ALPHA = diffuse_texel.a * layer_alpha;
}
