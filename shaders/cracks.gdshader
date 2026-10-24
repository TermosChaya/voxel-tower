shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;

// Cracks over a cracked voxel's open faces, on a box a hair larger than the voxel: dark lines along the edges between
// the cells of a jittered grid laid over the face's plane in the world (so they run on across neighbouring voxels),
// each edge open only once the crack is far enough on, wider the further; finer cracks between them as it gets bad,
// and a darkening of dust about it. How far it has cracked comes in the instance's colour (red).

uniform float vox = 0.0625;

varying vec3 v_pos;
varying vec3 v_normal;
varying float v_crack;

void vertex() {
	v_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz / vox;
	v_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	v_crack = COLOR.r;
}

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// The distance to the nearest edge between cells, and a number of that edge's own (the same seen from either side).
vec2 edge(vec2 x) {
	vec2 n = floor(x), f = fract(x), mg = vec2(0.0), mr = vec2(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash2(n + g) * 0.85 - f;
			float d = dot(r, r);
			if (d < md) { md = d; mr = r; mg = g; }
		}
	md = 8.0;
	float id = 0.0;
	for (int j = -2; j <= 2; j++)
		for (int i = -2; i <= 2; i++) {
			vec2 g = mg + vec2(float(i), float(j));
			vec2 r = g + hash2(n + g) * 0.85 - f;
			if (dot(mr - r, mr - r) < 1e-5) continue;
			float d = dot(0.5 * (mr + r), normalize(r - mr));
			if (d < md) { md = d; id = fract(sin(dot(2.0 * n + mg + g, vec2(12.9898, 78.233))) * 43758.5453); }
		}
	return vec2(md, id);
}

void fragment() {
	vec3 a = abs(v_normal);
	vec2 uv = a.x > a.y && a.x > a.z ? v_pos.zy : a.y > a.z ? v_pos.xz : v_pos.xy;
	// how many voxels a pixel spans here: from the usual height of the view a voxel is a few pixels, and the cracks
	// must still be seen
	float px = max(fwidth(uv.x), fwidth(uv.y));
	float far = smoothstep(0.06, 0.2, px);
	// the big cracks: cells of about three voxels; the fine ones between them, of about one, only close
	vec2 big = edge(uv / 3.1 + vec2(17.0, 3.0));
	vec2 fine = edge(uv / 1.2 + vec2(5.0, 41.0));
	float c = v_crack;
	// a line never thinner than a pixel and a half
	float wBig = max(0.05 + 0.18 * c, 1.5 * px / 3.1), wFine = 0.03 + 0.07 * c;
	float lineBig = (1.0 - smoothstep(wBig * 0.6, wBig, big.x)) * step(big.y, c * 1.25);
	float lineFine = (1.0 - smoothstep(wFine * 0.6, wFine, fine.x)) * step(fine.y, (c - 0.45) * 1.3) * (1.0 - far);
	float line = max(lineBig, lineFine * 0.8);
	// close, a faint darkening about the lines; from afar the cracked face goes pale with the dust and fresh broken
	// stone of it, the more the further cracked, so that a wall about to give way shows from the usual height
	float stain = mix(0.42 * c * c, 0.12 + 0.38 * c, far) * step(0.05, c);
	vec3 dust = mix(vec3(0.025, 0.02, 0.018), vec3(0.4, 0.36, 0.31), far);
	ALBEDO = mix(dust, vec3(0.025, 0.02, 0.018), step(stain, line * 0.95));
	ALPHA = max(line * 0.95, stain);
}
