shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// Filter modes None (0) and Transparent (1) belong to Warcraft III's opaque
// depth-writing group. Transparent is an alpha test, not alpha blending.
uniform sampler2D diffuse_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D emissive_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D team_color_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D environment_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer_alpha : hint_range(0.0, 1.0) = 1.0;
uniform int filter_mode = 0;
uniform float emissive_gain = 1.0;
uniform bool unshaded_layer = false;

void fragment() {
	vec4 diffuse_texel = texture(diffuse_map, UV);
	if (filter_mode == 1 && diffuse_texel.a * layer_alpha < 0.75) {
		discard;
	}
	vec4 orm = texture(orm_map, UV);
	vec3 team_color = texture(team_color_map, UV).rgb;
	if (orm.a > 0.1) {
		diffuse_texel.rgb *= team_color * orm.a;
	}
	vec3 normal_texel = texture(normal_map, UV).rgb;
	vec2 normal_xy = normal_texel.yx * 2.0 - 1.0;
	float normal_z = sqrt(max(1.0 - dot(normal_xy, normal_xy), 0.0));
	ALBEDO = diffuse_texel.rgb;
	NORMAL_MAP = vec3(normal_xy * 0.5 + 0.5, normal_z);
	AO = orm.r;
	ROUGHNESS = clamp(orm.g, 0.04, 1.0);
	METALLIC = clamp(orm.b, 0.0, 1.0);
	EMISSION = texture(emissive_map, UV).rgb * emissive_gain;
	if (unshaded_layer) {
		EMISSION += ALBEDO;
	}
}
