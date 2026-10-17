shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Every face shows its voxels: a per-voxel colour jitter, a thin bevel line where the grid is
// big enough on screen to read, corner shading baked by the mesher, and glow for embers.
// Dressed stone is drawn smooth instead: each stone whole, a faint mottling across it, its joints
// cut into the wall. Grass sways at its tips and leaves as a mass, in a wind that comes in gusts.
// Vertex data: COLOR = linear rgb + colour variance, UV = (corner shade, glow), UV2 = (sway, smooth).

varying vec3 v_vox;
varying vec3 v_nrm;
varying vec2 v_ext;
varying float v_smooth;

float vhash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

void vertex() {
	v_vox = VERTEX;
	v_nrm = NORMAL;
	v_ext = UV;
	v_smooth = UV2.y;
	if (UV2.x > 0.0) {
		// the wind: a slow swell along the valley, gusts running through it, each blade a little out of step
		vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float gust = 0.55 + 0.45 * sin(TIME * 0.31 + w.x * 0.21 + w.z * 0.13);
		float phase = w.x * 3.1 + w.z * 2.3;
		float sway = (sin(TIME * 1.9 + phase) * 0.6 + sin(TIME * 3.7 + phase * 1.7) * 0.25) * gust * UV2.x;
		VERTEX.x += sway * 0.55;
		VERTEX.z += sway * 0.35;
		VERTEX.y -= abs(sway) * 0.12;
	}
}

void fragment() {
	vec3 cell = floor(v_vox - v_nrm * 0.5 + 0.001);
	vec3 col;
	float line = 0.0;
	if (v_smooth > 0.5) {
		// one stone, one tone, with a faint mottling and a darker skin toward its edges
		vec3 p = v_vox * 0.55;
		float mott = sin(p.x * 1.7 + p.y * 0.9) * sin(p.z * 1.3 - p.y * 1.1) + sin(p.x * 3.1 - p.z * 2.3) * 0.35;
		col = COLOR.rgb * (1.0 + mott * 0.035 + (vhash(floor(v_vox * 0.5)) - 0.5) * COLOR.a);
	} else {
		col = COLOR.rgb * (1.0 + (vhash(cell) - 0.5) * COLOR.a);
		vec3 an = abs(v_nrm);
		vec3 fv = fract(v_vox);
		vec2 fp = an.x > 0.5 ? fv.yz : (an.y > 0.5 ? fv.xz : fv.xy);
		float ed = min(min(fp.x, 1.0 - fp.x), min(fp.y, 1.0 - fp.y));
		float px = length(fwidth(v_vox));
		line = 1.0 - smoothstep(0.0, max(0.16, px), ed);
		line *= 1.0 - smoothstep(0.35, 0.7, px);
	}
	col *= mix(1.0, 0.8, line);
	ALBEDO = col * v_ext.x;
	ROUGHNESS = v_smooth > 0.5 ? 0.86 : 0.93;
	SPECULAR = 0.3;
	EMISSION = col * v_ext.y;
}
