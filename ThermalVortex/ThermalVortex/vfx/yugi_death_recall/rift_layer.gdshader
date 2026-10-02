shader_type canvas_item;
render_mode unshaded;

varying vec4 vertex_color;

void vertex() {
    vertex_color = COLOR;
}

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    // One complete rear layer, with the supplied RGBA preserved. The body
    // naturally covers the rift wherever the body itself is opaque.
    COLOR = source * vertex_color;
}
