shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// The small life about the view (src/Game/AmbienceView.cs): birds, bats, butterflies, bees, fish, midges, each a few
// boxes in voxels. Their wings beat on a hinge along the body's side.
// Mesh: UV.x = which wing (-1 right, 1 left, 0 none), UV.y = how far out from the body's middle its hinge is;
// UV2.x = how light this part is (1 as the creature's colour), UV2.y = how much of the accent it takes.
// Instance: COLOR = the creature's colour; INSTANCE_CUSTOM = (the wings' angle in radians, up positive; how far they
// are folded, 0 spread to 1 tucked to the sides; the accent as a share of the colour, below 1 darker, above lighter; 0).

varying vec3 v_col;
varying float v_shade;
varying float v_acc;
varying float v_acck;

void vertex() {
	v_col = COLOR.rgb;
	v_shade = UV2.x;
	v_acc = UV2.y;
	v_acck = INSTANCE_CUSTOM.z;
	float side = UV.x;
	if (side != 0.0) {
		float hinge = UV.y * side;
		float fold = INSTANCE_CUSTOM.y;
		float dx = (VERTEX.x - hinge) * mix(1.0, 0.14, fold);
		float a = INSTANCE_CUSTOM.x * (1.0 - fold);
		VERTEX.x = hinge + dx * cos(a);
		VERTEX.y += abs(dx) * sin(a) - fold * 0.02;
	}
}

void fragment() {
	vec3 c = mix(v_col, clamp(v_col * v_acck, vec3(0.0), vec3(1.0)), v_acc);
	ALBEDO = c * v_shade;
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
}
