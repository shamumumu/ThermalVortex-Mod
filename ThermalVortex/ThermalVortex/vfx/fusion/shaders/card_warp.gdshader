shader_type canvas_item;
render_mode unshaded, blend_mix;

#include "res://ThermalVortex/vfx/fusion/shaders/card_surface.gdshaderinc"

uniform sampler2D card_texture : source_color, filter_linear, repeat_disable;
uniform sampler2D rounded_mask : filter_linear, repeat_disable;
uniform bool full_card = false;
uniform vec2 viewport_size = vec2(1280.0, 720.0);
uniform vec2 plane_center = vec2(0.5);
uniform float scale_factor = 1.28;
uniform float rotate_radians = 0.0;
uniform float twist_strength = 0.0;
uniform float plane_size = 0.96;
uniform vec2 card_offset = vec2(0.1, -0.5);
uniform vec2 card_uv_scale = vec2(2.0);
uniform vec4 card_color : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform float opacity = 1.0;

vec2 rotate_uv(vec2 p, float a) {
    return vec2(cos(a) * p.x - sin(a) * p.y, sin(a) * p.x + cos(a) * p.y);
}

void fragment() {
    vec2 plane_uv = (UV - plane_center) * vec2(viewport_size.x / viewport_size.y, 1.0) / plane_size + 0.5;
    vec2 p = (plane_uv - 0.5) * scale_factor;
    // Recovered common D3D11 fragment math of fusion01/02/03/04/05.
    vec2 w = vec2(0.5) + rotate_uv(p, length(p) * twist_strength - rotate_radians);
    // Native complete card canvas is 340 x 450; retain the source's radial
    // Rotate/Twist/Scale math while replacing its 1:1.45 display aspect.
    float card_ratio = 450.0 / 340.0;
    w = (w - vec2(0.5)) * vec2(card_ratio, 1.0) + vec2(0.5);
    vec2 card_uv = w * card_uv_scale + card_offset;
    vec2 edge = abs(card_uv * 2.0 - 1.0) - vec2(1.0);
    vec2 aa = max(fwidth(edge), vec2(0.0002));
    float clip_mask = clamp(min(1.0 - edge.x / aa.x, 1.0 - edge.y / aa.y), 0.0, 1.0);
    float mask = (full_card ? 1.0 : texture(rounded_mask, card_uv).r) * clip_mask;
    vec4 face = fusion_card_surface(card_texture, card_uv, full_card);
    // The original performs an overlay-like tint against a neutral 0.5 color.
    vec3 dark = 2.0 * face.rgb * card_color.rgb;
    vec3 light = 1.0 - 2.0 * (1.0 - face.rgb) * (1.0 - card_color.rgb);
    vec3 tinted = mix(dark, light, step(vec3(0.5), card_color.rgb));
    COLOR = vec4(tinted, mask * face.a * card_color.a * opacity);
}
