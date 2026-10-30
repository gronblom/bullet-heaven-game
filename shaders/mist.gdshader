shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;

// Soft translucent mist (bug spray puffs), drawn in a MultiMesh of spheres. Per instance COLOR:
// rgb tint, a = opacity. Edges fade out so overlapping puffs read as one cloud.

void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	ALBEDO = COLOR.rgb;
	ALPHA = COLOR.a * pow(facing, 1.5);
}
