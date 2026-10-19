shader_type spatial;
render_mode cull_disabled, world_vertex_coords, diffuse_burley, specular_schlick_ggx;

// Grass in blades and flowers on stems, finer than the voxels they stand in: a tuft is a clump of long blades leaning
// out every way, a flower a few leaves and a stem with its head, and near the view the open turf grows a short sward.
// They bend in the same wind as the leaves, and away from whoever walks through them, from a blast's wave as it
// passes and round a whirlwind; a storm lashes them harder.
// A blade far off is never drawn thinner than a pixel: it is widened to one, and the clump thinned to match, so the
// meadow keeps its tone at every distance instead of shimmering away.
// Mesh: VERTEX in voxels from the clump's root, CUSTOM0.xyz = how far this vertex lies from the blade's spine (or a
// petal's from its flower's heart) and CUSTOM0.w with UV2.y where the blade itself is rooted (x, z), UV = (way up the
// blade 0..1, which blade of the clump 0..1), UV2.x = part: 0 blade, 1 petal, 2 the heart of a flower.
// Instance: COLOR = the grass's colour, alpha how dry it is; INSTANCE_CUSTOM = (kind: 0 tuft, 1 yellow flower,
// 2 purple flower, 3 sward; its own random, 0, 0).

// how big a voxel is in the world, and where the voxel (0, 0, 0) lies
uniform float vox = 0.0625;
uniform vec3 origin;
// the turf's sward grows in near the view: round sward_at.xyz in full to sward_reach.x, gone by .y, grown by .w
global uniform vec4 sward_at;
global uniform vec2 sward_reach;
// the share of a sward clump's blades drawn: all through someone's eyes, fewer from above, where they crowd the pixels
uniform float keep = 1.0;
// whoever walks through the grass: feet (xyz) and how far they part it (w), all in the world
uniform vec4 pushers[32];
uniform int pusher_count = 0;
// a blast's wave: where it went off (xyz) and how long ago (w); how far it reaches (x), how hard (y), and whether it is
// a quake (z), which shakes the grass where it stands instead of throwing it flat
uniform vec4 blasts[8];
uniform vec4 blast_size[8];
uniform int blast_count = 0;
// a whirlwind: where it stands (xyz) and how wide it turns (w)
uniform vec4 whirls[4];
uniform int whirl_count = 0;
// the wind's strength: 1 on a fair day, more in a storm
global uniform float wind;

varying vec3 v_col;
varying float v_up;
varying float v_part;
varying float v_dry;
varying float v_tone;
varying float v_kind;

float vhash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

// the same tone the voxel shader gives the turf a blade stands on (see turf_tone there), so the blade matches it
float patches(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = vhash(vec3(i, 5.0)), b = vhash(vec3(i + vec2(1.0, 0.0), 5.0));
	float c = vhash(vec3(i + vec2(0.0, 1.0), 5.0)), d = vhash(vec3(i + vec2(1.0, 1.0), 5.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float turf_tone(vec3 cell) {
	return (vhash(cell) - 0.5) * 0.45 + (patches(cell.xz * 0.17) - 0.5) * 0.9 + (patches(cell.xz * 0.045 + 7.0) - 0.5) * 0.5;
}

void vertex() {
	float kind = INSTANCE_CUSTOM.x;
	float own = INSTANCE_CUSTOM.y;
	float up = UV.x;
	float part = UV2.x;
	vec3 root = MODEL_MATRIX[3].xyz;
	vec3 side = mat3(MODEL_MATRIX) * CUSTOM0.xyz;
	vec3 spine = VERTEX - side;
	vec3 base = mat3(MODEL_MATRIX) * vec3(CUSTOM0.w, 0.0, UV2.y);
	// the sward grows in with the zoom and gives way to the bare turf with distance
	float g = 1.0;
	if (kind > 2.5) {
		g = sward_at.w * (1.0 - smoothstep(sward_reach.x, sward_reach.y, length(root.xz - sward_at.xz))) * step(UV.y, keep);
	}
	// never thinner than a pixel; where a blade is widened that much, fewer blades keep the same tone (the projection
	// may be flipped upside down, so only the size of its scale counts)
	float ortho = step(0.5, PROJECTION_MATRIX[3][3]);
	float pixel = 2.0 / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float perpx = pixel * mix(max(-(VIEW_MATRIX * vec4(spine, 1.0)).z, 0.001), 1.0, ortho);
	float widen = max(1.0, 0.6 * perpx / max(length(side), 1e-6));
	// the clump thins by whole blades, all of a clump alike, by how much a blade of the usual width would have to widen
	// where it stands; a flower keeps its stems, or its heads would float
	float spread = max(1.0, 0.6 * pixel * mix(max(-(VIEW_MATRIX * vec4(root, 1.0)).z, 0.001), 1.0, ortho) / (0.04 * vox));
	if ((kind < 0.5 || kind > 2.5) && UV.y * min(spread, 8.0) > 1.6) g = 0.0;
	if (g < 0.02) {
		VERTEX = root;
	} else {
		spine = root + (spine - root) * g;
		// how long the whole blade is, from how far up it this vertex lies above the blade's own root
		float len = max(length(spine - root - base * g) / max(up, 0.3), 1e-4);
		// the wind: the same swell and gusts the leaves sway in, every clump a little out of step
		float gust = 0.55 + 0.45 * sin(TIME * 0.31 + root.x * 0.21 + root.z * 0.13);
		float phase = root.x * 3.1 + root.z * 2.3 + own * 2.0;
		float sway = (sin(TIME * 1.9 + phase) * 0.6 + sin(TIME * 3.7 + phase * 1.7) * 0.25) * gust * wind;
		vec2 bend = vec2(0.55, 0.35) * sway * len * 0.45;
		// parted by whoever walks through
		for (int i = 0; i < pusher_count; i++) {
			vec4 p = pushers[i];
			vec2 d = root.xz - p.xz;
			float dist = length(d);
			if (dist < p.w && abs(root.y - p.y) < p.w * 1.5) {
				float k = 1.0 - dist / p.w;
				bend += d / max(dist, 1e-4) * k * k * len * 1.3;
			}
		}
		// thrown flat by a blast's wave as it runs out, and swinging back behind it
		for (int i = 0; i < blast_count; i++) {
			vec4 b = blasts[i];
			vec4 s = blast_size[i];
			vec2 d = root.xz - b.xz;
			float dist = length(d);
			float front = b.w * s.x * 3.0;
			if (s.z > 0.5) {
				// a quake: the ground shudders under the grass and it whips to and fro
				float k = s.y * (1.0 - smoothstep(s.x * 0.6, s.x, dist)) * exp(-b.w * 0.7);
				bend += vec2(sin(TIME * 31.0 + own * 9.0), cos(TIME * 27.0 + own * 7.0)) * k * len * 0.45;
				continue;
			}
			if (dist > s.x * 1.6 || dist > front + s.x * 0.3) continue;
			float wave = exp(-pow((dist - front) / (s.x * 0.18 + vox * 4.0), 2.0));
			float after = step(dist, front) * sin((front - dist) / (s.x * 0.12 + vox) * 3.0) * exp(-b.w * 2.0) * 0.35;
			float fall = s.y * (1.0 - dist / (s.x * 1.6)) * exp(-b.w * 0.6);
			bend += d / max(dist, 1e-4) * (wave + after) * fall * len * 2.4;
		}
		// turned round a whirlwind and drawn into it
		for (int i = 0; i < whirl_count; i++) {
			vec4 w = whirls[i];
			vec2 d = root.xz - w.xz;
			float dist = length(d);
			if (dist < w.w * 1.5) {
				float k = 1.0 - dist / (w.w * 1.5);
				vec2 n = d / max(dist, 1e-4);
				bend += (vec2(-n.y, n.x) * 1.2 - n * 0.5) * k * len * (0.8 + 0.3 * sin(TIME * 9.0 + dist * 4.0));
			}
		}
		float bl = length(bend);
		if (bl > len * 0.95) bend *= len * 0.95 / bl;
		bl = min(bl, len * 0.95);
		float w2 = up * up;
		spine.xz += bend * w2;
		spine.y -= (len - sqrt(max(len * len - bl * bl, 0.0))) * w2;
		VERTEX = spine + side * (part < 0.5 ? widen : min(widen, 2.5)) * g;
	}
	// lit nearly as the turf it stands on, so a meadow reads as one
	NORMAL = normalize(mix(normalize(NORMAL), vec3(0.0, 1.0, 0.0), 0.62));
	vec3 cell = floor((root - origin) / vox + vec3(0.0, -0.5, 0.0));
	v_tone = turf_tone(cell) * 0.3 + (own - 0.5) * 0.12;
	v_col = COLOR.rgb;
	v_dry = COLOR.a;
	v_up = up;
	v_part = part;
	v_kind = kind;
}

void fragment() {
	// both faces of a blade are lit as its front: the normal leans up, so turned over it would face the ground
	if (!FRONT_FACING) NORMAL = -NORMAL;
	vec3 c = v_col * (1.0 + v_tone);
	if (v_part > 1.5) {
		// the heart of a flower
		c = v_kind < 1.5 ? vec3(0.62, 0.32, 0.03) : vec3(0.75, 0.62, 0.12);
	} else if (v_part > 0.5) {
		// its petals: buttercup yellow or a bellflower's violet
		c = v_kind < 1.5 ? vec3(0.78, 0.6, 0.06) : vec3(0.33, 0.17, 0.48);
		c *= 0.92 + 0.16 * v_up;
	} else {
		// darker in the shade at the root, lighter toward the tip where the light gets through it; dry grass bleaches
		c *= mix(0.62, 1.25, v_up);
		float lum = dot(c, vec3(0.3, 0.55, 0.15));
		c = mix(c, vec3(lum * 1.25, lum * 1.12, lum * 0.55), v_dry * v_up * 0.75);
	}
	ALBEDO = c;
	ROUGHNESS = 0.82;
	SPECULAR = 0.25;
	BACKLIGHT = c * 0.35;
}
