shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx, shadows_disabled;

// The land beyond the valley (FarLand): meadows in patches, forests as heaps of dark crowns, rock where it is steep;
// the old road a strip of beaten earth and the river a strip of water with the sky in it; all of it going into the
// colour of the horizon the further off it is, over and above the scene's fog, so its rim is no more than a shadow
// against the sky. Near the eyes it takes on the valley's own grain: each voxel of turf its own tone in patches a few
// voxels across, as the valley's turf is (voxel.gdshader), and the steps of a voxel's height drawn round the slopes as the
// edges of the valley's terraces are, so that where the valley ends the land beyond does not go smooth all at once.
// Vertex data: COLOR = linear rgb of the ground and how much of it is meadow, UV = how far from the river's edge and from
// the road's (scene units, negative on them).

global uniform vec4 sky_horizon;
global uniform vec4 sky_zenith;

// voxels to a scene unit (VoxelWorld.Vox is a sixteenth): the valley's grid, its origin on a whole voxel
const float VOXELS = 16.0;

varying vec3 v_world;

float h21(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1.0, 0.0)), f.x), mix(h21(i + vec2(0.0, 1.0)), h21(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 col = COLOR.rgb;
	// the meadow as the valley's looks under its tufts: the grass drying to straw in broad patches, the shade among the
	// blades darkening it a little
	float lum = dot(col, vec3(0.3, 0.55, 0.15));
	float dry = 0.3 + 0.4 * vnoise(v_world.xz * 0.07 + 3.0);
	col = mix(col, vec3(lum * 1.25, lum * 1.12, lum * 0.55), dry * COLOR.a) * mix(1.0, 0.9, COLOR.a);
	// patches over the meadows and crowns in the woods, fading out where they would shimmer
	float near = 1.0 - smoothstep(60.0, 220.0, dist);
	float grain = vnoise(v_world.xz * 2.1) * 0.55 + vnoise(v_world.xz * 0.45) * 0.45;
	col *= mix(1.0, 0.72 + 0.56 * grain, 0.4 + 0.6 * near);

	// the valley's grain near the eyes: a tone to each voxel, more of the patch it lies in, gone before a voxel is
	// smaller than a pixel; and the step of each voxel's height a darker line round the slope, gone where they crowd
	vec3 vox = v_world * VOXELS;
	float px = length(fwidth(vox.xz));
	float fine = 1.0 - smoothstep(0.35, 0.9, px);
	float tone = (h21(floor(vox.xz) + 0.5) - 0.5) * 0.45 * fine + (vnoise(vox.xz * 0.17) - 0.5) * 0.9 + (vnoise(vox.xz * 0.045 + 7.0) - 0.5) * 0.5;
	col *= 1.0 + tone * 0.3 * (0.35 + 0.65 * near);
	float py = fwidth(vox.y);
	float step_y = min(fract(vox.y), 1.0 - fract(vox.y));
	float terrace = (1.0 - smoothstep(0.0, max(py * 1.4, 0.04), step_y)) * (1.0 - smoothstep(0.25, 0.6, py)) * (1.0 - smoothstep(0.35, 0.9, px));
	col *= 1.0 - 0.14 * terrace;

	// the road and the river, their edges kept sharp
	float rp = fwidth(UV.y) + 1e-4, wp = fwidth(UV.x) + 1e-4;
	float road = 1.0 - smoothstep(-rp, rp, UV.y);
	col = mix(col, vec3(0.142, 0.094, 0.058) * (0.85 + 0.3 * grain) * (1.0 + tone * 0.2), road);
	float water = 1.0 - smoothstep(-wp, wp, UV.x);
	vec3 view = normalize(CAMERA_POSITION_WORLD - v_world);
	float fres = pow(1.0 - clamp(view.y, 0.0, 1.0), 4.0);
	col = mix(col, vec3(0.01, 0.017, 0.021), water);
	vec3 shine = mix(sky_zenith.rgb, sky_horizon.rgb, 0.7) * water * (0.18 + 0.7 * fres);

	// the haze: nothing over the near land (the scene's fog sees to that), the rim lost in the horizon
	float haze = 1.0 - exp(-max(dist - 110.0, 0.0) / 150.0);
	ALBEDO = col * (1.0 - haze);
	ROUGHNESS = mix(0.95, 0.14, water);
	SPECULAR = mix(0.15, 0.55, water) * (1.0 - haze);
	EMISSION = shine * (1.0 - haze) + sky_horizon.rgb * haze;
}
