shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx, shadows_disabled;

// Still water under the dusk sky: dark where the bottom drops away and clearer over the shallows, a slow ripple
// in the light, and a faint voxel grid on the surface so it sits in the diorama like everything else.

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 deep_color = vec3(0.012, 0.035, 0.05);
uniform vec3 shallow_color = vec3(0.07, 0.15, 0.15);
uniform float clarity = 1.4;

varying vec3 v_vox;
varying vec3 v_nrm;

void vertex() {
	v_vox = VERTEX;
	v_nrm = NORMAL;
}

float ripple(vec2 p, float t) {
	return sin(p.x * 0.9 + t * 0.7) * 0.5 + sin(p.y * 1.3 - t * 0.55) * 0.5 + sin((p.x + p.y) * 0.6 + t * 1.05) * 0.35;
}

void fragment() {
	// how much water the eye looks through: from the surface to whatever lies behind it
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float thick = max(0.0, VERTEX.z - behind.z);
	float k = 1.0 - exp(-thick * clarity);
	vec3 col = mix(shallow_color, deep_color, k);

	// a faint grid on the top faces, like the bevel on solid voxels
	vec2 fv = fract(v_vox.xz);
	float ed = min(min(fv.x, 1.0 - fv.x), min(fv.y, 1.0 - fv.y));
	float px = length(fwidth(v_vox));
	float line = (1.0 - smoothstep(0.0, max(0.12, px), ed)) * (1.0 - smoothstep(0.35, 0.7, px)) * step(0.5, v_nrm.y);
	col *= 1.0 - 0.18 * line;

	// ripples tilt the light, only on the surface facing up
	if (v_nrm.y > 0.5) {
		vec2 p = v_vox.xz * 0.22;
		float e = 0.08, t = TIME;
		float h0 = ripple(p, t), hx = ripple(p + vec2(e, 0.0), t), hz = ripple(p + vec2(0.0, e), t);
		vec3 n = normalize(vec3(-(hx - h0) / e * 0.05, 1.0, -(hz - h0) / e * 0.05));
		NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	}
	ALBEDO = col;
	ROUGHNESS = 0.16;
	SPECULAR = 0.55;
	ALPHA = mix(0.5, 0.93, k);
}
