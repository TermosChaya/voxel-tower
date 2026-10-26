shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The world map seen from far off (HexMapView.cs, HexFar.cs): the land's heights every few voxels, each point the colour
// of its ground, linear, with the vertices. A faint grain over it so its broad colours are not flat, the shadows of
// clouds going over as on the near land, and the unknown dark under a stirring murk: the fog is a grid of half-hexes by
// rows, read from a point's place in the map's voxels.

uniform sampler2D atlas_fog : filter_linear, repeat_disable;
uniform vec2 atlas_size = vec2(1.0);
uniform vec3 atlas_dark = vec3(0.035, 0.04, 0.06);
uniform float atlas_clouds = 0.0;

varying vec3 v_vox;

float h21(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float vn(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1.0, 0.0)), f.x), mix(h21(i + vec2(0.0, 1.0)), h21(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	v_vox = VERTEX;
}

void fragment() {
	vec2 p = v_vox.xz;
	vec3 col = COLOR.rgb * (0.9 + 0.2 * vn(p * 0.35));
	if (atlas_clouds > 0.0) {
		vec2 q = p * 0.0126 + vec2(TIME * 0.0093, TIME * 0.0051);
		col *= 1.0 - atlas_clouds * smoothstep(0.55, 0.8, vn(q));
	}
	float seen = texture(atlas_fog, p / atlas_size).r;
	if (seen < 0.995) {
		float murk = vn(p * 0.011 + vec2(TIME * 0.006, -TIME * 0.004));
		vec3 dark = atlas_dark + vec3(0.03, 0.034, 0.042) * smoothstep(0.3, 0.8, murk);
		col = mix(dark, col, 0.04 + 0.96 * seen);
	}
	ALBEDO = col;
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
