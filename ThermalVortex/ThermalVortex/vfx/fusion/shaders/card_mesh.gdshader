shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_opaque;

#include "res://ThermalVortex/vfx/fusion/shaders/card_surface.gdshaderinc"

uniform sampler2D card_texture : source_color, filter_linear, repeat_disable;
uniform sampler2D back_texture : source_color, filter_linear, repeat_disable;
uniform sampler2D rounded_mask : filter_linear, repeat_disable;
uniform bool full_card = false;
uniform float opacity : hint_range(0.0, 1.0) = 1.0;
uniform float edge_light = 0.0;
uniform vec3 rim_color : source_color = vec3(0.30, 0.45, 1.0);

void fragment() {
    vec2 uv = UV;
    vec4 face = FRONT_FACING ? fusion_card_surface(card_texture, uv, full_card) : texture(back_texture, vec2(1.0 - uv.x, uv.y));
    // The factory provides the native frame silhouette. Use its own alpha
    // instead of clipping it to the source effect's old rounded rectangle.
    float mask = full_card ? 1.0 : texture(rounded_mask, uv).r;
    float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
    float rim = (1.0 - smoothstep(0.012, 0.045, edge)) * edge_light;
    ALBEDO = face.rgb + rim_color * rim;
    ALPHA = face.a * mask * opacity;
}
