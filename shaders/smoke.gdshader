shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

// A puff of smoke on a quad turned full to the camera: a soft round cloud with a ragged edge, its colour and how
// thick it is from the instance's colour (it thins as it rises), its ragged edge seeded by the custom data.

varying float v_seed;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	v_seed = INSTANCE_CUSTOM.x;
}

void fragment() {
	vec2 d = UV - 0.5;
	float r = length(d) * 2.0;
	float ang = atan(d.y, d.x);
	float rag = sin(ang * 5.0 + v_seed * 31.0 + TIME * 0.4) * 0.08 + sin(ang * 9.0 - v_seed * 17.0) * 0.05;
	float a = 1.0 - smoothstep(0.25 + rag, 1.0 + rag, r);
	ALBEDO = COLOR.rgb;
	ALPHA = a * a * COLOR.a;
}
