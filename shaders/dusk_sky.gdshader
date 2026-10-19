shader_type sky;

// The sky over the valley through the day the siege keeps, painted from what Atmosphere tells it each frame in the
// sky_* globals: blue going to haze at the horizon by day; gold, then red round a sun that really goes down in the west
// and comes up in the east, with a pink band over the dusk opposite it; the moon over the south-west at night with its
// face mottled, red under the spell of the blood moon; the stars and a faint milky band; two layers of cloud drifting on
// the wind, lit from the sun's side, greyer and thicker in the rain, flaring when lightning goes off in them; and along
// the horizon far mountains, a forested ridge before them, the land under them lost in the haze.
// Seen from someone's eyes the sky is looked at along the view. Over the diorama an orthographic camera gives no rays to
// look along, so one is made from where the pixel is on the screen and how the camera turns and tilts: the horizon a
// third of the way down at the usual tilt, gone over the top when it looks down, and under it a sea of mist the valley
// floats on, going dark towards the foot of the screen under the interface.
// What lights the scene (the radiance pass) is still the violet sky above and the dark earth below, so the light on the
// valley is what it always was; the sky never reads TIME, so that pass is worked out again only when they change.

uniform vec3 amb_sky = vec3(0.254, 0.246, 0.578);
uniform vec3 amb_ground = vec3(0.023, 0.014, 0.009);
// Godot's own noise worked out at the start, tiling: r the heaps of the low clouds, g their finer stuff, b the streaks
// of the high ones, a a fine grain for the moon's face, the milky band and the mountains
uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;

global uniform vec4 sky_sun;      // xyz towards the sun as it stands (under the horizon at night), w how much of it shows through the weather
global uniform vec4 sky_moon;     // xyz towards the moon, w how red it is
global uniform vec4 sky_mood;     // the day (0 dawn, 1 sunset), the dark, the rain, the fog
global uniform vec4 sky_zenith;   // rgb overhead, a how much the stars show
global uniform vec4 sky_horizon;  // rgb at the horizon, a how much the haze hides of the mountains
global uniform vec4 sky_glow;     // rgb the glow over the horizon round the sun, a the pink band opposite it at dusk and dawn
global uniform vec4 sky_light;    // rgb the sunlight the clouds and the disc are lit by, a how high and bright the moon is
global uniform vec4 sky_view;     // the diorama's turn and tilt, 1 if seen from someone's eyes, the screen's width to its height
global uniform vec4 sky_flash;    // xyz towards the last lightning, w how bright it is now
global uniform vec4 sky_clock;    // seconds, the clouds' drift (x, z), how much of the sky they cover

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

vec3 hash33(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

float hash11(float p) {
	p = fract(p * 0.1031);
	p *= p + 33.33;
	p *= p + p;
	return fract(p);
}

// the diorama's ray: azimuth across the screen from the camera's turn, elevation down it from where its tilt sets the
// horizon, a radian of sky to about nine-tenths of the screen's height either way so the sun and the moon stay round
vec3 diorama_ray(vec2 uv, out float horizon) {
	horizon = mix(0.45, -0.22, smoothstep(0.3, 1.3, sky_view.y));
	float k = 1.15;
	float el = (horizon - uv.y) * k;
	float az = sky_view.x - (uv.x - 0.5) * k * sky_view.w;
	return vec3(sin(az) * cos(el), sin(el), cos(az) * cos(el));
}

// the colour of the air in a direction: the horizon's going up into the zenith's, the glow over the horizon round the
// sun, and at dusk and dawn the pink band opposite it over the earth's blue shadow
vec3 air(vec3 d, vec3 sun) {
	float h = max(d.y, 0.0);
	vec3 c = mix(sky_horizon.rgb, sky_zenith.rgb, 1.0 - exp(-h * 3.2));
	vec2 flat_d = normalize(d.xz + vec2(1e-5, 0.0)), flat_s = normalize(sun.xz + vec2(1e-5, 0.0));
	float side = dot(flat_d, flat_s) * 0.5 + 0.5;
	float low = exp(-h * 5.5);
	c += sky_glow.rgb * pow(side, 2.6) * low;
	float away = pow(1.0 - side, 2.0) * sky_glow.a;
	c = mix(c, c * vec3(0.62, 0.66, 0.86), away * exp(-h * 18.0));
	c += vec3(0.30, 0.13, 0.17) * away * exp(-pow((h - 0.14) / 0.09, 2.0)) * (sky_horizon.r + sky_horizon.g);
	return c;
}

// the stars: one cell in some hundreds of a grid round the sky holds one, a point a little off its middle, of its own
// brightness and tint, twinkling slowly
vec3 stars(vec3 d) {
	vec3 c = vec3(0.0);
	for (int layer = 0; layer < 2; layer++) {
		float scale = layer == 0 ? 260.0 : 110.0;
		float keep = layer == 0 ? 0.985 : 0.9955;
		vec3 p = d * scale;
		vec3 cell = floor(p);
		float h = hash13(cell + float(layer) * 17.0);
		if (h < keep) continue;
		vec3 jit = hash33(cell + 3.7) * 0.5 + 0.25;
		float r = length(fract(p) - jit);
		float bright = (h - keep) / (1.0 - keep);
		bright = layer == 0 ? 0.25 + bright * 0.9 : 1.0 + bright * 2.6;
		float twinkle = 0.75 + 0.25 * sin(sky_clock.x * (1.3 + h * 3.0) + h * 91.0);
		vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.86, 0.68), hash11(h * 311.0));
		c += tint * bright * twinkle * smoothstep(0.32, 0.0, r);
	}
	return c;
}

// the milky band: a great circle across the night, patchy, a darker lane of dust along its middle
vec3 milky(vec3 d) {
	vec3 n = normalize(vec3(0.42, 0.55, -0.72));
	vec3 b1 = normalize(cross(n, vec3(0.0, 1.0, 0.0)));
	vec3 b2 = cross(n, b1);
	float across = dot(d, n);
	float along = atan(dot(d, b1), dot(d, b2)) / TAU;
	float band = exp(-pow(across / 0.2, 2.0));
	float grain = textureLod(cloud_noise, vec2(along * 4.0, across * 1.3 + 0.5), 0.0).a;
	float clumps = textureLod(cloud_noise, vec2(along * 9.0, across * 3.0 + 0.2), 0.0).g;
	float lane = 1.0 - 0.7 * exp(-pow((across - 0.02 + (clumps - 0.5) * 0.08) / 0.045, 2.0));
	return vec3(0.62, 0.66, 0.85) * band * lane * (0.4 + 0.9 * grain * grain) * (0.5 + clumps) * 0.032;
}

// the clouds: low heaps on a plane over the valley seen through a curved sky, and high streaks along the wind; lit on
// the side towards the sun, a silver edge against it, pink and gold underneath at dusk, moonlit at night, red underneath
// under the blood moon, grey and thick in the rain; lightning lights them from inside
vec4 clouds(vec3 d, vec3 sun, out vec4 high) {
	high = vec4(0.0);
	if (d.y <= 0.0) return vec4(0.0);
	float y = d.y, cover = sky_clock.w, wet = sky_mood.z, dark = sky_mood.y, blood = sky_moon.w;
	vec2 drift = sky_clock.yz;
	vec3 amb = mix(sky_horizon.rgb, sky_zenith.rgb, 0.45);
	vec3 sun_col = sky_light.rgb;
	float mu = dot(d, sun);
	vec3 moon = normalize(sky_moon.xyz);
	vec3 moon_col = mix(vec3(0.42, 0.48, 0.66), vec3(0.75, 0.10, 0.06), blood) * sky_light.a * 0.055;
	vec3 flash = vec3(0.72, 0.76, 1.0) * sky_flash.w * (0.05 + 1.3 * pow(max(dot(d, normalize(sky_flash.xyz)), 0.0), 14.0));

	// the high streaks, along the way the wind blows
	vec2 q = d.xz / (y + 0.2) * 0.11 + drift * 0.55;
	vec2 wd = normalize(drift + vec2(1.0, 0.3));
	q = vec2(dot(q, wd) * 0.3, dot(q, vec2(-wd.y, wd.x)) * 1.7);
	float s = texture(cloud_noise, q).b * 0.75 + texture(cloud_noise, q * 3.1 + 0.4).a * 0.25;
	float ha = smoothstep(0.56, 0.86, s) * (0.3 + 0.35 * cover) * smoothstep(0.015, 0.22, y) * (1.0 - wet * 0.7);
	vec3 hc = amb * 1.15 + sun_col * 0.85 + sky_glow.rgb * 0.9 + moon_col * 0.6 + flash * 0.5;
	high = vec4(mix(hc, sky_horizon.rgb, (1.0 - smoothstep(0.0, 0.3, y)) * 0.5), ha);

	// the low heaps
	vec2 p = d.xz / (y + 0.07) * 0.22 + drift;
	vec2 toward = normalize(sun.xz + vec2(1e-4, 0.0)) * 0.03;
	float n = texture(cloud_noise, p).r * 0.66 + texture(cloud_noise, p * 2.4 + vec2(0.37, 0.71)).g * 0.34;
	float n2 = texture(cloud_noise, p + toward).r * 0.66 + texture(cloud_noise, (p + toward) * 2.4 + vec2(0.37, 0.71)).g * 0.34;
	float lo = mix(0.69, 0.3, cover);
	float dens = smoothstep(lo, lo + 0.2, n);
	float core = smoothstep(lo + 0.08, lo + 0.42, n);
	float lit = clamp(0.5 + (n - n2) * 9.0, 0.0, 1.0);
	float under = 1.0 - lit;
	vec3 c = amb * (0.78 - 0.3 * core) + sun_col * (0.25 + 0.75 * lit) * (0.85 - 0.4 * core);
	c += sky_glow.rgb * under * 0.75;
	c += moon_col * (0.35 + lit) * (1.0 + 2.0 * pow(max(dot(d, moon), 0.0), 6.0));
	c += vec3(0.5, 0.035, 0.025) * blood * dark * under * (0.5 + core) * 0.32;
	c += sun_col * pow(max(mu, 0.0), 10.0) * (1.0 - dens) * 1.6;
	c *= 1.0 - wet * core * 0.4;
	// lightning lights the thick of the cloud from inside, its thin edges less
	c += flash * (0.2 + 0.8 * core);
	float a = dens * (0.88 + 0.1 * wet) * smoothstep(0.0, mix(0.09, 0.02, wet), y);
	c = mix(c, sky_horizon.rgb, (1.0 - smoothstep(0.0, 0.26, y)) * 0.62 * (1.0 - 0.6 * wet));
	return vec4(c, a);
}

// how high the far mountains stand over the horizon in a direction, and the ridge before them with its forest
float mountains(float u) {
	float a = textureLod(cloud_noise, vec2(u * 3.0, 0.13), 0.0).r;
	float b = textureLod(cloud_noise, vec2(u * 13.0, 0.47), 0.0).a;
	float peaks = 1.0 - abs(a * 2.0 - 1.0);
	return 0.04 + pow(peaks, 2.2) * 0.1 + (b - 0.5) * 0.026;
}

// the colour of the mountains' faces: each spur coming down from the crest lit on the side towards the sun and in
// shade on the other, the light going off them lower down into the haze at their feet
vec3 mountain_face(float u, float y, float top, vec3 sun, vec3 base) {
	float depth = max(top - y, 0.0);
	// spurs run down and out from the peaks: the face is looked up a little to the side the lower it lies
	float v = u * 60.0 + depth * 14.0 * (textureLod(cloud_noise, vec2(u * 7.0, 0.83), 0.0).g - 0.5);
	float f0 = textureLod(cloud_noise, vec2(v * 0.05, depth * 2.2 + 0.31), 0.0).a;
	float f1 = textureLod(cloud_noise, vec2(v * 0.05 + 0.004, depth * 2.2 + 0.31), 0.0).a;
	float side = sign(dot(vec2(cos(u * 6.2831853), -sin(u * 6.2831853)), normalize(sun.xz + vec2(1e-4, 0.0))));
	float lit = clamp((f0 - f1) * 60.0 * side, -1.0, 1.0) * (1.0 - smoothstep(0.0, 0.05, depth) * 0.5);
	return base * (1.0 + 0.22 * lit) * (0.9 + 0.2 * smoothstep(0.0, 0.03, depth));
}

float ridge(float u) {
	float a = textureLod(cloud_noise, vec2(u * 5.0, 0.71), 0.0).g;
	float b = textureLod(cloud_noise, vec2(u * 17.0, 0.29), 0.0).r;
	return 0.028 + a * a * 0.06 + (b - 0.5) * 0.012;
}

// a forest along the ridge: spruce after spruce, each a narrow cone of its own height, here and there a gap
float forest(float u, float base) {
	float k = u * 760.0;
	float i = floor(k), f = fract(k);
	float h1 = hash11(i + 0.5), h2 = hash11(i * 1.7 + 9.1);
	float tall = (0.004 + 0.007 * h1) * step(0.18, h2);
	float w = 0.28 + 0.18 * h2;
	return base + max(tall * (1.0 - abs(f - 0.5) / w), 0.0);
}

void sky() {
	if (AT_CUBEMAP_PASS) {
		COLOR = mix(amb_ground, amb_sky, smoothstep(-0.25, 0.35, EYEDIR.y));
	} else {
		bool eyes = sky_view.z > 0.5;
		float horizon_at = 0.0;
		vec3 d = eyes ? EYEDIR : diorama_ray(SCREEN_UV, horizon_at);
		vec3 sun = normalize(sky_sun.xyz);
		vec3 moon = normalize(sky_moon.xyz);
		// over the diorama the moon is brought down to where a backdrop shows it
		if (!eyes) moon = normalize(vec3(moon.x, length(moon.xz) * 0.36, moon.z));
		float dark = sky_mood.y, blood = sky_moon.w;
		float px = max(fwidth(d.y), 1e-5);

		vec3 c = air(d, sun);
		// the night: stars and the milky band, hidden by the haze low down and by the rain
		float night = sky_zenith.a * smoothstep(0.0, 0.18, d.y);
		if (night > 0.001) c += (stars(d) + milky(d)) * night;

		// the sun: a disc too bright to look at, a glare round it, dimmed and reddened by the air when it is low
		float mu = dot(d, sun);
		float sun_r = 0.00009;
		float disc = smoothstep(sun_r + px * 0.02, sun_r - px * 0.02, 1.0 - mu);
		float limb = 0.75 + 0.25 * sqrt(clamp(1.0 - (1.0 - mu) / sun_r, 0.0, 1.0));
		vec3 sun_wide = sky_light.rgb * (pow(max(mu, 0.0), 60.0) * 0.32 + pow(max(mu, 0.0), 6.0) * 0.07);
		vec3 sun_glare = sky_light.rgb * pow(max(mu, 0.0), 900.0) * 1.4 + sun_wide;
		c += sky_light.rgb * disc * limb * 26.0 * sky_sun.w;

		// the moon: a disc with dark seas on its face, rounder lit in the middle; under the spell a red one, burning
		float mm = dot(d, moon);
		float moon_r = 0.0004;
		vec3 moon_col = mix(vec3(0.86, 0.9, 1.0) * 1.15, vec3(1.0, 0.11, 0.04) * 1.25, blood);
		vec3 t1 = normalize(cross(moon, vec3(0.0, 1.0, 0.0)));
		vec3 t2 = cross(t1, moon);
		vec2 face = vec2(dot(d, t1), dot(d, t2)) / sqrt(2.0 * moon_r);
		float fr = length(face);
		float moon_disc = smoothstep(1.0 + px * 60.0, 1.0 - px * 60.0, fr) * step(0.0, mm);
		float moon_up = sky_light.a;
		// its face is looked up only on the disc, filtered by how fast it runs across the screen
		vec2 fdx = dFdx(face), fdy = dFdy(face);
		if (moon_disc * moon_up > 0.0) {
			float seas = textureGrad(cloud_noise, face * 0.2 + vec2(0.31, 0.67), fdx * 0.2, fdy * 0.2).r;
			float craters = textureGrad(cloud_noise, face * 0.09 + vec2(0.13, 0.29), fdx * 0.09, fdy * 0.09).a;
			float shade = (0.62 + 0.38 * sqrt(clamp(1.0 - fr * fr, 0.0, 1.0))) * (1.0 - 0.38 * smoothstep(0.5, 0.64, seas) - 0.14 * smoothstep(0.52, 0.72, craters));
			c = mix(c, moon_col * shade, moon_disc * moon_up * (0.25 + 0.75 * sky_sun.w));
		}
		vec3 moon_glare = moon_col * (pow(max(mm, 0.0), 1500.0) * 0.2 + pow(max(mm, 0.0), 90.0) * 0.1 + pow(max(mm, 0.0), 8.0) * (0.015 + 0.03 * blood)) * moon_up;

		// the clouds, the high ones first; the glare of the sun and the moon comes through them, less where they are thick
		vec4 hi;
		vec4 lo = clouds(d, sun, hi);
		c = mix(c, hi.rgb, hi.a);
		c = mix(c, lo.rgb, lo.a);
		c += (sun_glare * sky_sun.w + moon_glare) * (1.0 - 0.75 * lo.a);
		// lightning far off lights the air round it a little, even where there is no cloud
		c += vec3(0.3, 0.33, 0.45) * sky_flash.w * 0.06 * pow(max(dot(d, normalize(sky_flash.xyz)), 0.0), 6.0);

		float u = atan(d.x, d.z) / TAU;
		float haze = sky_horizon.a;
		// rain and fog hide the mountains
		float seen = 1.0 - 0.85 * max(sky_mood.z, sky_mood.w);
		// how much of the far land stands in the way: the air before it still glows round the sun behind it
		float land = 0.0;
		// the mountains stand no higher than a tenth of a radian or so: the sky over them need not look for them
		if (eyes && d.y < 0.2) {
			// the far mountains in the haze, their spurs lit on the side towards the sun; the forested ridge before them darker
			float mtn = mountains(u);
			vec3 far_col = mountain_face(u, d.y, mtn, sun, sky_horizon.rgb * vec3(0.46, 0.5, 0.66));
			far_col = mix(far_col, sky_horizon.rgb, 0.32 + 0.6 * haze + 0.25 * (1.0 - smoothstep(0.0, mtn, d.y)));
			land = smoothstep(mtn + px, mtn - px, d.y) * seen;
			c = mix(c, far_col, land);
			float rg = ridge(u);
			float trees = forest(u, rg);
			vec3 near_col = mix(sky_horizon.rgb * vec3(0.32, 0.37, 0.42), sky_horizon.rgb, 0.3 + 0.45 * haze);
			float near_k = smoothstep(trees + px, trees - px, d.y) * (1.0 - 0.5 * max(sky_mood.z, sky_mood.w));
			c = mix(c, near_col, near_k);
			land = max(land, near_k * 0.8);
			// under the horizon: the land far off, lost in the haze
			c = mix(c, sky_horizon.rgb * 0.8, smoothstep(0.0, -0.08, d.y) * 0.5);
			c += sun_wide * sky_sun.w * land * 0.6 * (1.0 - 0.75 * lo.a);
		} else if (!eyes) {
			// the diorama floats on a sea of mist with the far mountains standing out of it
			vec3 mist_top = sky_horizon.rgb * 0.72;
			if (d.y < 0.2) {
				float mtn = mountains(u) * 1.15;
				vec3 far_col = mountain_face(u, d.y, mtn, sun, sky_horizon.rgb * vec3(0.4, 0.44, 0.58));
				far_col = mix(far_col, sky_horizon.rgb, 0.25 + 0.6 * haze);
				float foot = smoothstep(-0.01, mtn * 0.6, d.y);
				land = smoothstep(mtn + px, mtn - px, d.y) * mix(1.0, seen, foot);
				c = mix(c, mix(mist_top, far_col, foot), land);
			}
			if (d.y < 0.0) {
				// the tops of the mist seen from above, in swells, the sun catching them; lower down only the haze
				vec2 p = d.xz / (-d.y + 0.035) * 0.16 + sky_clock.yz * 0.4;
				float m = texture(cloud_noise, p).r * 0.7 + texture(cloud_noise, p * 2.6 + 0.3).g * 0.3;
				float swell = smoothstep(0.3, 0.8, m);
				vec3 mist = mist_top * (0.62 + 0.42 * swell) + sky_light.rgb * 0.07 * swell * max(sun.y + 0.1, 0.0);
				c = mix(c, mist, smoothstep(0.0, -0.015, d.y));
			}
			// the glow of the sun behind the mountains comes through the air before them
			c += sun_wide * sky_sun.w * land * 0.6 * (1.0 - 0.75 * lo.a);
			// darker towards the foot of the screen, under the interface: the old dusk under everything
			vec3 deep = mix(vec3(0.0033, 0.0027, 0.0044), sky_horizon.rgb * 0.06, 0.5);
			c = mix(c, deep, smoothstep(max(horizon_at, 0.0) + 0.02, 0.8, SCREEN_UV.y));
		}
		COLOR = c;
	}
}
