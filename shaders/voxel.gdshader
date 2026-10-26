shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Every face shows its voxels: a per-voxel colour jitter, a thin bevel line where the grid is
// big enough on screen to read (the player may have it or not), corner shading baked by the mesher, and glow for embers.
// Dressed stone is drawn smooth instead: each stone whole, a faint mottling across it, its joints
// cut into the wall. Grass sways at its tips and leaves as a mass, in a wind that comes in gusts.
// The steel, mail and brass of the figures shine: smoother, and coloured by what they reflect.
// What lives is drawn finer than its voxels once they are big enough on screen: blades on the turf and a fringe of them
// over the earth down its sides, leaves and needles in every voxel of a crown, furrows up a trunk, dashes across a birch.
// Vertex data: COLOR = linear rgb + colour variance, UV = (corner shade, glow),
// UV2 = (sway, skin: 0 rough, 1 dressed stone, 2 metal, 3 turf, 4 leaves, 5 bark, 6 birch bark, 7 needles, 8 burnt turf,
// 9 the earth of a bank under turf, grown over with grass, 10 lava, 11 iron plate).
// The figures are meshed in their own voxels but sculpted in halves of them: grid says how many cells
// a unit of the mesh holds, so the jitter and the bevel follow the cells they are made of.

uniform float grid = 1.0;
// a figure's tint (grey when dead, red when struck), for a hurt part drawn on its own: its kind's figures have theirs
// from the MultiMesh, in COLOR already
instance uniform vec4 tint = vec4(1.0);
// the bevel line between voxels: 1 drawn, 0 not (a setting of the player's)
global uniform float voxel_lines;
// the wind's strength: 1 on a fair day, more in a storm (the grass hears of it too)
global uniform float wind;
// where the sward of blades grows over the turf (see grass.gdshader): round (xyz) in full to x of reach, gone by y,
// grown by w; the turf under it is the shade between its blades
global uniform vec4 sward_at;
global uniform vec2 sward_reach;
// The map of valleys (AtlasView.cs) draws its ground with this too: the middle of its first cell and a cell's radius,
// voxels, and whether it is the map (w 1; 0 everywhere else, and none of this is worked out); its size; how much of each
// column is seen (1) or lies in the fog (0); the middle of the cell pointed at (xy) and of the one chosen (zw); the dark
// of the fog. A cell's sides are a faint line on the ground, brighter about the cell pointed at, gold about the one chosen.
uniform vec4 atlas = vec4(0.0);
uniform vec2 atlas_size = vec2(1.0);
uniform sampler2D atlas_fog : filter_linear, repeat_disable;
uniform vec4 atlas_hover = vec4(-1e5);
uniform vec3 atlas_dark = vec3(0.035, 0.04, 0.06);
// how much of what lies in the fog still shows through it (the big world map has it nearly black, as «Heroes»)
uniform float atlas_through = 0.1;

varying vec3 v_vox;
varying vec3 v_nrm;
varying vec2 v_ext;
varying flat int v_skin;
varying vec3 v_world;

float vhash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

// a smooth swell of light and shade across the turf, a few voxels to a patch, so a meadow is not a chequerboard
float patches(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = vhash(vec3(i, 5.0)), b = vhash(vec3(i + vec2(1.0, 0.0), 5.0));
	float c = vhash(vec3(i + vec2(0.0, 1.0), 5.0)), d = vhash(vec3(i + vec2(1.0, 1.0), 5.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// how a voxel of turf differs from the next: a little of its own, more of the patch it lies in
float turf_tone(vec3 cell) {
	return (vhash(cell) - 0.5) * 0.45 + (patches(cell.xz * 0.17) - 0.5) * 0.9 + (patches(cell.xz * 0.045 + 7.0) - 0.5) * 0.5;
}

vec3 vhash3(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

// how far a pixel reaches across a pattern of this many marks to a voxel: past about a third of a mark it shimmers
float fade(float px, float marks) {
	return 1.0 - smoothstep(0.12, 0.38, px * marks);
}

// Turf seen from above: in each quarter of a voxel a blade or two lying every way, darker at the root and catching the
// light toward the tip, the earth between them a little darker. Returns a factor on the colour, about 1 on average.
float turf_top(vec2 p, float px) {
	float amp = fade(px, 4.0);
	if (amp <= 0.0) return 1.0;
	vec2 q = p * 4.0, c = floor(q);
	float aa = max(px * 4.0, 0.02), lit = 0.0, cover = 0.0;
	for (int j = -1; j <= 1; j++)
		for (int i = -1; i <= 1; i++) {
			vec2 k = c + vec2(float(i), float(j));
			vec3 h = vhash3(vec3(k, 17.0));
			vec2 root = k + h.xy;
			float ang = h.z * 6.2832, len = 0.8 + 0.7 * fract(h.x * 7.3);
			vec2 dir = vec2(cos(ang), sin(ang)), d = q - root;
			float t = clamp(dot(d, dir), 0.0, len), along = t / len;
			float w = 0.16 * (1.0 - along * 0.75);
			float s = 1.0 - smoothstep(w - aa, w + aa, length(d - dir * t));
			cover = max(cover, s);
			lit = max(lit, s * (0.35 + 0.65 * along) * (0.7 + 0.6 * h.y));
		}
	return 1.0 + amp * (lit * 0.3 - (1.0 - cover) * 0.07 - 0.06);
}

// Turf, or a bank grown over under it, seen from the side: grass all over, blades hanging down it in streaks, the tips
// of those from the top catching the light in a ragged fringe along its upper edge, and the shade deeper toward the foot
// of the bank. A voxel is a hand's breadth: grass covers it whole, it is no slab of sod.
vec3 turf_side(vec3 col, float across, float up, float px, float fringe) {
	float amp = fade(px, 6.0);
	float edge = 0.62 + 0.14 * sin(across * 23.0 + sin(across * 7.0) * 2.0) + 0.12 * (vhash(vec3(floor(across * 6.0), 3.0, 1.0)) - 0.5);
	float aa = max(px * 1.5, 0.03);
	float tips = smoothstep(edge - aa, edge + aa, up) * fringe;
	float streak = sin(across * 37.0 + vhash(vec3(floor(across * 9.0), 5.0, 2.0)) * 6.0 + up * 3.0) * 0.5 + 0.5;
	float fine = sin(across * 71.0 + vhash(vec3(floor(across * 17.0), 8.0, 4.0)) * 6.0) * 0.5 + 0.5;
	return col * (0.86 + 0.14 * up + amp * ((streak * 0.6 + fine * 0.4) * 0.24 - 0.12) + tips * (0.1 + amp * 0.08));
}

// A crown's voxel face: three leaves across, each turned its own way and lit its own way; the gaps between them deep.
float leaves(vec2 p, float px, float n, vec2 shape) {
	float amp = fade(px, n);
	if (amp <= 0.0) return 1.0;
	vec2 q = p * n, c = floor(q);
	float best = 9.0, tone = 0.5;
	for (int j = -1; j <= 1; j++)
		for (int i = -1; i <= 1; i++) {
			vec2 k = c + vec2(float(i), float(j));
			vec3 h = vhash3(vec3(k, 41.0));
			vec2 o = q - (k + 0.15 + 0.7 * h.xy);
			float a = h.z * 3.1416, ca = cos(a), sa = sin(a);
			o = vec2(ca * o.x + sa * o.y, -sa * o.x + ca * o.y) / shape;
			float d = length(o);
			if (d < best) { best = d; tone = h.z; }
		}
	// the edge of a leaf soft over about a pixel across it, however narrow the leaf
	float aa = max(px * n * 1.2 / min(shape.x, shape.y), 0.04);
	float inside = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, best);
	return 1.0 + amp * (inside * ((tone - 0.5) * 0.36 + 0.07 - best * 0.06) - (1.0 - inside) * 0.2);
}

// Bark: furrows wandering up the trunk, darker in their depths, the ridges between them a little lighter.
float bark(float across, float up, float px) {
	float amp = fade(px, 5.0);
	if (amp <= 0.0) return 1.0;
	float wob = sin(up * 1.7 + across * 3.0) * 0.18 + sin(up * 4.3) * 0.07;
	float f = sin((across * 5.0 + wob) * 6.2832);
	float plate = vhash(vec3(floor(across * 5.0 + wob), floor(up * 1.6), 9.0));
	return 1.0 + amp * (smoothstep(-0.2, 0.9, f) * 0.12 - smoothstep(0.3, 1.0, -f) * 0.3 + (plate - 0.5) * 0.08);
}

// Birch bark: the white broken by short dark dashes lying across the trunk.
float birch(float across, float up, float px) {
	float amp = fade(px, 6.0);
	if (amp <= 0.0) return 1.0;
	vec2 q = vec2(across * 2.0, up * 6.0), c = floor(q);
	vec3 h = vhash3(vec3(c, 23.0));
	vec2 f = fract(q);
	float aa = max(px * 6.0, 0.05);
	float dash = step(0.55, h.x) * (1.0 - smoothstep(0.5 * h.y + 0.15 - aa, 0.5 * h.y + 0.15 + aa, abs(f.x - 0.5)))
		* (1.0 - smoothstep(0.18 - aa, 0.18 + aa, abs(f.y - 0.5)));
	return 1.0 + amp * (-dash * 0.62 + (h.z - 0.5) * 0.05);
}

// Burnt turf: flecks of grey ash and the black stubs of the grass on its top.
float scorch(vec2 p, float px) {
	float amp = fade(px, 5.0);
	if (amp <= 0.0) return 1.0;
	vec2 q = p * 5.0, c = floor(q);
	vec3 h = vhash3(vec3(c, 61.0));
	float aa = max(px * 5.0, 0.05);
	float fleck = 1.0 - smoothstep(0.17 - aa, 0.17 + aa, length(fract(q) - 0.2 - 0.6 * h.xy));
	return 1.0 + amp * fleck * (h.z > 0.62 ? 1.1 : -0.5);
}

void vertex() {
#ifdef VIEW_MODEL
	// what one gone into holds before its eyes (ViewModel.cs defines this): drawn as if almost at the eye, its depth
	// squeezed into the nearest sliver before the world's, so that a blade held out never sinks into a wall
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION.z = mix(POSITION.z, POSITION.w, 0.85);
#endif
	COLOR.rgb *= tint.rgb;
	v_vox = VERTEX * grid;
	v_nrm = NORMAL;
	v_ext = UV;
	v_skin = int(UV2.y + 0.5);
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (UV2.x > 0.0) {
		// the wind: a slow swell along the valley, gusts running through it, each blade a little out of step
		vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float gust = 0.55 + 0.45 * sin(TIME * 0.31 + w.x * 0.21 + w.z * 0.13);
		float phase = w.x * 3.1 + w.z * 2.3;
		// a crown or a bush is a mass tied to a trunk and the ground that do not sway: a storm sets it shivering rather
		// than sliding off them, while what sways at its tips takes the wind's full strength
		float storm = max(wind - 1.0, 0.0);
		float amp = UV2.x < 0.9 ? clamp(mix(1.0, wind, 0.3), 0.5, 1.4) : wind;
		float sway = (sin(TIME * 1.9 + phase) * 0.6 + sin(TIME * 3.7 + phase * 1.7) * 0.25) * gust * UV2.x * amp
			+ sin(TIME * 7.3 + phase * 2.3) * 0.08 * storm * UV2.x * step(UV2.x, 0.9);
		VERTEX.x += sway * 0.55;
		VERTEX.z += sway * 0.35;
		VERTEX.y -= abs(sway) * 0.12;
	}
}

// lava: a dark crust broken into plates that drift and turn slowly, molten rock showing bright in the cracks between
// them, its glow breathing; how hot each point is (0 the crust, 1 the cracks)
vec3 lava(vec3 base, vec2 p, float t, out float hot) {
	vec2 i = floor(p), f = fract(p);
	float d1 = 8.0, d2 = 8.0;
	for (int y = -1; y <= 1; y++)
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 h = vhash3(vec3(i + o, 3.0)).xy;
			h = 0.5 + 0.38 * sin(t * 0.35 + 6.2831 * h);
			float d = length(o + h - f);
			if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
		}
	float crack = 1.0 - smoothstep(0.02, 0.09, d2 - d1);
	float pulse = 0.8 + 0.2 * sin(t * 1.4 + vhash(vec3(i, 7.0)) * 6.2831);
	hot = mix(0.06, 0.8, crack) * pulse;
	return mix(base * vec3(0.16, 0.1, 0.09) + vec3(0.03, 0.025, 0.022), base * 1.1 + vec3(0.25, 0.1, 0.0), crack);
}

// iron laid in plates half a metre by a little less, each row set half a plate over the one under it: dark seams
// between them, a rivet head every voxel along a plate's top and bottom, each plate a tone of its own and hammered
// faintly; the rivets go once they are smaller than a pixel. A rivet's head is smoother than the plate.
vec3 plates(vec3 base, float var, vec2 face, float px, out float smooth_) {
	vec2 size = vec2(4.0, 3.0);
	float row = floor(face.y / size.y);
	vec2 q = vec2(face.x / size.x + mod(row, 2.0) * 0.5, face.y / size.y);
	vec2 id = floor(q);
	vec2 f = fract(q) * size;
	float edge = min(min(f.x, size.x - f.x), min(f.y, size.y - f.y));
	float seam = 1.0 - smoothstep(0.02, 0.02 + max(0.08, px * 1.2), edge);
	float dent = sin(face.x * 2.3 + id.y * 1.7) * sin(face.y * 2.9 - id.x * 2.1);
	vec3 col = base * (1.0 + (vhash(vec3(id, 5.0)) - 0.5) * var * 2.4 + dent * 0.025);
	float rv = min(abs(f.y - 0.36), abs(f.y - (size.y - 0.36)));
	float d = length(vec2(fract(f.x) - 0.5, rv));
	float near = 1.0 - smoothstep(0.25, 0.5, px);
	float head = (1.0 - smoothstep(0.1, 0.14, d)) * near;
	float rim = smoothstep(0.1, 0.14, d) * (1.0 - smoothstep(0.14, 0.19, d)) * near;
	col *= mix(1.0, 0.45, seam) * (1.0 + head * 0.55 - rim * 0.3);
	smooth_ = head;
	return col;
}

void fragment() {
	vec3 cell = floor(v_vox - v_nrm * 0.5 + 0.001);
	vec3 col;
	float line = 0.0;
	int skin = v_skin;
	float px = length(fwidth(v_vox));
	float hot = 1.0, shine = 0.0;
	if (skin == 10) {
		// its plates a little over a voxel across, on the top and down the sides of a tongue of it
		vec3 an = abs(v_nrm);
		vec2 face = an.y > 0.5 ? v_vox.xz : (an.x > 0.5 ? v_vox.zy : v_vox.xy);
		col = lava(COLOR.rgb, face * 0.4 + vec2(TIME * 0.03, TIME * 0.02), TIME, hot);
	} else if (skin == 11) {
		vec3 an = abs(v_nrm);
		vec2 face = an.x > 0.5 ? v_vox.zy : (an.y > 0.5 ? v_vox.xz : v_vox.xy);
		col = plates(COLOR.rgb, COLOR.a, face, px, shine);
	} else if (skin == 1) {
		// one stone, one tone, with a faint mottling and a darker skin toward its edges
		vec3 p = v_vox * 0.55;
		float mott = sin(p.x * 1.7 + p.y * 0.9) * sin(p.z * 1.3 - p.y * 1.1) + sin(p.x * 3.1 - p.z * 2.3) * 0.35;
		col = COLOR.rgb * (1.0 + mott * 0.035 + (vhash(floor(v_vox * 0.5)) - 0.5) * COLOR.a);
	} else {
		col = COLOR.rgb * (1.0 + (skin == 3 || skin == 9 ? turf_tone(cell) : vhash(cell) - 0.5) * COLOR.a);
		vec3 an = abs(v_nrm);
		vec3 fv = fract(v_vox);
		vec2 fp = an.x > 0.5 ? fv.yz : (an.y > 0.5 ? fv.xz : fv.xy);
		float ed = min(min(fp.x, 1.0 - fp.x), min(fp.y, 1.0 - fp.y));
		line = 1.0 - smoothstep(0.0, max(0.16, px), ed);
		line *= (1.0 - smoothstep(0.35, 0.7, px)) * voxel_lines;
		if (skin >= 3) {
			// the face's own two directions in voxels: across it and, on a side, up it
			bool top = an.y > 0.5;
			vec2 face = an.x > 0.5 ? v_vox.zy : (top ? v_vox.xz : v_vox.xy);
			float across = an.x > 0.5 ? v_vox.z : v_vox.x;
			if (skin == 3) {
				// its top grows blades (and the sward's shade), its sides a fringe over the earth; its underside is earth
				if (v_nrm.y > 0.5) {
					float under = sward_at.w * (1.0 - smoothstep(sward_reach.x, sward_reach.y, length(v_world.xz - sward_at.xz)));
					col *= turf_top(face, px) * mix(1.0, 0.74, under);
				}
				else if (!top) col = turf_side(col, across, fract(v_vox.y), px, 1.0);
			} else if (skin == 4) col *= leaves(face, px, 3.0, vec2(0.62, 0.36));
			else if (skin == 7) col *= leaves(face, px * 1.5, 5.0, vec2(0.75, 0.16));
			else if (skin == 8) { if (v_nrm.y > 0.5) col *= scorch(face, px); }
			// the earth of a bank under its turf, grown over: grass all down it, a little deeper in shade
			else if (skin == 9) col = turf_side(col, across, fract(v_vox.y), px, 0.0) * 0.9;
			else if (!top) col *= skin == 6 ? birch(across, v_vox.y, px) : bark(across, v_vox.y, px);
			// a crown's faces are leaves, not a wall of bricks: its bevel is lighter
			if (skin == 4 || skin == 7) line *= 0.5;
		}
	}
	col *= mix(1.0, 0.8, line);
	if (atlas.w > 0.5) {
		vec2 p = v_vox.xz;
		// the cell this is in (axial, pointed north and south), its middle, how far in from its nearest side
		vec2 rel = (p - atlas.xy) / atlas.z;
		vec3 f = vec3(0.57735 * rel.x - rel.y / 3.0, 2.0 / 3.0 * rel.y, 0.0);
		f.z = -f.x - f.y;
		vec3 rc = round(f), dd = abs(rc - f);
		if (dd.x > dd.y && dd.x > dd.z) rc.x = -rc.y - rc.z; else if (dd.y > dd.z) rc.y = -rc.x - rc.z;
		vec2 mid = atlas.xy + atlas.z * vec2(1.7320508 * (rc.x + rc.y * 0.5), 1.5 * rc.y);
		vec2 o = abs(p - mid);
		float edge = min(atlas.z * 0.8660254 - o.x, (atlas.z - o.y - o.x * 0.57735) * 0.8660254);
		float up = step(0.5, v_nrm.y), aa = max(px, 0.05);
		col *= 1.0 - 0.2 * (1.0 - smoothstep(0.3, 0.3 + aa * 1.5, edge)) * up;
		bool hov = distance(mid, atlas_hover.xy) < 1.0, pick = distance(mid, atlas_hover.zw) < 1.0;
		if (hov || pick) {
			float ring = (1.0 - smoothstep(0.7, 0.7 + aa * 2.0, edge)) * up;
			col = mix(col * (hov ? 1.07 : 1.0), pick ? vec3(1.0, 0.82, 0.38) : vec3(0.96, 0.95, 0.86), ring * 0.85);
		}
		float seen = texture(atlas_fog, p / atlas_size).r;
		col = mix(atlas_dark, col, atlas_through + (1.0 - atlas_through) * seen);
	}
	ALBEDO = col * v_ext.x;
	ROUGHNESS = skin == 11 ? mix(0.52, 0.3, shine) : skin == 2 ? 0.42 : skin == 1 ? 0.86 : 0.93;
	METALLIC = skin == 11 ? 0.4 : skin == 2 ? 0.6 : 0.0;
	SPECULAR = skin == 2 || skin == 11 ? 0.6 : 0.3;
	EMISSION = col * v_ext.y * hot;
}
