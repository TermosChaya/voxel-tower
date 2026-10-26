shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

// The clouds over what the map of valleys does not see (AtlasView.cs): each a broad flat sheet lying over a cell in the
// fog, its stuff a slow drifting noise of the world's place, so the sheets run together into one bank; thin where the
// fog gives way to what is seen, gone over it. Lit from above: brighter where it is thick.

uniform sampler2D atlas_fog : filter_linear, repeat_disable;
uniform vec2 atlas_size = vec2(1.0);
// the scene's place of the field's corner (xy) and voxels to a scene unit (z)
uniform vec3 to_vox = vec3(0.0, 0.0, 16.0);

varying vec3 v_world;

float h2(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float vn(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h2(i), h2(i + vec2(1.0, 0.0)), f.x), mix(h2(i + vec2(0.0, 1.0)), h2(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int o = 0; o < 4; o++) { s += a * vn(p); p = p * 2.07 + 13.1; a *= 0.5; }
	return s / 0.9375;
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 vox = (v_world.xz - to_vox.xy) * to_vox.z;
	float seen = texture(atlas_fog, vox / atlas_size).r;
	float n = fbm(vox * 0.03 + vec2(TIME * 0.025, TIME * 0.011));
	float sheet = 1.0 - smoothstep(0.55, 1.0, length(UV * 2.0 - 1.0));
	float a = smoothstep(0.3, 0.6, n) * sheet * (1.0 - smoothstep(0.15, 0.7, seen));
	ALBEDO = mix(vec3(0.34, 0.36, 0.42), vec3(0.88, 0.89, 0.93), smoothstep(0.38, 0.8, n));
	ALPHA = a * 0.95;
}
