shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

// Spilled liquid lying flat on a surface, drawn as a MultiMesh of unit planes.
// Per instance: COLOR = liquid colour (MultiMesh colours),
//   INSTANCE_CUSTOM.x = shape seed, .y = opacity (fades as jam dries), .z = thickness (0 water .. 1 honey).
// The outline is a wobbly blob (a few sine lobes round the centre), cut out with discard so it
// sorts like an opaque surface. Thin liquids ripple and mirror the room; thick ones bulge at the edge.

uniform float ripple_strength = 0.25;

varying vec3 tint;
varying vec3 params;

float hash(float n) { return fract(sin(n) * 43758.5453); }

void vertex() {
	tint = COLOR.rgb;
	params = INSTANCE_CUSTOM.xyz;
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float a = atan(p.y, p.x);
	float seed = params.x * 17.0;
	float edge = 0.78
		+ 0.09 * sin(a * 3.0 + hash(seed) * 6.28)
		+ 0.06 * sin(a * 5.0 + hash(seed + 1.0) * 6.28)
		+ 0.04 * sin(a * 9.0 + hash(seed + 2.0) * 6.28);
	// A couple of droplets just outside the main blob.
	vec2 d1 = vec2(cos(hash(seed + 3.0) * 6.28), sin(hash(seed + 3.0) * 6.28)) * 0.9;
	vec2 d2 = vec2(cos(hash(seed + 4.0) * 6.28), sin(hash(seed + 4.0) * 6.28)) * 0.86;
	float inside = max(step(r, edge), max(step(length(p - d1), 0.07), step(length(p - d2), 0.05)));
	if (inside < 0.5 || params.y < 0.02) {
		discard;
	}

	float thick = params.z;
	float rim = smoothstep(edge - 0.18, edge, r) * thick; // surface tension bulge at the edge
	float t = TIME * mix(1.0, 0.15, thick);
	vec2 wave = vec2(sin(p.x * 23.0 + t * 2.1 + seed), sin(p.y * 19.0 - t * 1.7 + seed * 0.5))
		+ 0.5 * vec2(sin((p.x + p.y) * 41.0 + t * 3.3), sin((p.x - p.y) * 37.0 - t * 2.9));
	vec3 n = normalize(vec3(wave * ripple_strength * (1.0 - thick) * 0.15 + p * rim * 0.6, 1.0));
	NORMAL_MAP = n * 0.5 + 0.5;

	// Darker in the middle where it's deepest; the room shows in the reflection (SSR).
	float depth = 1.0 - smoothstep(0.0, edge, r);
	ALBEDO = tint * mix(1.1, 0.6, depth * (0.4 + 0.6 * thick));
	ROUGHNESS = mix(0.04, 0.12, thick);
	METALLIC = 0.0;
	SPECULAR = 0.7;
	ALPHA = mix(0.55, 0.95, thick) * params.y;
}
