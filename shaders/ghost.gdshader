shader_type spatial;
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_back, shadows_disabled, fog_disabled;

// A stroke before it is marked: its own colours, washed toward a tint
// (ember when the stores hold enough, blood-red when not or when it is to be dug).

uniform vec3 tint = vec3(0.9, 0.55, 0.25);
uniform float alpha = 0.55;
uniform float tint_mix = 0.4;
// digging ghosts sit on the very faces they will take away: push them out a little
uniform float inflate = 0.0;

void vertex() {
	VERTEX += NORMAL * inflate;
}

void fragment() {
	ALBEDO = mix(COLOR.rgb * 1.5, tint, tint_mix);
	ALPHA = alpha;
}
