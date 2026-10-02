shader_type canvas_item;
render_mode unshaded;

uniform sampler2D portal_contour : filter_linear, repeat_disable;
uniform vec2 portal_size = vec2(1120.0);
uniform vec2 portal_uv_origin;
uniform vec2 portal_uv_x;
uniform vec2 portal_uv_y;
uniform bool mask_active = false;
uniform sampler2D initial_texture : source_color, filter_linear, repeat_disable;
uniform vec2 initial_uv_origin;
uniform vec2 initial_uv_x;
uniform vec2 initial_uv_y;
uniform float initial_weight = 0.0;
uniform float rim_light_strength : hint_range(0.0, 1.0) = 0.0;
uniform float rim_light_width : hint_range(0.5, 12.0) = 3.0;
uniform vec4 rim_light_color : source_color = vec4(0.67, 0.38, 1.0, 1.0);
varying vec4 vertex_color;

void vertex() {
    vertex_color = COLOR;
}

float recall_alpha_at(sampler2D body_texture, vec2 body_uv) {
    float body_alpha = texture(body_texture, body_uv).a;
    if (any(lessThan(body_uv, vec2(0.0))) || any(greaterThan(body_uv, vec2(1.0)))) {
        body_alpha = 0.0;
    }
    vec2 initial_uv = initial_uv_origin + initial_uv_x * body_uv.x + initial_uv_y * body_uv.y;
    float initial_alpha = texture(initial_texture, initial_uv).a;
    if (any(lessThan(initial_uv, vec2(0.0))) || any(greaterThan(initial_uv, vec2(1.0)))) {
        initial_alpha = 0.0;
    }
    return mix(body_alpha, initial_alpha, initial_weight);
}

void fragment() {
    vec4 body = texture(TEXTURE, UV);
    vec2 initial_uv = initial_uv_origin + initial_uv_x * UV.x + initial_uv_y * UV.y;
    vec4 initial = texture(initial_texture, initial_uv);
    if (any(lessThan(initial_uv, vec2(0.0))) || any(greaterThan(initial_uv, vec2(1.0)))) {
        initial = vec4(0.0);
    }
    float alpha = mix(body.a, initial.a, initial_weight);
    vec3 premultiplied = mix(body.rgb * body.a, initial.rgb * initial.a, initial_weight);
    vec2 portal_uv = portal_uv_origin + portal_uv_x * UV.x + portal_uv_y * UV.y;
    float contour_rows = float(textureSize(portal_contour, 0).y);
    float contour_row = clamp((portal_uv.y * portal_size.y - 0.5) / 4.0, 0.0, contour_rows - 1.0);
    vec2 contour = texture(portal_contour, vec2(0.5, (contour_row + 0.5) / contour_rows)).rg;
    float band = clamp((contour.y - contour.x) * portal_size.x * 0.13, 16.0, 48.0);
    float gate = contour.x + (band + 1.0) / portal_size.x;
    // The actor renders over the complete rift. Only pixels past its LEFT
    // rim's RIGHT/INNER edge disappear into transparent scene space; no foreground rim or
    // solid background-colored rectangle covers the actor.
    float keep = smoothstep(gate - 1.5 / portal_size.x, gate + 1.5 / portal_size.x, portal_uv.x);
    if (mask_active) {
        alpha *= keep;
    }
    float silhouette_alpha = mix(body.a, initial.a, initial_weight);
    vec3 body_color = premultiplied / max(silhouette_alpha, 0.00001);
    if (rim_light_strength > 0.0 && silhouette_alpha > 0.0) {
        // Convert the direction to the rift from portal UVs back to body UVs.
        // This follows either facing without assuming a screen-space left side.
        float mapping_determinant = portal_uv_x.x * portal_uv_y.y - portal_uv_y.x * portal_uv_x.y;
        if (abs(mapping_determinant) > 0.000001) {
            vec2 to_light = vec2((contour.x + contour.y) * 0.5, 0.5) - portal_uv;
            vec2 body_direction = vec2(
                portal_uv_y.y * to_light.x - portal_uv_y.x * to_light.y,
                -portal_uv_x.y * to_light.x + portal_uv_x.x * to_light.y
            ) / mapping_determinant;
            vec2 body_size = vec2(textureSize(TEXTURE, 0));
            vec2 pixel_direction = body_direction * body_size;
            if (length(pixel_direction) > 0.000001) {
                pixel_direction = normalize(pixel_direction);
                vec2 side_direction = vec2(-pixel_direction.y, pixel_direction.x);
                vec2 step_uv = pixel_direction * rim_light_width / body_size;
                vec2 side_uv = side_direction * rim_light_width * 0.4 / body_size;
                float adjacent_alpha = recall_alpha_at(TEXTURE, UV + step_uv) * 0.5;
                adjacent_alpha += recall_alpha_at(TEXTURE, UV + step_uv + side_uv) * 0.25;
                adjacent_alpha += recall_alpha_at(TEXTURE, UV + step_uv - side_uv) * 0.25;
                float inner_edge = clamp((silhouette_alpha - adjacent_alpha) / max(silhouette_alpha, 0.00001), 0.0, 1.0);
                float portal_gap = max(max(contour.x - portal_uv.x, portal_uv.x - contour.y), 0.0) * portal_size.x;
                float light_reach = max((contour.y - contour.x) * portal_size.x * 0.85, 56.0);
                float proximity = 1.0 - smoothstep(0.0, light_reach, portal_gap);
                float rim = smoothstep(0.02, 0.7, inner_edge) * proximity * rim_light_strength;
                // Light only the existing silhouette. Alpha and the exit mask
                // stay untouched, so no new outline survives the recall.
                body_color = min(body_color + rim_light_color.rgb * rim * 0.65, vec3(1.0));
            }
        }
    }
    COLOR = vec4(body_color, alpha) * vertex_color;
}
