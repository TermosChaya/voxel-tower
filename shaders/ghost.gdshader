shader_type spatial;
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_back, shadows_disabled, fog_disabled;

// The planned building before it is paid for: its own colours, washed toward a tint
// (ember when affordable, blood-red when not).

uniform vec3 tint = vec3(0.9, 0.55, 0.25);
uniform float alpha = 0.55;
uniform float tint_mix = 0.4;

void fragment() {
	ALBEDO = mix(COLOR.rgb * 1.5, tint, tint_mix);
	ALPHA = alpha;
}
