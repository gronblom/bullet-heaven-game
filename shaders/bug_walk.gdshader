shader_type spatial;

// Walk cycle for the generated bug models, drawn in MultiMesh batches.
//
// Per vertex, the "anim" vertex colour written by tools/blender/generate_models.py:
//   RGB = part code as bits (0 body, 1-3 one side's legs front-to-back, 4-6 the other side's, 7 antennae)
//   A   = how far along the limb (0 at the hip, 1 at the tip), so feet move most.
// Per instance (MultiMesh custom data):
//   x = walk phase (radians, advances with distance walked), y = stride (0 standing .. 1 full speed),
//   z = hit flash (0..1).
// Mesh space is the model's own: forward +Z, up +Y.

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D orm_texture : hint_default_white, filter_linear_mipmap, repeat_enable; // glTF: roughness G, metallic B
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float stride_length = 0.16;
uniform float step_height = 0.12;
uniform float antenna_sway = 0.07;
uniform float matte = 0.6;

varying float flash;

void vertex() {
	float code = step(0.5, COLOR.r) + 2.0 * step(0.5, COLOR.g) + 4.0 * step(0.5, COLOR.b);
	float along = COLOR.a;
	float phase = INSTANCE_CUSTOM.x;
	float stride = INSTANCE_CUSTOM.y;
	flash = INSTANCE_CUSTOM.z;

	if (code > 0.5 && code < 6.5) {
		float side = code > 3.5 ? 1.0 : 0.0;
		float index = code - 1.0 - side * 3.0;
		// Tripod gait: front and back legs of one side step together with the other side's middle leg.
		float leg_phase = phase + mod(index + side, 2.0) * PI;
		VERTEX.z += sin(leg_phase) * stride_length * along * stride;
		VERTEX.y += max(0.0, cos(leg_phase)) * step_height * along * stride;
	} else if (code > 6.5) {
		// Antennae feel around constantly, a bit faster while walking.
		VERTEX.x += sin(TIME * 6.0 + phase * 0.25) * antenna_sway * along;
		VERTEX.y += cos(TIME * 4.3 + phase * 0.2) * antenna_sway * 0.6 * along;
	}
}

void fragment() {
	vec3 albedo = texture(albedo_texture, UV).rgb;
	vec4 orm = texture(orm_texture, UV);
	ALBEDO = mix(albedo, vec3(1.0, 0.95, 0.85), flash);
	// Matte, waxy chitin: only a faint sheen, no plastic highlights.
	ROUGHNESS = mix(orm.g, 1.0, matte);
	METALLIC = orm.b * 0.2;
	SPECULAR = 0.25;
	NORMAL_MAP = texture(normal_texture, UV).rgb;
	EMISSION = vec3(1.0, 0.6, 0.3) * flash * 1.5;
}
