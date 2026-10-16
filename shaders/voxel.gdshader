shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Every face shows its voxels: a per-voxel colour jitter, a thin bevel line where the grid is
// big enough on screen to read, corner shading baked by the mesher, and glow for embers.
// Vertex data: COLOR = linear rgb + colour variance, UV = (corner shade, glow).

varying vec3 v_vox;
varying vec3 v_nrm;
varying vec2 v_ext;

float vhash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

void vertex() {
	v_vox = VERTEX;
	v_nrm = NORMAL;
	v_ext = UV;
}

void fragment() {
	vec3 cell = floor(v_vox - v_nrm * 0.5 + 0.001);
	vec3 col = COLOR.rgb * (1.0 + (vhash(cell) - 0.5) * COLOR.a);
	vec3 an = abs(v_nrm);
	vec3 fv = fract(v_vox);
	vec2 fp = an.x > 0.5 ? fv.yz : (an.y > 0.5 ? fv.xz : fv.xy);
	float ed = min(min(fp.x, 1.0 - fp.x), min(fp.y, 1.0 - fp.y));
	float px = length(fwidth(v_vox));
	float line = 1.0 - smoothstep(0.0, max(0.16, px), ed);
	line *= 1.0 - smoothstep(0.35, 0.7, px);
	col *= mix(1.0, 0.8, line);
	ALBEDO = col * v_ext.x;
	ROUGHNESS = 0.93;
	SPECULAR = 0.3;
	EMISSION = col * v_ext.y;
}
