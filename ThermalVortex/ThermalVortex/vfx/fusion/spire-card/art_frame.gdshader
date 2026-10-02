shader_type canvas_item;
render_mode unshaded;

uniform sampler2D portrait : source_color, filter_linear, repeat_disable;
uniform vec2 portrait_size = vec2(1.0);
uniform bool has_portrait = false;
uniform bool card_back = false;
uniform bool extra_deck = false;
uniform vec3 frame_hsv = vec3(0.756, 0.77, 0.75);

// Native ExtraDeckCardMaterials.ColdWhiteShaderCode defaults and color mapping.
uniform vec4 shadow_tint : source_color = vec4(0.42, 0.55, 0.64, 1.0);
uniform vec4 mid_tint : source_color = vec4(0.78, 0.93, 0.98, 1.0);
uniform vec4 highlight_tint : source_color = vec4(1.0, 1.0, 0.97, 1.0);
uniform float contrast = 1.32;
uniform float highlight_strength = 0.16;
uniform float cool_edge_strength = 0.12;

varying vec4 modulate_color;

void vertex() {
    modulate_color = COLOR;
}

vec3 native_pool_color(vec3 color) {
    // Native hsv.gdshader uses YIQ hue rotation rather than HSV replacement.
    mat3 rgb_to_yiq = mat3(vec3(0.2989, 0.5959, 0.2115),
        vec3(0.5870, -0.2774, -0.5229), vec3(0.1140, -0.3216, 0.3114));
    vec3 yiq = rgb_to_yiq * color;
    float angle = (1.0 - frame_hsv.x) * 6.283185;
    mat3 hue_shift = mat3(vec3(1.0, 0.0, 0.0),
        vec3(0.0, cos(angle), -sin(angle)), vec3(0.0, sin(angle), cos(angle)));
    yiq *= hue_shift;
    yiq *= vec3(1.0, frame_hsv.y, frame_hsv.y) * frame_hsv.z;
    return clamp(inverse(rgb_to_yiq) * yiq, vec3(0.0), vec3(1.0));
}

vec3 native_extra_deck_color(vec3 color) {
    float luma = dot(color, vec3(0.299, 0.587, 0.114));
    float shade = clamp((luma - 0.5) * contrast + 0.5, 0.0, 1.0);
    vec3 cold = mix(shadow_tint.rgb, mid_tint.rgb, smoothstep(0.05, 0.78, shade));
    cold = mix(cold, highlight_tint.rgb, smoothstep(0.70, 0.98, shade));
    float edge = smoothstep(0.22, 0.55, shade) * (1.0 - smoothstep(0.80, 1.0, shade));
    float high = smoothstep(0.74, 1.0, shade);
    cold += vec3(0.0, 0.05, 0.09) * edge * cool_edge_strength;
    cold += vec3(0.12, 0.16, 0.18) * high * highlight_strength;
    return clamp(cold, vec3(0.0), vec3(1.0));
}

void fragment() {
    vec4 frame = texture(TEXTURE, UV);
    frame.rgb = extra_deck ? native_extra_deck_color(frame.rgb) : native_pool_color(frame.rgb);

    if (has_portrait && !card_back) {
        // Native card.tscn: Frame is (-150,-211)..(150,211), while Portrait
        // is (-125,-168)..(125,22). Draw art behind this upper window only.
        // The source frame, including its entire empty rules panel, stays intact.
        vec2 portrait_area = vec2(250.0, 190.0);
        vec2 local_uv = (UV * vec2(300.0, 422.0) - vec2(25.0, 43.0)) / portrait_area;
        float inside = step(0.0, local_uv.x) * step(local_uv.x, 1.0)
            * step(0.0, local_uv.y) * step(local_uv.y, 1.0);
        vec2 dimensions = max(portrait_size, vec2(1.0));
        float fit = min(portrait_area.x / dimensions.x, portrait_area.y / dimensions.y);
        vec2 portrait_uv = (local_uv - 0.5) * portrait_area / (dimensions * fit) + 0.5;
        float visible_portrait = step(0.0, portrait_uv.x) * step(portrait_uv.x, 1.0)
            * step(0.0, portrait_uv.y) * step(portrait_uv.y, 1.0);
        vec4 art = texture(portrait, clamp(portrait_uv, vec2(0.0), vec2(1.0)));
        vec3 portrait_color = mix(vec3(0.045, 0.035, 0.055), art.rgb, art.a * visible_portrait);
        float alpha = frame.a + inside * (1.0 - frame.a);
        vec3 color = (frame.rgb * frame.a + portrait_color * inside * (1.0 - frame.a)) / max(alpha, 0.00001);
        COLOR = vec4(color, alpha) * modulate_color;
    } else {
        // A quiet, text-free mod back, fitted inside the same native outline.
        vec2 inner_min = vec2(0.082, 0.070);
        vec2 inner_max = vec2(0.918, 0.928);
        vec2 inner_uv = (UV - inner_min) / (inner_max - inner_min);
        vec2 feather = vec2(0.0025, 0.0018);
        float interior = smoothstep(inner_min.x, inner_min.x + feather.x, UV.x)
            * (1.0 - smoothstep(inner_max.x - feather.x, inner_max.x, UV.x))
            * smoothstep(inner_min.y, inner_min.y + feather.y, UV.y)
            * (1.0 - smoothstep(inner_max.y - feather.y, inner_max.y, UV.y));
        vec3 face_color = vec3(0.105, 0.062, 0.155);
        vec2 p = abs(inner_uv - 0.5);
        float diamond = abs(p.x * 1.2 + p.y * 0.82 - 0.33);
        float smaller = abs(p.x * 1.2 + p.y * 0.82 - 0.26);
        float ring = (1.0 - smoothstep(0.003, 0.012, diamond)) * 0.75
            + (1.0 - smoothstep(0.002, 0.006, smaller)) * 0.25;
        face_color += vec3(0.34, 0.22, 0.44) * ring;
        face_color += vec3(0.03, 0.015, 0.045) * (1.0 - smoothstep(0.0, 0.7, length(p)));
        COLOR = mix(frame, vec4(face_color, 1.0), interior) * modulate_color;
    }
}
