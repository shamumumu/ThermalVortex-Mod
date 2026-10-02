shader_type canvas_item;
render_mode unshaded;

uniform vec2 guard_origin;
uniform vec2 guard_x = vec2(1.0, 0.0);
uniform vec2 guard_y = vec2(0.0, 1.0);
uniform vec2 dome_center;
uniform vec2 dome_radii;
varying vec2 guard_pixel;
varying vec4 sprite_color;

void vertex() {
    guard_pixel = guard_origin + guard_x * VERTEX.x + guard_y * VERTEX.y;
    sprite_color = COLOR;
}

void fragment() {
    vec4 ink = texture(TEXTURE, UV) * sprite_color;
    if (dome_radii.x > 0.0 && dome_radii.y > 0.0) {
        float x = (guard_pixel.x - dome_center.x) / dome_radii.x;
        if (abs(x) < 1.0) {
            float surface = dome_center.y - dome_radii.y * sqrt(max(0.0, 1.0 - x*x));
            // Also remove the ray below the dome: clipping just its interior
            // would leave the native bolt visible again at the actor's feet.
            ink.a *= 1.0 - smoothstep(surface - 1.0, surface + 1.0, guard_pixel.y);
        }
    }
    COLOR = ink;
}
