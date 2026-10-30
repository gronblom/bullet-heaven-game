shader_type spatial;

// Wooden floor: the baked floorboard textures (tools/blender, a seamless 16 x 16 unit patch)
// repeated across the room, with a large, soft variation in tone and sheen laid over the top so
// the repeat doesn't show. Anisotropic filtering keeps the boards crisp at the camera's low angle.

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roughness_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D variation_texture : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec2 repeats = vec2(10.0, 6.875); // how many 16-unit patches fit the floor
uniform float tone_variation = 0.25;
uniform float sheen_variation = 0.18;

void fragment() {
	vec2 uv = UV * repeats;
	// Two octaves of soft noise at room scale: big patches that are a little lighter or darker,
	// and more or less worn.
	float big = texture(variation_texture, UV * 1.3).r;
	float medium = texture(variation_texture, UV * 4.7 + vec2(0.37, 0.71)).r;
	float variation = mix(big, medium, 0.35);

	vec3 albedo = texture(albedo_texture, uv).rgb;
	ALBEDO = albedo * (1.0 + (variation - 0.5) * 2.0 * tone_variation);
	ROUGHNESS = clamp(texture(roughness_texture, uv).r + (medium - 0.5) * 2.0 * sheen_variation, 0.08, 1.0);
	NORMAL_MAP = texture(normal_texture, uv).rgb;
	SPECULAR = 0.45;
}
