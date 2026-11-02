shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;

// Fireflies (src/Game/AmbienceView.cs): a small light each. Instance: COLOR = the light's colour, its alpha how
// bright it glows now (0 out, 1 at the top of its blink).

varying vec4 v_col;

void vertex() {
	v_col = COLOR;
}

void fragment() {
	ALBEDO = v_col.rgb * v_col.a * 2.5;
	ALPHA = v_col.a;
}
