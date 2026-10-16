shader_type sky;

// What the camera sees behind the diorama is a dusk gradient across the screen.
// What the scene is lit by (the radiance pass) is a violet sky above and dark earth below.

uniform vec3 bg_top = vec3(0.027, 0.017, 0.040);
uniform vec3 bg_mid = vec3(0.009, 0.006, 0.015);
uniform vec3 bg_low = vec3(0.0033, 0.0027, 0.0044);
uniform vec3 amb_sky = vec3(0.254, 0.246, 0.578);
uniform vec3 amb_ground = vec3(0.023, 0.014, 0.009);

void sky() {
	if (AT_CUBEMAP_PASS) {
		COLOR = mix(amb_ground, amb_sky, smoothstep(-0.25, 0.35, EYEDIR.y));
	} else {
		vec2 p = (SCREEN_UV - vec2(0.5, 0.06)) / vec2(1.3, 0.95);
		float r = length(p);
		vec3 c = mix(bg_top, bg_mid, smoothstep(0.0, 0.52, r));
		COLOR = mix(c, bg_low, smoothstep(0.52, 1.0, r));
	}
}
