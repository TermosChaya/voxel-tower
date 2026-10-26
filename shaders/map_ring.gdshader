// The ring about the hero chosen on the big world map, lying on the ground: a gold band breathing slowly, a faint
// glow inside it, six notches toward the six sides of a hex.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec4 color : source_color = vec4(1.0, 0.82, 0.38, 1.0);

void fragment() {
	vec2 q = UV * 2.0 - 1.0;
	float r = length(q);
	float aa = fwidth(r) * 1.5;
	float band = smoothstep(0.8 - aa, 0.8 + aa, r) * (1.0 - smoothstep(0.9 - aa, 0.9 + aa, r));
	float glow = (1.0 - smoothstep(0.35, 0.85, r)) * 0.1;
	float a = atan(q.y, q.x);
	float notch = smoothstep(0.92, 0.97, abs(cos(a * 3.0))) * smoothstep(0.62, 0.68, r) * (1.0 - smoothstep(0.76, 0.8, r));
	float breath = 0.78 + 0.22 * sin(TIME * 2.6);
	ALBEDO = color.rgb;
	ALPHA = clamp((band * breath + glow + notch * 0.9) * color.a, 0.0, 1.0);
}
