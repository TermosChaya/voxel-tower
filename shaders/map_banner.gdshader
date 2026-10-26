// A banner of the big world map's figures: cloth hung from its pole (at z = +half_w of the mesh), stirring in the
// wind the more the further from the pole, a gilt hem along its foot and a gilt round in its middle, its end cut into
// a swallow's tail.
shader_type spatial;
render_mode cull_disabled;

uniform vec3 cloth : source_color = vec3(0.2, 0.33, 0.66);
uniform vec3 hem : source_color = vec3(0.85, 0.68, 0.3);
uniform float half_w = 0.21;
uniform float half_h = 0.16;
uniform float phase = 0.0;

varying vec2 v_at;

void vertex() {
	// 0 at the pole, 1 at the free end; 0 at the foot, 1 at the top
	float out_ = clamp((half_w - VERTEX.z) / (2.0 * half_w), 0.0, 1.0);
	v_at = vec2(out_, (VERTEX.y + half_h) / (2.0 * half_h));
	float wave = sin(TIME * 3.3 + phase - out_ * 5.5) + 0.35 * sin(TIME * 5.1 + phase * 1.7 - out_ * 9.0);
	VERTEX.x += wave * 0.045 * out_;
	VERTEX.y -= out_ * out_ * 0.03;
	NORMAL = normalize(NORMAL + vec3(0.0, 0.0, wave * 0.35 * out_));
}

void fragment() {
	if (v_at.x > 0.78 && abs(v_at.y - 0.5) < (v_at.x - 0.78) * 1.7) discard;
	vec3 c = cloth * (0.92 + 0.12 * sin(v_at.x * 40.0));
	if (v_at.y < 0.16) c = hem;
	vec2 q = (v_at - vec2(0.42, 0.56)) * vec2(1.0, 1.25);
	float r = length(q);
	if (r < 0.15 && r > 0.1) c = hem;
	if (r < 0.06) c = hem;
	ALBEDO = c;
	ROUGHNESS = 0.85;
}
