shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, specular_schlick_ggx;

// Water in a glass: shows the scene behind it bent by the surface (screen-space refraction),
// tinted more the deeper you look through it, with animated ripples on the top surface and a
// glossy, Fresnel-weighted sheen.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear;
uniform vec3 shallow_tint : source_color = vec3(0.82, 0.95, 0.97);
uniform vec3 deep_tint : source_color = vec3(0.28, 0.58, 0.62);
uniform float absorption = 0.35;        // per unit of water looked through
uniform float refraction_strength = 0.04;
uniform float ripple_frequency = 2.2;
uniform float ripple_strength = 0.3;

// Slope of a few travelling waves at a point on the surface (world XZ).
vec2 ripple_slope(vec2 p, float t) {
	vec2 slope = vec2(0.0);
	vec2 dirs[4] = vec2[](vec2(1.0, 0.3), vec2(-0.4, 1.0), vec2(0.7, -0.8), vec2(-1.0, -0.2));
	float freqs[4] = float[](1.0, 1.4, 1.9, 2.6);
	float speeds[4] = float[](1.4, 1.9, 2.4, 3.1);
	for (int i = 0; i < 4; i++) {
		vec2 d = normalize(dirs[i]);
		float phase = dot(p, d) * freqs[i] * ripple_frequency + t * speeds[i];
		slope += d * cos(phase) / float(i + 1);
	}
	return slope;
}

void fragment() {
	vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;

	// Ripples only on the top surface; the sides stay smooth.
	float top = smoothstep(0.6, 0.9, world_normal.y);
	vec2 slope = ripple_slope(world_pos.xz, TIME) * ripple_strength * top;
	vec3 n = normalize(world_normal + vec3(-slope.x, 0.0, -slope.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// How much water lies between this surface and whatever is behind it.
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 behind_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	behind_view.xyz /= behind_view.w;
	float thickness = clamp(VERTEX.z - behind_view.z, 0.0, 12.0);

	vec2 offset = NORMAL.xy * refraction_strength * clamp(thickness, 0.3, 2.0);
	vec3 behind = textureLod(screen_texture, SCREEN_UV + offset, 0.0).rgb;
	vec3 tint = mix(shallow_tint, deep_tint, 1.0 - exp(-thickness * absorption));
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 5.0);

	ALBEDO = vec3(0.0);
	EMISSION = behind * tint * (1.0 - 0.5 * fresnel);
	ROUGHNESS = 0.02;
	METALLIC = 0.0;
	SPECULAR = 0.6 + 0.4 * fresnel;
	ALPHA = 1.0;
}
