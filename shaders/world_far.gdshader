shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The world map seen from far off (HexMapView.cs, HexFar.cs): each hex a flat hexagon of its ground's colour, a hill or
// a mountain a pyramid; the colour comes with the vertices, linear. What is not seen lies in the dark, as in the
// voxels of the near view: the fog is a grid of half-hexes by rows, read from a point's place in the map's voxels.

uniform sampler2D atlas_fog : filter_linear, repeat_disable;
uniform vec2 atlas_size = vec2(1.0);
uniform vec3 atlas_dark = vec3(0.035, 0.04, 0.06);

varying vec3 v_vox;

void vertex() {
	v_vox = VERTEX;
}

void fragment() {
	float seen = texture(atlas_fog, v_vox.xz / atlas_size).r;
	ALBEDO = mix(atlas_dark, COLOR.rgb * 0.82, 0.04 + 0.96 * seen);
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
