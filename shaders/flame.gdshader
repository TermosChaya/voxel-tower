shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

// A tongue of flame on a quad that stands upright and turns to the camera: a white-hot heart, yellow and orange
// round it, red at the licking edges, narrowing as it rises and flickering on its own time (the instance's custom
// data: x its seed, y how hot it burns).

varying float v_seed;
varying float v_heat;

void vertex() {
	// upright, turned to the camera about the vertical
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(normalize(cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz)), 0.0),
		vec4(0.0, 1.0, 0.0, 0.0), vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))), 0.0), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	v_seed = INSTANCE_CUSTOM.x;
	v_heat = INSTANCE_CUSTOM.y;
}

void fragment() {
	float t = TIME * (2.0 + v_seed * 1.4) + v_seed * 17.0;
	float y = 1.0 - UV.y;
	// the edges lick: the middle of the tongue sways more the higher it is
	float sway = sin(y * 6.0 - t * 3.1) * 0.07 + sin(y * 13.0 - t * 5.7 + v_seed * 4.0) * 0.035;
	float half_w = 0.46 * (1.0 - y * 0.85) + 0.02;
	float x = (UV.x - 0.5 - sway * y) / half_w;
	float top = 0.6 + 0.25 * sin(t * 1.7 + v_seed * 3.0);
	float body = clamp(1.0 - x * x, 0.0, 1.0) * smoothstep(0.0, 0.1, y) * (1.0 - smoothstep(top * 0.6, top, y));
	// now and then a tongue breaks off above the rest
	float wisp = smoothstep(0.75, 1.0, sin(t * 2.3 + v_seed * 9.0)) * clamp(1.0 - abs(x) * 1.6, 0.0, 1.0)
		* smoothstep(top * 0.7, top, y) * (1.0 - smoothstep(top, 1.0, y));
	float a = max(body, wisp * 0.6);
	float core = pow(body, 4.0);
	vec3 col = mix(vec3(0.75, 0.1, 0.02), vec3(1.0, 0.45, 0.06), smoothstep(0.1, 0.8, a));
	col = mix(col, vec3(1.0, 0.82, 0.4), core * 0.8);
	// laid over what is behind rather than added to it, so where many tongues overlap a blaze stays orange, and
	// a little over full brightness so it glows
	ALBEDO = col * (1.25 + v_heat * 0.25);
	ALPHA = smoothstep(0.0, 0.35, a) * 0.85;
}
