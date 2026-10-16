shader_type spatial;
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_back, shadows_disabled, fog_disabled;

// Work the player has marked out: planned masonry as a pale ghost in its own colours, and what is to be dug,
// quarried or felled washed red and pushed a hair out of the faces it covers. The voxel edges stay readable.

uniform vec3 tint = vec3(0.9, 0.55, 0.25);
uniform float tint_mix = 0.3;
uniform float alpha = 0.45;
uniform float inflate = 0.0;
// diagonal stripes half a metre apart, so marked ground reads as marked and not as a red road
uniform float hatch = 0.0;

varying vec3 v_vox;
varying vec3 v_nrm;

void vertex() {
	v_vox = VERTEX;
	v_nrm = NORMAL;
	VERTEX += NORMAL * inflate;
}

void fragment() {
	vec3 an = abs(v_nrm);
	vec3 fv = fract(v_vox);
	vec2 fp = an.x > 0.5 ? fv.yz : (an.y > 0.5 ? fv.xz : fv.xy);
	float ed = min(min(fp.x, 1.0 - fp.x), min(fp.y, 1.0 - fp.y));
	float px = length(fwidth(v_vox));
	float line = (1.0 - smoothstep(0.0, max(0.12, px), ed)) * (1.0 - smoothstep(0.4, 0.8, px));
	vec3 col = mix(COLOR.rgb * 1.5, tint, tint_mix);
	float stripe = hatch * step(0.5, fract((v_vox.x + v_vox.y + v_vox.z) * 0.0625));
	ALBEDO = col * mix(1.0, 0.65, line) * (1.0 - 0.35 * stripe);
	ALPHA = alpha * (1.0 - 0.45 * stripe);
}
