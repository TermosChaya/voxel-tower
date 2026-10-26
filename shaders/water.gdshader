shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx, shadows_disabled;

// Water under the dusk sky: dark where the bottom drops away and clearer over the shallows, ripples in the light, and,
// when the player has the voxels drawn with their lines, a faint voxel grid on the surface as on everything else. It runs where it runs: a flow map
// over the valley carries the ripples and a glint across each voxel of the surface along the river's bed, and along
// the way water let out of a pond went; it frets white in the shallows where it runs, and falls white over an edge.
// Vertices are in whole voxels of the valley.

// the lines round the voxels: 1 drawn, 0 not (a setting of the player's)
global uniform float voxel_lines;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D flow_tex : filter_linear, repeat_disable;
uniform vec2 map_size = vec2(1024.0, 1024.0);
uniform float flow_max = 6.0;
uniform vec3 deep_color = vec3(0.012, 0.035, 0.05);
uniform vec3 shallow_color = vec3(0.07, 0.15, 0.15);
uniform vec3 foam_color = vec3(0.30, 0.36, 0.38);
uniform float clarity = 1.4;
// on the map of valleys (AtlasView.cs): its size in voxels, how much of each column is seen, the dark of its fog; off (w 0) in a valley
uniform vec4 atlas = vec4(0.0);
uniform vec2 atlas_size = vec2(1.0);
uniform sampler2D atlas_fog : filter_linear, repeat_disable;
uniform vec3 atlas_dark = vec3(0.035, 0.04, 0.06);
uniform float atlas_through = 0.1;

varying vec3 v_vox;
varying vec3 v_nrm;

float hash2(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), f.x), mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	v_vox = VERTEX;
	v_nrm = NORMAL;
}

float ripple(vec2 p, float t) {
	return sin(p.x * 0.9 + t * 0.7) * 0.5 + sin(p.y * 1.3 - t * 0.55) * 0.5 + sin((p.x + p.y) * 0.6 + t * 1.05) * 0.35;
}

vec2 slope(vec2 p, float t) {
	float e = 0.08, h0 = ripple(p, t);
	return vec2(ripple(p + vec2(e, 0.0), t) - h0, ripple(p + vec2(0.0, e), t) - h0) / e;
}

void fragment() {
	// how much water the eye looks through: from the surface to whatever lies behind it
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	behind.xyz /= behind.w;
	float thick = max(0.0, VERTEX.z - behind.z);
	float k = 1.0 - exp(-thick * clarity);
	vec3 col = mix(shallow_color, deep_color, k);
	float alpha = mix(0.5, 0.93, k);

	// a faint grid on the top faces, like the bevel on solid voxels, when the player has those drawn
	vec2 fv = fract(v_vox.xz);
	float ed = min(min(fv.x, 1.0 - fv.x), min(fv.y, 1.0 - fv.y));
	float px = length(fwidth(v_vox));
	float line = (1.0 - smoothstep(0.0, max(0.12, px), ed)) * (1.0 - smoothstep(0.35, 0.7, px)) * step(0.5, v_nrm.y) * voxel_lines;
	col *= 1.0 - 0.18 * line;

	if (v_nrm.y > 0.5) {
		// the current, voxels a second; two copies of each pattern ride on it half a cycle apart, each fading out as the
		// other is at its strongest, so the water runs on and the patterns never stretch
		vec2 flow = (texture(flow_tex, v_vox.xz / map_size).rg - 0.5) * 2.0 * flow_max;
		float speed = length(flow), cyc = 1.8;
		float ph0 = fract(TIME / cyc), ph1 = fract(TIME / cyc + 0.5), w0 = 1.0 - abs(ph0 * 2.0 - 1.0);
		vec2 o0 = flow * ph0 * cyc, o1 = flow * ph1 * cyc;
		// ripples tilt the light
		vec2 p = v_vox.xz * 0.22;
		vec2 g = mix(slope(p - o1 * 0.22, TIME), slope(p - o0 * 0.22, TIME), w0) * 0.05;
		vec3 n = normalize(vec3(-g.x, 1.0, -g.y));
		NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
		// each voxel of the surface glints as the current carries a ripple of light over it
		vec2 cell = floor(v_vox.xz) + 0.5;
		float s = mix(vnoise((cell - o1) * 0.3), vnoise((cell - o0) * 0.3), w0);
		float running = clamp(speed / 2.5, 0.0, 1.0);
		col += vec3(0.04, 0.055, 0.06) * smoothstep(0.62, 0.88, s) * running;
		// where it runs shallow it frets white
		float fret = (1.0 - smoothstep(0.02, 0.3, k)) * running * smoothstep(0.5, 0.8, s);
		col = mix(col, foam_color, fret * 0.45);
		alpha = max(alpha, fret * 0.75);
	} else {
		// a face out over an edge: water falling, in white streaks down it
		vec2 n2 = normalize(v_nrm.xz + vec2(1e-4));
		float along = floor(dot(v_vox.xz, vec2(-n2.y, n2.x)));
		float s = vnoise(vec2(along * 0.7, (floor(v_vox.y) + TIME * 9.0) * 0.2));
		col = mix(col, foam_color, 0.3 + 0.45 * smoothstep(0.35, 0.8, s));
		alpha = max(alpha, 0.85);
	}
	if (atlas.w > 0.5) col = mix(atlas_dark, col, atlas_through + (1.0 - atlas_through) * texture(atlas_fog, v_vox.xz / atlas_size).r);
	ALBEDO = col;
	// the camera looks along one direction everywhere: a sharp sun glint would light a whole pond at once at some turn
	// of the view, so the glint is broad and soft
	ROUGHNESS = 0.32;
	SPECULAR = 0.4;
	ALPHA = alpha;
}
