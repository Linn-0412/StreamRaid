// トゥーン調の輪郭線(裏返したメッシュを少し膨らませて描く)
shader_type spatial;
render_mode unshaded, cull_front, shadows_disabled;

uniform vec4 color : source_color = vec4(0.10, 0.07, 0.14, 1.0);
uniform float width = 0.035;

void vertex() {
	VERTEX += NORMAL * width;
}

void fragment() {
	ALBEDO = color.rgb;
}
